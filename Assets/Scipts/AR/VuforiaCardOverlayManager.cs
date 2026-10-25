using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Vuforia;

/* Drives real card state (identity, zone placement, stand/rest) from Vuforia: Image Targets made
 from the decks before the match (DeckTargetLoader), plus Cloud Reco when useCloudReco is on.
 Replaces the ARFoundation-based CardOverlayManager, which can't run alongside Cloud Reco's
 camera pipeline.

 Zone placement projects each card's on-screen center onto the board (BoardLayout.ZoneAtScreenPoint),
 so the real playmat must line up with the zones drawn on screen. */
public class VuforiaCardOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public BoardLayout board;
    public GameObject overlayPrefab; // optional custom overlay prefab; a minimal one is built if left empty

    [Header("Cloud Databases")]
    [Tooltip("Off: only cards in the loaded decks are recognised. On: Cloud Reco also searches the cloud databases, e.g. for cards outside the decks.")]
    public bool useCloudReco = false;
    [Tooltip("One CloudRecoBehaviour per Vuforia Cloud database (DSeries1/2/3, etc). Each database has its own Access/Secret key pair and Vuforia can't search more than one per query, so every database needs its own CloudRecoBehaviour running at once. Leave empty to auto-collect every CloudRecoBehaviour under this GameObject.")]
    public List<CloudRecoBehaviour> cloudRecoBehaviours = new List<CloudRecoBehaviour>();

    [Header("Stand / Rest")]
    [Tooltip("Degrees a card must turn from upright on screen (vs the board's card axis) to count as rested.")]
    [Range(10f, 90f)] public float restAngle = 55f;
    [Tooltip("Degrees from upright below which a rested card counts as standing again. The gap to restAngle stops flicker.")]
    [Range(0f, 80f)] public float standAngle = 35f;

    [Header("Counter Blast")]
    [Tooltip("Seconds a damage-zone card must stay unseen before it counts as flipped face down. A hand briefly covering it shouldn't count.")]
    public float faceDownAfterSeconds = 1.5f;

    [Header("Zone Detection")]
    [Tooltip("Seconds a card must stay over a zone before it counts as placed there, so sliding a card across the board doesn't enter every zone on the way.")]
    public float zoneSettleSeconds = 0.4f;

    [Header("Duplicate Targets")]
    [Tooltip("Two tracked targets whose centers are closer on screen than this fraction of a card's longer side count as the same physical card, and only one of them is shown. Raise it if duplicates still slip through, lower it if neighbouring cards hide each other.")]
    [Range(0.1f, 1f)] public float duplicateOverlapFraction = 0.5f;
    [Tooltip("A new match also counts as a duplicate of a card last tracked up to this many seconds ago, in case Vuforia drops the original as the duplicate starts tracking. Keep it short: placing a different card on a circle right after lifting one off must not count.")]
    [Range(0f, 1f)] public float duplicateGraceSeconds = 0.25f;

    readonly List<Action> mUnregisterHandlers = new List<Action>();

    // Last query error per database, so a failing one logs once instead of every query
    readonly Dictionary<CloudRecoBehaviour, CloudRecoBehaviour.QueryError> mLastQueryError =
        new Dictionary<CloudRecoBehaviour, CloudRecoBehaviour.QueryError>();

    class TrackedCard
    {
        public CardInstance instance;
        public PlayerState owner;
        public ObserverBehaviour target;
        public GameObject overlay;
        public CardOverlayUI overlayUI;
        public string currentZoneKey;
        public string pendingZoneKey; // zone it's over now, committed after zoneSettleSeconds
        public float pendingSince;
        public bool hasPose; // false while Vuforia has lost this target (out of view, occluded, etc.)
        public TrackedCard duplicateOf; // the tracked target already showing this same physical card, if any
        public string cardNumber; // Cloud Reco metadata, e.g. "DZ-BT07-024"
        public bool awaitingFirstPose = true; // new match, not yet checked by CheckNewMatches
        public bool retired; // dropped as a duplicate match; kept so Cloud Reco re-matches stay ignored
        public float lastSeenTime = float.NegativeInfinity; // Time.time of its last tracked frame
    }

    // Keyed by Cloud Reco's UniqueTargetId, or "Deck_" + card number for deck targets
    readonly Dictionary<string, TrackedCard> mTrackedCards = new Dictionary<string, TrackedCard>();

    /* In the order their card data resolved: when several targets cover one physical card,
     the earliest here is the one shown (see ResolveDuplicates). */
    readonly List<TrackedCard> mResolvedCards = new List<TrackedCard>();
    readonly List<TrackedCard> mLiveCards = new List<TrackedCard>(); // rebuilt every frame by ResolveDuplicates

    void Awake()
    {
        if (cloudRecoBehaviours == null || cloudRecoBehaviours.Count == 0)
            cloudRecoBehaviours = new List<CloudRecoBehaviour>(GetComponentsInChildren<CloudRecoBehaviour>(true));

        // A disabled CloudRecoBehaviour stops querying the cloud
        if (!useCloudReco)
            foreach (CloudRecoBehaviour cloudReco in cloudRecoBehaviours)
                if (cloudReco != null) cloudReco.enabled = false;
    }

    void OnEnable()
    {
        if (!useCloudReco) return;

        foreach (CloudRecoBehaviour cloudReco in cloudRecoBehaviours)
        {
            CloudRecoBehaviour captured = cloudReco;
            Action<CloudRecoBehaviour.CloudRecoSearchResult> onResult = result => OnNewSearchResult(captured, result);
            Action<CloudRecoBehaviour> onReady = _ => Debug.Log("[CloudReco] " + Describe(captured) + " initialized, scanning.");
            Action<CloudRecoBehaviour.InitError> onInitError = error => Debug.LogError("[CloudReco] " + Describe(captured) + " failed to initialize: " + error);
            Action<CloudRecoBehaviour.QueryError> onQueryError = error => OnQueryError(captured, error);

            captured.RegisterOnNewSearchResultEventHandler(onResult);
            captured.RegisterOnInitializedEventHandler(onReady);
            captured.RegisterOnInitErrorEventHandler(onInitError);
            captured.RegisterOnUpdateErrorEventHandler(onQueryError);

            mUnregisterHandlers.Add(() =>
            {
                captured.UnregisterOnNewSearchResultEventHandler(onResult);
                captured.UnregisterOnInitializedEventHandler(onReady);
                captured.UnregisterOnInitErrorEventHandler(onInitError);
                captured.UnregisterOnUpdateErrorEventHandler(onQueryError);
            });
        }
    }

    void OnDisable()
    {
        foreach (Action unregister in mUnregisterHandlers) unregister();
        mUnregisterHandlers.Clear();
    }

    void OnQueryError(CloudRecoBehaviour source, CloudRecoBehaviour.QueryError error)
    {
        if (mLastQueryError.TryGetValue(source, out CloudRecoBehaviour.QueryError last) && last == error) return;
        mLastQueryError[source] = error;
        Debug.LogError("[CloudReco] " + Describe(source) + " query failed: " + error);
    }

    // GameObject name plus the start of its access key, to tell the databases apart in logs
    static string Describe(CloudRecoBehaviour cloudReco)
    {
        string key = cloudReco.AccessKey ?? "";
        return "'" + cloudReco.name + "' (key " + (key.Length > 8 ? key.Substring(0, 8) + "..." : key) + ")";
    }

    void OnNewSearchResult(CloudRecoBehaviour source, CloudRecoBehaviour.CloudRecoSearchResult result)
    {
        if (mTrackedCards.ContainsKey(result.UniqueTargetId)) return; // tracking physical card

        Debug.Log("[CloudReco] " + Describe(source) + " matched '" + result.TargetName + "', metadata: '" + result.MetaData + "'");

        /* A unique name per result (not one shared template GameObject) lets multiple
         physical cards stay tracked on the board at the same time. */
        ObserverBehaviour target = source.EnableObservers(result, "CardTarget_" + result.UniqueTargetId);
        if (target == null)
        {
            Debug.LogWarning("[CloudReco] EnableObservers returned null for '" + result.TargetName + "'");
            return;
        }

        RegisterTarget(result.UniqueTargetId, target, result.MetaData);
    }

    // Called by DeckTargetLoader for each Image Target it makes from a deck card's picture
    public void AddDeckTarget(ObserverBehaviour target, string cardNumber)
    {
        string key = "Deck_" + cardNumber;
        if (mTrackedCards.ContainsKey(key)) return;
        RegisterTarget(key, target, cardNumber);
    }

    void RegisterTarget(string key, ObserverBehaviour target, string cardNumber)
    {
        GameObject overlay = BuildOverlay(target.transform);
        CardOverlayUI ui = overlay.GetComponent<CardOverlayUI>();

        /* Card data is fetched by CheckNewMatches on the first tracked frame, once the pose is
         real and the match can be checked for being a duplicate of a card already in view. */
        TrackedCard tracked = new TrackedCard
        {
            target = target,
            overlay = overlay,
            overlayUI = ui,
            cardNumber = cardNumber,
            hasPose = target.TargetStatus.Status == Status.TRACKED,
        };
        mTrackedCards[key] = tracked;
        RefreshOverlayVisibility(tracked); // stays hidden until its card data resolves

        /* Vuforia freezes a lost target's transform at its last pose instead of disabling it,
         which would leave the overlay (and its board zone) stuck there. Only TRACKED counts as
         live: EXTENDED_TRACKED is a guessed pose for a card that has left the camera image. */
        target.OnTargetStatusChanged += (observer, status) =>
        {
            tracked.hasPose = status.Status == Status.TRACKED;
            RefreshOverlayVisibility(tracked);
        };
    }

    /* A new match that lands on a card already in view is that same physical card matched by
     a second database entry (a duplicate upload, or a same-art reprint). It's dropped before
     its data is fetched, so it never shows, logs, or takes over when the original flickers. */
    void CheckNewMatches()
    {
        Camera cam = Camera.main;

        foreach (TrackedCard tracked in mTrackedCards.Values)
        {
            if (!tracked.awaitingFirstPose || !tracked.hasPose || tracked.target == null) continue;
            tracked.awaitingFirstPose = false;

            TrackedCard original = cam != null ? FindCardCovering(cam, tracked) : null;
            if (original != null)
            {
                Debug.Log("[VuforiaCardOverlayManager] Ignoring " + tracked.cardNumber + ": same physical card as " + original.cardNumber);
                tracked.retired = true;
                Destroy(tracked.target.gameObject); // stops Vuforia tracking it (and removes its hidden overlay)
                continue;
            }

            ResolveCardData(tracked.cardNumber, tracked);
        }
    }

    // A card tracked now (or within duplicateGraceSeconds) sitting on the same spot on screen
    TrackedCard FindCardCovering(Camera cam, TrackedCard tracked)
    {
        foreach (TrackedCard other in mTrackedCards.Values)
        {
            if (other == tracked || other.retired || other.target == null) continue;
            if (other.awaitingFirstPose) continue; // two new matches in one frame: the first checked is kept
            if (Time.time - other.lastSeenTime > duplicateGraceSeconds) continue;
            if (CoversSameCard(cam, other, tracked)) return other;
        }

        return null;
    }

    /* Firestore is the source of truth for card stats; the local REST scraper cache
     (ApiCardDatabase) is only a fallback when Firestore is unreachable or unconfigured. */
    void ResolveCardData(string cardNumber, TrackedCard tracked)
    {
        if (CardFirestoreDatabase.Instance != null)
        {
            CardFirestoreDatabase.Instance.GetCardByNumber(cardNumber,
                data => ApplyCardData(tracked, data, cardNumber),
                error =>
                {
                    Debug.LogWarning("[VuforiaCardOverlayManager] Firestore lookup failed for " + cardNumber + ": " + error + " — falling back to local API cache.");
                    ApplyCardData(tracked, ApiCardDatabase.Instance != null ? ApiCardDatabase.Instance.GetByNumber(cardNumber) : null, cardNumber);
                });
        }
        else
        {
            Debug.LogWarning("[VuforiaCardOverlayManager] CardFirestoreDatabase not found in scene — add it to a GameObject to enable Firebase lookups. Using local API cache only.");
            ApplyCardData(tracked, ApiCardDatabase.Instance != null ? ApiCardDatabase.Instance.GetByNumber(cardNumber) : null, cardNumber);
        }
    }

    void ApplyCardData(TrackedCard tracked, CardData data, string cardNumber)
    {
        if (data == null)
        {
            Debug.LogWarning("[VuforiaCardOverlayManager] No CardData found for scanned card: " + cardNumber);
            return;
        }

        tracked.instance = new CardInstance(data);
        tracked.overlayUI?.Populate(data);
        mResolvedCards.Add(tracked); // ResolveDuplicates decides on the next Update whether it gets shown

        Debug.Log("[VuforiaCardOverlayManager] Now tracking " + cardNumber + "\n" + FormatCardDetails(data));
    }

    // Logs a scanned card's main stats, to confirm what Firestore/the local API resolved
    static string FormatCardDetails(CardData data)
    {
        string clanRace = string.IsNullOrEmpty(data.clan) ? data.race : data.clan + " / " + data.race;

        return
            "  Name:     " + data.cardName + (string.IsNullOrEmpty(data.cardNameEn) ? "" : " (" + data.cardNameEn + ")") + "\n" +
            "  Number:   " + data.cardNumber + "\n" +
            "  Clan:     " + clanRace + (string.IsNullOrEmpty(data.nation) ? "" : " [" + data.nation + "]") + "\n" +
            "  Grade:    G" + data.grade + "   Power: " + data.power + "   Shield: " + data.shield + "   Critical: " + data.critical + "\n" +
            "  Trigger:  " + data.triggerIcon + (string.IsNullOrEmpty(data.skillText) ? "" : " (" + data.skillText + ")") + "\n" +
            "  Rarity:   " + data.rarity + "\n" +
            "  Effect:   " + data.abilityText;
    }

    void Update()
    {
        foreach (TrackedCard tracked in mTrackedCards.Values)
            if (tracked.hasPose) tracked.lastSeenTime = Time.time;

        CheckNewMatches();
        ResolveDuplicates();

        Camera cam = Camera.main;
        if (board == null || cam == null) return;

        foreach (TrackedCard tracked in mTrackedCards.Values)
        {
            if (tracked.target == null || tracked.instance == null) continue; // instance pending resolution
            if (tracked.duplicateOf != null) continue; // another target is already standing for this physical card

            UpdateFacing(tracked);
            if (!tracked.hasPose) continue; // target currently untracked

            UpdateOrientation(tracked, cam);
            UpdateZone(tracked, cam);
        }
    }

    /* Vuforia can't see a face-down card, so a damage card that stays unseen counts as flipped
     (Counter Blast) and flips back face up once seen again. */
    void UpdateFacing(TrackedCard tracked)
    {
        GameState gs = GameState.Instance;
        if (gs == null) return;

        if (tracked.hasPose)
            gs.SetFacing(tracked.instance, CardFacing.FaceUp);
        else if (tracked.instance.zone == "Damage" && Time.time - tracked.lastSeenTime > faceDownAfterSeconds)
            gs.SetFacing(tracked.instance, CardFacing.FaceDown);
    }

    void RefreshOverlayVisibility(TrackedCard tracked)
    {
        tracked.overlay.SetActive(tracked.hasPose && tracked.instance != null && tracked.duplicateOf == null);
    }

    /* Several Cloud Reco targets can match one physical card (a duplicate upload, or a reprint
     with the same art). Only the first to resolve is shown; later ones overlapping it are
     hidden and kept off the board. */
    void ResolveDuplicates()
    {
        Camera cam = Camera.main;
        mLiveCards.Clear();

        foreach (TrackedCard tracked in mResolvedCards)
        {
            if (tracked.target == null || !tracked.hasPose) continue; // keeps its last verdict until it's back in view

            TrackedCard coveredBy = null;
            if (cam != null)
            {
                foreach (TrackedCard live in mLiveCards)
                {
                    if (!CoversSameCard(cam, live, tracked)) continue;
                    coveredBy = live;
                    break;
                }
            }

            if (coveredBy == null) mLiveCards.Add(tracked);

            if (coveredBy != tracked.duplicateOf)
            {
                /* Taking over from the target this one was hidden behind, now out of view:
                 pull that one off the board so the card isn't counted in a zone twice. */
                if (coveredBy == null && !tracked.duplicateOf.hasPose)
                    SetZone(tracked.duplicateOf, null);

                tracked.duplicateOf = coveredBy;
                if (coveredBy != null) SetZone(tracked, null);
            }

            RefreshOverlayVisibility(tracked);
        }
    }

    /* Compared on screen, not in world space: Vuforia infers distance from the uploaded
     target width, so two targets of one card can sit at different depths yet overlap. */
    bool CoversSameCard(Camera cam, TrackedCard live, TrackedCard other)
    {
        Vector3 liveCenter = cam.WorldToScreenPoint(live.target.transform.position);
        Vector3 otherCenter = cam.WorldToScreenPoint(other.target.transform.position);
        if (liveCenter.z <= 0f || otherCenter.z <= 0f) return false; // behind the camera

        float limit = duplicateOverlapFraction * ScreenExtent(cam, live.target, liveCenter);
        return Vector2.Distance(liveCenter, otherCenter) < limit;
    }

    // Length in pixels of the target's longer side as the camera currently sees it
    static float ScreenExtent(Camera cam, ObserverBehaviour target, Vector2 screenCenter)
    {
        float width = 1f, height = 1f;
        if (target is ImageTargetBehaviour image)
        {
            var size = image.GetSize();
            width = size.x;
            height = size.y;
        }

        // Image targets lie in their local XZ plane: X across the card, Z along it
        Transform t = target.transform;
        Vector2 toSideEdge = (Vector2)cam.WorldToScreenPoint(t.position + t.right * (width * 0.5f)) - screenCenter;
        Vector2 toTopEdge = (Vector2)cam.WorldToScreenPoint(t.position + t.forward * (height * 0.5f)) - screenCenter;
        return 2f * Mathf.Max(toSideEdge.magnitude, toTopEdge.magnitude);
    }

    /* Stand vs rest, from the card's angle on screen against the board's card axis, so it
     doesn't depend on how the camera is mounted. Upside down still counts as standing. */
    void UpdateOrientation(TrackedCard tracked, Camera cam)
    {
        Transform t = tracked.target.transform;
        Vector2 cardAxis = ScreenDirection(cam, t.position, t.forward); // image targets run along local Z
        Vector2 boardAxis = ScreenDirection(cam, board.transform.position, board.CardLengthAxis);

        float angle = Vector2.Angle(cardAxis, boardAxis);
        float fromUpright = Mathf.Min(angle, 180f - angle);

        CardOrientation orientation = tracked.instance.orientation; // kept between the two thresholds
        if (fromUpright > restAngle) orientation = CardOrientation.Rest;
        else if (fromUpright < standAngle) orientation = CardOrientation.Stand;

        GameState.Instance?.SetOrientation(tracked.instance, orientation);
        tracked.overlayUI?.SetRestIndicator(orientation == CardOrientation.Rest);
    }

    // On-screen direction of a world direction at a point; the step scales with distance to stay scale-free
    static Vector2 ScreenDirection(Camera cam, Vector3 origin, Vector3 direction)
    {
        float step = 0.05f * Vector3.Distance(cam.transform.position, origin);
        return (Vector2)(cam.WorldToScreenPoint(origin + direction * step) - cam.WorldToScreenPoint(origin));
    }

    // Moves the card into whichever board zone its on-screen center falls in
    void UpdateZone(TrackedCard tracked, Camera cam)
    {
        Vector3 screen = cam.WorldToScreenPoint(tracked.target.transform.position);
        string key = screen.z > 0f ? board.ZoneAtScreenPoint(screen, cam, tracked.currentZoneKey) : null;

        if (key != tracked.pendingZoneKey)
        {
            tracked.pendingZoneKey = key;
            tracked.pendingSince = Time.time;
        }

        if (Time.time - tracked.pendingSince >= zoneSettleSeconds)
            SetZone(tracked, key);
    }

    // Moves the card into the given board zone, or off the board entirely when key is null
    void SetZone(TrackedCard tracked, string key)
    {
        if (key == tracked.currentZoneKey) return; // no change
        tracked.currentZoneKey = key;

        GameState gs = GameState.Instance;
        if (gs == null) return;

        tracked.owner = key == null ? null : board.ZoneOwner(gs, key);
        gs.MoveCard(tracked.instance, tracked.owner, key?.Substring(3)); // suffix after "P1_"/"P2_"
    }

    // Builds a minimal overlay canvas when no custom prefab is assigned
    GameObject BuildOverlay(Transform parent)
    {
        if (overlayPrefab != null)
        {
            GameObject instance = Instantiate(overlayPrefab, parent);
            instance.transform.localPosition = Vector3.zero;
            return instance;
        }

        GameObject canvasObj = new GameObject("CardOverlayCanvas");
        canvasObj.transform.SetParent(parent, false);
        canvasObj.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        canvasObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(630f, 880f);
        canvasObj.transform.localScale = Vector3.one * OverlayScale(parent, canvasRT.sizeDelta.x);

        TextMeshProUGUI nameText = CreateText(canvasObj.transform, "CardName", new Vector2(0f, 0.85f), new Vector2(1f, 1f), 60, Color.black);
        TextMeshProUGUI powerText = CreateText(canvasObj.transform, "Power", new Vector2(0f, 0.4f), new Vector2(1f, 0.7f), 80, Color.black);
        TextMeshProUGUI critText = CreateText(canvasObj.transform, "Critical", new Vector2(0f, 0.25f), new Vector2(1f, 0.4f), 50, Color.black);

        // Small tap target in the corner opens the detail panel built below
        GameObject toggleBtnObj = new GameObject("DetailToggleButton");
        toggleBtnObj.transform.SetParent(canvasObj.transform, false);
        RectTransform toggleRT = toggleBtnObj.AddComponent<RectTransform>();
        toggleRT.anchorMin = new Vector2(0.78f, 0.88f);
        toggleRT.anchorMax = new Vector2(1f, 1f);
        toggleRT.offsetMin = Vector2.zero;
        toggleRT.offsetMax = Vector2.zero;
        UnityEngine.UI.Image toggleBg = toggleBtnObj.AddComponent<UnityEngine.UI.Image>();
        toggleBg.color = new Color(1f, 1f, 1f, 0.25f);
        Button toggleBtn = toggleBtnObj.AddComponent<Button>();
        CreateText(toggleBtnObj.transform, "Label", Vector2.zero, Vector2.one, 40, Color.black).text = "i";

        /* Skill panel: name, grade/power line and the card's full skill text. Hidden until toggled,
         then held upright toward the camera (see CardOverlayUI.LateUpdate) so the text is readable.
         Card-shaped and half again as big as the card, since skill text runs long. */
        GameObject detailObj = new GameObject("DetailPanel");
        detailObj.transform.SetParent(canvasObj.transform, false);
        detailObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        detailObj.transform.localScale = Vector3.one * 1.5f;
        Canvas detailCanvas = detailObj.AddComponent<Canvas>();
        detailCanvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler detailScaler = detailObj.AddComponent<CanvasScaler>();
        detailScaler.dynamicPixelsPerUnit = 100;
        detailObj.AddComponent<GraphicRaycaster>();
        RectTransform detailRT = detailObj.GetComponent<RectTransform>();
        detailRT.sizeDelta = new Vector2(630f, 880f);
        UnityEngine.UI.Image detailBg = detailObj.AddComponent<UnityEngine.UI.Image>();
        detailBg.color = new Color(1f, 1f, 1f, 0.92f); // light so the black text reads

        TextMeshProUGUI skillTitle = CreateText(detailObj.transform, "SkillTitle", new Vector2(0.04f, 0.89f), new Vector2(0.96f, 0.98f), 44, Color.black);
        skillTitle.enableAutoSizing = true;
        skillTitle.fontSizeMin = 24;
        skillTitle.fontSizeMax = 44;
        TextMeshProUGUI skillInfo = CreateText(detailObj.transform, "SkillInfo", new Vector2(0.04f, 0.83f), new Vector2(0.96f, 0.89f), 26, new Color(0.25f, 0.25f, 0.25f));
        skillInfo.enableAutoSizing = true;
        skillInfo.fontSizeMin = 16;
        skillInfo.fontSizeMax = 26;
        TextMeshProUGUI skillBody = CreateText(detailObj.transform, "SkillBody", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.82f), 32, Color.black);
        skillBody.alignment = TextAlignmentOptions.TopLeft;
        skillBody.enableAutoSizing = true; // long skills shrink to fit
        skillBody.fontSizeMin = 14;
        skillBody.fontSizeMax = 32;

        CardOverlayUI ui = canvasObj.AddComponent<CardOverlayUI>();
        ui.cardNameText = nameText;
        ui.powerText = powerText;
        ui.criticalText = critText;
        ui.detailPanel = detailObj;
        ui.skillTitleText = skillTitle;
        ui.skillInfoText = skillInfo;
        ui.skillBodyText = skillBody;
        ui.detailToggleButton = toggleBtn;

        /* ui.Awake() already ran (AddComponent calls it synchronously) before these fields
         were assigned, so wire the listener and initial state here. */
        toggleBtn.onClick.AddListener(ui.ToggleDetailPanel);
        detailObj.SetActive(false);

        return canvasObj;
    }

    /* Scale that makes the overlay canvas as wide as the card. Deck targets are real size (63 mm)
     while Cloud Reco targets use their uploaded width, so a fixed scale only fits one of them. */
    static float OverlayScale(Transform target, float canvasWidth)
    {
        if (!(target.GetComponent<ObserverBehaviour>() is ImageTargetBehaviour image)) return 0.001f;

        float cardWidth = image.GetSize().x;
        float targetScale = target.lossyScale.x;
        if (cardWidth <= 0f || targetScale <= 0f) return 0.001f;
        return cardWidth / (canvasWidth * targetScale);
    }

    TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int fontSize, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return tmp;
    }

    // Called externally (e.g. by AROverlayEventBridge) to push a power update onto a card's overlay
    public void UpdateCardPower(string cardName, int basePower, int modifier)
    {
        foreach (TrackedCard tracked in mTrackedCards.Values)
            if (tracked.instance?.data.cardName == cardName)
                tracked.overlayUI?.UpdatePower(basePower, modifier);
    }

    public void ShowRulePrompt(string cardName, string promptText)
    {
        foreach (TrackedCard tracked in mTrackedCards.Values)
            if (tracked.instance?.data.cardName == cardName && tracked.overlay.activeInHierarchy) // hidden overlays can't run the prompt's auto-hide coroutine; retired duplicates have no instance (or overlay)
                tracked.overlayUI?.ShowRulePrompt(promptText);
    }
}

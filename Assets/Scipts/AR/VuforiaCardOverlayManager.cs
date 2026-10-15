using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Vuforia;

/* Drives real card state (identity, zone placement, stand/rest) from Vuforia Cloud Reco.
 Replaces the ARFoundation-based CardOverlayManager, which can't run alongside Cloud Reco's
 camera pipeline.

 Zone placement is nearest-neighbor against BoardLayout.circles, so each zone key (e.g.
 "P1_Vanguard") must resolve to a GameObject that tracks the real playmat: a Vuforia Image
 Target (BoardLayout.arZoneMarkers) or, for testing, BoardLayout's procedural quad. */
public class VuforiaCardOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public BoardLayout board;
    public GameObject overlayPrefab; // optional custom overlay prefab; a minimal one is built if left empty

    [Header("Cloud Databases")]
    [Tooltip("One CloudRecoBehaviour per Vuforia Cloud database (DSeries1/2/3, etc). Each database has its own Access/Secret key pair and Vuforia can't search more than one per query, so every database needs its own CloudRecoBehaviour running at once. Leave empty to auto-collect every CloudRecoBehaviour under this GameObject.")]
    public List<CloudRecoBehaviour> cloudRecoBehaviours = new List<CloudRecoBehaviour>();

    [Header("Zone Detection")]
    [Tooltip("Max distance, in the same units as the zone marker transforms, for a card to count as 'on' a zone. Needs tuning once measured against the real playmat.")]
    public float maxZoneDistance = 0.15f;

    [Header("Duplicate Targets")]
    [Tooltip("Two tracked targets whose centers are closer on screen than this fraction of a card's longer side count as the same physical card, and only one of them is shown. Raise it if duplicates still slip through, lower it if neighbouring cards hide each other.")]
    [Range(0.1f, 1f)] public float duplicateOverlapFraction = 0.5f;
    [Tooltip("A new match also counts as a duplicate of a card last tracked up to this many seconds ago, in case Vuforia drops the original as the duplicate starts tracking. Keep it short: placing a different card on a circle right after lifting one off must not count.")]
    [Range(0f, 1f)] public float duplicateGraceSeconds = 0.25f;

    readonly Dictionary<CloudRecoBehaviour, Action<CloudRecoBehaviour.CloudRecoSearchResult>> mHandlers =
        new Dictionary<CloudRecoBehaviour, Action<CloudRecoBehaviour.CloudRecoSearchResult>>();

    class TrackedCard
    {
        public CardInstance instance;
        public PlayerState owner;
        public ObserverBehaviour target;
        public GameObject overlay;
        public CardOverlayUI overlayUI;
        public string currentZoneKey;
        public bool hasPose; // false while Vuforia has lost this target (out of view, occluded, etc.)
        public TrackedCard duplicateOf; // the tracked target already showing this same physical card, if any
        public string cardNumber; // Cloud Reco metadata, e.g. "DZ-BT07-024"
        public bool awaitingFirstPose = true; // new match, not yet checked by CheckNewMatches
        public bool retired; // dropped as a duplicate match; kept so Cloud Reco re-matches stay ignored
        public float lastSeenTime = float.NegativeInfinity; // Time.time of its last tracked frame
    }

    // Keyed by Cloud Reco's UniqueTargetId — one physical card, tracked continuously
    readonly Dictionary<string, TrackedCard> mTrackedCards = new Dictionary<string, TrackedCard>();

    /* In the order their card data resolved: when several targets cover one physical card,
     the earliest here is the one shown (see ResolveDuplicates). */
    readonly List<TrackedCard> mResolvedCards = new List<TrackedCard>();
    readonly List<TrackedCard> mLiveCards = new List<TrackedCard>(); // rebuilt every frame by ResolveDuplicates

    void Awake()
    {
        if (cloudRecoBehaviours == null || cloudRecoBehaviours.Count == 0)
            cloudRecoBehaviours = new List<CloudRecoBehaviour>(GetComponentsInChildren<CloudRecoBehaviour>(true));
    }

    void OnEnable()
    {
        foreach (CloudRecoBehaviour cloudReco in cloudRecoBehaviours)
        {
            CloudRecoBehaviour captured = cloudReco;
            Action<CloudRecoBehaviour.CloudRecoSearchResult> handler = result => OnNewSearchResult(captured, result);
            mHandlers[captured] = handler;
            captured.RegisterOnNewSearchResultEventHandler(handler);
        }
    }

    void OnDisable()
    {
        foreach (KeyValuePair<CloudRecoBehaviour, Action<CloudRecoBehaviour.CloudRecoSearchResult>> kvp in mHandlers)
            kvp.Key.UnregisterOnNewSearchResultEventHandler(kvp.Value);
        mHandlers.Clear();
    }

    void OnNewSearchResult(CloudRecoBehaviour source, CloudRecoBehaviour.CloudRecoSearchResult result)
    {
        if (mTrackedCards.ContainsKey(result.UniqueTargetId)) return; // tracking physical card

        /* A unique name per result (not one shared template GameObject) lets multiple
         physical cards stay tracked on the board at the same time. */
        ObserverBehaviour target = source.EnableObservers(result, "CardTarget_" + result.UniqueTargetId);
        if (target == null) return;

        GameObject overlay = BuildOverlay(target.transform);
        CardOverlayUI ui = overlay.GetComponent<CardOverlayUI>();

        /* Card data is fetched by CheckNewMatches on the first tracked frame, once the pose is
         real and the match can be checked for being a duplicate of a card already in view. */
        TrackedCard tracked = new TrackedCard
        {
            target = target,
            overlay = overlay,
            overlayUI = ui,
            cardNumber = result.MetaData,
            hasPose = target.TargetStatus.Status == Status.TRACKED,
        };
        mTrackedCards[result.UniqueTargetId] = tracked;
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

        if (board == null) return;

        foreach (TrackedCard tracked in mTrackedCards.Values)
        {
            if (tracked.target == null || tracked.instance == null || !tracked.hasPose) continue; // instance pending resolution, or target currently untracked
            if (tracked.duplicateOf != null) continue; // another target is already standing for this physical card

            UpdateOrientation(tracked);
            UpdateZone(tracked);
        }
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

    /* Stand vs rest, from the tracked card's rotation. The axis/threshold depends on how
     the target is defined in Vuforia and needs tuning against real cards. */
    void UpdateOrientation(TrackedCard tracked)
    {
        float zRot = tracked.target.transform.eulerAngles.z;
        bool isRested = zRot > 60f && zRot < 120f;

        tracked.instance.orientation = isRested ? CardOrientation.Rest : CardOrientation.Stand;
        tracked.overlayUI?.SetRestIndicator(isRested);
    }

    // Finds the nearest registered board zone and, if within range, moves the card into it
    void UpdateZone(TrackedCard tracked)
    {
        string bestKey = null;
        float bestDist = maxZoneDistance;

        foreach (KeyValuePair<string, GameObject> kvp in board.circles)
        {
            float dist = Vector3.Distance(tracked.target.transform.position, kvp.Value.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestKey = kvp.Key;
            }
        }

        SetZone(tracked, bestKey);
    }

    // Moves the card into the given board zone, or off the board entirely when key is null
    void SetZone(TrackedCard tracked, string key)
    {
        if (key == tracked.currentZoneKey) return; // no change

        if (tracked.currentZoneKey != null)
        {
            board.RemoveCardFromCircle(tracked.currentZoneKey);
            tracked.owner?.RemoveCardFromAllZones(tracked.instance);
        }

        tracked.currentZoneKey = key;

        if (key == null)
        {
            tracked.owner = null;
        }
        else
        {
            string prefix = key.Substring(0, 2); // "P1" or "P2"
            string suffix = key.Substring(3); // after "P1_"/"P2_"
            GameState gs = GameState.Instance;
            tracked.owner = gs != null ? gs.players[prefix == "P1" ? 0 : 1] : null;

            if (tracked.owner != null && tracked.owner.PlaceCardInZone(tracked.instance, suffix))
                board.PlaceCardOnCircle(key);
        }

        GameState.Instance?.BroadcastState();
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
        canvasObj.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(630f, 880f);

        TextMeshProUGUI nameText = CreateText(canvasObj.transform, "CardName", new Vector2(0f, 0.85f), new Vector2(1f, 1f), 60, Color.white);
        TextMeshProUGUI powerText = CreateText(canvasObj.transform, "Power", new Vector2(0f, 0.4f), new Vector2(1f, 0.7f), 80, Color.white);
        TextMeshProUGUI critText = CreateText(canvasObj.transform, "Critical", new Vector2(0f, 0.25f), new Vector2(1f, 0.4f), 50, Color.red);

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
        CreateText(toggleBtnObj.transform, "Label", Vector2.zero, Vector2.one, 40, Color.white).text = "i";

        /* Detail panel (clan/race/skill badge/flavor/rarity): hidden until toggled, then
         billboards toward the camera (see CardOverlayUI.LateUpdate) so body text is readable. */
        GameObject detailObj = new GameObject("DetailPanel");
        detailObj.transform.SetParent(canvasObj.transform, false);
        detailObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        Canvas detailCanvas = detailObj.AddComponent<Canvas>();
        detailCanvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler detailScaler = detailObj.AddComponent<CanvasScaler>();
        detailScaler.dynamicPixelsPerUnit = 100;
        detailObj.AddComponent<GraphicRaycaster>();
        RectTransform detailRT = detailObj.GetComponent<RectTransform>();
        detailRT.sizeDelta = new Vector2(630f, 500f);
        UnityEngine.UI.Image detailBg = detailObj.AddComponent<UnityEngine.UI.Image>();
        detailBg.color = new Color(0f, 0f, 0f, 0.85f);

        TextMeshProUGUI clanRaceText = CreateText(detailObj.transform, "ClanRace", new Vector2(0f, 0.82f), new Vector2(1f, 1f), 34, Color.white);
        TextMeshProUGUI skillBadgeText = CreateText(detailObj.transform, "SkillBadge", new Vector2(0f, 0.68f), new Vector2(1f, 0.82f), 28, Color.cyan);
        TextMeshProUGUI flavorBodyText = CreateText(detailObj.transform, "Flavor", new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.65f), 24, Color.gray);
        TextMeshProUGUI rarityTextTmp = CreateText(detailObj.transform, "Rarity", new Vector2(0f, 0f), new Vector2(1f, 0.16f), 24, Color.yellow);

        CardOverlayUI ui = canvasObj.AddComponent<CardOverlayUI>();
        ui.cardNameText = nameText;
        ui.powerText = powerText;
        ui.criticalText = critText;
        ui.detailPanel = detailObj;
        ui.clanRaceText = clanRaceText;
        ui.skillBadgeText = skillBadgeText;
        ui.flavorBodyText = flavorBodyText;
        ui.rarityText = rarityTextTmp;
        ui.detailToggleButton = toggleBtn;

        /* ui.Awake() already ran (AddComponent calls it synchronously) before these fields
         were assigned, so wire the listener and initial state here. */
        toggleBtn.onClick.AddListener(ui.ToggleDetailPanel);
        detailObj.SetActive(false);

        return canvasObj;
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

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
 so the real playmat must line up with the zones drawn on screen. Its UI is built in VuforiaCardOverlayManager.UI.cs. */
public partial class VuforiaCardOverlayManager : MonoBehaviour
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
    [Tooltip("The same, when the card back is seen where the card was (CardBackTracker).")]
    public float faceDownWithBackSeconds = 0.3f;

    [Header("Zone Detection")]
    [Tooltip("On: a card is only scanned (looked up, shown and put in play) once its center is inside a zone's border; cards held in a hand or lying off the board are ignored. Off: any recognised card is scanned.")]
    public bool scanOnlyInsideZones = true;
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
        public bool insideBorder; // tracked with its center inside a zone's border this frame
        public TrackedCard duplicateOf; // the tracked target already showing this same physical card, if any
        public string cardNumber; // Cloud Reco metadata, e.g. "DZ-BT07-024"
        public bool awaitingFirstPose = true; // new match, not yet checked by CheckNewMatches
        public bool retired; // dropped as a duplicate match; kept so Cloud Reco re-matches stay ignored
        public bool isDeckCopy; // one of several deck targets with the same picture, for identical cards
        public float lastSeenTime = float.NegativeInfinity; // Time.time of its last tracked frame
    }

    // Keyed by Cloud Reco's UniqueTargetId, or "Deck_" + card number + "#" + copy for deck targets
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

    // Called by DeckTargetLoader for each Image Target it makes from a deck card's picture; copy numbers identical cards' targets
    public void AddDeckTarget(ObserverBehaviour target, string cardNumber, int copy = 0)
    {
        string key = "Deck_" + cardNumber + "#" + copy;
        if (mTrackedCards.ContainsKey(key)) return;
        RegisterTarget(key, target, cardNumber);
        mTrackedCards[key].isDeckCopy = true;
    }

    void RegisterTarget(string key, ObserverBehaviour target, string cardNumber)
    {
        GameObject overlay = BuildOverlay(target.transform);
        overlay.AddComponent<OverlaySmoothing>(); // Vuforia's pose wobbles; this stops the overlay shaking with it
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
            if (!tracked.awaitingFirstPose || !tracked.hasPose || !tracked.insideBorder || tracked.target == null) continue;

            TrackedCard original = cam != null ? FindCardCovering(cam, tracked) : null;

            /* A spare deck copy on a card another copy already reads: it waits, still unresolved,
             until Vuforia puts it on a second identical card somewhere else */
            if (original != null && tracked.isDeckCopy) continue;

            tracked.awaitingFirstPose = false;
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
        Camera cam = Camera.main;
        foreach (TrackedCard tracked in mTrackedCards.Values)
        {
            if (tracked.hasPose) tracked.lastSeenTime = Time.time;
            tracked.insideBorder = tracked.hasPose && tracked.target != null && IsInsideBorder(tracked, cam);
        }

        CheckNewMatches();
        ResolveDuplicates();

        if (board == null || cam == null) return;

        foreach (TrackedCard tracked in mTrackedCards.Values)
        {
            if (tracked.target == null || tracked.instance == null) continue; // instance pending resolution
            if (tracked.duplicateOf != null) continue; // another target is already standing for this physical card

            UpdateFacing(tracked, cam);
            if (!tracked.hasPose) continue; // target currently untracked

            if (tracked.insideBorder) UpdateOrientation(tracked, cam); // a card in hand isn't standing or resting
            UpdateZone(tracked, cam); // also takes it off the board once it has left
        }
    }

    /* Vuforia can't see a face-down card's front, so a damage card that stays unseen counts as
     flipped (Counter Blast) and flips back face up once seen again. A card back showing where it
     was (CardBackTracker) confirms the flip, so then it needn't wait the full faceDownAfterSeconds. */
    void UpdateFacing(TrackedCard tracked, Camera cam)
    {
        GameState gs = GameState.Instance;
        if (gs == null) return;

        if (tracked.hasPose)
        {
            gs.SetFacing(tracked.instance, CardFacing.FaceUp);
            return;
        }
        if (tracked.instance.zone != "Damage" || tracked.instance.facing == CardFacing.FaceDown) return;

        float unseen = Time.time - tracked.lastSeenTime;
        if (unseen > faceDownAfterSeconds || (unseen > faceDownWithBackSeconds && CardBackWhereItWas(tracked, cam)))
            gs.SetFacing(tracked.instance, CardFacing.FaceDown);
    }

    // Vuforia leaves a lost target at its last pose, so that's where the card was
    bool CardBackWhereItWas(TrackedCard tracked, Camera cam)
    {
        if (CardBackTracker.Instance == null || cam == null) return false;
        Vector3 screen = cam.WorldToScreenPoint(tracked.target.transform.position);
        if (screen.z <= 0f) return false;
        return CardBackTracker.Instance.IsBackAt(cam, screen, 0.5f * ScreenExtent(cam, tracked.target, screen));
    }

    void RefreshOverlayVisibility(TrackedCard tracked)
    {
        tracked.overlay.SetActive(tracked.hasPose && tracked.insideBorder && tracked.instance != null && tracked.duplicateOf == null);
    }

    // Whether the card's on-screen center is inside one of the board's zone borders
    bool IsInsideBorder(TrackedCard tracked, Camera cam)
    {
        if (!scanOnlyInsideZones || board == null) return true;
        if (cam == null) return false;

        Vector3 screen = cam.WorldToScreenPoint(tracked.target.transform.position);
        return screen.z > 0f && board.ZoneAtScreenPoint(screen, cam, tracked.currentZoneKey) != null;
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

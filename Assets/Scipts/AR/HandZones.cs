using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

/* Hand gestures: a Next Phase button drawn over the camera view (off unless useButtons), and the
 turn player's Deck zone. A few times a second the camera's own picture (the webcam Vuforia reads, so none of
 the overlays drawn on top) is checked; an area that moves away from how it usually looks has a
 hand over it. Holding a hand on one button for holdSeconds runs it. In the Draw
 Phase, or after an Energy Blast, a hand going onto the deck and away again counts as the draw: the camera can't see one card
 leave a face-down deck, but it can see the hand. CardBackTracker losing and finding the deck's
 card back counts the same. In the Ride Phase a hand going onto the ride deck and away is a card
 taken from it, so the ride counts as from the ride deck (RulesEngine.RideDeckTouched). H shows what the check sees. Added by UISetup. Its UI is built in HandZones.UI.cs. */
public partial class HandZones : MonoBehaviour
{
    [Tooltip("Show the hand buttons (Next Phase, attack targets). Off: only the hand-on-deck draw is watched.")]
    public bool useButtons = false;
    [Tooltip("After a trigger, its owner's circles light up; a finger held on one gives it the trigger's power, critical or stand.")]
    public bool useTriggerPicks = true;
    [Tooltip("Seconds a finger must stay on a circle to pick it.")]
    public float pickHoldSeconds = 1f;
    [Range(0.05f, 1f)] public float pickCoveredFraction = 0.2f; // a finger covers less of a circle than a hand does a button
    [Tooltip("More of a circle than this changed is a hand or arm lying over it, not a fingertip: its hold starts over.")]
    [Range(0.1f, 1f)] public float fingerMaxFraction = 0.7f;

    public static HandZones Instance { get; private set; }
    [Tooltip("Seconds a hand must stay on a button before it runs.")]
    public float holdSeconds = 0.8f;
    [Tooltip("Shortest hand-on-deck that counts as a draw, so a hand passing over it doesn't.")]
    public float minDeckTouchSeconds = 0.4f;
    [Tooltip("How far apart in seconds the hand on the deck and its card back going out of view may be and still be one draw.")]
    public float deckTouchTolerance = 0.5f;
    [Tooltip("Covered longer than this isn't a gesture (e.g. something left on it); it does nothing until it clears.")]
    public float maxCoverSeconds = 8f;
    [Tooltip("An area that's changed but stays perfectly still this long (a card placed, rested or flipped there) takes that as its normal look. Keep it above the hold times.")]
    public float settleSeconds = 2f;
    public float sampleInterval = 0.066f;
    [Range(0.02f, 0.5f)] public float cellThreshold = 0.12f; // colour change (0-1) for one cell to count as covered
    [Range(0.1f, 1f)] public float coveredFraction = 0.45f; // of an area's cells, to count as covered
    [Range(0f, 1f)] public float clearFraction = 0.2f; // below this it's uncovered again
    [Tooltip("Button row across the top of the camera view, as fractions of it (x, y from bottom-left, width, height). Keep it off the mat.")]
    public Rect buttonRow = new Rect(0.2f, 0.86f, 0.6f, 0.12f);
    [Tooltip("Shows what the hand check sees, with each area's change. The picture should match the camera view.")]
    public Key previewKey = Key.H;
    [Tooltip("Turn on if the preview (H) shows the picture upside down.")]
    public bool flipPicture;

    const int CaptureWidth = 256, CaptureHeight = 144; // fallback render, the camera view's 16:9
    const int SamplesPerCellSide = 8; // pixels read across and down each cell, however big the picture
    const int Grid = 5; // cells per side of an area
    const int GlobalCols = 16, GlobalRows = 9; // whole-picture cells, to tell a hand from the camera moving
    const float Adapt = 0.05f; // how fast an uncovered area's normal look follows slow light changes
    const float ArmSeconds = 0.6f;
    const float RelearnSeconds = 1f, MaxRelearnSeconds = 4f; // a lit circle quiet this long, or this long lit at most
    static readonly int Slots = RulesEngine.FrontRow.Length; // the row: Next Phase in the middle, or the three attack targets

    class Area
    {
        public bool isNextPhase;
        public string targetZone; // an attack target button: the defender's circle it picks
        public string pickKey; // a trigger pick: the board zone key of the circle, e.g. "P1_LeftFront"
        public string pickZone; // and its circle, e.g. "LeftFront"
        public bool isDeck;
        public bool isRideDeck;
        public Rect view; // fraction of the camera view, origin bottom-left
        public Color[] baseline; // normal look per cell; null until the first picture
        public float baselineSince;
        public bool covered;
        public float coveredSince;
        public bool fired; // ran during this cover; it has to clear before running again
        public bool shown;
        public float changed; // last fraction of cells away from the normal look, for the preview
        public Color[] lastCells; // the previous sample, to tell a still card from a moving hand
        public Color[] previousBaseline; // the normal look before the last settle, in case what settled leaves again
        public float shownSince; // Time.time it last lit up
        public float stillSince; // Time.time it stopped changing between samples
        public bool relearn; // a circle just lit: its look is learnt again until it's quiet (Check)
        public RectTransform ui;
        public RectTransform fill;
        public TextMeshProUGUI label;
        public RectTransform previewFrame;
        public TextMeshProUGUI previewText;
    }

    // A picture to check: pixels bottom-up, and the part of it the camera view shows
    class Picture
    {
        public Color32[] pixels;
        public int width, height;
        public Rect visible; // fraction of the picture; Vuforia crops the camera picture to the view's shape
        public bool flipY;
    }

    readonly List<Area> mAreas = new List<Area>();
    Area mDeck;
    Area mRideDeck;
    Color[] mGlobalBaseline;
    int mDeckDrawTurn = -1; // turn whose draw the deck touch already counted
    float mDrawWatchStart = float.PositiveInfinity; // Time.time the deck started being watched this Draw Phase
    Vector2? mHandTouch; // this Draw Phase's latest hand-on-deck (start, end), from the picture
    Vector2? mBackTouch; // and its latest card-back-hidden (start, end), from CardBackTracker
    string mDeckKey;

    Camera mCamera;
    BoardLayout mBoard;
    RulesEngine mRules;
    RenderTexture mCapture;
    bool mReadingBack;
    float mNextSample;
    RectTransform mCanvas;

    WebCamTexture mWebcam;
    float mNextWebcamLookup;

    GameObject mPreview;
    RawImage mPreviewImage;

    void Start()
    {
        BuildCanvas();

        Area nextPhase = new Area { isNextPhase = true, view = ButtonSlot(Slots / 2) };
        BuildButton(nextPhase, "Next\nPhase");
        mAreas.Add(nextPhase);

        // While an attack's target is being picked, the row's slots pick it instead
        string[] targetLabels = { "Attack\nLeft RG", "Attack\nVanguard", "Attack\nRight RG" };
        for (int i = 0; i < Slots; i++)
        {
            Area area = new Area { targetZone = RulesEngine.FrontRow[i], view = ButtonSlot(i) };
            BuildButton(area, targetLabels[i]);
            mAreas.Add(area);
        }

        // Every unit circle of each player's field this camera shows, lit up only while a trigger is being placed
        foreach (string prefix in new[] { "P1", "P2" })
            foreach (string zone in new[] { "LeftFront", "Vanguard", "RightFront", "LeftBack", "CenterBack", "RightBack" })
            {
                Area pick = new Area { pickKey = prefix + "_" + zone, pickZone = zone };
                BuildPickFrame(pick);
                mAreas.Add(pick);
            }

        mDeck = new Area { isDeck = true };
        BuildDeckFrame(mDeck, "Draw\nfrom here");
        mAreas.Add(mDeck);

        mRideDeck = new Area { isRideDeck = true };
        BuildDeckFrame(mRideDeck, "Ride\nfrom here");
        mAreas.Add(mRideDeck);

        BuildPreview();
    }

    Rect ButtonSlot(int i)
    {
        float width = buttonRow.width / Slots;
        return new Rect(buttonRow.x + i * width + width * 0.06f, buttonRow.y, width * 0.88f, buttonRow.height);
    }

    void Awake() => Instance = this;

    void OnEnable() => CardBackTracker.OnDeckTouched += OnDeckBackTouched;

    void OnDisable() => CardBackTracker.OnDeckTouched -= OnDeckBackTouched;

    void OnDestroy()
    {
        if (mCapture != null) mCapture.Release();
    }

    void OnDeckBackTouched(string zoneKey, float hiddenSince)
    {
        if (zoneKey != mDeckKey || mDeck == null || !mDeck.shown) return;
        mBackTouch = new Vector2(hiddenSince, Time.time);
        TryCountDraw();
    }

    /* The draw counts only for a touch that starts after the Draw Phase does. When the deck's card
     back is being followed, it needs both the hand over the deck and the card back going out of view
     at about the same time: either alone is also what glare or Vuforia losing the card looks like.
     Without the card back (not tracked), the hand alone counts. */
    void TryCountDraw()
    {
        bool hand = mHandTouch is Vector2 h && h.x >= mDrawWatchStart;
        bool back = mBackTouch is Vector2 b && b.x >= mDrawWatchStart;

        CardBackTracker backs = CardBackTracker.Instance;
        bool backFollowed = backs != null && Time.time - backs.DeckLastSeen(mDeckKey) < 10f;

        bool backHidden = hand && backs != null &&
            backs.DeckHiddenDuring(mDeckKey, mHandTouch.Value.x - deckTouchTolerance, mHandTouch.Value.y + deckTouchTolerance);
        if (backFollowed ? hand && (backHidden || (back && Overlap(mHandTouch.Value, mBackTouch.Value))) : hand)
            DrawFromDeck(backFollowed ? "hand over the deck and its card back hidden" : "hand over the deck");
    }

    bool Overlap(Vector2 a, Vector2 b) => a.x <= b.y + deckTouchTolerance && b.x <= a.y + deckTouchTolerance;

    void Update()
    {
        if (mCamera == null) mCamera = Camera.main;
        if (mBoard == null) mBoard = FindFirstObjectByType<BoardLayout>();
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();
        GameState gs = GameState.Instance;
        if (mCamera == null || gs == null || gs.players[0] == null) return;

        UpdateDeckArea(gs);
        UpdatePickAreas();
        foreach (Area area in mAreas)
        {
            // A hand already on a button as it appears (e.g. the one that just picked a target) must lift first
            bool shown = IsShown(area, gs);
            if (shown && !area.shown)
            {
                area.shownSince = Time.time;
                if (area.pickZone != null)
                {
                    // Its look from now on, e.g. with the attacker just rested on it, not from before
                    area.relearn = true;
                    area.baseline = null;
                    area.covered = false;
                }
                else if (area.covered) area.fired = true;
            }
            if (area.isDeck && shown && !area.shown) StartDrawWatch();
            area.shown = shown;
            PlaceUI(area);
        }

        if (Keyboard.current != null && Keyboard.current[previewKey].wasPressedThisFrame) mPreview.SetActive(!mPreview.activeSelf);

        if (!mReadingBack && Time.time >= mNextSample)
        {
            mNextSample = Time.time + sampleInterval;
            Sample();
        }
        UpdatePreview();
    }

    /* Next Phase only on your turn; the deck only while your Draw Phase, Energy Blast or skill draw is still
     to come; the attack target buttons, in place of the usual ones, while your attack needs a target */
    bool IsShown(Area area, GameState gs)
    {
        if (gs.gameOver) return false; // nothing more to do once it's over
        bool yourTurn = MatchSession.IsLocal(gs.CurrentPlayer);
        if (area.isDeck)
            return yourTurn && area.view.width > 0f &&
                ((gs.currentPhase == TurnPhase.DrawPhase && !DrewThisTurn(gs)) || ExtraDrawDue);

        // The ride deck while this turn's ride is still to come, once its discard is in the drop zone
        if (area.isRideDeck) return yourTurn && area.view.width > 0f && mRules != null && mRules.RideDeckRideDue && !mRules.RideDiscardDue;

        if (area.pickZone != null)
        {
            if (!useTriggerPicks || mRules == null || area.view.width <= 0f) return false;

            // Online, picking an attack target: your own front row, each circle standing for the one it faces
            if (mRules.IsChoosingTarget)
            {
                if (!MatchSession.IsOnline || !yourTurn || !area.pickKey.StartsWith("P1")) return false;
                string target = AttackTargetPicker.Facing(area.pickZone);
                return target != null && (target == "Vanguard" || gs.Opponent.CardOnCircle(target) != null);
            }

            if (mRules.PickNow == RulesEngine.TriggerPick.None) return false;
            PlayerState owner = mBoard != null ? mBoard.ZoneOwner(gs, area.pickKey) : null;
            return owner == mRules.PickOwner && MatchSession.IsLocal(owner) && mRules.PickZones().Contains(area.pickZone);
        }

        if (!useButtons) return false;
        bool picking = yourTurn && mRules != null && mRules.IsChoosingTarget;
        if (area.targetZone != null)
            return picking && (area.targetZone == "Vanguard" || gs.Opponent.CardOnCircle(area.targetZone) != null);
        return yourTurn && !picking;
    }

    // The unit circles, projected through the camera; a circle that moves (calibration) starts its normal look over
    void UpdatePickAreas()
    {
        foreach (Area area in mAreas)
        {
            if (area.pickKey == null) continue;
            Rect view = Rect.zero;
            if (mBoard != null) mBoard.TryGetZoneViewRect(area.pickKey, mCamera, out view);
            if (Vector2.Distance(view.center, area.view.center) > 0.01f || Mathf.Abs(view.width - area.view.width) > 0.01f)
            {
                area.view = view;
                area.baseline = null;
            }
        }
    }

    // The turn player's Deck and Ride Deck zones, projected through the camera
    void UpdateDeckArea(GameState gs)
    {
        mDeckKey = TurnPlayerKey(gs, "Deck");
        Follow(mDeck, mDeckKey);
        Follow(mRideDeck, TurnPlayerKey(gs, "RideDeck"));
    }

    string TurnPlayerKey(GameState gs, string zone) => mBoard == null ? null
        : mBoard.boardMode == BoardLayout.BoardMode.Multiplayer ? "P1_" + zone : "P" + (gs.currentPlayerIndex + 1) + "_" + zone;

    // Puts the area on the zone; its normal look restarts when the zone moves
    void Follow(Area area, string key)
    {
        Rect view = Rect.zero;
        if (mBoard != null) mBoard.TryGetZoneViewRect(key, mCamera, out view);

        if (Vector2.Distance(view.center, area.view.center) > 0.01f || Mathf.Abs(view.width - area.view.width) > 0.01f)
        {
            area.view = view;
            area.baseline = null;
        }
    }

    /* The webcam's own picture when there is one: the board's zone lines, card overlays and labels
     drawn over it would otherwise look like changes. Otherwise (e.g. a build with another camera
     path) the AR camera rendered small, overlays and all. */
    void Sample()
    {
        if ((mWebcam == null || !mWebcam.isPlaying) && Time.time >= mNextWebcamLookup)
        {
            mWebcam = Resources.FindObjectsOfTypeAll<WebCamTexture>().FirstOrDefault(t => t.isPlaying);
            mNextWebcamLookup = Time.time + 1f;
        }

        /* The webcam picture shrunk on the graphics card to the part the camera view shows, then read
         back without stalling: copying all of a 1080p picture every sample made the game stutter */
        if (mWebcam != null && mWebcam.isPlaying && mWebcam.width > 16)
        {
            if (mCapture == null) mCapture = new RenderTexture(CaptureWidth, CaptureHeight, 24);
            Rect vis = VisiblePart(mWebcam.width, mWebcam.height);
            bool flip = mWebcam.videoVerticallyMirrored != flipPicture;
            Graphics.Blit(mWebcam, mCapture, new Vector2(vis.width, flip ? -vis.height : vis.height), new Vector2(vis.x, flip ? vis.yMax : vis.y));
            ReadBack(false);
            return;
        }

        RenderCapture();
    }

    // The small capture, read back and checked once it arrives
    void ReadBack(bool flipY)
    {
        mReadingBack = true;
        AsyncGPUReadback.Request(mCapture, 0, TextureFormat.RGBA32, done =>
        {
            mReadingBack = false;
            if (this == null || done.hasError) return;
            Analyse(new Picture
            {
                pixels = done.GetData<Color32>().ToArray(),
                width = CaptureWidth,
                height = CaptureHeight,
                visible = new Rect(0f, 0f, 1f, 1f),
                flipY = flipY,
            });
        });
    }

    // Vuforia fills the camera's box with the picture, cropping whichever sides don't fit its shape
    Rect VisiblePart(int width, int height)
    {
        float view = mCamera.pixelWidth / (float)Mathf.Max(1, mCamera.pixelHeight);
        float picture = width / (float)height;
        float w = picture > view ? view / picture : 1f;
        float h = picture > view ? 1f : picture / view;
        return new Rect((1f - w) * 0.5f, (1f - h) * 0.5f, w, h);
    }

    void RenderCapture()
    {
        if (mCapture == null) mCapture = new RenderTexture(CaptureWidth, CaptureHeight, 24);

        Rect rect = mCamera.rect;
        mCamera.rect = new Rect(0f, 0f, 1f, 1f);
        var request = new RenderPipeline.StandardRequest { destination = mCapture };
        if (RenderPipeline.SupportsRenderRequest(mCamera, request))
            RenderPipeline.SubmitRenderRequest(mCamera, request);
        else
        {
            RenderTexture target = mCamera.targetTexture; // RotatedView's, when the view is turned
            mCamera.targetTexture = mCapture;
            mCamera.Render();
            mCamera.targetTexture = target;
        }
        mCamera.rect = rect;
        ReadBack(flipPicture);
    }

    void Analyse(Picture picture)
    {
        /* The whole picture first: camera exposure shifts everything a little (taken out of each
         area's change), and the camera moving changes most of it, which isn't a hand */
        Color[] global = Cells(picture, new Rect(0f, 0f, 1f, 1f), GlobalCols, GlobalRows);
        Color shift = Color.clear;
        if (mGlobalBaseline != null)
        {
            shift = Mean(global) - Mean(mGlobalBaseline);
            if (ChangedFraction(global, mGlobalBaseline, shift) > 0.6f)
            {
                mGlobalBaseline = global;
                foreach (Area area in mAreas) ResetArea(area);
                return;
            }
        }
        mGlobalBaseline = Blend(mGlobalBaseline, global, Adapt);

        foreach (Area area in mAreas)
            if (area.view.width > 0f) Check(area, Cells(picture, area.view, Grid, Grid), shift);

        /* One hand covers one slot, maybe two. The whole row changing together is the light or
         something moving under it, so the row starts over from how it looks now. */
        if (mAreas.Where(a => IsButton(a) && a.covered).Select(a => a.view.center).Distinct().Count() >= Slots)
            foreach (Area area in mAreas.Where(IsButton)) ResetArea(area);
    }

    void Check(Area area, Color[] cells, Color shift)
    {
        float now = Time.time;
        if (area.baseline == null)
        {
            area.baseline = cells;
            area.baselineSince = now;
            area.changed = 0f;
            return;
        }

        float changed = ChangedFraction(cells, area.baseline, shift);
        area.changed = changed;
        bool armed = now - area.baselineSince >= ArmSeconds;

        /* Covered but not moving: a card put down, rested or flipped there, not a hand, which never
         keeps that still for long. Its look now becomes the normal one, so the area works again. */
        bool still = area.lastCells != null && ChangedFraction(cells, area.lastCells, Color.clear) == 0f;
        if (!still) area.stillSince = now;
        area.lastCells = cells;

        /* A circle just lit: what's still moving there, e.g. the hand that rested the attacker and the
         card it turned, isn't a finger. Its look is learnt again until it's been quiet for a moment. */
        if (area.relearn)
        {
            area.covered = false;
            if (!still)
            {
                area.baseline = (Color[])cells.Clone();
                area.baselineSince = now;
            }
            if (now - area.baselineSince >= RelearnSeconds || now - area.shownSince >= MaxRelearnSeconds) area.relearn = false;
            return;
        }

        /* Not while lit if it was covered after lighting up: that's an arm resting there. Covered from
         before (e.g. the attacker's card just rested) it settles all the same. */
        bool maySettle = !area.shown || area.coveredSince < area.shownSince;
        if (area.covered && still && maySettle && now - area.stillSince >= settleSeconds)
        {
            area.previousBaseline = area.baseline;
            area.baseline = (Color[])cells.Clone();
            area.baselineSince = now - ArmSeconds; // armed straight away
            area.covered = false;
            return;
        }

        if (!area.covered)
        {
            if (armed && changed >= (area.pickZone != null ? pickCoveredFraction : coveredFraction))
            {
                // Back to the look before the last settle (an arm that stayed still has gone): normal again, not covered
                if (area.previousBaseline != null && ChangedFraction(cells, area.previousBaseline, shift) <= clearFraction)
                {
                    area.baseline = area.previousBaseline;
                    area.previousBaseline = null;
                    return;
                }
                area.covered = true;
                area.coveredSince = now;
                area.fired = false;
            }
            else area.baseline = Blend(area.baseline, cells, Adapt);
            return;
        }

        // A circle clears lower than it covers, or a fingertip near the line flickers and its hold keeps starting over
        if (changed <= (area.pickZone != null ? Mathf.Min(clearFraction, pickCoveredFraction * 0.5f) : clearFraction))
        {
            area.covered = false;
            // Deck: on and off again, one half of the draw (TryCountDraw)
            float held = now - area.coveredSince;
            if (area.isDeck && area.shown && held >= minDeckTouchSeconds && held < maxCoverSeconds)
            {
                mHandTouch = new Vector2(area.coveredSince, now);
                TryCountDraw();
            }
            // Ride deck: on and off again, a card taken off it to ride
            if (area.isRideDeck && area.shown && held >= minDeckTouchSeconds && held < maxCoverSeconds) mRules?.RideDeckTouched();
            return;
        }

        if (now - area.coveredSince >= maxCoverSeconds) area.fired = true;

        // A finger on a lit circle: the arm reaching it covers circles on the way, so the one furthest out is the fingertip
        if (area.pickZone != null)
        {
            if (changed > fingerMaxFraction) area.coveredSince = now; // a hand or arm over it: the hold starts over
            if (area.shown && !area.fired && now - area.coveredSince >= pickHoldSeconds && IsFingertip(area))
            {
                /* Every circle under the arm has to clear too, or the next pick (a Critical trigger's power)
                 goes to the one the arm crossed as the hand pulls back */
                foreach (Area a in mAreas)
                    if (a.pickZone != null && a.covered) a.fired = true;
                if (mRules != null && mRules.IsChoosingTarget) mRules.ChooseTarget(AttackTargetPicker.Facing(area.pickZone));
                else mRules?.Pick(area.pickZone);
            }
            return;
        }

        if (IsButton(area) && area.shown && !area.fired && now - area.coveredSince >= holdSeconds && !OtherButtonCovered(area))
        {
            area.fired = true;
            if (area.targetZone != null) mRules?.ChooseTarget(area.targetZone);
            else if (area.isNextPhase) mRules?.AdvancePhase();
        }
    }

    // Another button slot covered at the same time: not one deliberate hand, so neither runs
    bool OtherButtonCovered(Area area) =>
        mAreas.Any(a => IsButton(a) && a.covered && a.view.center != area.view.center);

    static bool IsButton(Area area) => !area.isDeck && !area.isRideDeck && area.pickZone == null;

    bool IsFingertip(Area area) =>
        !mAreas.Any(a => a != area && a.pickZone != null && a.shown && a.covered && a.changed <= fingerMaxFraction && Reach(a) > Reach(area));

    // How far a finger hold on a circle has got (0 to 1), e.g. for AttackTargetPicker to show
    public float PickProgress(string pickKey)
    {
        Area area = mAreas.FirstOrDefault(a => a.pickKey == pickKey);
        if (area == null || !area.shown || !area.covered || area.fired || !IsFingertip(area)) return 0f;
        return Mathf.Clamp01((Time.time - area.coveredSince) / pickHoldSeconds);
    }

    /* How far a circle is from its player's edge of the board, which is where their arm comes in.
     Online the field runs up the view from the player at the bottom; Over The Board, P1 sits at
     the left edge and P2 at the right. */
    float Reach(Area area)
    {
        Vector2 c = area.view.center;
        if (mBoard == null || mBoard.boardMode == BoardLayout.BoardMode.Multiplayer) return c.y;
        return area.pickKey.StartsWith("P1") ? c.x : 1f - c.x;
    }

    // A new Draw Phase: only touches from now on count
    void StartDrawWatch()
    {
        mDrawWatchStart = Time.time;
        mHandTouch = null;
        mBackTouch = null;
    }

    // A draw outside the Draw Phase: the Energy Blast's, or a skill's
    bool ExtraDrawDue => mRules != null && (mRules.EnergyDrawDue || mRules.SkillDrawDue);

    // Once per Draw Phase; an Energy Blast or skill draw clears ExtraDrawDue as it's counted
    void DrawFromDeck(string seen)
    {
        GameState gs = GameState.Instance;
        if (!ExtraDrawDue)
        {
            if (DrewThisTurn(gs)) return;
            mDeckDrawTurn = gs.turnNumber;
        }
        Debug.Log("[HandZones] Draw counted: the camera saw the " + seen + ".");
        UISetup.Draw(gs.CurrentPlayer);
    }

    // The rules' own record, which Undo puts back, so an undone draw is watched for again
    bool DrewThisTurn(GameState gs) => mRules != null ? mRules.DrewThisTurn : mDeckDrawTurn == gs.turnNumber;

    static void ResetArea(Area area)
    {
        area.baseline = null;
        area.covered = false;
    }

    /* Average colour of each cell of a cols x rows grid over part of the camera view, reading at
     most SamplesPerCellSide pixels across and down each cell */
    static Color[] Cells(Picture picture, Rect view, int cols, int rows)
    {
        var cells = new Color[cols * rows];
        Rect vis = picture.visible;
        float x0 = (vis.x + Mathf.Clamp01(view.xMin) * vis.width) * picture.width;
        float x1 = (vis.x + Mathf.Clamp01(view.xMax) * vis.width) * picture.width;
        float y0 = (vis.y + Mathf.Clamp01(view.yMin) * vis.height) * picture.height;
        float y1 = (vis.y + Mathf.Clamp01(view.yMax) * vis.height) * picture.height;
        float cellW = (x1 - x0) / cols, cellH = (y1 - y0) / rows;

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            float r = 0f, g = 0f, b = 0f;
            int n = 0;
            for (int sy = 0; sy < SamplesPerCellSide; sy++)
            for (int sx = 0; sx < SamplesPerCellSide; sx++)
            {
                int x = Mathf.Clamp((int)(x0 + (col + (sx + 0.5f) / SamplesPerCellSide) * cellW), 0, picture.width - 1);
                int y = Mathf.Clamp((int)(y0 + (row + (sy + 0.5f) / SamplesPerCellSide) * cellH), 0, picture.height - 1);
                if (picture.flipY) y = picture.height - 1 - y;

                Color32 c = picture.pixels[y * picture.width + x];
                r += c.r;
                g += c.g;
                b += c.b;
                n++;
            }
            cells[row * cols + col] = new Color(r / n / 255f, g / n / 255f, b / n / 255f);
        }
        return cells;
    }

    float ChangedFraction(Color[] cells, Color[] baseline, Color shift)
    {
        int changed = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            Color d = cells[i] - baseline[i] - shift;
            if ((Mathf.Abs(d.r) + Mathf.Abs(d.g) + Mathf.Abs(d.b)) / 3f > cellThreshold) changed++;
        }
        return (float)changed / cells.Length;
    }

    static Color Mean(Color[] cells)
    {
        Color sum = Color.clear;
        foreach (Color c in cells) sum += c;
        return sum / cells.Length;
    }

    static Color[] Blend(Color[] baseline, Color[] cells, float t)
    {
        if (baseline == null) return cells;
        for (int i = 0; i < cells.Length; i++) baseline[i] = Color.Lerp(baseline[i], cells[i], t);
        return baseline;
    }
}

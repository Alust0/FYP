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
 Phase a hand going onto the deck and away again counts as the draw: the camera can't see one card
 leave a face-down deck, but it can see the hand. CardBackTracker losing and finding the deck's
 card back counts the same. H shows what the check sees. Added by UISetup. */
public class HandZones : MonoBehaviour
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
    static readonly int Slots = RulesEngine.FrontRow.Length; // the row: Next Phase in the middle, or the three attack targets

    class Area
    {
        public bool isNextPhase;
        public string targetZone; // an attack target button: the defender's circle it picks
        public string pickKey; // a trigger pick: the board zone key of the circle, e.g. "P1_LeftFront"
        public string pickZone; // and its circle, e.g. "LeftFront"
        public bool isDeck;
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
        BuildDeckFrame(mDeck);
        mAreas.Add(mDeck);

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
                if (area.covered) area.fired = true;
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

    /* Next Phase only on your turn; the deck only while your draw is still to come; the attack
     target buttons, in place of the usual ones, while your attack needs a target */
    bool IsShown(Area area, GameState gs)
    {
        bool yourTurn = MatchSession.IsLocal(gs.CurrentPlayer);
        if (area.isDeck)
            return yourTurn && gs.currentPhase == TurnPhase.DrawPhase && mDeckDrawTurn != gs.turnNumber && area.view.width > 0f;

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

    // The turn player's Deck zone, projected through the camera; its normal look restarts when it moves
    void UpdateDeckArea(GameState gs)
    {
        Rect view = Rect.zero;
        string key = mBoard == null ? null
            : mBoard.boardMode == BoardLayout.BoardMode.Multiplayer ? "P1_Deck" : "P" + (gs.currentPlayerIndex + 1) + "_Deck";
        mDeckKey = key;
        if (mBoard != null) mBoard.TryGetZoneViewRect(key, mCamera, out view);

        if (Vector2.Distance(view.center, mDeck.view.center) > 0.01f || Mathf.Abs(view.width - mDeck.view.width) > 0.01f)
        {
            mDeck.view = view;
            mDeck.baseline = null;
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
            mCamera.targetTexture = mCapture;
            mCamera.Render();
            mCamera.targetTexture = null;
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

        if (changed <= clearFraction)
        {
            area.covered = false;
            // Deck: on and off again, one half of the draw (TryCountDraw)
            float held = now - area.coveredSince;
            if (area.isDeck && area.shown && held >= minDeckTouchSeconds && held < maxCoverSeconds)
            {
                mHandTouch = new Vector2(area.coveredSince, now);
                TryCountDraw();
            }
            return;
        }

        if (now - area.coveredSince >= maxCoverSeconds) area.fired = true;

        // A finger on a lit circle: the arm reaching it covers circles on the way, so the one furthest out is the fingertip
        if (area.pickZone != null)
        {
            if (changed > fingerMaxFraction) area.coveredSince = now; // a hand or arm over it: the hold starts over
            if (area.shown && !area.fired && now - area.coveredSince >= pickHoldSeconds && IsFingertip(area))
            {
                area.fired = true;
                if (mRules != null && mRules.IsChoosingTarget) mRules.ChooseTarget(AttackTargetPicker.Facing(area.pickZone));
                else mRules?.Pick(area.pickZone);
            }
            return;
        }

        if (!area.isDeck && area.shown && !area.fired && now - area.coveredSince >= holdSeconds && !OtherButtonCovered(area))
        {
            area.fired = true;
            if (area.targetZone != null) mRules?.ChooseTarget(area.targetZone);
            else if (area.isNextPhase) mRules?.AdvancePhase();
        }
    }

    // Another button slot covered at the same time: not one deliberate hand, so neither runs
    bool OtherButtonCovered(Area area) =>
        mAreas.Any(a => IsButton(a) && a.covered && a.view.center != area.view.center);

    static bool IsButton(Area area) => !area.isDeck && area.pickZone == null;

    bool IsFingertip(Area area) =>
        !mAreas.Any(a => a != area && a.pickZone != null && a.shown && a.covered && a.changed <= fingerMaxFraction && Reach(a) > Reach(area));

    // How far a finger hold on a circle has got (0 to 1), e.g. for AttackTargetPicker to show
    public float PickProgress(string pickKey)
    {
        Area area = mAreas.FirstOrDefault(a => a.pickKey == pickKey);
        if (area == null || !area.shown || !area.covered || area.fired) return 0f;
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

    // Once per Draw Phase
    void DrawFromDeck(string seen)
    {
        GameState gs = GameState.Instance;
        if (mDeckDrawTurn == gs.turnNumber) return;
        mDeckDrawTurn = gs.turnNumber;
        Debug.Log("[HandZones] Draw counted: the camera saw the " + seen + ".");
        UISetup.Draw(gs.CurrentPlayer);
    }

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

    // UI: on its own overlay canvas, so it isn't in the camera picture being checked

    void BuildCanvas()
    {
        GameObject canvasObj = new GameObject("HandZonesCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -5; // under the HUD, which sits beside the camera view anyway
        canvasObj.AddComponent<GraphicRaycaster>(); // the trigger pick circles can be clicked
        mCanvas = (RectTransform)canvasObj.transform; // no scaler: one unit is one screen pixel
    }

    void BuildButton(Area area, string text)
    {
        bool target = area.targetZone != null;
        area.ui = AddRect(mCanvas, "HandButton_" + (target ? area.targetZone : "NextPhase"),
            target ? new Color(0.5f, 0.05f, 0.05f, 0.6f) : new Color(0.05f, 0.06f, 0.1f, 0.55f));
        AddFrame(area.ui, new Color(1f, 1f, 1f, 0.8f));

        area.fill = AddRect(area.ui, "Fill", new Color(0.2f, 1f, 0.4f, 0.45f));
        area.fill.anchorMin = Vector2.zero;
        area.fill.anchorMax = Vector2.one;
        area.fill.pivot = new Vector2(0f, 0.5f); // fills left to right
        area.fill.offsetMin = area.fill.offsetMax = Vector2.zero;

        area.label = AddLabel(area.ui, text);
    }

    // A lit circle for a trigger pick; also clickable, for when a finger isn't picked up
    void BuildPickFrame(Area area)
    {
        area.ui = AddRect(mCanvas, "Pick_" + area.pickKey, new Color(0.2f, 0.8f, 1f, 0.18f));
        Image background = area.ui.GetComponent<Image>();
        background.raycastTarget = true;
        Button button = area.ui.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        string zone = area.pickZone;
        button.onClick.AddListener(() => mRules?.Pick(zone));
        AddFrame(area.ui, new Color(0.2f, 0.8f, 1f, 0.95f));

        area.fill = AddRect(area.ui, "Fill", new Color(0.2f, 1f, 0.4f, 0.45f));
        area.fill.anchorMin = Vector2.zero;
        area.fill.anchorMax = Vector2.one;
        area.fill.pivot = new Vector2(0f, 0.5f);
        area.fill.offsetMin = area.fill.offsetMax = Vector2.zero;

        area.label = AddLabel(area.ui, "");
    }

    void BuildDeckFrame(Area area)
    {
        area.ui = AddRect(mCanvas, "HandDeck", new Color(1f, 0.85f, 0.2f, 0.12f));
        AddFrame(area.ui, new Color(1f, 0.85f, 0.2f, 0.95f));
        area.label = AddLabel(area.ui, "Draw\nfrom here");
    }

    /* H: the picture being checked, bottom-right of the camera view, with every live area boxed
     (green while covered) and how much of it has changed. If the picture doesn't match the camera
     view, the hand check is looking in the wrong place. */
    void BuildPreview()
    {
        mPreview = new GameObject("HandZonesPreview", typeof(RectTransform));
        mPreview.transform.SetParent(mCanvas, false);
        mPreviewImage = mPreview.AddComponent<RawImage>();
        mPreviewImage.raycastTarget = false;
        RectTransform rt = (RectTransform)mPreview.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;

        foreach (Area area in mAreas)
        {
            area.previewFrame = AddRect(rt, "Area", Color.clear);
            AddFrame(area.previewFrame, Color.white);
            area.previewText = AddLabel(area.previewFrame, "");
        }
        mPreview.SetActive(false);
    }

    void UpdatePreview()
    {
        if (mPreview == null || !mPreview.activeSelf) return;

        Rect px = mCamera.pixelRect;
        RectTransform rt = (RectTransform)mPreview.transform;
        Vector2 size = new Vector2(px.width, px.height) * 0.35f;
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(px.xMax - size.x - 8f, px.y + 8f);

        bool webcam = mWebcam != null && mWebcam.isPlaying;
        mPreviewImage.texture = webcam ? mWebcam : (Texture)mCapture;
        Rect vis = webcam ? VisiblePart(mWebcam.width, mWebcam.height) : new Rect(0f, 0f, 1f, 1f);
        bool flip = webcam ? mWebcam.videoVerticallyMirrored != flipPicture : flipPicture;
        mPreviewImage.uvRect = flip ? new Rect(vis.x, vis.yMax, vis.width, -vis.height) : vis;

        foreach (Area area in mAreas)
        {
            bool live = area.view.width > 0f && area.baseline != null;
            area.previewFrame.gameObject.SetActive(live);
            if (!live) continue;

            area.previewFrame.anchoredPosition = new Vector2(area.view.x * size.x, area.view.y * size.y);
            area.previewFrame.sizeDelta = new Vector2(area.view.width * size.x, area.view.height * size.y);
            foreach (Image edge in area.previewFrame.GetComponentsInChildren<Image>())
                if (edge.transform != area.previewFrame) edge.color = area.covered ? Color.green : Color.white;
            area.previewText.text = Mathf.RoundToInt(area.changed * 100f) + "%";
        }
    }

    void PlaceUI(Area area)
    {
        bool visible = area.shown && area.view.width > 0f && !(area.pickZone != null && mRules != null && mRules.IsChoosingTarget); // AttackTargetPicker draws those
        if (area.ui.gameObject.activeSelf != visible) area.ui.gameObject.SetActive(visible);
        if (!visible) return;

        Rect px = mCamera.pixelRect;
        area.ui.anchoredPosition = new Vector2(px.x + area.view.x * px.width, px.y + area.view.y * px.height);
        area.ui.sizeDelta = new Vector2(area.view.width * px.width, area.view.height * px.height);

        if (area.fill != null)
        {
            float hold = area.pickZone != null ? pickHoldSeconds : holdSeconds;
            float progress = area.covered && !area.fired ? (Time.time - area.coveredSince) / hold : 0f;
            area.fill.localScale = new Vector3(Mathf.Clamp01(progress), 1f, 1f);
        }

        // What this circle would get, and whether it has it now
        if (area.pickZone != null && mRules != null)
        {
            string what = mRules.PickNow == RulesEngine.TriggerPick.Critical ? "Critical +1"
                : mRules.PickNow == RulesEngine.TriggerPick.Stand ? "Stand" : "Power";
            area.label.text = what + (area.pickZone == mRules.PickCurrentZone ? "\n(on it now)" : "");
        }
    }

    static RectTransform AddRect(Transform parent, string name, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        Image image = obj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    // A thin border just inside the rect
    static void AddFrame(RectTransform parent, Color color)
    {
        const float t = 3f;
        (Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)[] sides =
        {
            (new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, t)),
            (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -t), Vector2.zero),
            (new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(t, 0f)),
            (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-t, 0f), Vector2.zero),
        };
        foreach (var side in sides)
        {
            RectTransform rt = AddRect(parent, "Edge", color);
            rt.anchorMin = side.min;
            rt.anchorMax = side.max;
            rt.offsetMin = side.offMin;
            rt.offsetMax = side.offMax;
        }
    }

    static TextMeshProUGUI AddLabel(RectTransform parent, string text)
    {
        GameObject obj = new GameObject("Label", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);

        TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8;
        label.fontSizeMax = 22;
        label.outlineWidth = 0.2f;
        label.outlineColor = Color.black;
        label.raycastTarget = false;
        return label;
    }
}

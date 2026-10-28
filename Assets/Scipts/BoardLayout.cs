using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Spawns and positions all card circles matching the official Vanguard D format playmat
public class BoardLayout : MonoBehaviour
{
    // Prefabs assigned in the Unity Inspector
    public GameObject circlePrefab;
    public GameObject zoneMarkerPrefab;

    // Card dimensions in Unity units
    public float cardWidth = 1.2f;
    public float cardHeight = 1.6f;
    public float colGap = 0.15f;
    public float rowGap = 0.15f;

    // How far each player's field is from the center line
    public float playerFieldOffset = 2.8f;

    /* Unparented (no tracked mat yet), the board sits this far in front of the AR camera so
     the near clip plane doesn't cut it out. Ignored once parented under an Image Target. */
    public float distanceFromCameraIfUnparented = 3f;

    [Header("Zone Appearance")]
    [Tooltip("Base fill color for zones — alpha 0 keeps them fully transparent so only the outline shows. Also used to reset a zone after HighlightCircle.")]
    public Color fillColor = new Color(1f, 1f, 1f, 0f);
    [Tooltip("Color of the box outline drawn around each zone.")]
    public Color outlineColor = Color.black;
    public float outlineWidth = 0.006f;
    [Tooltip("Zone name text; it shrinks to fit inside its box.")]
    public Color labelColor = Color.white;

    // Cloned per zone so HighlightCircle can tint one without affecting the rest
    static Material sZoneFillMaterialTemplate;

    // Holds all spawned GameObjects by key
    public Dictionary<string, GameObject> circles = new Dictionary<string, GameObject>();

    // Tracks which circles have a card placed on them
    private Dictionary<string, bool> hasCard = new Dictionary<string, bool>();

    /* Each zone's area in board-local XY (the board's own coordinates). Zones are spaced by
     colGap/rowGap, so these never overlap and a point is in at most one zone. */
    readonly Dictionary<string, Rect> mZoneRects = new Dictionary<string, Rect>();
    readonly Dictionary<string, Rect> mDefaultZoneRects = new Dictionary<string, Rect>(); // as spawned, for ResetZoneLayouts
    readonly HashSet<string> mHiddenZones = new HashSet<string>(); // hidden in calibration: not drawn, never matched

    [Header("Zone Detection")]
    [Tooltip("Board units a card can drift past its current zone's edge before it counts as leaving, so a card on an edge doesn't flicker between zones.")]
    public float zoneStickMargin = 0.1f;

    /* Pairs a zone key (e.g. "P1_Vanguard") with a Vuforia Image Target in the scene, so the
     zone tracks the real playmat instead of a procedural quad. Assign in the Inspector. */
    [System.Serializable]
    public class ArZoneMarker
    {
        public string key;
        public GameObject trackedObject;
    }

    [Header("AR Zone Markers (optional) — overrides the procedural quad for matching keys")]
    public List<ArZoneMarker> arZoneMarkers = new List<ArZoneMarker>();

    /* Multiplayer: only P1's half, laid out vertically (each player on their own PC and camera).
     OverTheBoard: both halves side by side horizontally on one PC, P1 on the left.
     Order matters: scenes save this as a number (0 = Multiplayer, 1 = OverTheBoard). */
    public enum BoardMode { Multiplayer, OverTheBoard }

    [Header("Layout")]
    public BoardMode boardMode = BoardMode.OverTheBoard;

    [Tooltip("Off: the Crest zone isn't drawn or matched, as no rule uses it yet.")]
    public bool showCrest = false;
    [Tooltip("Off: the Bind zone isn't drawn or matched, as no rule uses it yet.")]
    public bool showBind = false;

    bool IsHorizontal => boardMode == BoardMode.OverTheBoard;

    // In Awake so DeckTargetLoader sees the mode in its Start
    void Awake()
    {
        if (MatchSession.IsOnline) boardMode = BoardMode.Multiplayer;
    }

    // Online, this PC's own half is always drawn as P1, whichever seat it has in the match
    static int SeatOfZone(string key) => key.StartsWith("P1") ? MatchSession.LocalSeat : MatchSession.RemoteSeat;

    [Header("Fit To Camera")]
    [Tooltip("Scales the whole board (unparented preview mode only) so it fits within the camera's view instead of using raw editor units.")]
    public bool fitToCamera = true;
    [Range(0.1f, 1f)] public float cameraFillFraction = 0.9f; // fraction of the visible frame the board should occupy

    void Start()
    {
        /* Local, not world, so a board parented under a playmat Image Target follows it.
         Unparented, it rides on the AR camera with a forward offset instead: Vuforia moves
         the camera once a card is tracked, which would leave a world-fixed board out of view. */
        bool unparented = transform.parent == null;
        if (unparented && Camera.main != null)
            transform.SetParent(Camera.main.transform, false);

        transform.localPosition = unparented
            ? new Vector3(0f, 0f, distanceFromCameraIfUnparented)
            : Vector3.zero;
        transform.localRotation = Quaternion.identity;

        SpawnPlayerField(isOpponent: false); // P1 bottom half

        if (boardMode == BoardMode.OverTheBoard)
            SpawnPlayerField(isOpponent: true); // P2 top half flipped

        // Real AR-tracked zone markers take priority over the procedural quads above
        foreach (ArZoneMarker marker in arZoneMarkers)
        {
            if (marker.trackedObject == null || string.IsNullOrEmpty(marker.key)) continue;
            circles[marker.key] = marker.trackedObject;
            hasCard[marker.key] = false;
            mZoneRects.Remove(marker.key); // its area now follows the marker, see TryGetZoneRect
        }

        if (fitToCamera)
            FitToCamera();

        GameState.OnGameStateUpdated += SyncFromGameState;
    }

    /* Vuforia settles the AR camera's real FOV some time after Start(), tied to no single
     event, so re-fit whenever the FOV changes. A no-op once it stops changing. */
    float mLastFittedFov = -1f;

    void Update()
    {
        if (!fitToCamera) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        if (!Mathf.Approximately(cam.fieldOfView, mLastFittedFov))
        {
            mLastFittedFov = cam.fieldOfView;
            FitToCamera();
        }
    }

    /* Scales the board to fit what the main camera sees at its distance, then centers the
     content (not this transform's pivot) on the camera's forward axis. Multiplayer only
     spawns one side, so the content isn't symmetric around the local origin. */
    public void FitToCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        if (!TryGetSpawnedBounds(out Bounds bounds)) return;

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        float visibleHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float visibleWidth = visibleHeight * cam.aspect;

        float scaleX = (visibleWidth * cameraFillFraction) / bounds.size.x;
        float scaleY = (visibleHeight * cameraFillFraction) / bounds.size.y;
        transform.localScale *= Mathf.Min(scaleX, scaleY);

        // Re-measure post-scale, then center the content on the camera's forward axis
        if (!TryGetSpawnedBounds(out bounds)) return;

        Vector3 localCenter = cam.transform.InverseTransformPoint(bounds.center);
        transform.position += cam.transform.TransformVector(new Vector3(-localCenter.x, -localCenter.y, 0f));
    }

    // Combines the world-space Renderer bounds of every spawned zone
    bool TryGetSpawnedBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        bool hasBounds = false;

        foreach (GameObject obj in circles.Values)
        {
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend == null) continue;

            if (!hasBounds) { bounds = rend.bounds; hasBounds = true; }
            else bounds.Encapsulate(rend.bounds);
        }

        return hasBounds;
    }

    void OnDestroy()
    {
        GameState.OnGameStateUpdated -= SyncFromGameState;
    }

    /* Spawns the full field for one player:
     [ Damage ][ LB ][ CB ][ RB ][ Drop ][ OOG ]
     [ Order ][ LF ][ VG ][ RF ][ Deck ][ Ride ]
     [ Crest ][ ][ GD ][ TG ][ Bind ]
     Multiplayer runs this grid left-to-right (vertical); OverTheBoard turns it 90° (horizontal). */
    void SpawnPlayerField(bool isOpponent)
    {
        string prefix = isOpponent ? "P2" : "P1";
        float flip = isOpponent ? 1f : -1f; // P1 negative side, P2 positive side

        float stepX = cardWidth + colGap;
        float stepY = cardHeight + rowGap;

        // Distance from the center line — thirdRow closest, backRow furthest
        float thirdRowDist = playerFieldOffset - stepY;
        float frontRowDist = playerFieldOffset;
        float backRowDist = playerFieldOffset + stepY;

        // Position across the row, centered at 0
        float leftCol = -stepX;
        float centerCol = 0f;
        float rightCol = stepX;

        float leftSideCol = leftCol - stepX - 0.1f;
        float rightSideCol = rightCol + stepX + 0.1f;
        float farRightSideCol = rightSideCol + stepX + 0.1f; // OOG and Ride are one further out

        /* Maps (position across row, distance from center line) to a position. P2 is P1 turned
         180° (players face each other), and horizontal is vertical turned 90° with P1 on the left,
         so each player's Left/Right and Deck/Drop sides match their real playmat. */
        Vector3 Pos(float col, float dist) => IsHorizontal
            ? new Vector3(flip * dist, flip * col, 0f)
            : new Vector3(-flip * col, flip * dist, 0f);

        /* Zone sizes as the player sees them (across, toward them). Unit circles are squares
         the width of the mat's circles; sideways ones (Trigger) are 90% so they don't overlap Guardian. */
        Vector2 unitCircle = new Vector2(cardWidth, cardWidth);
        Vector2 cardArea = new Vector2(cardWidth, cardHeight);
        Vector2 sideways = new Vector2(cardHeight, cardWidth) * 0.9f;
        Vector2 smallArea = new Vector2(cardWidth * 0.85f, cardHeight * 0.6f);
        Vector2 smallLabel = new Vector2(cardWidth, cardHeight) * 0.6f;

        // Back row circles
        SpawnCircle(prefix + "_LeftBack", Pos(leftCol, backRowDist), "Left Back", unitCircle);
        SpawnCircle(prefix + "_CenterBack", Pos(centerCol, backRowDist), "Center Back", unitCircle);
        SpawnCircle(prefix + "_RightBack", Pos(rightCol, backRowDist), "Right Back", unitCircle);

        // Front row circles
        SpawnCircle(prefix + "_LeftFront", Pos(leftCol, frontRowDist), "Left Front", unitCircle);
        SpawnCircle(prefix + "_Vanguard", Pos(centerCol, frontRowDist), "Vanguard", unitCircle);
        SpawnCircle(prefix + "_RightFront", Pos(rightCol, frontRowDist), "Right Front", unitCircle);

        // Third row: Guardian center, Trigger right of Guardian, Bind right of Trigger
        SpawnCircle(prefix + "_Guardian", Pos(centerCol, thirdRowDist), "Guardian", unitCircle);
        SpawnCircle(prefix + "_Trigger", Pos(rightCol, thirdRowDist), "Trigger", sideways);
        if (showBind) SpawnCircle(prefix + "_Bind", Pos(rightSideCol, thirdRowDist), "Bind", cardArea);

        // Side zones
        SpawnZoneMarker(prefix + "_Damage", Pos(leftSideCol, backRowDist), "Damage", cardArea);
        SpawnZoneMarker(prefix + "_Order", Pos(leftSideCol, frontRowDist), "Order", smallArea);
        if (showCrest) SpawnZoneMarker(prefix + "_Crest", Pos(leftSideCol, thirdRowDist), "Crest", smallArea);

        SpawnZoneMarker(prefix + "_Drop", Pos(rightSideCol, backRowDist), "Drop", cardArea);
        SpawnZoneMarker(prefix + "_Deck", Pos(rightSideCol, frontRowDist), "Deck", cardArea);
        SpawnZoneMarker(prefix + "_RideDeck", Pos(farRightSideCol, frontRowDist), "Ride Deck", smallLabel);

        // Out of Game zone beside Drop
        SpawnZoneMarker(prefix + "_OOG", Pos(farRightSideCol, backRowDist), "Out of Game", smallLabel);
    }

    // Instantiates a card circle with a visible label that hides when a card is placed
    void SpawnCircle(string key, Vector3 position, string label, Vector2 playerSize) =>
        SpawnZone(circlePrefab, key, position, label, playerSize);

    // Instantiates a zone marker with a visible label
    void SpawnZoneMarker(string key, Vector3 position, string label, Vector2 playerSize) =>
        SpawnZone(zoneMarkerPrefab, key, position, label, playerSize);

    void SpawnZone(GameObject prefab, string key, Vector3 position, string label, Vector2 playerSize)
    {
        if (prefab == null) { Debug.LogWarning("Zone prefab not assigned for " + key); return; }

        GameObject obj = Instantiate(prefab, transform);
        obj.name = key;
        obj.transform.localPosition = position;
        obj.transform.localRotation = Quaternion.identity;
        Vector2 size = ZoneSize(playerSize.x, playerSize.y);
        obj.transform.localScale = new Vector3(size.x, size.y, 0.01f);

        StyleZone(obj);
        CreateLabel(obj, label);

        RegisterZone(key, obj, position, size);
    }

    // Horizontal turns the playmat 90°, so each zone's width and height swap with it
    Vector2 ZoneSize(float width, float height) => IsHorizontal
        ? new Vector2(height, width)
        : new Vector2(width, height);

    void RegisterZone(string key, GameObject obj, Vector3 position, Vector2 size)
    {
        circles[key] = obj;
        hasCard[key] = false;
        mZoneRects[key] = new Rect((Vector2)position - size * 0.5f, size);
        mDefaultZoneRects[key] = mZoneRects[key];
    }

    /* Which player a card in this zone belongs to. Over The Board, both Guardian zones sit on the
     mat's one shared circle, so a guard there belongs to the defender, not the turn player. */
    public PlayerState ZoneOwner(GameState gs, string key)
    {
        if (key == null) return null;
        if (boardMode == BoardMode.OverTheBoard && key.EndsWith("_Guardian")) return gs.Opponent;
        return gs.players[SeatOfZone(key)];
    }

    // A zone's area in board-local XY; false for AR marker zones, which have no fixed area
    public bool TryGetZoneLayout(string key, out Rect rect) => mZoneRects.TryGetValue(key, out rect);

    // A shown zone's area as fractions of the camera's view (origin bottom-left)
    public bool TryGetZoneViewRect(string key, Camera cam, out Rect view)
    {
        view = Rect.zero;
        if (key == null || cam == null || IsZoneHidden(key) || !mZoneRects.TryGetValue(key, out Rect zone)) return false;

        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector2 corner in new[] { zone.min, zone.max, new Vector2(zone.xMin, zone.yMax), new Vector2(zone.xMax, zone.yMin) })
        {
            Vector3 vp = cam.WorldToViewportPoint(transform.TransformPoint(corner));
            min = Vector2.Min(min, vp);
            max = Vector2.Max(max, vp);
        }
        view = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    // Moves/resizes one zone (BoardCalibrator), keeping its drawn box and detection area in step
    public void SetZoneLayout(string key, Rect rect)
    {
        if (!mZoneRects.ContainsKey(key) || !circles.TryGetValue(key, out GameObject obj)) return;

        mZoneRects[key] = rect;
        obj.transform.localPosition = new Vector3(rect.center.x, rect.center.y, obj.transform.localPosition.z);
        obj.transform.localScale = new Vector3(rect.width, rect.height, obj.transform.localScale.z);
        FitLabel(obj);
    }

    // Hidden zones aren't drawn and never match a card, for zones the real playmat doesn't have
    public void SetZoneHidden(string key, bool hidden)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;

        if (hidden) mHiddenZones.Add(key);
        else mHiddenZones.Remove(key);
        obj.SetActive(!hidden);
    }

    public bool IsZoneHidden(string key) => mHiddenZones.Contains(key);

    // Back to the spawned layout with every zone shown
    public void ResetZoneLayouts()
    {
        foreach (KeyValuePair<string, Rect> zone in mDefaultZoneRects)
        {
            SetZoneLayout(zone.Key, zone.Value);
            SetZoneHidden(zone.Key, false);
        }
    }

    // World direction a standing card's long side runs along; a rested card is turned 90° from it
    public Vector3 CardLengthAxis => IsHorizontal ? transform.right : transform.up;

    /* Which zone a screen point (e.g. a tracked card's center) is in. The point is projected
     onto the board plane, so it works wherever the camera is and at any scale. currentKey
     gets zoneStickMargin of slack so a card sitting on an edge keeps its zone. */
    public string ZoneAtScreenPoint(Vector2 screenPoint, Camera cam, string currentKey = null)
    {
        if (!TryScreenToBoard(screenPoint, cam, out Vector2 point)) return null;

        if (currentKey != null && !mHiddenZones.Contains(currentKey) && TryGetZoneRect(currentKey, cam, out Rect current))
        {
            Rect padded = new Rect(current.xMin - zoneStickMargin, current.yMin - zoneStickMargin,
                current.width + 2f * zoneStickMargin, current.height + 2f * zoneStickMargin);
            if (padded.Contains(point)) return currentKey;
        }

        foreach (string key in circles.Keys)
            if (!mHiddenZones.Contains(key) && TryGetZoneRect(key, cam, out Rect rect) && rect.Contains(point))
                return key;

        return null;
    }

    // Converts a screen point to board-local XY by casting it onto the board plane
    public bool TryScreenToBoard(Vector2 screenPoint, Camera cam, out Vector2 boardPoint)
    {
        boardPoint = default;
        Plane boardPlane = new Plane(transform.forward, transform.position); // zones lie in local XY
        Ray ray = cam.ScreenPointToRay(screenPoint);
        if (!boardPlane.Raycast(ray, out float enter)) return false;

        boardPoint = transform.InverseTransformPoint(ray.GetPoint(enter));
        return true;
    }

    // An AR marker zone has no fixed rect: it's a card-sized area wherever the marker shows on the board
    bool TryGetZoneRect(string key, Camera cam, out Rect rect)
    {
        if (mZoneRects.TryGetValue(key, out rect)) return true;
        if (!circles.TryGetValue(key, out GameObject obj) || obj == null) return false;

        Vector3 screen = cam.WorldToScreenPoint(obj.transform.position);
        if (screen.z <= 0f || !TryScreenToBoard(screen, cam, out Vector2 center)) return false;

        Vector2 size = ZoneSize(cardWidth, cardHeight);
        rect = new Rect(center - size * 0.5f, size);
        return true;
    }

    /* Hides a zone's fill and outlines its edges. The fill's MeshRenderer is switched off
     (not alpha-blended) so it's invisible under any pipeline/shader; HighlightCircle
     re-enables it to flag valid move targets. */
    void StyleZone(GameObject obj)
    {
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            // Sprites/Default is in Always Included Shaders, so Shader.Find still finds it in a build
            if (sZoneFillMaterialTemplate == null)
                sZoneFillMaterialTemplate = new Material(Shader.Find("Sprites/Default"));

            Material mat = new Material(sZoneFillMaterialTemplate) { color = fillColor };
            rend.material = mat;
            rend.enabled = fillColor.a > 0.001f;
        }

        LineRenderer outline = obj.AddComponent<LineRenderer>();
        outline.useWorldSpace = false;
        outline.loop = true;
        outline.positionCount = 4;
        const float z = -0.05f; // between the quad (z=0) and its label (z=-0.1), avoids z-fighting
        outline.SetPositions(new[]
        {
            new Vector3(-0.5f, -0.5f, z),
            new Vector3(-0.5f, 0.5f, z),
            new Vector3(0.5f, 0.5f, z),
            new Vector3(0.5f, -0.5f, z),
        });
        outline.widthMultiplier = outlineWidth;
        outline.material = new Material(Shader.Find("Sprites/Default"));
        outline.startColor = outlineColor;
        outline.endColor = outlineColor;
        outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outline.receiveShadows = false;
    }

    // Creates a label child on a circle or zone marker; auto-sized so it always fits its box
    void CreateLabel(GameObject parent, string labelText)
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent.transform, false);

        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        tmp.text = labelText;
        tmp.color = labelColor;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 3f;

        // Slightly in front of the quad so it renders on top
        labelObj.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        labelObj.transform.localRotation = Quaternion.identity;
        FitLabel(parent);
    }

    /* Undoes the zone's stretch so text keeps its shape, then sizes the text area to the box.
     Run again whenever the zone is resized. */
    static void FitLabel(GameObject zone)
    {
        if (!(zone.transform.Find("Label") is RectTransform label)) return;

        Vector3 zoneScale = zone.transform.localScale;
        label.localScale = new Vector3(1f / zoneScale.x, 1f / zoneScale.y, 1f);
        label.sizeDelta = new Vector2(zoneScale.x, zoneScale.y) * 0.85f;
    }

    // Call this when a card is placed on a circle — hides the label
    public void PlaceCardOnCircle(string key)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;

        hasCard[key] = true;

        // Hide the label
        Transform label = obj.transform.Find("Label");
        if (label != null) label.gameObject.SetActive(false);
    }

    // Call this when a card is removed from a circle — shows the label again
    public void RemoveCardFromCircle(string key)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;

        hasCard[key] = false;

        // Show the label again
        Transform label = obj.transform.Find("Label");
        if (label != null) label.gameObject.SetActive(true);
    }

    // Returns whether a circle currently has a card on it
    public bool CircleHasCard(string key)
    {
        return hasCard.TryGetValue(key, out bool val) && val;
    }

    // Returns the world position of a named circle
    public Vector3 GetCirclePosition(string key)
    {
        if (circles.TryGetValue(key, out GameObject obj)) return obj.transform.position;
        Debug.LogWarning("Circle not found: " + key);
        return Vector3.zero;
    }

    // Returns the GameObject of a named circle
    public GameObject GetCircleObject(string key)
    {
        circles.TryGetValue(key, out GameObject obj);
        return obj;
    }

    /* Tints a circle. The fill renderer is off by default (see StyleZone), so it's enabled
     here while the color is visible and disabled again on reset to fillColor. */
    public void HighlightCircle(string key, Color color)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend == null) return;

        rend.material.color = color;
        rend.enabled = color.a > 0.001f;
    }

    // Resets a circle back to its default transparent fill
    public void ResetCircleColor(string key)
    {
        HighlightCircle(key, fillColor);
    }

    // Highlights all valid circles for a given player action
    public void HighlightValidCircles(int playerIndex, string actionType)
    {
        string prefix = playerIndex == 0 ? "P1" : "P2";
        ResetAllCircleColors();

        switch (actionType)
        {
            case "CallUnit":
                HighlightCircle(prefix + "_LeftFront", Color.cyan);
                HighlightCircle(prefix + "_RightFront", Color.cyan);
                HighlightCircle(prefix + "_LeftBack", Color.cyan);
                HighlightCircle(prefix + "_CenterBack", Color.cyan);
                HighlightCircle(prefix + "_RightBack", Color.cyan);
                break;

            case "Ride":
                HighlightCircle(prefix + "_Vanguard", Color.yellow);
                break;

            case "Guard":
                HighlightCircle(prefix + "_Guardian", Color.green);
                break;

            case "Attack":
                HighlightCircle(prefix + "_LeftFront", Color.red);
                HighlightCircle(prefix + "_Vanguard", Color.red);
                HighlightCircle(prefix + "_RightFront", Color.red);
                break;

            case "SwitchColumn":
                HighlightCircle(prefix + "_LeftFront", Color.magenta);
                HighlightCircle(prefix + "_LeftBack", Color.magenta);
                HighlightCircle(prefix + "_Vanguard", Color.magenta);
                HighlightCircle(prefix + "_CenterBack", Color.magenta);
                HighlightCircle(prefix + "_RightFront", Color.magenta);
                HighlightCircle(prefix + "_RightBack", Color.magenta);
                break;
        }
    }

    // Resets all circle colors back to white
    public void ResetAllCircleColors()
    {
        foreach (string key in circles.Keys)
            ResetCircleColor(key);
    }

    // Syncs which zones show as occupied from the GameState (via OnGameStateUpdated)
    public void SyncFromGameState(GameState gs)
    {
        foreach (string key in new List<string>(circles.Keys))
        {
            PlayerState ps = gs.players[SeatOfZone(key)];
            if (ps == null) continue;

            SetCircleOccupied(key, ps.CountInZone(key.Substring(3)) > 0); // suffix after "P1_"/"P2_"
        }
    }

    private void SetCircleOccupied(string key, bool occupied)
    {
        if (occupied) PlaceCardOnCircle(key);
        else RemoveCardFromCircle(key);
    }
}

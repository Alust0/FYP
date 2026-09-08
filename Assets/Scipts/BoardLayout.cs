using System.Collections.Generic;
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

    // When this board has no parent (i.e. no tracked mat set up yet), it needs to sit
    // in front of the AR camera to be visible at all — the camera's near clip plane
    // otherwise clips out anything placed at its own position. Ignored once parented
    // under a tracked Image Target, since that already places the board correctly.
    public float distanceFromCameraIfUnparented = 3f;

    // Font size for zone label text
    public int labelFontSize = 14;

    // Holds all spawned GameObjects by key
    public Dictionary<string, GameObject> circles = new Dictionary<string, GameObject>();

    // Tracks which circles have a card placed on them
    private Dictionary<string, bool> hasCard = new Dictionary<string, bool>();

    // Pairs a zone key (e.g. "P1_Vanguard") with the GameObject holding that zone's
    // physical Vuforia Image Target, so zone position tracks the real playmat marker
    // instead of a procedurally placed quad. Requires the target to already exist as
    // a Vuforia database entry and be placed in the scene — assign in the Inspector.
    [System.Serializable]
    public class ArZoneMarker
    {
        public string key;
        public GameObject trackedObject;
    }

    [Header("AR Zone Markers (optional) — overrides the procedural quad for matching keys")]
    public List<ArZoneMarker> arZoneMarkers = new List<ArZoneMarker>();

    // Single player: only P1's side is spawned, useful when the camera only ever
    // needs to see one player's half of the board (e.g. each player holds their
    // own device). Multiplayer spawns both halves as before.
    public enum BoardMode { SinglePlayer, Multiplayer }

    // Vertical: P1/P2 stacked top/bottom (original layout). Horizontal: P1/P2
    // side-by-side, sharing one center line running the other direction.
    public enum LayoutOrientation { Vertical, Horizontal }

    [Header("Layout")]
    public BoardMode boardMode = BoardMode.Multiplayer;
    public LayoutOrientation layoutOrientation = LayoutOrientation.Vertical;

    [Header("Fit To Camera")]
    [Tooltip("Scales the whole board (unparented preview mode only) so it fits within the camera's view instead of using raw editor units.")]
    public bool fitToCamera = true;
    [Range(0.1f, 1f)] public float cameraFillFraction = 0.9f; // fraction of the visible frame the board should occupy

    void Start()
    {
        // Local, not world — so that if this object is parented under a tracked
        // Vuforia Image Target (the physical playmat), the whole board follows it.
        // Unparented, it needs a forward offset instead of sitting at the camera's
        // own position (which the near clip plane would otherwise cut out entirely).
        transform.localPosition = transform.parent == null
            ? new Vector3(0f, 0f, distanceFromCameraIfUnparented)
            : Vector3.zero;
        transform.localRotation = Quaternion.identity;

        SpawnPlayerField(isOpponent: false); // P1 bottom half

        if (boardMode == BoardMode.Multiplayer)
            SpawnPlayerField(isOpponent: true); // P2 top half flipped

        // Real AR-tracked zone markers take priority over the procedural quads above
        foreach (ArZoneMarker marker in arZoneMarkers)
        {
            if (marker.trackedObject == null || string.IsNullOrEmpty(marker.key)) continue;
            circles[marker.key] = marker.trackedObject;
            hasCard[marker.key] = false;
        }

        if (fitToCamera)
            FitToCamera();

        GameState.OnGameStateUpdated += SyncFromGameState;
    }

    // Vuforia finalizes the AR camera's real field of view asynchronously as its
    // camera device negotiation completes — sometime after Start(), and not
    // reliably tied to any single event. Re-fitting whenever the FOV actually
    // changes (rather than guessing when it's "ready") keeps the board correctly
    // sized regardless of when that settles; it's a no-op once FOV stops changing.
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

    // Scales the board so its total footprint fits within whatever the main camera
    // can actually see at the board's current distance, then re-centers the actual
    // content (not this transform's own pivot) onto the camera's forward axis —
    // needed because Single Player mode only spawns one player's rows, so the
    // content isn't symmetric around this object's local origin.
    void FitToCamera()
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

        // Re-measure post-scale, then shift so the content's center — not this
        // transform's pivot — lands on the camera's forward axis.
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

    // Spawns the full field for one player based on the confirmed layout
    // [ Damage ][ LB ][ CB ][ RB ][ Drop ][ OOG ]
    // [ Order ][ LF ][ VG ][ RF ][ Deck ][ Ride ]
    // [ ][ ][ GD ][ TG ][ Bind ]
    // In Vertical orientation this grid runs left-to-right with players stacked
    // top/bottom; in Horizontal it's transposed so players sit side-by-side.
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

        // Maps (position-across-row, distance-from-center-line) to a world position.
        // Vertical: rows run along Y (players stacked top/bottom), columns along X.
        // Horizontal: rows run along X (players side-by-side), columns along Y.
        Vector3 Pos(float col, float dist) => layoutOrientation == LayoutOrientation.Vertical
            ? new Vector3(col, flip * dist, 0f)
            : new Vector3(flip * dist, col, 0f);

        // Back row circles
        SpawnCircle(prefix + "_LeftBack", Pos(leftCol, backRowDist), "Left \nBack");
        SpawnCircle(prefix + "_CenterBack", Pos(centerCol, backRowDist), "Center \nBack");
        SpawnCircle(prefix + "_RightBack", Pos(rightCol, backRowDist), "Right \nBack");

        // Front row circles
        SpawnCircle(prefix + "_LeftFront", Pos(leftCol, frontRowDist), "Left \nFront");
        SpawnCircle(prefix + "_Vanguard", Pos(centerCol, frontRowDist), "Vanguard");
        SpawnCircle(prefix + "_RightFront", Pos(rightCol, frontRowDist), "Right \nFront");

        // Third row: Guardian center, Trigger right of Guardian, Bind right of Trigger
        SpawnCircle(prefix + "_Guardian", Pos(centerCol, thirdRowDist), "Guardian");
        SpawnCircle(prefix + "_Trigger", Pos(rightCol, thirdRowDist), "Trigger");
        SpawnCircle(prefix + "_Bind", Pos(rightSideCol, thirdRowDist), "Bind");

        // Side zones
        SpawnZoneMarker(prefix + "_Damage", Pos(leftSideCol, backRowDist), "Damage");
        SpawnZoneMarker(prefix + "_Order", Pos(leftSideCol, frontRowDist), "Order");

        SpawnZoneMarker(prefix + "_Drop", Pos(rightSideCol, backRowDist), "Drop");
        SpawnZoneMarker(prefix + "_Deck", Pos(rightSideCol, frontRowDist), "Deck");
        SpawnZoneMarker(prefix + "_RideDeck", Pos(farRightSideCol, frontRowDist), "Ride \nDeck");

        // Out of Game zone beside Drop
        SpawnZoneMarker(prefix + "_OOG", Pos(farRightSideCol, backRowDist), "Out \nof \nGame");
    }

    // Instantiates a card circle with a visible label that hides when a card is placed
    void SpawnCircle(string key, Vector3 position, string label)
    {
        if (circlePrefab == null) { Debug.LogWarning("circlePrefab not assigned."); return; }

        GameObject obj = Instantiate(circlePrefab, transform);
        obj.name = key;
        obj.transform.localPosition = position;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = new Vector3(cardWidth, cardHeight, 0.01f);

        // Create a label as a child TextMesh so it floats above the circle
        CreateLabel(obj, label);

        circles[key] = obj;
        hasCard[key] = false;
    }

    // Instantiates a zone marker with a visible label
    void SpawnZoneMarker(string key, Vector3 position, string label)
    {
        if (zoneMarkerPrefab == null) { Debug.LogWarning("zoneMarkerPrefab not assigned."); return; }

        GameObject obj = Instantiate(zoneMarkerPrefab, transform);
        obj.name = key;
        obj.transform.localPosition = position;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = new Vector3(cardWidth * 0.85f, cardHeight * 0.6f, 0.01f);

        CreateLabel(obj, label);

        circles[key] = obj;
        hasCard[key] = false;
    }

    // Creates a TextMesh label child on a circle or zone marker
    void CreateLabel(GameObject parent, string labelText)
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent.transform);

        // Position label slightly in front of the quad so it renders on top
        labelObj.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        labelObj.transform.localRotation = Quaternion.identity;
        labelObj.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = labelText;
        tm.fontSize = labelFontSize;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.black;
        tm.fontStyle = FontStyle.Bold;
    }

    // Call this when a card is placed on a circle — hides the label
    public void PlaceCardOnCircle(string key)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;

        hasCard[key] = true;

        // Hide the label
        TextMesh tm = obj.GetComponentInChildren<TextMesh>();
        if (tm != null) tm.gameObject.SetActive(false);
    }

    // Call this when a card is removed from a circle — shows the label again
    public void RemoveCardFromCircle(string key)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;

        hasCard[key] = false;

        // Show the label again
        TextMesh tm = obj.GetComponentInChildren<TextMesh>();
        if (tm != null) tm.gameObject.SetActive(true);
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

    // Highlights a circle by tinting it a given color
    public void HighlightCircle(string key, Color color)
    {
        if (!circles.TryGetValue(key, out GameObject obj)) return;
        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null) rend.material.color = color;
    }

    // Resets a circle back to white
    public void ResetCircleColor(string key)
    {
        HighlightCircle(key, Color.white);
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

    // Syncs which circles show as occupied based on the current GameState — called via OnGameStateUpdated
    public void SyncFromGameState(GameState gs)
    {
        foreach (PlayerState ps in gs.players)
        {
            if (ps == null) continue;
            string p = ps.CirclePrefix;

            SetCircleOccupied(p + "_Vanguard", ps.vanguardCircle != null);
            SetCircleOccupied(p + "_LeftFront", ps.frontRowRearguards[0] != null);
            SetCircleOccupied(p + "_RightFront", ps.frontRowRearguards[1] != null);
            SetCircleOccupied(p + "_LeftBack", ps.backRowRearguards[0] != null);
            SetCircleOccupied(p + "_RightBack", ps.backRowRearguards[1] != null);
            SetCircleOccupied(p + "_CenterBack", ps.centerBackRearguard != null);
            SetCircleOccupied(p + "_Guardian", ps.guardianCircle.Count > 0);
        }
    }

    private void SetCircleOccupied(string key, bool occupied)
    {
        if (occupied) PlaceCardOnCircle(key);
        else RemoveCardFromCircle(key);
    }
}

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

    // Font size for zone label text
    public int labelFontSize = 14;

    // Holds all spawned GameObjects by key
    public Dictionary<string, GameObject> circles = new Dictionary<string, GameObject>();

    // Tracks which circles have a card placed on them
    private Dictionary<string, bool> hasCard = new Dictionary<string, bool>();

    void Start()
    {
        transform.position = Vector3.zero;

        SpawnPlayerField(isOpponent: false); // P1 bottom half
        SpawnPlayerField(isOpponent: true); // P2 top half flipped

        GameState.OnGameStateUpdated += SyncFromGameState;
    }

    void OnDestroy()
    {
        GameState.OnGameStateUpdated -= SyncFromGameState;
    }

    // Spawns the full field for one player based on the confirmed layout
    // [ Damage ][ LB ][ CB ][ RB ][ Drop ][ OOG ]
    // [ Order ][ LF ][ VG ][ RF ][ Deck ][ Ride ]
    // [ ][ ][ GD ][ TG ][ Bind ]
    void SpawnPlayerField(bool isOpponent)
    {
        string prefix = isOpponent ? "P2" : "P1";
        float flip = isOpponent ? 1f : -1f; // P1 negative Y, P2 positive Y

        float stepX = cardWidth + colGap;
        float stepY = cardHeight + rowGap;

        // Y rows: guardian always closest to center line for both players
        // thirdRow = closest to center, backRow = furthest from center
        float thirdRowY = flip * (playerFieldOffset - stepY); // closest to center
        float frontRowY = flip * playerFieldOffset; // middle row
        float backRowY = flip * (playerFieldOffset + stepY); // furthest from center

        // P1 is below center (flip = -1) so closest to center = least negative
        // P2 is above center (flip = 1) so closest to center = least positive
        // No extra swap needed — thirdRowY is already closest to center for both

        // X columns centered at 0
        float leftX = -stepX;
        float centerX = 0f;
        float rightX = stepX;

        // Side X positions
        float leftSideX = leftX - stepX - 0.1f;
        float rightSideX = rightX + stepX + 0.1f;
        float farRightSideX = rightSideX + stepX + 0.1f; // OOG and Ride are one further right

        // Back row circles
        SpawnCircle(prefix + "_LeftBack", new Vector3(leftX, backRowY, 0), "Left \nBack");
        SpawnCircle(prefix + "_CenterBack", new Vector3(centerX, backRowY, 0), "Center \nBack");
        SpawnCircle(prefix + "_RightBack", new Vector3(rightX, backRowY, 0), "Right \nBack");

        // Front row circles
        SpawnCircle(prefix + "_LeftFront", new Vector3(leftX, frontRowY, 0), "Left \nFront");
        SpawnCircle(prefix + "_Vanguard", new Vector3(centerX, frontRowY, 0), "Vanguard");
        SpawnCircle(prefix + "_RightFront", new Vector3(rightX, frontRowY, 0), "Right \nFront");

        // Third row: Guardian center, Trigger right of Guardian, Bind right of Trigger
        SpawnCircle(prefix + "_Guardian", new Vector3(centerX, thirdRowY, 0), "Guardian");
        SpawnCircle(prefix + "_Trigger", new Vector3(rightX, thirdRowY, 0), "Trigger");
        SpawnCircle(prefix + "_Bind", new Vector3(rightSideX, thirdRowY, 0), "Bind");

        // Side zones
        // Left side: Damage (back row), Order (front row)
        SpawnZoneMarker(prefix + "_Damage", new Vector3(leftSideX, backRowY, 0), "Damage");
        SpawnZoneMarker(prefix + "_Order", new Vector3(leftSideX, frontRowY, 0), "Order");

        // Right side: Drop (back row), Deck (front row), Ride (front row far right)
        SpawnZoneMarker(prefix + "_Drop", new Vector3(rightSideX, backRowY, 0), "Drop");
        SpawnZoneMarker(prefix + "_Deck", new Vector3(rightSideX, frontRowY, 0), "Deck");
        SpawnZoneMarker(prefix + "_RideDeck", new Vector3(farRightSideX, frontRowY, 0), "Ride \nDeck");

        // Out of Game zone beside Drop
        SpawnZoneMarker(prefix + "_OOG", new Vector3(farRightSideX, backRowY, 0), "Out \nof \nGame");
    }

    // Instantiates a card circle with a visible label that hides when a card is placed
    void SpawnCircle(string key, Vector3 position, string label)
    {
        if (circlePrefab == null) { Debug.LogWarning("circlePrefab not assigned."); return; }

        GameObject obj = Instantiate(circlePrefab, position, Quaternion.identity, transform);
        obj.name = key;
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

        GameObject obj = Instantiate(zoneMarkerPrefab, position, Quaternion.identity, transform);
        obj.name = key;
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

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/* Lines the drawn board up with the real playmat as this PC's camera sees it, saved per PC
 (one layout per board mode). Toggle from the in-game Settings menu or the C key while playing:
  drag empty space = move board, drag a zone = move that zone,
  scroll = resize board, Shift+scroll = wider/narrower, Ctrl+scroll = taller/shorter, Q/E = rotate,
  click a zone then [ / ] = shrink/grow it (Shift = width only, Ctrl = height only), H = hide/show it. */
public class BoardCalibrator : MonoBehaviour
{
    [SerializeField] private BoardLayout board;
    [SerializeField] private Button calibrateButton; // the old on-screen button, hidden now that the Settings menu opens calibration
    [SerializeField] private Key toggleKey = Key.C;

    [Serializable]
    class SavedZone
    {
        public string key;
        public Rect rect;
        public bool hidden;
    }

    // Bump when the default zone shapes change, so layouts saved for the old shapes are ignored
    const int LayoutVersion = 2;

    [Serializable]
    class SavedLayout
    {
        public int version;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public List<SavedZone> zones = new List<SavedZone>();
    }

    static readonly Color SelectedColor = new Color(1f, 0.85f, 0f, 0.35f);
    const float PanelWidth = 380f, PanelHeight = 352f; // IMGUI space (y down), before GuiScale
    const float PanelScale = 1.7f; // at 1080p; IMGUI ignores canvas scaling, so it's sized by hand
    static float GuiScale => PanelScale * Screen.height / 1080f;

    // Top-left of the camera area, or top-right (left of the sidebar) so it doesn't cover zones there
    static bool sPanelRight;
    static Rect PanelRect => new Rect(
        sPanelRight ? Screen.width * (1f - SplitScreenView.SidebarWidth) / GuiScale - PanelWidth - 10f : 10f,
        10f, PanelWidth, PanelHeight);

    bool mCalibrating;
    Vector3 mSpawnScale;
    string mSelectedZone;
    string mDragZone; // zone being dragged, or null when dragging the whole board
    bool mDragging;
    Plane mDragPlane;
    Vector3 mLastDragWorld;
    Vector2 mDragStartBoard;
    Rect mDragStartRect;

    /* Per editor window too: a Multiplayer Play Mode window shares the main editor's PlayerPrefs but
     not its camera, so one shared layout would be overwritten by whichever window saved last */
    string PrefsKey => "BoardCalibration_" + board.boardMode + (CameraChoice.WindowId == "Main" ? "" : "." + CameraChoice.WindowId);

    void Awake()
    {
        if (board != null) mSpawnScale = board.transform.localScale; // before BoardLayout fits it
    }

    IEnumerator Start()
    {
        if (board == null)
        {
            Debug.LogError("[BoardCalibrator] Drag the Board into the Board slot.", this);
            yield break;
        }
        if (calibrateButton != null) calibrateButton.gameObject.SetActive(false);

        yield return null; // after BoardLayout.Start has spawned the zones
        LoadSaved();
    }

    void Update()
    {
        if (board == null) return;
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) ToggleCalibration();
        if (!mCalibrating) return;

        Camera cam = Camera.main;
        if (cam == null || Mouse.current == null || Keyboard.current == null) return;

        Vector2 mouse = Mouse.current.position.ReadValue();
        HandleDrag(cam, mouse);
        HandleScroll(cam, mouse);
        HandleKeys(cam);
    }

    public void ToggleCalibration()
    {
        mCalibrating = !mCalibrating;
        if (mCalibrating) board.fitToCamera = false; // so it doesn't re-fit over the manual layout
        else Select(null);
    }

    void HandleDrag(Camera cam, Vector2 mouse)
    {
        var left = Mouse.current.leftButton;

        if (left.wasPressedThisFrame && !OverPanel(mouse))
        {
            mDragZone = board.ZoneAtScreenPoint(mouse, cam);
            Select(mDragZone);
            mDragPlane = new Plane(board.transform.forward, board.transform.position);
            mDragging = mDragZone != null
                ? board.TryScreenToBoard(mouse, cam, out mDragStartBoard) && board.TryGetZoneLayout(mDragZone, out mDragStartRect)
                : TryPlanePoint(cam, mouse, out mLastDragWorld);
        }
        else if (left.wasReleasedThisFrame)
        {
            mDragging = false;
        }
        else if (mDragging && left.isPressed)
        {
            if (mDragZone != null)
            {
                if (!board.TryScreenToBoard(mouse, cam, out Vector2 point)) return;
                Rect rect = mDragStartRect;
                rect.center += point - mDragStartBoard;
                board.SetZoneLayout(mDragZone, rect);
            }
            else if (TryPlanePoint(cam, mouse, out Vector3 world))
            {
                board.transform.position += world - mLastDragWorld;
                mLastDragWorld = world;
            }
        }
    }

    void HandleScroll(Camera cam, Vector2 mouse)
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f) || OverPanel(mouse)) return;

        float factor = scroll > 0f ? 1.03f : 1f / 1.03f;
        Vector3 axes = Keyboard.current.shiftKey.isPressed ? new Vector3(factor, 1f, 1f)
                     : Keyboard.current.ctrlKey.isPressed ? new Vector3(1f, factor, 1f)
                     : new Vector3(factor, factor, 1f);

        // Scales around the point under the mouse so that spot stays put on screen
        mDragPlane = new Plane(board.transform.forward, board.transform.position);
        Vector3 pivot = TryPlanePoint(cam, mouse, out Vector3 world) ? world : board.transform.position;
        Vector3 localPivot = board.transform.InverseTransformPoint(pivot);
        board.transform.localScale = Vector3.Scale(board.transform.localScale, axes);
        board.transform.position += pivot - board.transform.TransformPoint(localPivot);
    }

    void HandleKeys(Camera cam)
    {
        Keyboard keys = Keyboard.current;

        float turn = (keys.eKey.isPressed ? 1f : 0f) - (keys.qKey.isPressed ? 1f : 0f);
        if (turn != 0f)
            board.transform.RotateAround(board.transform.position, cam.transform.forward, -turn * 20f * Time.deltaTime);

        if (mSelectedZone == null) return;

        if (keys.hKey.wasPressedThisFrame)
            board.SetZoneHidden(mSelectedZone, !board.IsZoneHidden(mSelectedZone));

        // [ / ] resize; with Shift only the width, with Ctrl only the height
        float grow = keys.rightBracketKey.wasPressedThisFrame ? 1.05f : keys.leftBracketKey.wasPressedThisFrame ? 1f / 1.05f : 1f;
        if (grow != 1f && board.TryGetZoneLayout(mSelectedZone, out Rect rect))
        {
            Vector2 center = rect.center;
            rect.size = Vector2.Scale(rect.size, keys.shiftKey.isPressed ? new Vector2(grow, 1f)
                                               : keys.ctrlKey.isPressed ? new Vector2(1f, grow)
                                               : new Vector2(grow, grow));
            rect.center = center;
            board.SetZoneLayout(mSelectedZone, rect);
        }
    }

    void Select(string key)
    {
        if (mSelectedZone != null) board.ResetCircleColor(mSelectedZone);
        mSelectedZone = key;
        if (key != null) board.HighlightCircle(key, SelectedColor);
    }

    bool TryPlanePoint(Camera cam, Vector2 screen, out Vector3 world)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        bool hit = mDragPlane.Raycast(ray, out float enter);
        world = hit ? ray.GetPoint(enter) : default;
        return hit;
    }

    // Input System's y runs bottom-up, IMGUI's top-down
    static bool OverPanel(Vector2 mouse) => PanelRect.Contains(new Vector2(mouse.x, Screen.height - mouse.y) / GuiScale);

    void Save()
    {
        Transform t = board.transform;
        var layout = new SavedLayout { version = LayoutVersion, position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
        foreach (string key in board.circles.Keys)
            if (board.TryGetZoneLayout(key, out Rect rect))
                layout.zones.Add(new SavedZone { key = key, rect = rect, hidden = board.IsZoneHidden(key) });

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(layout));
        PlayerPrefs.Save();
        Debug.Log("[BoardCalibrator] Saved " + board.boardMode + " board layout.");
    }

    // This PC's calibration, else the default shipped in Resources/BoardLayouts/<mode>.json
    void LoadSaved()
    {
        if (!PlayerPrefs.HasKey(PrefsKey) || !ApplyLayout(PlayerPrefs.GetString(PrefsKey), "Saved"))
            ApplyLayout(Resources.Load<TextAsset>("BoardLayouts/" + board.boardMode)?.text, "Default");
    }

    bool ApplyLayout(string json, string source)
    {
        SavedLayout layout = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SavedLayout>(json);
        if (layout == null) return false;
        if (layout.version != LayoutVersion)
        {
            Debug.Log("[BoardCalibrator] " + source + " " + board.boardMode + " layout is from older zone shapes, so it's ignored. Press C to calibrate again.");
            return false;
        }

        board.fitToCamera = false;
        Transform t = board.transform;
        t.localPosition = layout.position;
        t.localRotation = layout.rotation;
        t.localScale = layout.scale;
        foreach (SavedZone zone in layout.zones)
        {
            board.SetZoneLayout(zone.key, zone.rect);
            board.SetZoneHidden(zone.key, zone.hidden);
        }
        return true;
    }

    // Spawned grid, fitted to the camera again; the saved one is kept
    void RestoreDefaults()
    {
        Select(null);
        board.ResetZoneLayouts();

        Transform t = board.transform;
        t.localPosition = new Vector3(0f, 0f, board.distanceFromCameraIfUnparented);
        t.localRotation = Quaternion.identity;
        t.localScale = mSpawnScale;
        board.FitToCamera();
        board.fitToCamera = !mCalibrating;
    }

    void OnGUI()
    {
        if (!mCalibrating) return;

        Matrix4x4 matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(GuiScale, GuiScale, 1f));
        GUILayout.BeginArea(PanelRect, GUI.skin.box);
        GUILayout.Label("<b>Board calibration</b> (" + board.boardMode + ")", new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.Label("Drag empty space: move board\nDrag a zone: move that zone\nScroll: resize board\n" +
                        "Shift+scroll: wider/narrower   Ctrl+scroll: taller/shorter\nQ / E: rotate board\n" +
                        "Click a zone, then [ / ]: shrink/grow it,  H: hide/show it\n" +
                        "  Shift+[ / ]: narrower/wider   Ctrl+[ / ]: shorter/taller");
        GUILayout.Label("Selected: " + (mSelectedZone ?? "none"));

        if (GUILayout.Button(sPanelRight ? "Move this panel left" : "Move this panel right")) sPanelRight = !sPanelRight;
        if (GUILayout.Button("Save and close")) { Save(); ToggleCalibration(); }
        if (GUILayout.Button("Cancel (back to last save)")) { ToggleCalibration(); RestoreDefaults(); LoadSaved(); }
        if (GUILayout.Button("Show all hidden zones"))
            foreach (string key in new List<string>(board.circles.Keys)) board.SetZoneHidden(key, false);
        if (GUILayout.Button("Reset to default (deletes save)")) { PlayerPrefs.DeleteKey(PrefsKey); RestoreDefaults(); LoadSaved(); }
        GUILayout.EndArea();
        GUI.matrix = matrix;
    }
}

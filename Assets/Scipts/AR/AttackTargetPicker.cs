using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/* Pops up while the turn player's attack needs a target: a box for each of the defender's three
 front-row circles, picked by clicking, keys 1-3 (left, vanguard, right), saying "attack left",
 "attack vanguard" or "attack right", or online a finger held on the box (HandZones). Empty
 rear-guard circles can't be picked. Over The Board the boxes sit on the defender's circles in this
 camera's view; online on your own front row, each on the circle facing its target. Added by UISetup. Its UI is built in AttackTargetPicker.UI.cs. */
public partial class AttackTargetPicker : MonoBehaviour
{
    static readonly Key[] PickKeys = { Key.Digit1, Key.Digit2, Key.Digit3 };

    /* The defender's front-row circle facing one of yours across the table, and the other way
     round: their left is your right */
    public static string Facing(string zone)
    {
        switch (zone)
        {
            case "LeftFront": return "RightFront";
            case "RightFront": return "LeftFront";
            case "Vanguard": return "Vanguard";
            default: return null;
        }
    }
    static readonly string[] Names = { "Left RG", "Vanguard", "Right RG" };

    class Marker
    {
        public RectTransform rect;
        public Image image;
        public Button button;
        public TextMeshProUGUI label;
        public RectTransform fill; // a finger hold's progress (HandZones)
        public Ripple ripple; // only on a box that can be picked
    }

    readonly Marker[] mMarkers = new Marker[3];
    GameObject mCanvasObj;
    RulesEngine mRules;
    BoardLayout mBoard;

    void Start() => BuildCanvas();

    void OnDestroy()
    {
        if (mCanvasObj != null) Destroy(mCanvasObj);
    }

    void Update()
    {
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();
        if (mBoard == null) mBoard = FindFirstObjectByType<BoardLayout>();
        GameState gs = GameState.Instance;
        Camera cam = Camera.main;

        bool show = mRules != null && gs != null && cam != null && mRules.IsChoosingTarget && MatchSession.IsLocal(gs.CurrentPlayer);
        if (mCanvasObj.activeSelf != show) mCanvasObj.SetActive(show);
        if (!show) return;

        PlayerState defender = gs.Opponent;
        for (int i = 0; i < mMarkers.Length; i++)
        {
            string zone = RulesEngine.FrontRow[i];
            CardInstance unit = defender.CardOnCircle(zone);
            bool canPick = unit != null || zone == "Vanguard"; // the vanguard is there even if the camera missed it

            Marker marker = mMarkers[i];
            marker.button.interactable = canPick;
            marker.ripple.SetVisible(canPick);
            marker.image.color = canPick ? new Color(0.85f, 0.1f, 0.1f, 0.45f) : new Color(0.2f, 0.2f, 0.2f, 0.35f);
            marker.label.text = (i + 1) + "  " + Names[i] + "\n" + (unit != null ? unit.data.DisplayName + "\n" + unit.TotalPower : canPick ? "" : "empty");
            Place(marker.rect, ScreenRect(cam, defender, zone, i));
            float hold = MatchSession.IsOnline && HandZones.Instance != null ? HandZones.Instance.PickProgress("P1_" + Facing(zone)) : 0f;
            marker.fill.localScale = new Vector3(hold, 1f, 1f);

            if (canPick && Keyboard.current != null && Keyboard.current[PickKeys[i]].wasPressedThisFrame)
                mRules.ChooseTarget(zone);
        }
    }

    /* The defender's circle on screen in pixels. Online that's their camera picture, where their
     half uses the same layout as this PC's own (both drawn as P1), turned round. */
    Rect ScreenRect(Camera cam, PlayerState defender, string zone, int index)
    {
        Rect px = cam.pixelRect;

        if (!MatchSession.IsOnline)
        {
            string key = "P" + (defender.playerIndex + 1) + "_" + zone;
            if (mBoard != null && mBoard.TryGetZoneViewRect(key, cam, out Rect view))
                return new Rect(px.x + view.x * px.width, px.y + view.y * px.height, view.width * px.width, view.height * px.height);
        }
        /* Online: on your own front-row circle facing it, so a finger held there picks it (HandZones)
         and each box sits in line with its target in the opponent's picture above */
        else if (mBoard != null && mBoard.TryGetZoneViewRect("P1_" + Facing(zone), cam, out Rect yours))
            return new Rect(px.x + yours.x * px.width, px.y + yours.y * px.height, yours.width * px.width, yours.height * px.height);

        // Their circles aren't on screen: a row across the middle of this camera's view, below the hand buttons
        float boxW = px.width * 0.18f, boxH = px.height * 0.2f;
        float gap = px.width * 0.04f;
        float left = px.x + (px.width - 3f * boxW - 2f * gap) * 0.5f;
        return new Rect(left + index * (boxW + gap), px.y + (px.height - boxH) * 0.5f, boxW, boxH);
    }

    static void Place(RectTransform rect, Rect screen)
    {
        rect.anchoredPosition = screen.position;
        rect.sizeDelta = screen.size;
    }
}

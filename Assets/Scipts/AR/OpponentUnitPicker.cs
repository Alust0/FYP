using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/* Pops up while a skill has its owner choose one of the opponent's units (ChooseOpponentStep, e.g.
 "retire one of your opponent's rear-guards"): a red box with ripples on each unit that can be chosen,
 picked by clicking or keys 1-6. Over The Board the boxes sit on the opponent's circles in this
 camera's view; online, like AttackTargetPicker's, on the chooser's own side: each on their circle
 facing it across the table (Facing). Added by UISetup. Its UI is built in OpponentUnitPicker.UI.cs. */
public partial class OpponentUnitPicker : MonoBehaviour
{
    static readonly string[] Circles = { "LeftFront", "Vanguard", "RightFront", "LeftBack", "CenterBack", "RightBack" };
    static readonly Key[] PickKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };

    class Marker
    {
        public RectTransform rect;
        public TextMeshProUGUI label;
    }

    readonly Marker[] mMarkers = new Marker[Circles.Length];
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
        Camera cam = Camera.main;
        GameState gs = GameState.Instance;
        SkillRules skills = mRules != null ? mRules.Skills : null;

        List<string> choices = skills != null && gs != null && !gs.gameOver && cam != null ? skills.OpponentChoices() : new List<string>();
        bool show = choices.Count > 0;
        if (mCanvasObj.activeSelf != show) mCanvasObj.SetActive(show);
        if (!show) return;

        PlayerState opponent = skills.ChoiceOpponent;
        int key = 0;
        for (int i = 0; i < Circles.Length; i++)
        {
            Marker marker = mMarkers[i];
            string zone = Circles[i];
            bool canPick = choices.Contains(zone);
            if (marker.rect.gameObject.activeSelf != canPick) marker.rect.gameObject.SetActive(canPick);
            if (!canPick) continue;

            key++;
            CardInstance unit = opponent.CardOnCircle(zone);
            marker.label.text = key + "\n" + unit.data.DisplayName + "\n" + unit.TotalPower;
            Place(marker.rect, ScreenRect(cam, opponent, zone, i));
            if (Keyboard.current != null && Keyboard.current[PickKeys[key - 1]].wasPressedThisFrame) skills.ChooseOpponent(zone);
        }
    }

    // The opponent's circle on screen in pixels
    Rect ScreenRect(Camera cam, PlayerState opponent, string zone, int index)
    {
        Rect px = cam.pixelRect;
        if (mBoard != null)
        {
            if (!MatchSession.IsOnline && mBoard.TryGetZoneViewRect("P" + (opponent.playerIndex + 1) + "_" + zone, cam, out Rect view))
                return new Rect(px.x + view.x * px.width, px.y + view.y * px.height, view.width * px.width, view.height * px.height);

            // Online: on this PC's own circle facing it, in line with it in the opponent's picture above
            if (MatchSession.IsOnline && mBoard.TryGetZoneViewRect("P1_" + Facing(zone), cam, out Rect own))
                return new Rect(px.x + own.x * px.width, px.y + own.y * px.height, own.width * px.width, own.height * px.height);
        }

        // Their circles aren't on screen: two rows across the middle of this camera's view
        float boxW = px.width * 0.16f, boxH = px.height * 0.16f, gap = px.width * 0.03f;
        int column = index % 3, row = index / 3;
        float left = px.x + (px.width - 3f * boxW - 2f * gap) * 0.5f;
        return new Rect(left + column * (boxW + gap), px.y + px.height * 0.5f + (row == 0 ? gap * 0.5f : -boxH - gap * 0.5f), boxW, boxH);
    }

    // Their left is your right, front row and back row alike
    static string Facing(string zone) => zone.StartsWith("Left") ? "Right" + zone.Substring(4) : zone.StartsWith("Right") ? "Left" + zone.Substring(5) : zone;

    static void Place(RectTransform rect, Rect screen)
    {
        rect.anchoredPosition = screen.position;
        rect.sizeDelta = screen.size;
    }
}

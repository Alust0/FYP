using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Lights the zones to act on, and draws arrows (BoardArrow)
public partial class ZoneHighlighter : MonoBehaviour
{
    public Color zoneColor = new Color(1f, 0.82f, 0.2f);
    public Color arrowColor = new Color(1f, 0.42f, 0.3f);
    public Color boostColor = new Color(0.35f, 1f, 0.45f);
    public Color guardColor = new Color(1f, 0.9f, 0.2f);
    public Color attackColor = new Color(1f, 0.15f, 0.1f);
    public float attackArrowSize = 2.2f; // the attack arrow's thickness, times the others'

    class Highlight
    {
        public GameObject root;
        public Image glow;
        public Image[] edges;
        public Ripple ripple;
    }

    readonly List<Highlight> mHighlights = new List<Highlight>();
    readonly List<BoardArrow> mArrows = new List<BoardArrow>();
    BoardArrow mBoost;
    BoardArrow mGuard; // the same chevrons in yellow, from the Guardian circle to the guarded unit
    BoardArrow mAttack; // red chevrons from the attacker to its target
    readonly Dictionary<string, Color> mZones = new Dictionary<string, Color>();
    RectTransform mCanvas;
    RulesEngine mRules;
    BoardLayout mBoard;

    void Start() => BuildCanvas();

    void Update()
    {
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();
        if (mBoard == null) mBoard = FindFirstObjectByType<BoardLayout>();
        Camera cam = Camera.main;
        mZones.Clear();
        int arrows = 0;
        bool boosting = false, guarding = false, attacking = false;

        if (mRules != null && mBoard != null && cam != null)
        {
            Rect px = cam.pixelRect;
            foreach ((PlayerState owner, string zone) in mRules.AttentionZones())
                if (Shown(owner)) mZones[Key(owner, zone)] = zoneColor;

            foreach ((PlayerState owner, string from, string to, RulesEngine.ArrowKind kind) in mRules.AttentionArrows())
            {
                if (!Shown(owner)) continue;
                string toKey = Key(owner, to);
                // To the drop zone: it lights in the arrow's colour. A ride: the ride deck does, and the vanguard circle in yellow
                if (kind == RulesEngine.ArrowKind.Ride)
                {
                    mZones[Key(owner, from)] = arrowColor;
                    mZones[toKey] = zoneColor;
                }
                else if (kind == RulesEngine.ArrowKind.ToDrop) mZones[toKey] = arrowColor;
                // A skill's (a paid cost, a chosen unit): its zones stay lit as they are, and the drop zone lights if it isn't
                else mZones.TryAdd(toKey, zoneColor);
                if (!mBoard.TryGetZoneViewRect(Key(owner, from), cam, out Rect fromView) || !mBoard.TryGetZoneViewRect(toKey, cam, out Rect toView)) continue;
                if (arrows == mArrows.Count) mArrows.Add(BoardArrow.Add(mCanvas, BoardArrow.Style.Pulse, arrowColor));
                mArrows[arrows].color = kind == RulesEngine.ArrowKind.Check ? zoneColor : arrowColor;
                mArrows[arrows++].Show(ToPixels(px, fromView), ToPixels(px, toView));
            }

            var boost = mRules.BoostArrow;
            if (boost != null && Shown(boost.Value.owner) &&
                mBoard.TryGetZoneViewRect(Key(boost.Value.owner, boost.Value.from), cam, out Rect boosterView) &&
                mBoard.TryGetZoneViewRect(Key(boost.Value.owner, boost.Value.to), cam, out Rect boostedView))
            {
                if (mBoost == null) mBoost = BoardArrow.Add(mCanvas, BoardArrow.Style.Chevrons, boostColor);
                mBoost.color = boostColor;
                mBoost.Show(ToPixels(px, boosterView), ToPixels(px, boostedView));
                boosting = true;
            }

            var guard = mRules.GuardArrow;
            if (guard != null && Shown(guard.Value.owner) &&
                mBoard.TryGetZoneViewRect(Key(guard.Value.owner, guard.Value.from), cam, out Rect guardianView) &&
                mBoard.TryGetZoneViewRect(Key(guard.Value.owner, guard.Value.to), cam, out Rect guardedView))
            {
                if (mGuard == null) mGuard = BoardArrow.Add(mCanvas, BoardArrow.Style.Chevrons, guardColor);
                mGuard.color = guardColor;
                mGuard.Show(ToPixels(px, guardianView), ToPixels(px, guardedView));
                guarding = true;
            }

            // Both players' PCs: it crosses to the other side's view online
            var attack = mRules.AttackArrow;
            if (attack != null &&
                TryGetScreenRect(mBoard, attack.Value.attacker, attack.Value.from, out Rect attackerRect) &&
                TryGetScreenRect(mBoard, attack.Value.defender, attack.Value.to, out Rect targetRect))
            {
                if (mAttack == null) mAttack = BoardArrow.Add(mCanvas, BoardArrow.Style.Chevrons, attackColor, attackArrowSize);
                mAttack.color = attackColor;
                mAttack.Show(attackerRect, targetRect);
                attacking = true;
            }

            int used = 0;
            foreach (KeyValuePair<string, Color> zone in mZones)
            {
                if (!mBoard.TryGetZoneViewRect(zone.Key, cam, out Rect view)) continue;
                if (used == mHighlights.Count) mHighlights.Add(BuildHighlight());
                ShowHighlight(mHighlights[used++], ToPixels(px, view), zone.Value);
            }
            Hide(mHighlights, used);
        }
        else Hide(mHighlights, 0);

        for (int i = arrows; i < mArrows.Count; i++) mArrows[i].Hide();
        if (!boosting && mBoost != null) mBoost.Hide();
        if (!guarding && mGuard != null) mGuard.Hide();
        if (!attacking && mAttack != null) mAttack.Hide();
    }

    static bool Shown(PlayerState owner) => owner != null && (!MatchSession.IsOnline || MatchSession.IsLocal(owner));

    // "P1" is always this PC's own player (BoardLayout.SeatOfZone)
    static string Key(PlayerState owner, string zone) => (owner.playerIndex == MatchSession.LocalSeat ? "P1" : "P2") + "_" + zone;

    // A player's zone on screen in pixels; online the opponent's is in their video (layout assumed same as ours)
    public static bool TryGetScreenRect(BoardLayout board, PlayerState owner, string zone, out Rect box)
    {
        box = default;
        Camera cam = Camera.main;
        if (board == null || cam == null || owner == null) return false;

        if (!MatchSession.IsOnline || MatchSession.IsLocal(owner))
        {
            if (!board.TryGetZoneViewRect(Key(owner, zone), cam, out Rect view)) return false;
            box = ToPixels(cam.pixelRect, view);
            return true;
        }

        Rect? shown = SplitScreenView.OpponentView;
        if (shown == null || !board.TryGetZoneViewRect("P1_" + zone, cam, out Rect theirs)) return false;
        Rect o = shown.Value; // their picture is turned round
        box = new Rect((o.x + (1f - theirs.xMax) * o.width) * Screen.width, (o.y + (1f - theirs.yMax) * o.height) * Screen.height,
            theirs.width * o.width * Screen.width, theirs.height * o.height * Screen.height);
        return true;
    }

    static Rect ToPixels(Rect px, Rect view) =>
        new Rect(px.x + view.x * px.width, px.y + view.y * px.height, view.width * px.width, view.height * px.height);

    static void Hide(List<Highlight> highlights, int from)
    {
        for (int i = from; i < highlights.Count; i++)
            if (highlights[i].root.activeSelf) highlights[i].root.SetActive(false);
    }

    // Over the zone: a pulsing glow, its border and ripples, all in its colour
    void ShowHighlight(Highlight h, Rect rect, Color color)
    {
        if (!h.root.activeSelf) h.root.SetActive(true);
        RectTransform root = (RectTransform)h.root.transform;
        root.anchoredPosition = rect.center;
        root.sizeDelta = rect.size;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime / h.ripple.seconds * 2f * Mathf.PI);
        h.glow.color = new Color(color.r, color.g, color.b, 0.16f + 0.14f * pulse);
        foreach (Image edge in h.edges) edge.color = color;
        h.ripple.color = color;
    }
}

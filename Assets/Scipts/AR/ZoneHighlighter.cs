using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/* Lights up the board zones a player has to move cards on now (RulesEngine.AttentionZones), e.g. the
 trigger zone for a skill's reveal, the damage zone for a Counter Blast, the drop zone for a discard:
 each glows and sends out ripples (Ripple) so the eye goes to it. Cards to put in the drop zone
 (RulesEngine.AttentionArrows: a rear-guard the attack hit, guardians after the drive check) get an
 arrow from where they are, and the drop zone lights up in the arrow's colour; a ride from the ride
 deck, which the camera doesn't read, lights it in the arrow's colour with an arrow to the vanguard
 circle, lit yellow; a Soul Blast paid, an arrow from the vanguard to the drop zone. Online only this PC's
 own side; Over The Board, both. Added by UISetup. Its UI is built in ZoneHighlighter.UI.cs. */
public partial class ZoneHighlighter : MonoBehaviour
{
    public Color zoneColor = new Color(1f, 0.82f, 0.2f);
    public Color arrowColor = new Color(1f, 0.42f, 0.3f);

    class Highlight
    {
        public GameObject root;
        public Image glow;
        public Image[] edges;
        public Ripple ripple;
    }

    class Arrow
    {
        public RectTransform root;
        public Image[] parts; // shaft and the two sides of the head
    }

    readonly List<Highlight> mHighlights = new List<Highlight>();
    readonly List<Arrow> mArrows = new List<Arrow>();
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
                if (arrows == mArrows.Count) mArrows.Add(BuildArrow());
                ShowArrow(mArrows[arrows++], ToPixels(px, fromView), ToPixels(px, toView));
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

        for (int i = arrows; i < mArrows.Count; i++)
            if (mArrows[i].root.gameObject.activeSelf) mArrows[i].root.gameObject.SetActive(false);
    }

    static bool Shown(PlayerState owner) => owner != null && (!MatchSession.IsOnline || MatchSession.IsLocal(owner));

    // "P1" is always this PC's own player (BoardLayout.SeatOfZone)
    static string Key(PlayerState owner, string zone) => (owner.playerIndex == MatchSession.LocalSeat ? "P1" : "P2") + "_" + zone;

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

    // From one zone's edge to the other's, pulsing
    void ShowArrow(Arrow arrow, Rect from, Rect to)
    {
        if (!arrow.root.gameObject.activeSelf) arrow.root.gameObject.SetActive(true);
        Vector2 direction = (to.center - from.center).normalized;
        Vector2 start = from.center + direction * Mathf.Min(from.width, from.height) * 0.4f;
        Vector2 end = to.center - direction * Mathf.Min(to.width, to.height) * 0.4f;

        arrow.root.anchoredPosition = start;
        arrow.root.sizeDelta = new Vector2(Mathf.Max(0f, (end - start).magnitude), ArrowThickness);
        arrow.root.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

        float alpha = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / 0.8f));
        foreach (Image part in arrow.parts) part.color = new Color(arrowColor.r, arrowColor.g, arrowColor.b, alpha);
    }
}

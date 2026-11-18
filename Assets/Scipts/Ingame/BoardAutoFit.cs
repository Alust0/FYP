using System.Collections.Generic;
using UnityEngine;

// Lines the board up with each side's deck and ride deck (card backs) and starter (face down or up)
public static class BoardAutoFit
{
    static readonly string[] AnchorZones = { "_Vanguard", "_Deck", "_RideDeck" };
    static readonly Dictionary<string, string> ZoneNames = new Dictionary<string, string>
        { { "_Vanguard", "starter" }, { "_Deck", "deck" }, { "_RideDeck", "ride deck" } };
    const int MinPairs = 3;
    const int MaxPoints = 9;
    const float MaxMisfitCards = 0.75f; // rms distance of the backs from their zone centres, in card widths
    const float TurnPenalty = 0.3f; // per half turn, so one side's cards don't fit the other side's zones
    const float UnmatchedPenalty = 0.5f; // per card back left out of the fit

    // Moves, turns and scales the board onto the card backs; the message says what it did or why it couldn't
    public static bool Fit(BoardLayout board, Camera cam, out string message)
    {
        CardBackTracker backs = CardBackTracker.Instance;
        if (board == null || cam == null || backs == null)
        {
            message = "No camera or card back tracking.";
            return false;
        }

        Transform t = board.transform;
        Vector3 lossy = t.lossyScale;

        // Anchor zone centres, in world units along the board's current axes
        var keys = new List<string>();
        var anchors = new List<Vector2>();
        foreach (string prefix in new[] { "P1", "P2" })
            foreach (string zone in AnchorZones)
            {
                string key = prefix + zone;
                if (board.IsZoneHidden(key) || !board.TryGetZoneLayout(key, out Rect rect)) { continue; }
                keys.Add(key);
                anchors.Add(Vector2.Scale(rect.center, new Vector2(lossy.x, lossy.y)));
            }

        // Card backs, then face-up cards (a face-up starter), on the board plane in the same axes
        var plane = new Plane(t.forward, t.position);
        var points = new List<Vector2>();
        var screens = backs.BacksOnScreen(cam, Screen.height * 0.02f);
        int backCount = screens.Count;
        if (backCount > MaxPoints)
        {
            message = "Saw " + backCount + " card backs; leave only the decks, ride decks and starters face down.";
            return false;
        }
        foreach (VuforiaCardOverlayManager cards in Object.FindObjectsByType<VuforiaCardOverlayManager>(FindObjectsSortMode.None))
            foreach (Vector2 screen in cards.CardsOnScreen(cam))
                if (!screens.Exists(p => Vector2.Distance(p, screen) <= Screen.height * 0.02f)) { screens.Add(screen); }

        var isBack = new List<bool>();
        for (int i = 0; i < screens.Count; i++)
        {
            Ray ray = cam.ScreenPointToRay(screens[i]);
            if (!plane.Raycast(ray, out float enter)) { continue; }
            Vector3 offset = ray.GetPoint(enter) - t.position;
            points.Add(new Vector2(Vector3.Dot(offset, t.right), Vector3.Dot(offset, t.up)));
            isBack.Add(i < backCount);
        }

        int faceUp = screens.Count - backCount;
        if (points.Count < MinPairs || backCount == 0)
        {
            message = "Saw " + backCount + " card back(s) and " + faceUp + " face-up card(s). Lay out your deck, ride deck and starter on the Vanguard circle, then try again.";
            return false;
        }

        Transform view = cam.transform;
        float roll = Mathf.Atan2(Vector3.Dot(t.right, view.up), Vector3.Dot(t.right, view.right)); // board's turn on screen
        float cardWorld = board.cardWidth * 0.5f * (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y));
        int n = anchors.Count, m = points.Count;
        var vanguard = new bool[n];
        for (int k = 0; k < n; k++) vanguard[k] = keys[k].EndsWith("_Vanguard");
        var assign = new int[n];
        var used = new bool[m];
        var from = new List<Vector2>(n);
        var to = new List<Vector2>(n);
        int[] best = null;
        float bestScore = float.MaxValue, bestAngle = 0f, bestScale = 1f, bestMisfit = 0f;
        Vector2 bestShift = default;

        // Tries every way of pairing anchor zones with card backs (anchor i gets point assign[i], -1 none)
        void Try(int i, int pairs)
        {
            if (i == n)
            {
                if (pairs < MinPairs) { return; }
                from.Clear();
                to.Clear();
                for (int k = 0; k < n; k++)
                    if (assign[k] >= 0) { from.Add(anchors[k]); to.Add(points[assign[k]]); }
                if (!FitSimilarity(from, to, roll, out float angle, out float scale, out Vector2 shift, out float rms)) { return; }
                if (scale < 0.2f || scale > 5f) { return; }

                int unmatchedBacks = 0;
                for (int p = 0; p < m; p++) if (isBack[p] && !used[p]) { unmatchedBacks++; }
                float misfit = rms / (scale * cardWorld);
                float score = misfit + TurnPenalty * Mathf.Abs(angle) / Mathf.PI + UnmatchedPenalty * unmatchedBacks;
                if (score >= bestScore) { return; }
                bestScore = score;
                best = (int[])assign.Clone();
                bestAngle = angle;
                bestScale = scale;
                bestShift = shift;
                bestMisfit = misfit;
                return;
            }
            if (pairs + n - i < MinPairs) { return; }

            assign[i] = -1;
            Try(i + 1, pairs);
            for (int p = 0; p < m; p++)
            {
                if (used[p] || (!isBack[p] && !vanguard[i])) { continue; } // face-up cards only as the starter
                used[p] = true;
                assign[i] = p;
                Try(i + 1, pairs + 1);
                used[p] = false;
            }
            assign[i] = -1;
        }
        Try(0, 0);

        if (best == null || bestMisfit > MaxMisfitCards)
        {
            message = "The card backs don't match the deck / starter / ride deck zones" +
                      (best == null ? "." : " (off by " + bestMisfit.ToString("0.0") + " card widths).") +
                      " Check they're on those zones, or fix the zone spacing by hand.";
            return false;
        }

        // Turn about the board's facing, scale both axes alike, then shift within its plane
        float cos = Mathf.Cos(bestAngle), sin = Mathf.Sin(bestAngle);
        Vector3 right = t.right, up = t.up, forward = t.forward;
        Vector3 position = t.position + right * bestShift.x + up * bestShift.y;
        board.fitToCamera = false;
        t.rotation = Quaternion.LookRotation(forward, -sin * right + cos * up);
        t.localScale = new Vector3(t.localScale.x * bestScale, t.localScale.y * bestScale, t.localScale.z);
        t.position = position;

        var found = new List<string>();
        for (int k = 0; k < n; k++)
            if (best[k] >= 0) { found.Add(keys[k].Substring(0, 2) + " " + ZoneNames[keys[k].Substring(2)]); }
        message = "Fitted to " + string.Join(", ", found) + " (off by " + bestMisfit.ToString("0.00") +
                  " card widths). Check the other zones, then Save.";
        return true;
    }

    // Least-squares uniform scale and shift taking from[i] onto to[i], turned so the board stays square to the screen (roll: its turn now)
    static bool FitSimilarity(List<Vector2> from, List<Vector2> to, float roll, out float angle, out float scale, out Vector2 shift, out float rms)
    {
        angle = 0f; scale = 1f; shift = default; rms = 0f;
        int count = from.Count;
        Vector2 fromMean = default, toMean = default;
        for (int i = 0; i < count; i++) { fromMean += from[i]; toMean += to[i]; }
        fromMean /= count;
        toMean /= count;

        float dot = 0f, cross = 0f, spread = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector2 f = from[i] - fromMean, g = to[i] - toMean;
            dot += f.x * g.x + f.y * g.y;
            cross += f.x * g.y - f.y * g.x;
            spread += f.sqrMagnitude;
        }
        if (spread < 1e-8f) { return false; }

        const float Quarter = Mathf.PI * 0.5f;
        angle = Mathf.Round((roll + Mathf.Atan2(cross, dot)) / Quarter) * Quarter - roll;
        scale = (Mathf.Cos(angle) * dot + Mathf.Sin(angle) * cross) / spread;
        if (scale <= 0f) { return false; }
        shift = toMean - scale * Rotate(fromMean, angle);

        float sum = 0f;
        for (int i = 0; i < count; i++)
            sum += (to[i] - (scale * Rotate(from[i], angle) + shift)).sqrMagnitude;
        rms = Mathf.Sqrt(sum / count);
        return true;
    }

    static Vector2 Rotate(Vector2 v, float angle)
    {
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}

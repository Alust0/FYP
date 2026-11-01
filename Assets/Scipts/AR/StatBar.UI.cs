using TMPro;
using UnityEngine;

/* StatBar's pieces: a border in the seat's colour, the segments or battery cells, and the number badge.
 The rest of StatBar is in StatBar.cs. */
public partial class StatBar
{
    /* A bar at position (top-left, as UISetup's count lines) in a HUD column; damage or energy for that
     seat. Returns its root, to hide with the line. */
    public static StatBar Create(RectTransform parent, Vector2 position, Vector2 size, int player, bool damage)
    {
        var obj = new GameObject(damage ? "DamageBar" : "EnergyBar", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var root = (RectTransform)obj.transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = position;
        root.sizeDelta = size;

        StatBar bar = obj.AddComponent<StatBar>();
        bar.mPlayer = player;
        bar.mDamage = damage;
        bar.Build(root, size);
        return bar;
    }

    void Build(RectTransform root, Vector2 size)
    {
        float badge = size.y; // a circle as tall as the bar, at its right end
        float barWidth = size.x - badge - (mDamage ? 0f : 4f);

        // The frame: the seat's colour round a dark inside (energy's is green for both); energy gets a battery nub
        Color border = mDamage ? SeatBorder[mPlayer] : EnergyBorder;
        ShapeGraphic frame = Shape(root, "Frame", mDamage ? ShapeGraphic.Kind.Slant : ShapeGraphic.Kind.Rounded, border);
        frame.slant = mDamage ? 0.5f : 0f;
        Fit(frame.rectTransform, 0f, 0f, barWidth, size.y);
        ShapeGraphic inside = Shape(root, "Inside", frame.kind, new Color(0.12f, 0.12f, 0.14f));
        inside.slant = frame.slant;
        Fit(inside.rectTransform, 2f, 2f, barWidth - 4f, size.y - 4f);
        if (!mDamage)
        {
            ShapeGraphic nub = Shape(root, "Nub", ShapeGraphic.Kind.Rounded, border);
            Fit(nub.rectTransform, barWidth, size.y * 0.3f, 4f, size.y * 0.4f);
        }

        // The segments, evenly spaced inside
        int count = Size;
        float pad = 4f, gap = mDamage ? 1f : 1.5f;
        float width = (barWidth - 2f * pad - gap * (count - 1) - (mDamage ? size.y * 0.25f : 0f)) / count;
        for (int i = 0; i < count; i++)
        {
            ShapeGraphic segment = Shape(root, "Segment" + i, mDamage ? ShapeGraphic.Kind.Slant : ShapeGraphic.Kind.Rounded, Empty);
            segment.slant = 0.5f;
            segment.raycastTarget = true; // hovered for its card's name
            Fit(segment.rectTransform, pad + (mDamage ? size.y * 0.125f : 0f) + i * (width + gap), pad, width, size.y - 2f * pad);
            StatBarHover hover = segment.gameObject.AddComponent<StatBarHover>();
            hover.bar = this;
            hover.index = i;
            mSegments.Add(segment);
        }

        // The badge, in a ring of the seat's colour
        ShapeGraphic ring = Shape(root, "Ring", ShapeGraphic.Kind.Circle, border);
        Fit(ring.rectTransform, size.x - badge - 2f, -2f, badge + 4f, badge + 4f);
        mBadge = Shape(root, "Badge", ShapeGraphic.Kind.Circle, Color.white);
        Fit(mBadge.rectTransform, size.x - badge, 0f, badge, badge);

        mNumber = (TextMeshProUGUI)MenuUI.AddText(mBadge.rectTransform, "0", 16, Color.black, TextAlignmentOptions.Center);
        MenuUI.Place(mNumber.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        mNumber.fontStyle = FontStyles.Bold;
        mNumber.raycastTarget = false;
    }

    static ShapeGraphic Shape(Transform parent, string name, ShapeGraphic.Kind kind, Color color)
    {
        // The CanvasRenderer by hand: without it the shape isn't drawn, and the UI's raycasts throw (no clicks work)
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        obj.transform.SetParent(parent, false);
        ShapeGraphic shape = obj.AddComponent<ShapeGraphic>();
        shape.kind = kind;
        shape.color = color;
        shape.raycastTarget = false;
        return shape;
    }

    // x, y from the bar's top-left, in px
    static void Fit(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, height);
    }
}

using UnityEngine;
using UnityEngine.UI;

/* ZoneHighlighter's canvas, its zone highlights (a glow, a border and Ripple's rings) and its arrows.
 The rest of ZoneHighlighter is in ZoneHighlighter.cs. */
public partial class ZoneHighlighter
{
    const float ArrowThickness = 10f;
    const float ArrowHead = 34f;
    const int BoostChevrons = 3;
    const float BoostRunSeconds = 0.9f; // for a chevron to run the whole way

    class BoostArrow
    {
        public RectTransform root;
        public Image track;
        public RectTransform[] chevrons;
        public Image[][] parts; // each chevron's two sides
    }

    void BuildCanvas()
    {
        GameObject canvasObj = new GameObject("ZoneHighlightCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -4; // over HandZones' frames, under the HUD
        mCanvas = (RectTransform)canvasObj.transform; // no scaler: one unit is one screen pixel
        RotatedView.Follow(canvas);
    }

    Highlight BuildHighlight()
    {
        RectTransform root = AddRect(mCanvas, "ZoneHighlight");
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0.5f, 0.5f);

        RectTransform glow = AddRect(root, "Glow");
        glow.anchorMin = Vector2.zero;
        glow.anchorMax = Vector2.one;
        glow.offsetMin = glow.offsetMax = Vector2.zero;
        Image glowImage = glow.gameObject.AddComponent<Image>();
        glowImage.raycastTarget = false;

        return new Highlight
        {
            root = root.gameObject,
            glow = glowImage,
            edges = Ripple.AddEdges(root, 4f, zoneColor),
            ripple = Ripple.Add(root, zoneColor),
        };
    }

    // Laid along +x from its pivot at the start: a shaft, then two short sides meeting at the tip
    Arrow BuildArrow()
    {
        RectTransform root = AddRect(mCanvas, "ZoneArrow");
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0f, 0.5f);

        RectTransform shaft = AddRect(root, "Shaft");
        shaft.anchorMin = Vector2.zero;
        shaft.anchorMax = Vector2.one;
        shaft.offsetMin = Vector2.zero;
        shaft.offsetMax = new Vector2(-ArrowThickness * 0.5f, 0f);

        var parts = new Image[3];
        parts[0] = AddImage(shaft);
        for (int side = 0; side < 2; side++)
        {
            RectTransform head = AddRect(root, "Head");
            head.anchorMin = head.anchorMax = new Vector2(1f, 0.5f);
            head.pivot = new Vector2(1f, 0.5f); // turns about the tip
            head.sizeDelta = new Vector2(ArrowHead, ArrowThickness);
            head.localEulerAngles = new Vector3(0f, 0f, side == 0 ? 35f : -35f);
            parts[side + 1] = AddImage(head);
        }
        return new Arrow { root = root, parts = parts };
    }

    // Laid along +x from the booster's centre: a track, and chevrons (two sides meeting at a point) that ShowBoost runs along it
    BoostArrow BuildBoostArrow()
    {
        RectTransform root = AddRect(mCanvas, "BoostArrow");
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0f, 0.5f);

        RectTransform track = AddRect(root, "Track");
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = track.offsetMax = Vector2.zero;

        var boost = new BoostArrow
        {
            root = root,
            track = AddImage(track),
            chevrons = new RectTransform[BoostChevrons],
            parts = new Image[BoostChevrons][],
        };
        for (int i = 0; i < BoostChevrons; i++)
        {
            RectTransform chevron = AddRect(root, "Chevron");
            chevron.anchorMin = chevron.anchorMax = new Vector2(0f, 0.5f);
            chevron.sizeDelta = Vector2.zero;
            boost.chevrons[i] = chevron;
            boost.parts[i] = new Image[2];
            for (int side = 0; side < 2; side++)
            {
                RectTransform half = AddRect(chevron, "Side");
                half.anchorMin = half.anchorMax = new Vector2(0.5f, 0.5f);
                half.pivot = new Vector2(1f, 0.5f); // turns about the point
                half.sizeDelta = new Vector2(ArrowHead * 1.4f, ArrowThickness * 1.4f);
                half.localEulerAngles = new Vector3(0f, 0f, side == 0 ? 40f : -40f);
                boost.parts[i][side] = AddImage(half);
            }
        }
        root.gameObject.SetActive(false);
        return boost;
    }

    static RectTransform AddRect(Transform parent, string name)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }

    static Image AddImage(RectTransform rect)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}

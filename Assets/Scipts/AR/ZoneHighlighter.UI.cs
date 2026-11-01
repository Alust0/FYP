using UnityEngine;
using UnityEngine.UI;

/* ZoneHighlighter's canvas, its zone highlights (a glow, a border and Ripple's rings) and its arrows.
 The rest of ZoneHighlighter is in ZoneHighlighter.cs. */
public partial class ZoneHighlighter
{
    const float ArrowThickness = 10f;
    const float ArrowHead = 34f;

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

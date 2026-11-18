using UnityEngine;
using UnityEngine.UI;

// ZoneHighlighter's canvas and highlights
public partial class ZoneHighlighter
{
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

    static RectTransform AddRect(Transform parent, string name)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }
}

using UnityEngine;
using UnityEngine.UI;

/* Rings spreading out from a highlighted box and fading, so the eye goes to it: ZoneHighlighter's zones,
 HandZones' frames and AttackTargetPicker's boxes, each in its own colour. Runs while the box is shown. */
public class Ripple : MonoBehaviour
{
    public Color color = Color.white;
    [Tooltip("Seconds for one ring to spread out and fade.")]
    public float seconds = 1.2f;
    [Tooltip("How far a ring spreads past the box, as a fraction of its size.")]
    public float growth = 0.35f;

    const int Rings = 2; // half a ring apart, so one is always going out

    RectTransform[] mRings;
    Image[][] mEdges;
    bool mVisible = true;

    public static Ripple Add(RectTransform box, Color color)
    {
        Ripple ripple = box.gameObject.AddComponent<Ripple>();
        ripple.color = color;
        ripple.mRings = new RectTransform[Rings];
        ripple.mEdges = new Image[Rings][];
        for (int i = 0; i < Rings; i++)
        {
            GameObject obj = new GameObject("Ripple", typeof(RectTransform));
            obj.transform.SetParent(box, false);
            RectTransform ring = (RectTransform)obj.transform;
            ring.anchorMin = Vector2.zero;
            ring.anchorMax = Vector2.one;
            ring.offsetMin = ring.offsetMax = Vector2.zero;
            ring.pivot = new Vector2(0.5f, 0.5f); // grows out from the middle
            ripple.mRings[i] = ring;
            ripple.mEdges[i] = AddEdges(ring, 3f, color);
        }
        return ripple;
    }

    // Off for a box that's shown but not asking for anything, e.g. an attack target that can't be picked
    public void SetVisible(bool visible)
    {
        if (visible == mVisible) return;
        mVisible = visible;
        foreach (RectTransform ring in mRings) ring.gameObject.SetActive(visible);
    }

    void Update()
    {
        if (!mVisible) return;
        float t = Time.unscaledTime / seconds;
        for (int i = 0; i < Rings; i++)
        {
            float phase = Mathf.Repeat(t + (float)i / Rings, 1f);
            mRings[i].localScale = Vector3.one * (1f + growth * phase);
            Color edge = new Color(color.r, color.g, color.b, 0.9f * (1f - phase));
            foreach (Image image in mEdges[i]) image.color = edge;
        }
    }

    // A border just inside the rect: bottom, top, left, right
    public static Image[] AddEdges(RectTransform parent, float thickness, Color color)
    {
        (Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)[] sides =
        {
            (new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness)),
            (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero),
            (new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f)),
            (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero),
        };
        var edges = new Image[sides.Length];
        for (int i = 0; i < sides.Length; i++)
        {
            GameObject obj = new GameObject("Edge", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform edge = (RectTransform)obj.transform;
            edge.anchorMin = sides[i].min;
            edge.anchorMax = sides[i].max;
            edge.offsetMin = sides[i].offMin;
            edge.offsetMax = sides[i].offMax;
            edges[i] = obj.AddComponent<Image>();
            edges[i].color = color;
            edges[i].raycastTarget = false;
        }
        return edges;
    }
}

using UnityEngine;
using UnityEngine.UI;

/* A UI shape drawn from its own mesh, so the HUD needs no sprites: a slanted bar (StatBars' damage
 segments), a box with cut corners (its energy cells) or a circle (the number badges). Fills its rect. */
public class ShapeGraphic : MaskableGraphic
{
    public enum Kind { Slant, Rounded, Circle }

    public Kind kind = Kind.Rounded;
    [Range(0f, 1f)] public float slant = 0.35f; // how far the top leans right, as a fraction of the height

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        switch (kind)
        {
            case Kind.Slant:
                float lean = r.height * slant;
                Quad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + lean, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - lean, r.yMin));
                break;

            case Kind.Rounded:
                // An octagon: the corners cut off
                float c = Mathf.Min(r.width, r.height) * 0.22f;
                Fan(vh, r.center, new[]
                {
                    new Vector2(r.xMin + c, r.yMin), new Vector2(r.xMin, r.yMin + c), new Vector2(r.xMin, r.yMax - c), new Vector2(r.xMin + c, r.yMax),
                    new Vector2(r.xMax - c, r.yMax), new Vector2(r.xMax, r.yMax - c), new Vector2(r.xMax, r.yMin + c), new Vector2(r.xMax - c, r.yMin),
                });
                break;

            case Kind.Circle:
                const int Sides = 28;
                var rim = new Vector2[Sides];
                for (int i = 0; i < Sides; i++)
                {
                    float a = -i * 2f * Mathf.PI / Sides; // clockwise, as the quads are
                    rim[i] = r.center + new Vector2(Mathf.Cos(a) * r.width, Mathf.Sin(a) * r.height) * 0.5f;
                }
                Fan(vh, r.center, rim);
                break;
        }
    }

    void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int start = vh.currentVertCount;
        foreach (Vector2 p in new[] { a, b, c, d }) vh.AddVert(p, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    void Fan(VertexHelper vh, Vector2 center, Vector2[] rim)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        foreach (Vector2 p in rim) vh.AddVert(p, color, Vector2.zero);
        for (int i = 0; i < rim.Length; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % rim.Length);
    }
}

using UnityEngine;
using UnityEngine.UI;

// Reusable arrow between two boxes: Pulse (card to move) or Chevrons (boost, guard)
public class BoardArrow : MonoBehaviour
{
    public enum Style { Pulse, Chevrons }

    public Color color = Color.white;
    [Tooltip("Pulse: seconds for one pulse. Chevrons: seconds for a chevron to run the whole way.")]
    public float seconds = 0.8f;

    const float Thickness = 10f;
    const float Head = 55f;
    const int ChevronCount = 3;

    Style mStyle;
    RectTransform mRoot;
    Image[] mParts;          // Pulse: shaft and the two sides of the head
    Image mTrack;            // Chevrons (>): the faint line under them
    RectTransform[] mChevrons;
    Image[][] mSides;        // each chevron's two sides
    float mLength;
    float mSize = 1f;        // thickness and head scale
    float mSince;            // when it last came on
    static float sReplay;    // Undo: every arrow runs in again from here
    const float DrawInSeconds = 0.3f;

    // Every arrow on screen runs in again from its start (Undo)
    public static void Replay() => sReplay = Time.unscaledTime;

    float Age => Time.unscaledTime - Mathf.Max(mSince, sReplay);

    public static BoardArrow Add(RectTransform parent, Style style, Color color, float size = 1f)
    {
        RectTransform root = AddRect(parent, style == Style.Pulse ? "Arrow" : "ChevronArrow");
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0f, 0.5f); // laid along +x from the start

        BoardArrow arrow = root.gameObject.AddComponent<BoardArrow>();
        arrow.mStyle = style;
        arrow.mRoot = root;
        arrow.color = color;
        arrow.mSize = size;
        if (style == Style.Pulse) { arrow.BuildPulse(); }
        else
        {
            arrow.seconds = 0.9f;
            arrow.BuildChevrons();
        }
        root.gameObject.SetActive(false);
        return arrow;
    }

    // Points it from one box to the other (rects in the parent's pixels) and shows it
    public void Show(Rect from, Rect to)
    {
        if (!mRoot.gameObject.activeSelf)
        {
            mRoot.gameObject.SetActive(true);
            mSince = Time.unscaledTime;
        }
        Vector2 direction = (to.center - from.center).normalized;
        Vector2 start = from.center, end = to.center;
        if (mStyle == Style.Pulse)
        {
            // Edge to edge, so the boxes stay readable
            start += direction * Mathf.Min(from.width, from.height) * 0.4f;
            end -= direction * Mathf.Min(to.width, to.height) * 0.4f;
        }
        mLength = Mathf.Max(0f, (end - start).magnitude);
        if (mStyle == Style.Pulse)
        {
            mLength *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Age / DrawInSeconds)); // grows out from the start
        }
        mRoot.anchoredPosition = start;
        mRoot.sizeDelta = new Vector2(mLength, Thickness * mSize);
        mRoot.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    public void Hide()
    {
        if (mRoot.gameObject.activeSelf) { mRoot.gameObject.SetActive(false); }
    }

    void Update()
    {
        if (mStyle == Style.Pulse)
        {
            float alpha = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / seconds));
            foreach (Image part in mParts) part.color = new Color(color.r, color.g, color.b, alpha);
            return;
        }

        float age = Age;
        mTrack.color = new Color(color.r, color.g, color.b, 0.3f * Mathf.Clamp01(age / DrawInSeconds));
        for (int i = 0; i < mChevrons.Length; i++)
        {
            // From the start, one after another
            float raw = age / seconds - i / (float)mChevrons.Length;
            float along = Mathf.Repeat(raw, 1f);
            mChevrons[i].anchoredPosition = new Vector2(along * mLength, 0f);
            float alpha = raw < 0f ? 0f : Mathf.Sin(along * Mathf.PI);
            Color shade = new Color(color.r, color.g, color.b, alpha);
            foreach (Image side in mSides[i]) side.color = shade;
        }
    }

    // A shaft, then two short sides meeting at the tip
    void BuildPulse()
    {
        RectTransform shaft = AddRect(mRoot, "Shaft");
        shaft.anchorMin = Vector2.zero;
        shaft.anchorMax = Vector2.one;
        shaft.offsetMin = Vector2.zero;
        shaft.offsetMax = new Vector2(-Thickness * mSize * 0.5f, 0f);

        mParts = new Image[3];
        mParts[0] = AddImage(shaft);
        for (int side = 0; side < 2; side++)
        {
            RectTransform head = AddRect(mRoot, "Head");
            head.anchorMin = head.anchorMax = new Vector2(1f, 0.5f);
            head.pivot = new Vector2(1f, 0.5f); // turns about the tip
            head.sizeDelta = new Vector2(Head, Thickness) * mSize;
            head.localEulerAngles = new Vector3(0f, 0f, side == 0 ? 35f : -35f);
            mParts[side + 1] = AddImage(head);
        }
    }

    // -, and > (two sides meeting at a point) that Update runs along it
    void BuildChevrons()
    {
        RectTransform track = AddRect(mRoot, "Track");
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = track.offsetMax = Vector2.zero;
        mTrack = AddImage(track);

        mChevrons = new RectTransform[ChevronCount];
        mSides = new Image[ChevronCount][];
        for (int i = 0; i < ChevronCount; i++)
        {
            RectTransform chevron = AddRect(mRoot, "Chevron");
            chevron.anchorMin = chevron.anchorMax = new Vector2(0f, 0.5f);
            chevron.sizeDelta = Vector2.zero;
            mChevrons[i] = chevron;
            mSides[i] = new Image[2];
            for (int side = 0; side < 2; side++)
            {
                RectTransform half = AddRect(chevron, "Side");
                half.anchorMin = half.anchorMax = new Vector2(0.5f, 0.5f);
                half.pivot = new Vector2(1f, 0.5f); // turns about the point
                half.sizeDelta = new Vector2(Head * 1.4f, Thickness * 1.4f) * mSize;
                half.localEulerAngles = new Vector3(0f, 0f, side == 0 ? 40f : -40f);
                mSides[i][side] = AddImage(half);
            }
        }
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

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// A shield over the attacked unit during the battle: it shatters on a hit and holds on a miss
public class ShieldBreak : MonoBehaviour
{
    public float size = 0.95f;      // shield height, times the card box's shorter side
    public bool ownUnitsToo = true; // online: false shows shields only on the opponent's units
    public Color glowColor = new Color(0.45f, 0.85f, 1f);
    public Color hitColor = new Color(1f, 0.15f, 0.05f);

    const int H = 320;            // shield copy's height; its width follows the picture
    static int W = 240;
    const int Wedges = 8;         // outer shards; half as many inner ones
    static readonly Vector2 Impact = new Vector2(0.55f, 0.56f); // where the cracks start (uv)

    static Texture2D sShield, sCracked, sMask, sRing;
    static Texture2D[] sShards;
    static Vector2[] sCentroids;

    RectTransform mCanvas;
    RulesEngine mRules;
    BoardLayout mBoard;
    GameState mGame;
    RectTransform mLive;          // the shield shown while the attack goes on
    RawImage mLiveShield, mLiveGlow;
    string mLiveKey;
    float mLiveSince;
    Rect mLiveBox;

    void Start()
    {
        MakeTextures();
        if (sShield == null)
        {
            enabled = false; // no picture: no shields
            return;
        }
        GameObject canvasObj = new GameObject("ShieldBreakCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // over the camera views and highlights, under the pickers
        mCanvas = (RectTransform)canvasObj.transform; // no scaler: one unit is one screen pixel
        RotatedView.Follow(canvas);

        mLive = AddRect(mCanvas, "Shield");
        mLive.anchorMin = mLive.anchorMax = Vector2.zero;
        mLiveGlow = AddRaw(mLive, "Glow", sMask);
        mLiveShield = AddRaw(mLive, "Body", sShield);
        mLive.gameObject.SetActive(false);
        RulesEngine.OnHitDecided += Decided;
        RulesEngine.OnUndone += Undone;
    }

    void OnDestroy()
    {
        RulesEngine.OnHitDecided -= Decided;
        RulesEngine.OnUndone -= Undone;
        if (mCanvas != null) { Destroy(mCanvas.gameObject); }
    }

    // Undo: the results shown go, and the live shield pops in again (RulesEngine replays a decided hit)
    void Undone()
    {
        StopAllCoroutines();
        foreach (GameObject result in mResults)
        {
            if (result != null) { Destroy(result); }
        }
        mResults.Clear();
        mLiveKey = null;
    }

    readonly System.Collections.Generic.List<GameObject> mResults = new System.Collections.Generic.List<GameObject>(); // Break/Hold shields on screen

    bool Wanted(PlayerState defender) =>
        defender != null && (ownUnitsToo || !MatchSession.IsOnline || !MatchSession.IsLocal(defender));

    Vector2 ShieldSize(Rect box)
    {
        float height = Mathf.Min(box.width, box.height) * size;
        return new Vector2(height * W / H, height);
    }

    // Follows the attack's target from the guard step until the hit is decided
    void Update()
    {
        if (mRules == null) { mRules = FindFirstObjectByType<RulesEngine>(); }
        if (mBoard == null) { mBoard = FindFirstObjectByType<BoardLayout>(); }
        var attack = mRules != null ? mRules.AttackArrow : null;
        if (attack == null || !Wanted(attack.Value.defender) ||
            !ZoneHighlighter.TryGetScreenRect(mBoard, attack.Value.defender, attack.Value.to, out Rect box))
        {
            mLiveKey = null;
            if (mLive.gameObject.activeSelf) { mLive.gameObject.SetActive(false); }
            return;
        }

        string key = attack.Value.defender.playerIndex + "_" + attack.Value.to;
        if (key != mLiveKey)
        {
            mLiveKey = key;
            mLiveSince = Time.unscaledTime;
        }
        mLiveBox = box;
        if (!mLive.gameObject.activeSelf) { mLive.gameObject.SetActive(true); }

        // Pops in, then bobs gently with a pulsing glow
        float t = Time.unscaledTime - mLiveSince;
        float appear = Mathf.Clamp01(t / 0.3f);
        float scale = Mathf.LerpUnclamped(0.5f, 1f, EaseOutBack(appear)) * (1f + 0.02f * Mathf.Sin(t * 4f));
        mLive.anchoredPosition = box.center;
        mLive.sizeDelta = ShieldSize(box) * scale;
        Grow(mLiveGlow, mLive.sizeDelta.y * 0.1f);
        mLiveShield.color = new Color(1f, 1f, 1f, appear);
        mLiveGlow.color = new Color(glowColor.r, glowColor.g, glowColor.b, appear * (0.3f + 0.2f * Mathf.Sin(t * 5f)));
    }

    void Decided(PlayerState defender, string zone, bool hit)
    {
        if (!Wanted(defender)) { return; }
        string key = defender.playerIndex + "_" + zone;
        if (!ZoneHighlighter.TryGetScreenRect(mBoard, defender, zone, out Rect box))
        {
            if (key != mLiveKey) { return; }
            box = mLiveBox;
        }
        mLiveKey = null;
        mLive.gameObject.SetActive(false);

        // Stays until the battle moves on: a damage check still to do, or the next attack / end of the battle
        bool damageCheck = hit && mRules != null && mRules.Step == RulesEngine.BattleStep.Damage;
        System.Func<bool> waiting = damageCheck
            ? () => Battle(RulesEngine.BattleStep.Damage)
            : hit ? () => Battle(RulesEngine.BattleStep.Close) && defender.CardOnCircle(zone) != null // a hit rear-guard: until it's retired
            : () => Battle(RulesEngine.BattleStep.Close);
        System.Func<Rect> follow = () => ZoneHighlighter.TryGetScreenRect(mBoard, defender, zone, out Rect now) ? now : box;
        StartCoroutine(hit ? Break(box, waiting, follow) : Hold(box, waiting, follow));
    }

    bool Battle(RulesEngine.BattleStep step)
    {
        if (mGame == null) { mGame = FindFirstObjectByType<GameState>(); }
        return mRules != null && mRules.Step == step && mRules.AttackArrow == null && (mGame == null || !mGame.gameOver);
    }

    // Cracks, stays cracked until the battle moves on, then flies apart in shards with a red flash on the card
    IEnumerator Break(Rect box, System.Func<bool> waiting, System.Func<Rect> follow)
    {
        Vector2 shieldSize = ShieldSize(box);
        RectTransform root = AddRect(mCanvas, "ShieldBreak");
        mResults.Add(root.gameObject);
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.anchoredPosition = box.center;
        root.sizeDelta = shieldSize;

        Image flash = AddCentred(root, "Flash", box.size).gameObject.AddComponent<Image>();
        flash.raycastTarget = false;
        RawImage glow = AddRaw(root, "Glow", sMask);
        Grow(glow, shieldSize.y * 0.1f);
        RawImage body = AddRaw(root, "Body", sCracked);

        // Cracks spread with a shake
        float start = Time.unscaledTime;
        for (float t = 0f; t < 0.22f; t = Time.unscaledTime - start)
        {
            body.rectTransform.anchoredPosition = Random.insideUnitCircle * shieldSize.y * 0.035f;
            glow.color = new Color(1f, 1f, 1f, 0.8f * (1f - t / 0.22f));
            flash.color = new Color(hitColor.r, hitColor.g, hitColor.b, 0.5f * t / 0.22f);
            yield return null;
        }

        // Cracked and trembling, glowing red, while the damage check (or retire) is still to come
        body.rectTransform.anchoredPosition = Vector2.zero;
        start = Time.unscaledTime;
        while (waiting())
        {
            float t = Time.unscaledTime - start;
            box = follow();
            root.anchoredPosition = box.center;
            body.rectTransform.anchoredPosition = Random.insideUnitCircle * shieldSize.y * 0.006f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6f);
            glow.color = new Color(hitColor.r, hitColor.g, hitColor.b, 0.35f + 0.35f * pulse);
            flash.color = new Color(hitColor.r, hitColor.g, hitColor.b, Mathf.Lerp(0.5f, 0.12f + 0.1f * pulse, Mathf.Clamp01(t / 0.3f)));
            yield return null;
        }
        body.gameObject.SetActive(false);
        glow.gameObject.SetActive(false);

        // Shards, sparks and a shock ring
        int count = sShards.Length;
        var shards = new RectTransform[count];
        var shardImages = new RawImage[count];
        var velocity = new Vector2[count];
        var spin = new float[count];
        for (int i = 0; i < count; i++)
        {
            shards[i] = AddRect(root, "Shard");
            shards[i].anchorMin = shards[i].anchorMax = shards[i].pivot = sCentroids[i];
            shards[i].sizeDelta = shieldSize;
            shardImages[i] = shards[i].gameObject.AddComponent<RawImage>();
            shardImages[i].texture = sShards[i];
            shardImages[i].raycastTarget = false;
            Vector2 away = Vector2.Scale(sCentroids[i] - Impact, shieldSize);
            away = away.sqrMagnitude > 1f ? away.normalized : Random.insideUnitCircle.normalized;
            velocity[i] = away * shieldSize.y * Random.Range(1.4f, 2.6f) + Vector2.up * shieldSize.y * 0.8f;
            spin[i] = Random.Range(-360f, 360f);
        }

        RawImage ring = AddCentred(root, "Ring", Vector2.zero).gameObject.AddComponent<RawImage>();
        ring.texture = sRing;
        ring.raycastTarget = false;

        const int SparkCount = 14;
        var sparks = new RectTransform[SparkCount];
        var sparkImages = new Image[SparkCount];
        var sparkVelocity = new Vector2[SparkCount];
        for (int i = 0; i < SparkCount; i++)
        {
            sparks[i] = AddCentred(root, "Spark", Vector2.one * shieldSize.y * Random.Range(0.04f, 0.07f));
            sparks[i].localEulerAngles = new Vector3(0f, 0f, 45f);
            sparkImages[i] = sparks[i].gameObject.AddComponent<Image>();
            sparkImages[i].raycastTarget = false;
            sparkVelocity[i] = Random.insideUnitCircle.normalized * shieldSize.y * Random.Range(2f, 3.5f);
        }

        const float Seconds = 0.9f;
        Vector2 gravity = Vector2.down * shieldSize.y * 5f;
        start = Time.unscaledTime;
        for (float t = 0f; t < Seconds; t = Time.unscaledTime - start)
        {
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, Seconds, t));
            for (int i = 0; i < count; i++)
            {
                shards[i].anchoredPosition = velocity[i] * t + 0.5f * gravity * t * t;
                shards[i].localEulerAngles = new Vector3(0f, 0f, spin[i] * t);
                shards[i].localScale = Vector3.one * (1f - 0.3f * t);
                shardImages[i].color = new Color(1f, 1f, 1f, fade);
            }
            for (int i = 0; i < SparkCount; i++)
            {
                sparks[i].anchoredPosition = sparkVelocity[i] * t;
                sparkImages[i].color = new Color(0.85f, 0.95f, 1f, Mathf.Clamp01(1f - t / 0.5f));
            }
            float ringT = Mathf.Clamp01(t / 0.45f);
            ring.rectTransform.sizeDelta = Vector2.one * shieldSize.y * Mathf.Lerp(0.6f, 2.2f, ringT);
            ring.color = new Color(1f, 0.6f, 0.35f, 0.9f * (1f - ringT));
            flash.color = new Color(hitColor.r, hitColor.g, hitColor.b, 0.5f * Mathf.Clamp01(1f - t / 0.4f));
            yield return null;
        }
        mResults.Remove(root.gameObject);
        Destroy(root.gameObject);
    }

    // Flashes and swells as it takes the blow, glows whole until the battle moves on, then fades away
    IEnumerator Hold(Rect box, System.Func<bool> waiting, System.Func<Rect> follow)
    {
        Vector2 shieldSize = ShieldSize(box);
        RectTransform root = AddRect(mCanvas, "ShieldHold");
        mResults.Add(root.gameObject);
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.anchoredPosition = box.center;
        root.sizeDelta = shieldSize;

        RawImage ring = AddCentred(root, "Ring", Vector2.zero).gameObject.AddComponent<RawImage>();
        ring.texture = sRing;
        ring.raycastTarget = false;
        RawImage glow = AddRaw(root, "Glow", sMask);
        Grow(glow, shieldSize.y * 0.1f);
        RawImage body = AddRaw(root, "Body", sShield);
        RawImage flash = AddRaw(root, "Flash", sMask);

        // The blow: a white flash, a punch and a ring
        float start = Time.unscaledTime;
        for (float t = 0f; t < 0.6f || waiting(); t = Time.unscaledTime - start)
        {
            root.anchoredPosition = follow().center;
            float punch = t < 0.35f ? 0.14f * Mathf.Sin(Mathf.PI * t / 0.35f) : 0f;
            float bob = t < 0.6f ? 0f : 0.02f * Mathf.Sin(t * 4f);
            root.localScale = Vector3.one * (1f + punch + bob);
            body.color = Color.white;
            flash.color = new Color(1f, 1f, 1f, 0.85f * Mathf.Clamp01(1f - t / 0.35f));
            float steady = 0.3f + 0.2f * Mathf.Sin(t * 5f);
            glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, Mathf.Lerp(0.9f, steady, Mathf.Clamp01(t / 0.5f)));
            float ringT = Mathf.Clamp01(t / 0.5f);
            ring.rectTransform.sizeDelta = Vector2.one * shieldSize.y * Mathf.Lerp(0.8f, 2f, ringT);
            ring.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0.9f * (1f - ringT));
            yield return null;
        }

        // The battle's over: swells a little and fades
        const float Seconds = 0.45f;
        start = Time.unscaledTime;
        for (float t = 0f; t < Seconds; t = Time.unscaledTime - start)
        {
            float fade = 1f - t / Seconds;
            root.localScale = Vector3.one * (1f + 0.1f * (1f - fade));
            body.color = new Color(1f, 1f, 1f, fade);
            flash.color = Color.clear;
            glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, fade * 0.3f);
            ring.color = Color.clear;
            yield return null;
        }
        mResults.Remove(root.gameObject);
        Destroy(root.gameObject);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // The shield picture (Resources/Effects/Shield.png, CC0 "Shield" by knik1985 on OpenGameArt) copied at H pixels high
    static Color32[] ReadShield()
    {
        Texture2D source = Resources.Load<Texture2D>("Effects/Shield");
        if (source == null)
        {
            Debug.LogWarning("ShieldBreak: Resources/Effects/Shield.png is missing.");
            return null;
        }
        W = Mathf.Max(1, Mathf.RoundToInt(H * (float)source.width / source.height));
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture active = RenderTexture.active;
        Graphics.Blit(source, rt); // the picture needn't be readable
        RenderTexture.active = rt;
        var copy = new Texture2D(W, H, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        copy.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        Color32[] pixels = copy.GetPixels32();
        Destroy(copy);
        return pixels;
    }

    // The shield, a cracked copy, its silhouette, its shards and a ring, made once
    static void MakeTextures()
    {
        if (sShield != null) { return; }
        Color32[] shield = ReadShield();
        if (shield == null) { return; }
        var mask = new Color32[W * H];
        for (int p = 0; p < W * H; p++) mask[p] = new Color32(255, 255, 255, shield[p].a);

        // Shards: jagged wedges round the impact point, the inner ring split again
        var rng = new System.Random(7);
        var bounds = new float[Wedges];
        float step = 2f * Mathf.PI / Wedges;
        for (int i = 0; i < Wedges; i++) bounds[i] = (i + 0.5f * ((float)rng.NextDouble() - 0.5f)) * step;
        int shardCount = Wedges + Wedges / 2;
        var index = new int[W * H];
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                float dx = px + 0.5f - Impact.x * W, dy = py + 0.5f - Impact.y * H;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Repeat(Mathf.Atan2(dy, dx), 2f * Mathf.PI);
                int sector = Mathf.RoundToInt(angle / step) % Wedges; // each crack zigzags its own way
                float a = Mathf.Repeat(angle + 0.16f * Zigzag(r / 16f, sector, 0) * Mathf.Min(1f, r / 25f), 2f * Mathf.PI);
                int wedge = Wedges - 1;
                for (int i = 1; i < Wedges; i++) if (a < bounds[i]) { wedge = i - 1; break; }
                if (a < bounds[0]) { wedge = Wedges - 1; }
                float inner = 0.2f * H * (1f + 0.25f * Zigzag(angle / (2f * Mathf.PI) * 7f, 99, 7));
                index[py * W + px] = r < inner ? Wedges + wedge / 2 : wedge;
            }

        var crack = new bool[W * H];
        for (int py = 0; py < H - 1; py++)
            for (int px = 0; px < W - 1; px++)
            {
                int p = py * W + px;
                if (shield[p].a < 40) { continue; } // not the soft shadow round it
                if (index[p] != index[p + 1] || index[p] != index[p + W]) { crack[p] = crack[p + 1] = crack[p + W] = true; }
            }

        var cracked = (Color32[])shield.Clone();
        var pieces = new Color32[shardCount][];
        var sums = new Vector3[shardCount];
        for (int k = 0; k < shardCount; k++) pieces[k] = new Color32[W * H];
        for (int p = 0; p < W * H; p++)
        {
            if (shield[p].a == 0) { continue; }
            if (crack[p])
            {
                cracked[p] = new Color32(25, 28, 45, shield[p].a); // dark lines on the bright metal
                continue;
            }
            pieces[index[p]][p] = shield[p];
            float weight = shield[p].a / 255f;
            sums[index[p]] += new Vector3(((p % W) + 0.5f) / W * weight, ((p / W) + 0.5f) / H * weight, weight);
        }

        sShield = ToTexture(shield, W, H);
        sCracked = ToTexture(cracked, W, H);
        sMask = ToTexture(mask, W, H);
        int kept = 0;
        for (int k = 0; k < shardCount; k++) if (sums[k].z > 0f) { kept++; }
        sShards = new Texture2D[kept];
        sCentroids = new Vector2[kept];
        for (int k = 0, n = 0; k < shardCount; k++)
        {
            if (sums[k].z <= 0f) { continue; }
            sShards[n] = ToTexture(pieces[k], W, H);
            sCentroids[n++] = new Vector2(sums[k].x / sums[k].z, sums[k].y / sums[k].z);
        }

        const int R = 128;
        var ring = new Color32[R * R];
        for (int py = 0; py < R; py++)
            for (int px = 0; px < R; px++)
            {
                float d = new Vector2(px + 0.5f - R * 0.5f, py + 0.5f - R * 0.5f).magnitude / (R * 0.5f);
                float alpha = d < 1f ? Mathf.Exp(-Mathf.Pow((d - 0.85f) / 0.07f, 2f)) : 0f;
                ring[py * R + px] = new Color(1f, 1f, 1f, alpha);
            }
        sRing = ToTexture(ring, R, R);
    }

    // Straight-edged random zigzag, -1..1 (period > 0 makes it wrap round)
    static float Zigzag(float t, int line, int period)
    {
        int i = Mathf.FloorToInt(t);
        float f = t - i;
        int next = i + 1;
        if (period > 0)
        {
            i = ((i % period) + period) % period;
            next = ((next % period) + period) % period;
        }
        return Mathf.Lerp(Hash(i, line), Hash(next, line), f) * 2f - 1f;
    }

    static float Hash(int i, int j)
    {
        float v = Mathf.Sin(i * 127.1f + j * 311.7f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    static Texture2D ToTexture(Color32[] pixels, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    // Fills its parent
    static RawImage AddRaw(RectTransform parent, string name, Texture texture)
    {
        RectTransform rect = AddRect(parent, name);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        RawImage image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    // A stretched image this many pixels bigger than its parent on each side (the glow)
    static void Grow(RawImage image, float pixels)
    {
        image.rectTransform.offsetMin = new Vector2(-pixels, -pixels);
        image.rectTransform.offsetMax = new Vector2(pixels, pixels);
    }

    static RectTransform AddCentred(RectTransform parent, string name, Vector2 boxSize)
    {
        RectTransform rect = AddRect(parent, name);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = boxSize;
        return rect;
    }

    static RectTransform AddRect(Transform parent, string name)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }
}

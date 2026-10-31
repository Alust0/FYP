using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Vuforia;

/* Tracks face-down cards by their backs: every picture in StreamingAssets/CardBack (the plain card
 back, CardBack.png, plus any extra sleeve pictures put there) and the sleeves the match's decks
 picked in the deck builder (UseSleeve, from DeckTargetLoader). Several targets are made from each
 picture, as with identical deck cards, since one target only follows one physical card. A ride
 deck sleeve (or a picture with "Ride" in its name) gets fewer, as the ride deck never goes face
 down elsewhere. Used for:
  - Counter Blast: a damage card that disappears with a card back showing in its place has been
    flipped, so VuforiaCardOverlayManager can turn it face down straight away.
  - Drawing: the deck's top card back going out of view and coming back is a hand taking a card,
    reported through OnDeckTouched. Added by UISetup. */
public class CardBackTracker : MonoBehaviour
{
    public static CardBackTracker Instance { get; private set; }

    // Zone key (e.g. "P1_Deck") whose card back was hidden for a while and is seen again, and when it went
    public static System.Action<string, float> OnDeckTouched;

    [Tooltip("Card backs that can be followed at once: face-down damage, the deck and the ride deck. Every one uses a slot of Vuforia's simultaneous image target limit.")]
    [Range(1, 10)] public int copies = 6;
    [Tooltip("Copies of a picture with \"Ride\" in its name, for the ride deck's sleeve.")]
    [Range(1, 10)] public int rideCopies = 1;
    public float cardWidth = 0.063f;
    [Tooltip("Width of a sleeve picture (any picture but CardBack.png); sleeves are a little wider than cards.")]
    public float sleeveWidth = 0.067f;
    [Tooltip("Shortest time the deck's card back must be hidden to count as a hand taking a card, so tracking flicker doesn't.")]
    public float minDeckHiddenSeconds = 0.3f;
    public float maxDeckHiddenSeconds = 8f;
    [Tooltip("Seconds the deck's card back must have been in view before a touch counts.")]
    public float deckArmSeconds = 1f;

    const string Folder = "CardBack";
    const string PlainBack = "CardBack.png";

    class Back
    {
        public ImageTargetBehaviour target;
        public bool tracked;
        public string zoneKey; // board zone its center is over, null if none or lost
    }

    class DeckWatch
    {
        public bool visible;
        public float visibleSince;
        public float hiddenSince;
        public float lastHiddenStart = float.NegativeInfinity, lastHiddenEnd = float.NegativeInfinity; // its last time out of view
    }

    readonly List<Back> mBacks = new List<Back>();
    readonly Dictionary<string, DeckWatch> mDecks = new Dictionary<string, DeckWatch>();
    BoardLayout mBoard;
    bool mReady; // Vuforia is running and the folder's pictures have targets

    // Decks' sleeves waiting for targets; static, as decks can load before this is added
    static readonly List<(string id, bool rideDeck)> sSleeves = new List<(string, bool)>();
    static readonly HashSet<string> sSleevesUsed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        sSleeves.Clear(); // the next match's decks bring their own
        sSleevesUsed.Clear();
    }

    // A deck's sleeve (SleeveLibrary id) to follow; null, empty or already added does nothing
    public static void UseSleeve(string sleeveId, bool rideDeck)
    {
        if (!SleeveLibrary.IsSleeve(sleeveId) || !sSleevesUsed.Add(sleeveId)) return;
        sSleeves.Add((sleeveId, rideDeck));
    }

    IEnumerator Start()
    {
        if (VuforiaBehaviour.Instance == null)
        {
            enabled = false;
            yield break;
        }
        yield return new WaitUntil(() => VuforiaApplication.Instance.IsRunning);

        string folder = Path.Combine(Application.streamingAssetsPath, Folder);
        var pictures = new List<string>();
        if (Directory.Exists(folder))
            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") pictures.Add(file);
            }
        if (pictures.Count == 0) Debug.LogWarning("[CardBackTracker] No card back pictures in " + folder);

        foreach (string path in pictures)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            bool plain = Path.GetFileName(path).Equals(PlainBack, System.StringComparison.OrdinalIgnoreCase);
            bool ride = name.IndexOf("Ride", System.StringComparison.OrdinalIgnoreCase) >= 0;
            AddTargets(path, plain ? cardWidth : sleeveWidth, ride ? rideCopies : copies, name);
        }
        Debug.Log("[CardBackTracker] " + mBacks.Count + " card back targets ready from " + pictures.Count + " pictures.");
        mReady = true;
    }

    void AddTargets(string path, float width, int count, string name)
    {
        for (int i = 0; i < count; i++)
        {
            ImageTargetBehaviour target = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(path, width, "CardBack#" + name + "#" + i);
            if (target == null) continue;

            Back back = new Back { target = target };
            target.OnTargetStatusChanged += (_, status) => back.tracked = status.Status == Status.TRACKED;
            mBacks.Add(back);
        }
    }

    void AddSleeves()
    {
        foreach ((string id, bool rideDeck) in sSleeves)
        {
            string path = SleeveLibrary.GetPath(id);
            if (path == null)
            {
                Debug.LogWarning("[CardBackTracker] No picture for sleeve " + id);
                continue;
            }
            int before = mBacks.Count;
            AddTargets(LargeEnough(path), sleeveWidth, rideDeck ? rideCopies : copies, id);
            Debug.Log("[CardBackTracker] Sleeve " + id + (rideDeck ? " (ride deck)" : " (deck)") + ": " + (mBacks.Count - before) + " targets.");
        }
        sSleeves.Clear();
    }

    /* Vuforia wants pictures at least 320 px wide and the sleeve pictures are 300, so a narrower one
     is saved at twice the size in the temporary cache and that copy is used */
    static string LargeEnough(string path)
    {
        var small = new Texture2D(2, 2);
        if (!small.LoadImage(File.ReadAllBytes(path)) || small.width >= 320)
        {
            Destroy(small);
            return path;
        }

        int width = small.width * 2, height = small.height * 2;
        RenderTexture scaled = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(small, scaled);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = scaled;
        var large = new Texture2D(width, height, TextureFormat.RGB24, false);
        large.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        large.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(scaled);

        string folder = Path.Combine(Application.temporaryCachePath, "Sleeves");
        Directory.CreateDirectory(folder);
        string largePath = Path.Combine(folder, Path.GetFileName(path));
        File.WriteAllBytes(largePath, large.EncodeToPNG());
        Destroy(small);
        Destroy(large);
        return largePath;
    }

    void Update()
    {
        if (mReady && sSleeves.Count > 0) AddSleeves();

        Camera cam = Camera.main;
        if (mBoard == null) mBoard = FindFirstObjectByType<BoardLayout>();
        if (cam == null || mBoard == null) return;

        var decksSeen = new HashSet<string>();
        foreach (Back back in mBacks)
        {
            back.zoneKey = null;
            if (!back.tracked) continue;

            Vector3 screen = cam.WorldToScreenPoint(back.target.transform.position);
            if (screen.z <= 0f) continue;
            back.zoneKey = mBoard.ZoneAtScreenPoint(screen, cam);
            if (back.zoneKey != null && back.zoneKey.EndsWith("_Deck")) decksSeen.Add(back.zoneKey);
        }

        foreach (string key in decksSeen)
            if (!mDecks.ContainsKey(key)) mDecks[key] = new DeckWatch();

        foreach (KeyValuePair<string, DeckWatch> deck in mDecks)
            UpdateDeck(deck.Key, deck.Value, decksSeen.Contains(deck.Key));
    }

    void UpdateDeck(string key, DeckWatch deck, bool seen)
    {
        float now = Time.time;
        if (seen == deck.visible) return;
        deck.visible = seen;

        if (!seen)
        {
            deck.hiddenSince = now;
            return;
        }

        float hidden = now - deck.hiddenSince;
        bool wasArmed = deck.hiddenSince - deck.visibleSince >= deckArmSeconds;
        deck.lastHiddenStart = deck.hiddenSince;
        deck.lastHiddenEnd = now;
        deck.visibleSince = now;
        if (wasArmed && hidden >= minDeckHiddenSeconds && hidden <= maxDeckHiddenSeconds)
            OnDeckTouched?.Invoke(key, deck.hiddenSince);
    }

    /* Whether the deck's card back was out of view at some point between from and to (Time.time),
     still being out of view counting too: lets a draw count as soon as the hand leaves, without
     waiting for Vuforia to find the card back again */
    public bool DeckHiddenDuring(string key, float from, float to)
    {
        if (key == null || !mDecks.TryGetValue(key, out DeckWatch deck)) return false;
        if (!deck.visible && deck.hiddenSince <= to) return true;
        return deck.lastHiddenStart <= to && deck.lastHiddenEnd >= from;
    }

    // Time.time the deck's card back was last in view; never seen is minus infinity
    public float DeckLastSeen(string key)
    {
        if (key == null || !mDecks.TryGetValue(key, out DeckWatch deck)) return float.NegativeInfinity;
        return deck.visible ? Time.time : deck.hiddenSince;
    }

    // Whether a card back is in view within maxPixels of a screen point, e.g. where a damage card was
    public bool IsBackAt(Camera cam, Vector2 screenPoint, float maxPixels)
    {
        foreach (Back back in mBacks)
        {
            if (!back.tracked) continue;
            Vector3 screen = cam.WorldToScreenPoint(back.target.transform.position);
            if (screen.z > 0f && Vector2.Distance(screen, screenPoint) <= maxPixels) return true;
        }
        return false;
    }

    // Card backs in view on one board zone, e.g. "P1_Damage"
    public int CountIn(string zoneKey)
    {
        int count = 0;
        foreach (Back back in mBacks)
            if (back.zoneKey == zoneKey) count++;
        return count;
    }
}

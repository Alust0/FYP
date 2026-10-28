using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Vuforia;

/* Tracks face-down cards by the card back every card shares (StreamingAssets/CardBack/CardBack.png).
 Several targets are made from the one picture, as with identical deck cards, since one target
 only follows one physical card. Used for:
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
    public float cardWidth = 0.063f;
    [Tooltip("Shortest time the deck's card back must be hidden to count as a hand taking a card, so tracking flicker doesn't.")]
    public float minDeckHiddenSeconds = 0.3f;
    public float maxDeckHiddenSeconds = 8f;
    [Tooltip("Seconds the deck's card back must have been in view before a touch counts.")]
    public float deckArmSeconds = 1f;

    const string FileName = "CardBack/CardBack.png";

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

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    IEnumerator Start()
    {
        if (VuforiaBehaviour.Instance == null)
        {
            enabled = false;
            yield break;
        }
        yield return new WaitUntil(() => VuforiaApplication.Instance.IsRunning);

        string path = Path.Combine(Application.streamingAssetsPath, FileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[CardBackTracker] No card back picture at " + path);
            enabled = false;
            yield break;
        }

        for (int i = 0; i < copies; i++)
        {
            ImageTargetBehaviour target = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(path, cardWidth, "CardBack#" + i);
            if (target == null) continue;

            Back back = new Back { target = target };
            target.OnTargetStatusChanged += (_, status) => back.tracked = status.Status == Status.TRACKED;
            mBacks.Add(back);
        }
        Debug.Log("[CardBackTracker] " + mBacks.Count + " card back targets ready.");
    }

    void Update()
    {
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

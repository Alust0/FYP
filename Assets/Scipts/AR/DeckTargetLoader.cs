using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Vuforia;

/* Before the match: loads the decks and turns each unique card's picture into a Vuforia Image
 Target named by its card number, then hands it to VuforiaCardOverlayManager to track.
 Multiplayer loads only this PC's deck (Player 1); OverTheBoard loads both. */
public class DeckTargetLoader : MonoBehaviour
{
    [SerializeField] private DeckSource deckSource;
    [SerializeField] private BoardLayout board;
    [SerializeField] private VuforiaCardOverlayManager overlayManager;

    [Header("Card Size (metres)")]
    [SerializeField] private float cardWidth = 0.063f; // Vanguard cards are 63 x 88 mm
    [SerializeField] private float cardHeight = 0.088f;

    [Header("Identical Cards")]
    [Tooltip("Targets made per card. Vuforia follows one physical card per target, so this is how many copies of the same card can be read on the board at once.")]
    [Range(1, 4)] [SerializeField] private int copiesPerCard = 4; // a deck holds up to 4 of a card

    [Header("Loading Screen (optional)")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private TMP_Text loadingText;

    [Tooltip("Runs once every card target is ready, e.g. to start the turn flow.")]
    public UnityEvent onReady;

    public bool IsReady { get; private set; }

    bool mCameraFailed;

    /* Subscribed in Awake, before Vuforia can report. Without a camera (e.g. a second editor window
     while the first holds the webcam) the game carries on with a notice, instead of Vuforia's
     full-screen error whose Close button ends Play Mode. */
    void Awake()
    {
        CameraChoice.Apply(); // editor: this window's webcam, in case Vuforia reloaded its settings during the scene load
        VuforiaApplication.Instance.OnVuforiaInitialized += OnVuforiaInitialized;
        StartCoroutine(CameraChoice.StartVuforia()); // Vuforia is set to wait for this
    }

    void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaInitialized -= OnVuforiaInitialized;
        CameraChoice.EndCameraTurn();
    }

    void OnVuforiaInitialized(VuforiaInitError error)
    {
        if (error == VuforiaInitError.NONE) return;
        mCameraFailed = true;
        CameraChoice.EndCameraTurn();

        foreach (DefaultInitializationErrorHandler handler in FindObjectsByType<DefaultInitializationErrorHandler>(FindObjectsSortMode.None))
            handler.enabled = false;
        if (loadingPanel != null) loadingPanel.SetActive(false);

        Debug.LogWarning("[DeckTargetLoader] Vuforia couldn't start (" + error + "); playing without card scanning.");
        GameState.OnRuleNotice?.Invoke("No camera", "The camera couldn't start (" + error + "), so cards won't be scanned here." +
            (Application.isEditor ? " Press F2 to place cards by hand." : ""));
    }

    IEnumerator Start()
    {
        if (deckSource == null || board == null || overlayManager == null)
        {
            Debug.LogError("[DeckTargetLoader] Fill the Deck Source, Board and Overlay Manager slots in the Inspector.", this);
            yield break;
        }

        if (loadingPanel != null) loadingPanel.SetActive(true);
        SetText("Loading decks...");

        int playerCount = board.boardMode == BoardLayout.BoardMode.OverTheBoard ? 2 : 1;
        var cardNumbers = new List<string>();
        var deckNames = new List<string>();
        for (int player = 0; player < playerCount; player++)
        {
            Task<Deck> load = deckSource.LoadDeckAsync(player);
            yield return new WaitUntil(() => load.IsCompleted);

            if (load.IsFaulted)
            {
                Debug.LogException(load.Exception, this);
                SetText("Couldn't load Player " + (player + 1) + "'s deck.");
                yield break;
            }
            if (load.Result == null)
            {
                Debug.LogWarning("[DeckTargetLoader] Player " + (player + 1) + " has no deck.");
                continue;
            }
            cardNumbers.AddRange(load.Result.UniqueCardNumbers);
            deckNames.Add(load.Result.deckName);
            // This PC's deck is its own seat's when playing one side (online or solo)
            int seat = !MatchSession.IsOneSide ? player : player == 0 ? MatchSession.LocalSeat : MatchSession.RemoteSeat;
            FindFirstObjectByType<RulesEngine>()?.SetDeck(seat, load.Result);
            CardBackTracker.UseSleeve(load.Result.deckSleeve, false);
            CardBackTracker.UseSleeve(load.Result.rideDeckSleeve, true);
        }
        cardNumbers = cardNumbers.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
        CardFirestoreDatabase.Instance?.CacheDeck(cardNumbers); // so seen cards need no Firestore read

        SetText("Starting camera...");
        yield return new WaitUntil(() => VuforiaApplication.Instance.IsRunning || mCameraFailed);
        if (mCameraFailed) yield break;
        StartCoroutine(CameraChoice.CheckVuforiaCamera()); // editor: makes sure this window got its own webcam

        var missing = new List<string>();
        for (int i = 0; i < cardNumbers.Count; i++)
        {
            SetText("Preparing cards " + (i + 1) + " / " + cardNumbers.Count + "...");
            for (int copy = 0; copy < copiesPerCard; copy++)
            {
                if (CreateTarget(cardNumbers[i], copy)) continue;
                missing.Add(cardNumbers[i]);
                break;
            }
            yield return null; // one card per frame so the loading text keeps updating
        }

        if (missing.Count > 0)
            Debug.LogWarning("[DeckTargetLoader] No picture or target for: " + string.Join(", ", missing));
        Debug.Log("[DeckTargetLoader] " + (cardNumbers.Count - missing.Count) + " cards ready (" + copiesPerCard + " targets each), from " + string.Join(" + ", deckNames) +
            ". Only these cards are recognised.");

        if (loadingPanel != null) loadingPanel.SetActive(false);
        SetText("");
        IsReady = true;
        onReady?.Invoke();
    }

    /* Each copy is its own target with the same picture, which is Vuforia's way to follow several
     identical cards: one target only ever follows one of them. */
    bool CreateTarget(string cardNumber, int copy)
    {
        string path = CardImageLibrary.GetPath(cardNumber);
        if (path == null) return false;

        // Sideways pictures are as wide as a card is tall
        float width = CardImageLibrary.IsLandscape(path) ? cardHeight : cardWidth;
        ImageTargetBehaviour target = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(path, width, cardNumber + "#" + copy);
        if (target == null) return false;

        overlayManager.AddDeckTarget(target, cardNumber, copy);
        return true;
    }

    void SetText(string text)
    {
        if (loadingText != null) loadingText.text = text;
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vanguard.Api;

// Singleton that loads card data from the Python REST API and provides query and deck-building methods.
// Separate from AR/CardDatabase.cs which is the Resources-based static lookup used by the AR overlays.
public class ApiCardDatabase : MonoBehaviour
{
    public static ApiCardDatabase Instance { get; private set; }

    [Header("Set to Pre-load on Start (leave blank to skip)")]
    [SerializeField] public string defaultSetCode = "";

    private readonly List<CardData> allCards = new List<CardData>();

    public bool IsLoaded { get; private set; }
    public int CardCount => allCards.Count;

    public event System.Action OnLoaded;
    public event System.Action<string> OnLoadError;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(defaultSetCode))
            StartCoroutine(LoadSet(defaultSetCode));
    }

    // Fetch all cards from a set code (e.g. "D-BT01") and add them to the cache
    public IEnumerator LoadSet(string setCode)
    {
        IsLoaded = false;
        allCards.Clear();

        bool done = false;
        string error = null;

        CardApiClient.Instance.GetSetCards(setCode, cards =>
        {
            if (cards != null)
                foreach (ScrapedCardDto dto in cards)
                    allCards.Add(dto.ToCardData());
            done = true;
        },
        err =>
        {
            error = err;
            done = true;
        });

        yield return new WaitUntil(() => done);

        if (error != null)
        {
            Debug.LogError("[ApiCardDatabase] " + error);
            OnLoadError?.Invoke(error);
        }
        else
        {
            Debug.Log("[ApiCardDatabase] Loaded " + allCards.Count + " cards from set " + setCode);
            IsLoaded = true;
            OnLoaded?.Invoke();
        }
    }

    // Search cards by keyword across all sets and append results to the cache
    public IEnumerator Search(string query)
    {
        bool done = false;

        CardApiClient.Instance.SearchCards(query, cards =>
        {
            if (cards != null)
                foreach (ScrapedCardDto dto in cards)
                    allCards.Add(dto.ToCardData());
            done = true;
        },
        err =>
        {
            Debug.LogError("[ApiCardDatabase] Search error: " + err);
            done = true;
        });

        yield return new WaitUntil(() => done);
        IsLoaded = true;
        OnLoaded?.Invoke();
    }

    // Manually add a card (useful for tests or synthetic entries)
    public void Register(CardData card) => allCards.Add(card);

    // --- Query methods ---

    public List<CardData> GetAll() => new List<CardData>(allCards);
    public List<CardData> GetByGrade(int grade) => allCards.Where(c => c.grade == grade).ToList();
    public List<CardData> GetByGradeRange(int min, int max) => allCards.Where(c => c.grade >= min && c.grade <= max).ToList();
    public List<CardData> GetTriggers() => allCards.Where(c => c.isTriggerUnit).ToList();
    public List<CardData> GetTriggers(TriggerIcon icon) => allCards.Where(c => c.isTriggerUnit && c.triggerIcon == icon).ToList();
    public List<CardData> GetSentinels() => allCards.Where(c => c.hasSentinel).ToList();
    public List<CardData> GetOrders(bool blitzOnly = false) => blitzOnly ? allCards.Where(c => c.isBlitzOrder).ToList() : allCards.Where(c => c.isOrderCard).ToList();

    public CardData GetByName(string cardName)
        => allCards.FirstOrDefault(c => string.Equals(c.cardName, cardName, System.StringComparison.OrdinalIgnoreCase));

    // --- Deck building ---

    // Returns one unit per grade (0-3) to use as the ride deck
    public List<CardData> BuildRideDeck()
    {
        var ride = new List<CardData>();
        for (int g = 0; g <= 3; g++)
        {
            CardData card = allCards.FirstOrDefault(c => c.grade == g && !c.isTriggerUnit && !c.isOrderCard);
            if (card != null) ride.Add(card);
        }
        return ride;
    }

    // Builds a legal 50-card D-format main deck: 4x each standard trigger type, rest filled with units
    public List<CardData> BuildMainDeck(int vanguardGrade = 3)
    {
        var deck = new List<CardData>();

        foreach (TriggerIcon icon in new[] { TriggerIcon.Critical, TriggerIcon.Draw, TriggerIcon.Stand, TriggerIcon.Heal })
        {
            List<CardData> pool = GetTriggers(icon);
            if (pool.Count == 0) continue;
            for (int i = 0; i < 4; i++)
                deck.Add(pool[i % pool.Count]);
        }

        int needed = 50 - deck.Count;
        List<CardData> fillerPool = allCards
            .Where(c => !c.isTriggerUnit && !c.isOrderCard && c.grade >= 1 && c.grade <= vanguardGrade)
            .OrderBy(_ => Random.value)
            .Take(needed)
            .ToList();

        deck.AddRange(fillerPool);

        List<CardData> grade1 = allCards.Where(c => c.grade == 1 && !c.isTriggerUnit).ToList();
        int idx = 0;
        while (deck.Count < 50 && grade1.Count > 0)
            deck.Add(grade1[idx++ % grade1.Count]);

        Shuffle(deck);
        return deck.Take(50).ToList();
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}

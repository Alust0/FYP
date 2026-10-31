using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Vanguard.Api;
using CardFields = System.Collections.Generic.Dictionary<string, object>;
#if UNITY_WSA
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
#else
using Firebase;
using Firebase.Extensions;
using Firebase.Firestore;
#endif

/*
Looks up full card details from Firestore once Vuforia Cloud Reco has identified a card's
set/number code (ObserverBehaviour MetaData). ApiCardDatabase (the local REST scraper cache)
is the offline/editor fallback; both map fields through ScrapedCardDto.ToCardData().

Firestore layout (ar-cardgame project, 288 sets / 6,368 cards as of 2024-06-05):
  sets/{setId}                    — setId is the clean set code, e.g. "BT01", "TD17", "DZ-BT16"
  sets/{setId}/cards/{cardDocId}  — cardDocId is the card number with "/" replaced by "-",
                                    e.g. "TD17/001" -> "TD17-001"
  card.number field               — stored with the slash, e.g. "TD17/001"

Set codes can contain hyphens (e.g. DZ-BT16), so hyphenated card IDs are split by
longest-prefix match against the cached list of valid set IDs.

Firebase's Unity SDK doesn't support UWP, so UWP builds read the same database through
Firestore's REST API instead. Only the backend sections at the bottom differ per platform.

Firestore's free plan counts every read, so it's the backup rather than the first stop: a card
comes from memory, then this PC's cache (CardCache.json), then Firestore. CacheDeck fills the cache
at match start with the decks' cards from the deck builder's scraper data (LocalCardData), so a
match only needs Firestore for cards outside them, like an online opponent's. Cards Firestore
lacks (or can't answer for) are taken from LocalCardData too. A Firestore miss is remembered,
and asked again after MissingRecheckDays.
*/
public class CardFirestoreDatabase : MonoBehaviour
{
    public static CardFirestoreDatabase Instance { get; private set; }

    const double MissingRecheckDays = 7;

    bool mReady;
    readonly Dictionary<string, CardData> mCache = new Dictionary<string, CardData>(); // by CardKey
    readonly List<Action> mPendingOnReady = new List<Action>();

    // This PC's cache of every card looked up, by CardKey
    Dictionary<string, CardFields> mStored = new Dictionary<string, CardFields>();
    bool mStoreChanged;
    bool mSaveQueued;
    // Lookups already on their way, so asking twice doesn't read twice
    readonly Dictionary<string, List<(Action<CardData> ok, Action<string> fail)>> mWaiting =
        new Dictionary<string, List<(Action<CardData>, Action<string>)>>();

    List<string> mSetIdsByLengthDesc; // null until loaded
    bool mSetIdsLoading;
    readonly List<Action> mPendingOnSetIds = new List<Action>();

    static string StorePath => Path.Combine(Application.persistentDataPath, "CardCache.json");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        mStored = ReadStore();
        InitBackend();
    }

    void OnDestroy()
    {
        if (Instance == this && mStoreChanged) SaveStore();
    }

    // Called by the backend once it can serve requests
    void OnBackendReady()
    {
        mReady = true;
        Debug.Log("[CardFirestoreDatabase] Connected — Firestore ready.");

        List<Action> pending = new List<Action>(mPendingOnReady);
        mPendingOnReady.Clear();
        foreach (Action action in pending) action();
    }

    /* Resolves a card from Cloud Reco metadata, either "{setId}/{number}" or the hyphenated
     "{setId}-{number}" doc-ID form: memory, this PC's cache, Firestore, then the scraper data. */
    public void GetCardByNumber(string cardNumber, Action<CardData> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(cardNumber)) { onError?.Invoke("Empty card number"); return; }

        string key = CardKey(cardNumber);
        if (mCache.TryGetValue(key, out CardData cached))
        {
            onSuccess?.Invoke(cached);
            return;
        }

        if (mWaiting.TryGetValue(key, out var waiting))
        {
            waiting.Add((onSuccess, onError));
            return;
        }
        mWaiting[key] = new List<(Action<CardData>, Action<string>)> { (onSuccess, onError) };

        if (mStored.TryGetValue(key, out CardFields stored) && !Stale(stored))
        {
            Finish(key, stored, null);
            return;
        }

        FromFirestore(cardNumber, (fields, error) =>
        {
            // Firestore's scrape stopped at 24 cards in many sets, and its free quota can run out
            if ((fields == null || error != null) && LocalCardData.TryGet(key, out CardFields local))
            {
                if (error != null) Debug.LogWarning("[CardFirestoreDatabase] " + error + " — using the deck builder's card data.");
                fields = local;
                error = null;
            }
            if (error == null)
            {
                fields = fields ?? new CardFields { ["missing"] = true, ["checkedMs"] = NowMs };
                Store(key, fields);
            }
            Finish(key, fields, error);
        });
    }

    /* Caches the cards of the decks going into a match from the deck builder's data, so seeing them
     needs no Firestore read; cards it doesn't have are looked up as usual (Firestore) */
    public void CacheDeck(IEnumerable<string> cardNumbers)
    {
        int cached = 0;
        foreach (string number in cardNumbers)
        {
            string key = CardKey(number);
            if (LocalCardData.TryGet(key, out CardFields fields))
            {
                Store(key, fields);
                mCache[key] = FromFields(fields);
                cached++;
            }
            else GetCardByNumber(number, null, null);
        }
        Debug.Log("[CardFirestoreDatabase] Cached " + cached + " deck cards from the deck builder's data.");
    }

    // "D-BT13/004" and "D-BT13-004" are the same card; also safe as a JSON or database key
    public static string CardKey(string cardNumber) => cardNumber.Trim().ToUpperInvariant()
        .Replace('/', '-').Replace('.', '_').Replace('#', '_').Replace('$', '_').Replace('[', '_').Replace(']', '_');

    static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // A card Firestore didn't have, long enough ago that it may have been added since
    static bool Stale(CardFields fields) =>
        fields.GetBool("missing") && NowMs - fields.GetLong("checkedMs") > MissingRecheckDays * 24 * 3600 * 1000;

    // Answers everyone waiting on the card
    void Finish(string key, CardFields fields, string error)
    {
        CardData card = null;
        if (error == null && fields != null && !fields.GetBool("missing"))
        {
            card = FromFields(fields);
            mCache[key] = card;
        }

        if (!mWaiting.TryGetValue(key, out var waiting)) return;
        mWaiting.Remove(key);
        foreach (var (ok, fail) in waiting)
        {
            if (card != null) ok?.Invoke(card);
            else fail?.Invoke(error ?? "No card found for number: " + key);
        }
    }

    // Keeps the card in this PC's cache
    void Store(string key, CardFields fields)
    {
        mStored[key] = fields;
        mStoreChanged = true;
        if (!mSaveQueued)
        {
            mSaveQueued = true;
            StartCoroutine(SaveSoon());
        }
    }

    // Many cards often arrive together, so they're saved in one write
    IEnumerator SaveSoon()
    {
        yield return new WaitForSecondsRealtime(3f);
        mSaveQueued = false;
        SaveStore();
    }

    static Dictionary<string, CardFields> ReadStore()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonConvert.DeserializeObject<Dictionary<string, CardFields>>(File.ReadAllText(StorePath)) ?? new Dictionary<string, CardFields>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CardFirestoreDatabase] Couldn't read " + StorePath + ", starting a new one: " + e.Message);
        }
        return new Dictionary<string, CardFields>();
    }

    // Adds to the file rather than replacing it, as another Play Mode window may have saved cards too
    void SaveStore()
    {
        try
        {
            Dictionary<string, CardFields> merged = ReadStore();
            foreach (KeyValuePair<string, CardFields> pair in mStored) merged[pair.Key] = pair.Value;

            string temp = StorePath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(merged));
            if (File.Exists(StorePath)) File.Delete(StorePath);
            File.Move(temp, StorePath);
            mStoreChanged = false;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CardFirestoreDatabase] Couldn't save " + StorePath + ": " + e.Message);
        }
    }

    // Passes (fields, or null if Firestore has no such card; error, or null)
    void FromFirestore(string cardNumber, Action<CardFields, string> done)
    {
        if (!mReady)
        {
            mPendingOnReady.Add(() => FromFirestore(cardNumber, done));
            return;
        }
        EnsureSetIdsLoaded(() => ResolveByNumber(cardNumber, done));
    }

    /* Loads every document ID under the top-level "sets" collection once (288 docs), so
     hyphenated metadata can be matched against real set codes. */
    void EnsureSetIdsLoaded(Action onReady)
    {
        if (mSetIdsByLengthDesc != null) { onReady(); return; }

        mPendingOnSetIds.Add(onReady);
        if (mSetIdsLoading) return;
        mSetIdsLoading = true;

        LoadSetIds(ids =>
        {
            // null means the load failed: degrade to query-only resolution in ResolveByNumber
            mSetIdsByLengthDesc = ids == null
                ? new List<string>()
                : ids.OrderByDescending(id => id.Length).ToList();

            List<Action> pending = new List<Action>(mPendingOnSetIds);
            mPendingOnSetIds.Clear();
            foreach (Action action in pending) action();
        });
    }

    void ResolveByNumber(string cardNumber, Action<CardFields, string> done)
    {
        string setId = null;
        string cardDocId = null;
        string slashNumber = null; // the "number" field's stored form, for the query fallback

        int slashIndex = cardNumber.IndexOf('/');
        if (slashIndex > 0)
        {
            setId = cardNumber.Substring(0, slashIndex);
            cardDocId = cardNumber.Replace('/', '-');
            slashNumber = cardNumber;
        }
        else
        {
            /* Longest-prefix match: a shorter set code (e.g. "D") could otherwise spuriously
             match a longer one (e.g. "D-BT04"). */
            setId = mSetIdsByLengthDesc.FirstOrDefault(id => cardNumber.StartsWith(id + "-"));
            if (setId != null)
            {
                cardDocId = cardNumber;
                slashNumber = setId + "/" + cardNumber.Substring(setId.Length + 1);
            }
        }

        if (setId == null)
        {
            // Couldn't resolve a set ID — query as-is; unlikely to match but it's the best fallback.
            QueryByNumber(cardNumber, done);
            return;
        }

        FetchCard(setId, cardDocId, fields =>
        {
            if (fields != null)
            {
                done(fields, null);
                return;
            }

            /* Direct path missed (not seeded, or wrong set ID guess): fall back to a
             collection group query on the slash-form "number" field. */
            QueryByNumber(slashNumber, done);
        });
    }

    void QueryByNumber(string numberValue, Action<CardFields, string> done) =>
        QueryCardByNumber(numberValue, (fields, error) =>
            done(fields, error == null ? null : "Firestore query failed for " + numberValue + ": " + error));

    static readonly string[] TextFields =
    {
        "name", "nameEn", "number", "typeEn", "type", "clan", "clanEn", "nation", "nationEn", "race", "raceEn", "rarity",
        "skill", "skillEn", "triggerType", "triggerTypeEn", "flavor", "flavorEn", "effect", "effectEn", "productName",
    };
    static readonly string[] NumberFields = { "grade", "power", "shield", "critical" };

    // A card document's fields, as CardCache.json and the deck builder's data keep them
    static CardFields ToFields(Func<string, string> getString, Func<string, int> getInt)
    {
        var fields = new CardFields();
        foreach (string field in TextFields)
        {
            string value = getString(field);
            if (value != null) fields[field] = value;
        }
        foreach (string field in NumberFields) fields[field] = getInt(field);
        return fields;
    }

    /* Converts a card's fields into a CardData, reusing ScrapedCardDto's mapping
     so Firestore and the REST scraper API stay in sync. */
    public static CardData FromFields(CardFields fields)
    {
        ScrapedCardDto dto = new ScrapedCardDto
        {
            name = fields.GetString("name"),
            name_en = fields.GetString("nameEn"),
            number = fields.GetString("number"),
            type = fields.GetString("typeEn") ?? fields.GetString("type"),
            clan = fields.GetString("clan"),
            clan_en = fields.GetString("clanEn"),
            nation = fields.GetString("nation"),
            nation_en = fields.GetString("nationEn"),
            race = fields.GetString("race"),
            race_en = fields.GetString("raceEn"),
            rarity = fields.GetString("rarity"),
            skill = fields.GetString("skill"),
            skill_en = fields.GetString("skillEn"),
            trigger_type = fields.GetString("triggerType"),
            trigger_type_en = fields.GetString("triggerTypeEn"),
            flavor = fields.GetString("flavor"),
            flavor_en = fields.GetString("flavorEn"),
            grade = (int)fields.GetLong("grade"),
            power = (int)fields.GetLong("power"),
            shield = (int)fields.GetLong("shield"),
            critical = (int)fields.GetLong("critical"),
            effect = fields.GetString("effect"),
            effect_en = fields.GetString("effectEn"),
            productName = fields.GetString("productName"),
        };

        return dto.ToCardData();
    }

#if !UNITY_WSA
    // Firebase SDK backend (editor, Windows, Android, iOS)

    FirebaseFirestore mDb;

    void InitBackend()
    {
        Debug.Log("[CardFirestoreDatabase] Checking Firebase dependencies...");

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available)
            {
                Debug.LogError("[CardFirestoreDatabase] Firebase dependencies unavailable: " + task.Result);
                return;
            }

            mDb = FirebaseFirestore.DefaultInstance;
            OnBackendReady();
        });
    }

    // Passes null if the load failed
    void LoadSetIds(Action<List<string>> onDone)
    {
        mDb.Collection("sets").GetSnapshotAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[CardFirestoreDatabase] Failed to load set IDs: " + task.Exception);
                onDone(null);
                return;
            }

            onDone(task.Result.Documents.Select(d => d.Id).ToList());
        });
    }

    // Passes null if the document doesn't exist or the read failed
    void FetchCard(string setId, string cardDocId, Action<CardFields> onDone)
    {
        mDb.Collection("sets").Document(setId).Collection("cards").Document(cardDocId)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                bool found = !task.IsFaulted && !task.IsCanceled && task.Result.Exists;
                onDone(found ? FromDocument(task.Result) : null);
            });
    }

    // Passes (card or null if no match, error or null)
    void QueryCardByNumber(string numberValue, Action<CardFields, string> onDone)
    {
        mDb.CollectionGroup("cards")
            .WhereEqualTo("number", numberValue)
            .Limit(1)
            .GetSnapshotAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    onDone(null, task.Exception != null ? task.Exception.ToString() : "cancelled");
                    return;
                }

                DocumentSnapshot doc = task.Result.Documents.FirstOrDefault();
                onDone(doc != null ? FromDocument(doc) : null, null);
            });
    }

    static CardFields FromDocument(DocumentSnapshot doc) => ToFields(
        field => doc.TryGetValue(field, out string value) ? value : null,
        field =>
        {
            if (doc.TryGetValue(field, out long longValue)) return (int)longValue;
            if (doc.TryGetValue(field, out double doubleValue)) return (int)doubleValue;
            return 0;
        });
#else
    // REST backend (UWP): the same database over https://firestore.googleapis.com

    string mDocumentsUrl; // .../v1/projects/{projectId}/databases/(default)/documents
    string mApiKey;

    // Reads the project ID and web API key from the google-services.json the SDK also uses
    void InitBackend()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "google-services.json");
            JObject config = JObject.Parse(File.ReadAllText(path));

            string projectId = (string)config["project_info"]["project_id"];
            mApiKey = (string)config["client"][0]["api_key"][0]["current_key"];
            mDocumentsUrl = "https://firestore.googleapis.com/v1/projects/" + projectId + "/databases/(default)/documents";
        }
        catch (Exception e)
        {
            Debug.LogError("[CardFirestoreDatabase] Couldn't read google-services.json: " + e.Message);
            return;
        }

        OnBackendReady();
    }

    void LoadSetIds(Action<List<string>> onDone) => StartCoroutine(LoadSetIdsRoutine(onDone));

    // Passes null if the load failed. Pages through the collection 300 documents at a time.
    IEnumerator LoadSetIdsRoutine(Action<List<string>> onDone)
    {
        List<string> ids = new List<string>();
        string pageToken = null;

        do
        {
            string url = mDocumentsUrl + "/sets?pageSize=300&key=" + mApiKey
                + (pageToken != null ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[CardFirestoreDatabase] Failed to load set IDs: " + request.error);
                    onDone(null);
                    yield break;
                }

                JObject page = JObject.Parse(request.downloadHandler.text);
                foreach (JToken doc in page["documents"] ?? new JArray())
                    ids.Add(DocumentId(doc));

                pageToken = (string)page["nextPageToken"];
            }
        }
        while (!string.IsNullOrEmpty(pageToken));

        onDone(ids);
    }

    void FetchCard(string setId, string cardDocId, Action<CardFields> onDone)
        => StartCoroutine(FetchCardRoutine(setId, cardDocId, onDone));

    // Passes null if the document doesn't exist (404) or the read failed
    IEnumerator FetchCardRoutine(string setId, string cardDocId, Action<CardFields> onDone)
    {
        // Set IDs can be non-ASCII (e.g. "2011年度"), so each path segment is escaped
        string url = mDocumentsUrl + "/sets/" + Uri.EscapeDataString(setId)
            + "/cards/" + Uri.EscapeDataString(cardDocId) + "?key=" + mApiKey;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            bool found = request.result == UnityWebRequest.Result.Success;
            onDone(found ? FromRestDocument(JObject.Parse(request.downloadHandler.text)) : null);
        }
    }

    void QueryCardByNumber(string numberValue, Action<CardFields, string> onDone)
        => StartCoroutine(QueryCardByNumberRoutine(numberValue, onDone));

    // Passes (card or null if no match, error or null)
    IEnumerator QueryCardByNumberRoutine(string numberValue, Action<CardFields, string> onDone)
    {
        JObject body = new JObject
        {
            ["structuredQuery"] = new JObject
            {
                ["from"] = new JArray(new JObject { ["collectionId"] = "cards", ["allDescendants"] = true }),
                ["where"] = new JObject
                {
                    ["fieldFilter"] = new JObject
                    {
                        ["field"] = new JObject { ["fieldPath"] = "number" },
                        ["op"] = "EQUAL",
                        ["value"] = new JObject { ["stringValue"] = numberValue },
                    },
                },
                ["limit"] = 1,
            },
        };

        using (UnityWebRequest request = new UnityWebRequest(mDocumentsUrl + ":runQuery?key=" + mApiKey, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onDone(null, request.error + " " + request.downloadHandler.text);
                yield break;
            }

            // One entry per match; with no match the only entry is {"readTime": ...}
            JToken doc = JArray.Parse(request.downloadHandler.text)
                .FirstOrDefault(entry => entry["document"] != null)?["document"];
            onDone(doc != null ? FromRestDocument(doc) : null, null);
        }
    }

    // ".../documents/sets/BT01" -> "BT01"
    static string DocumentId(JToken doc)
    {
        string name = (string)doc["name"];
        return name.Substring(name.LastIndexOf('/') + 1);
    }

    // REST wraps each value in its type, e.g. "grade": {"integerValue": "3"} (int64s come as strings)
    static CardFields FromRestDocument(JToken doc)
    {
        JToken fields = doc["fields"];

        return ToFields(
            field => (string)fields?[field]?["stringValue"],
            field =>
            {
                JToken value = fields?[field];
                if (value?["integerValue"] != null) return (int)long.Parse((string)value["integerValue"]);
                if (value?["doubleValue"] != null) return (int)(double)value["doubleValue"];
                return 0;
            });
    }
#endif
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Vanguard.Api
{
    // DTO matching individual items inside AllSets.json
    [System.Serializable]
    public class ScrapedSetDto
    {
        public string url;
        public string code;
        public string name;
        public string cardsUrl;
    }

    // Wrapped root object for JSON response containing array of sets
    [System.Serializable]
    public class WrappedSetsResponse
    {
        public ScrapedSetDto[] sets;
    }

    // DTO matching individual scraped card objects inside set JSON files
    [System.Serializable]
    public class ScrapedCardDto
    {
        public string name;
        public string name_en;
        public string number;
        public string url;
        public string image_url;
        public string image_file_type;
        public string type;
        public string clan;
        public string clan_en;
        public string nation;
        public string nation_en;
        public string race;
        public string race_en;
        public string rarity;
        public string illustrator;
        public string skill;
        public string skill_en;
        public string trigger_type;
        public string trigger_type_en;
        public string flavor;
        public string flavor_en;
        public int grade;
        public int power;
        public int shield;
        public int critical;
        public string effect;
        public string effect_en;
        public string productName;

        // Automatically maps scraped DTO fields into a Unity CardData ScriptableObject instance
        public CardData ToCardData()
        {
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.cardName = !string.IsNullOrEmpty(name) ? name : "Unknown Card";
            card.cardNameEn = name_en;
            card.cardNumber = number;
            card.clan = !string.IsNullOrEmpty(clan_en) ? clan_en : clan;
            card.nation = !string.IsNullOrEmpty(nation_en) ? nation_en : nation;
            card.race = !string.IsNullOrEmpty(race_en) ? race_en : race;
            card.rarity = rarity;
            card.illustrator = illustrator;
            card.skillText = !string.IsNullOrEmpty(skill_en) ? skill_en : skill;
            card.cardType = type;
            card.flavorText = !string.IsNullOrEmpty(flavor_en) ? flavor_en : flavor;
            card.imageUrl = image_url;
            card.grade = grade;
            card.power = power;
            card.shield = shield;
            card.critical = critical > 0 ? critical : 1; // default starting critical when unset

            // Prefer English translated effect if present, otherwise fallback to Japanese
            card.abilityText = !string.IsNullOrEmpty(effect_en) ? effect_en : (!string.IsNullOrEmpty(effect) ? effect : "");
            card.abilityTextJa = effect ?? ""; // skills are parsed from this: its wording is more consistent
            card.raceJa = race;

            // Deduce Card Type
            if (!string.IsNullOrEmpty(type))
            {
                if (type.Contains("トリガーユニット") || type.Contains("Trigger Unit") || type.Contains("Trigger"))
                {
                    card.isTriggerUnit = true;
                }
                if (type.Contains("オーダー") || type.Contains("Order"))
                {
                    card.isOrderCard = true;
                    if (type.Contains("ブリッツ") || type.Contains("Blitz"))
                        card.isBlitzOrder = true;
                }
            }

            /* Trigger icon: use the explicit trigger_type when present (Firestore always has it,
             the REST scraper may not), else guess from name/type text. */
            string triggerSource = !string.IsNullOrEmpty(trigger_type_en) ? trigger_type_en : trigger_type;
            if (!string.IsNullOrEmpty(triggerSource))
            {
                card.isTriggerUnit = true;
                card.triggerIcon = ParseTriggerIcon(triggerSource);
            }
            else if (card.isTriggerUnit)
            {
                card.triggerIcon = ParseTriggerIcon((type ?? "") + " " + (name ?? ""));
            }

            // Standard Vanguard mechanics deduction based on grade
            if (grade == 0 || grade == 1)
            {
                card.hasBoost = true;
            }
            if (grade == 2)
            {
                card.hasIntercept = true;
            }
            if (grade >= 3)
            {
                card.drive = 2; // Default twin drive for G3+
            }
            else
            {
                card.drive = 1;
            }

            // Check for Sentinel/Perfect Guard keyword; the Japanese names it reliably, the English may say "Guardian"
            if (card.abilityText.Contains("Sentinel") || System.Text.RegularExpressions.Regex.IsMatch(card.abilityTextJa, @"【永】[:：]\s*守護者"))
            {
                card.hasSentinel = true;
            }

            // Persona Ride icon: the scraped data lists it with Twin Drive in the skill field, not the effect
            string icons = (skill ?? "") + " " + (skill_en ?? "") + " " + card.abilityText;
            if (icons.Contains("Persona Ride") || icons.Contains("ペルソナライド"))
            {
                card.hasPersonaRideIcon = true;
            }

            return card;
        }

        static TriggerIcon ParseTriggerIcon(string source)
        {
            if (source.Contains("Critical") || source.Contains("クリティカル")) return TriggerIcon.Critical;
            if (source.Contains("Draw") || source.Contains("ドロー")) return TriggerIcon.Draw;
            if (source.Contains("Stand") || source.Contains("スタンド")) return TriggerIcon.Stand;
            if (source.Contains("Heal") || source.Contains("ヒール")) return TriggerIcon.Heal;
            if (source.Contains("Front") || source.Contains("フロント")) return TriggerIcon.Front;
            if (source.Contains("Over") || source.Contains("オーバー")) return TriggerIcon.Over;
            return TriggerIcon.None;
        }
    }

    // Wrapped root object for JSON response containing array of cards
    [System.Serializable]
    public class WrappedCardsResponse
    {
        public ScrapedCardDto[] cards;
    }

    // Companion MonoBehaviour / Client Utility for querying the local Python REST API
    public class CardApiClient : MonoBehaviour
    {
        [Header("API Settings")]
        [SerializeField] private string baseApiUrl = "http://localhost:8080/api";

        private static CardApiClient _instance;
        public static CardApiClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<CardApiClient>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("CardApiClient");
                        _instance = go.AddComponent<CardApiClient>();
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        #region Coroutine API Methods

        // Fetches all scraped sets via Coroutine
        public void GetSets(Action<ScrapedSetDto[]> onSuccess, Action<string> onError)
        {
            StartCoroutine(FetchData<WrappedSetsResponse>($"{baseApiUrl}/sets", response => onSuccess?.Invoke(response?.sets), onError));
        }

        // Fetches cards from a specific set via Coroutine
        public void GetSetCards(string setCode, Action<ScrapedCardDto[]> onSuccess, Action<string> onError)
        {
            StartCoroutine(FetchData<WrappedCardsResponse>($"{baseApiUrl}/sets/{setCode}", response => onSuccess?.Invoke(response?.cards), onError));
        }

        // Searches cards globally across sets via Coroutine
        public void SearchCards(string query, Action<ScrapedCardDto[]> onSuccess, Action<string> onError)
        {
            string encodedQuery = UnityWebRequest.EscapeURL(query);
            StartCoroutine(FetchData<WrappedCardsResponse>($"{baseApiUrl}/cards?query={encodedQuery}", response => onSuccess?.Invoke(response?.cards), onError));
        }

        private IEnumerator FetchData<T>(string url, Action<T> onSuccess, Action<string> onError) where T : class
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                // Request JSON response format explicitly
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                {
                    onError?.Invoke($"API Error ({url}): {request.error}");
                }
                else
                {
                    try
                    {
                        string jsonText = request.downloadHandler.text;
                        T data = JsonUtility.FromJson<T>(jsonText);
                        onSuccess?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke($"JSON Parse Error: {ex.Message}");
                    }
                }
            }
        }

        #endregion

        #region Async/Await Task API Methods

        // Fetches all scraped sets using async/await Task
        public async Task<ScrapedSetDto[]> GetSetsAsync()
        {
            WrappedSetsResponse response = await FetchDataAsync<WrappedSetsResponse>($"{baseApiUrl}/sets");
            return response?.sets ?? new ScrapedSetDto[0];
        }

        // Fetches cards from a specific set using async/await Task
        public async Task<ScrapedCardDto[]> GetSetCardsAsync(string setCode)
        {
            WrappedCardsResponse response = await FetchDataAsync<WrappedCardsResponse>($"{baseApiUrl}/sets/{setCode}");
            return response?.cards ?? new ScrapedCardDto[0];
        }

        // Searches cards globally across sets using async/await Task
        public async Task<ScrapedCardDto[]> SearchCardsAsync(string query)
        {
            string encodedQuery = UnityWebRequest.EscapeURL(query);
            WrappedCardsResponse response = await FetchDataAsync<WrappedCardsResponse>($"{baseApiUrl}/cards?query={encodedQuery}");
            return response?.cards ?? new ScrapedCardDto[0];
        }

        private async Task<T> FetchDataAsync<T>(string url) where T : class
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Accept", "application/json");

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

#if UNITY_2020_1_OR_NEWER
                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
#else
                if (request.isNetworkError || request.isHttpError)
#endif
                {
                    throw new Exception($"API Error ({url}): {request.error}");
                }

                return JsonUtility.FromJson<T>(request.downloadHandler.text);
            }
        }

        #endregion
    }
}

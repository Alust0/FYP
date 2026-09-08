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
        public string number;
        public string url;
        public string image_url;
        public string image_file_type;
        public string type;
        public string nation;
        public int grade;
        public int power;
        public int shield;
        public string effect;
        public string effect_en;
        public string productName;

        // Automatically maps scraped DTO fields into a Unity CardData ScriptableObject instance
        public CardData ToCardData()
        {
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.cardName = !string.IsNullOrEmpty(name) ? name : "Unknown Card";
            card.cardNumber = number;
            card.grade = grade;
            card.power = power;
            card.shield = shield;
            card.critical = 1; // Default starting critical

            // Prefer English translated effect if present, otherwise fallback to Japanese
            card.abilityText = !string.IsNullOrEmpty(effect_en) ? effect_en : (!string.IsNullOrEmpty(effect) ? effect : "");

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

            // Deduce trigger icon from type string and card name
            if (card.isTriggerUnit)
            {
                string combined = (type ?? "") + " " + (name ?? "");
                if (combined.Contains("Critical") || combined.Contains("クリティカル")) card.triggerIcon = TriggerIcon.Critical;
                else if (combined.Contains("Draw") || combined.Contains("ドロー")) card.triggerIcon = TriggerIcon.Draw;
                else if (combined.Contains("Stand") || combined.Contains("スタンド")) card.triggerIcon = TriggerIcon.Stand;
                else if (combined.Contains("Heal") || combined.Contains("ヒール")) card.triggerIcon = TriggerIcon.Heal;
                else if (combined.Contains("Front") || combined.Contains("フロント")) card.triggerIcon = TriggerIcon.Front;
                else if (combined.Contains("Over") || combined.Contains("オーバー")) card.triggerIcon = TriggerIcon.Over;
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

            // Check for Sentinel/Perfect Guard keyword
            if (card.abilityText.Contains("Sentinel") || card.abilityText.Contains("守護者"))
            {
                card.hasSentinel = true;
            }

            // Check for Persona Ride icon mention
            if (card.abilityText.Contains("Persona Ride") || card.abilityText.Contains("ペルソナライド"))
            {
                card.hasPersonaRideIcon = true;
            }

            return card;
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

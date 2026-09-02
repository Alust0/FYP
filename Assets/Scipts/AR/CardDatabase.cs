using System.Collections.Generic;
using UnityEngine;

// Simple lookup table for CardData assets — load all cards from Resources/Cards folder
public class CardDatabase : MonoBehaviour
{
    private static Dictionary<string, CardData> cardLookup = new Dictionary<string, CardData>();

    // Loads all CardData assets from the Resources/Cards folder on startup
    void Awake()
    {
        LoadAllCards();
    }

    // Loads every CardData ScriptableObject from Resources/Cards into the lookup table
    void LoadAllCards()
    {
        CardData[] allCards = Resources.LoadAll<CardData>("Cards");

        foreach (CardData card in allCards)
        {
            if (!cardLookup.ContainsKey(card.cardName))
                cardLookup[card.cardName] = card;
            else
                Debug.LogWarning("[CardDatabase] Duplicate card name found: " + card.cardName);
        }

        Debug.Log("[CardDatabase] Loaded " + cardLookup.Count + " cards.");
    }

    // Returns the CardData for the given card name or null if not found
    public static CardData Lookup(string cardName)
    {
        cardLookup.TryGetValue(cardName, out CardData data);
        return data;
    }

    // Returns true if the database has a card with the given name
    public static bool Contains(string cardName)
    {
        return cardLookup.ContainsKey(cardName);
    }
}

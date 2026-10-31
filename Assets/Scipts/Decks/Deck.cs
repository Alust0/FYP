using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// One line of a deck list: a card number and how many copies
[Serializable]
public class DeckEntry
{
    public string cardNumber; // e.g. "DZ-BT01-001", same form as the picture file names
    [Min(1)] public int count = 1;
}

// A deck list. Plain data, so it can come from the Inspector or the account's saved decks (DeckRepository)
[Serializable]
public class Deck
{
    public string id; // saved decks: a GUID; starter decks: the set code; Inspector decks: empty
    public string deckName;
    public List<DeckEntry> cards = new List<DeckEntry>(); // the main deck
    public List<DeckEntry> rideDeck = new List<DeckEntry>(); // up to 4 cards; part of the 50
    public List<DeckEntry> extras = new List<DeckEntry>(); // tokens and crests, outside the 50
    // Sleeve pictures (SleeveLibrary ids, e.g. "s120"), so the camera knows the card backs; empty for none
    public string deckSleeve;
    public string rideDeckSleeve;
    // For later: the G zone and gift markers
    //public List<DeckEntry> gZone = new List<DeckEntry>();
    //public List<DeckEntry> gifts = new List<DeckEntry>();

    // Decks saved before the ride deck and extras existed load them as null
    public IEnumerable<DeckEntry> AllEntries => cards.Concat(rideDeck ?? new List<DeckEntry>()).Concat(extras ?? new List<DeckEntry>());

    // Main deck and ride deck, which make up the 50
    public int TotalCards => cards.Concat(rideDeck ?? new List<DeckEntry>())
        .Where(entry => !string.IsNullOrWhiteSpace(entry.cardNumber)).Sum(entry => entry.count);

    // The first card, shown as the deck's cover
    public string CoverCard => UniqueCardNumbers.FirstOrDefault();

    // Every section's cards, so ride deck cards, tokens and crests are recognised in a match too
    public IEnumerable<string> UniqueCardNumbers =>
        AllEntries.Where(entry => !string.IsNullOrWhiteSpace(entry.cardNumber))
             .Select(entry => entry.cardNumber.Trim())
             .Distinct(StringComparer.OrdinalIgnoreCase);
}

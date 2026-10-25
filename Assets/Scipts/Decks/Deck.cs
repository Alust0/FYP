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

// A deck list. Plain data, so it can come from the Inspector now and Firestore later
[Serializable]
public class Deck
{
    public string deckName;
    public List<DeckEntry> cards = new List<DeckEntry>();

    public IEnumerable<string> UniqueCardNumbers =>
        cards.Where(entry => !string.IsNullOrWhiteSpace(entry.cardNumber))
             .Select(entry => entry.cardNumber.Trim())
             .Distinct(StringComparer.OrdinalIgnoreCase);
}

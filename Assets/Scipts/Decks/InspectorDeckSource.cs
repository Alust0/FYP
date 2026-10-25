using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Test decks typed into the Inspector, until decks are saved in Firestore
public class InspectorDeckSource : DeckSource
{
    [SerializeField] private Deck player1Deck = new Deck { deckName = "Player 1 test deck" };
    [Tooltip("Set codes like \"D-SD03\": every card in that set's pictures is added with Count 1.")]
    [SerializeField] private List<string> player1WholeSets = new List<string>();

    [SerializeField] private Deck player2Deck = new Deck { deckName = "Player 2 test deck" };
    [Tooltip("Set codes like \"D-SD05\": every card in that set's pictures is added with Count 1.")]
    [SerializeField] private List<string> player2WholeSets = new List<string>();

    /* Online, this PC's deck (index 0, see DeckTargetLoader) is the one picked in the lobby, if any;
     otherwise the Inspector decks below */
    public override Task<Deck> LoadDeckAsync(int playerIndex)
    {
        if (playerIndex == 0 && MatchSession.IsOnline && !string.IsNullOrEmpty(MatchSession.DeckSet))
            return Task.FromResult(WithWholeSets(new Deck { deckName = MatchSession.DeckSet }, new List<string> { MatchSession.DeckSet }));

        return Task.FromResult(playerIndex == 0
            ? WithWholeSets(player1Deck, player1WholeSets)
            : WithWholeSets(player2Deck, player2WholeSets));
    }

    // A copy of the deck with each whole set's cards added, so the Inspector lists stay as typed
    static Deck WithWholeSets(Deck deck, List<string> wholeSets)
    {
        var result = new Deck { deckName = deck.deckName, cards = new List<DeckEntry>(deck.cards) };
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeckEntry entry in deck.cards)
            if (!string.IsNullOrWhiteSpace(entry.cardNumber)) listed.Add(entry.cardNumber.Trim());

        foreach (string setCode in wholeSets)
        {
            if (string.IsNullOrWhiteSpace(setCode)) continue;

            List<string> numbers = CardImageLibrary.CardNumbersInSet(setCode);
            if (numbers.Count == 0) Debug.LogWarning("[InspectorDeckSource] No card pictures found for set " + setCode);

            foreach (string number in numbers)
                if (listed.Add(number)) result.cards.Add(new DeckEntry { cardNumber = number, count = 1 });
        }
        return result;
    }
}

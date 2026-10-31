using System.Threading.Tasks;
using UnityEngine;

/* This PC's deck from the lobby: one of the account's saved decks or a starter deck. Anything
 else (Player 2 over the board, or Ingame opened without the lobby) uses the Inspector test decks. */
public class AccountDeckSource : InspectorDeckSource
{
    public override async Task<Deck> LoadDeckAsync(int playerIndex)
    {
        if (playerIndex == 0 && MatchSession.IsOneSide && !string.IsNullOrEmpty(MatchSession.DeckId))
        {
            Deck deck = await DeckRepository.FindAsync(MatchSession.DeckId);
            if (deck != null) return deck;
            Debug.LogWarning("[AccountDeckSource] No deck with id " + MatchSession.DeckId + "; using the Inspector test deck.");
        }
        return await base.LoadDeckAsync(playerIndex);
    }
}

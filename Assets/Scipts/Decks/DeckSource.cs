using System.Threading.Tasks;
using UnityEngine;

/* Where the players' decks come from: AccountDeckSource (the lobby's pick from the account's
 decks), falling back to InspectorDeckSource's test decks. */
public abstract class DeckSource : MonoBehaviour
{
    // playerIndex: 0 = Player 1, 1 = Player 2. Null when that player has no deck
    public abstract Task<Deck> LoadDeckAsync(int playerIndex);
}

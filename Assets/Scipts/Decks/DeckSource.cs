using System.Threading.Tasks;
using UnityEngine;

/* Where the players' decks come from. InspectorDeckSource for now; a Firestore source (saved decks
 keyed by PlayerSession.Id) can subclass this later and drop into the same slot. */
public abstract class DeckSource : MonoBehaviour
{
    // playerIndex: 0 = Player 1, 1 = Player 2. Null when that player has no deck
    public abstract Task<Deck> LoadDeckAsync(int playerIndex);
}

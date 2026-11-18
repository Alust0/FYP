using System.Threading.Tasks;
using UnityEngine;

// Where decks come from: account, else Inspector test decks
public abstract class DeckSource : MonoBehaviour
{
    // playerIndex: 0 = Player 1, 1 = Player 2. Null when that player has no deck
    public abstract Task<Deck> LoadDeckAsync(int playerIndex);
}

using UnityEngine;

/* Creates the two empty PlayerState slots GameState needs at startup. Cards are physical
 now: their zone/state comes from VuforiaCardOverlayManager as they're scanned. */
public class GameSetup : MonoBehaviour
{
    void Start()
    {
        GameState gs = GameState.Instance;

        gs.players[0] = new PlayerState { playerName = "Player 1", playerIndex = 0 };
        gs.players[1] = new PlayerState { playerName = "Player 2", playerIndex = 1 };

        gs.BroadcastState();
    }
}

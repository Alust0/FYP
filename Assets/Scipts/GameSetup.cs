using UnityEngine;

/* Creates the two empty PlayerState slots GameState needs at startup. Cards are physical
 now: their zone/state comes from VuforiaCardOverlayManager as they're scanned. Online, the
 players are named after the lobby's two accounts and OnlineMatch syncs the two PCs. */
public class GameSetup : MonoBehaviour
{
    void Start()
    {
        GameState gs = GameState.Instance;

        gs.players[0] = new PlayerState { playerName = MatchSession.PlayerNames[0], playerIndex = 0 };
        gs.players[1] = new PlayerState { playerName = MatchSession.PlayerNames[1], playerIndex = 1 };

        if (MatchSession.IsOnline)
        {
            gameObject.AddComponent<OnlineMatch>();
            gameObject.AddComponent<SplitScreenView>(); // both players' camera views
        }

        gs.BroadcastState();
    }
}

using UnityEngine;

/* How long the match has run, from the first turn until the game is over, and the lobby's time
 limit (MatchSession.TimerMinutes): when it runs out the match ends in a draw. Online, each PC
 times its own match; they start within a second of each other. Created by UISetup. */
public class MatchTimer : MonoBehaviour
{
    static double sStart = -1; // realtimeSinceStartup at turn 1, or -1 before it
    static double sEnd = -1;   // when the game ended, or -1 while it's on

    public static bool HasLimit => MatchSession.TimerMinutes > 0;

    public static double Elapsed
    {
        get
        {
            if (sStart < 0) return 0;
            return (sEnd >= 0 ? sEnd : Time.realtimeSinceStartupAsDouble) - sStart;
        }
    }

    public static double Remaining => HasLimit ? System.Math.Max(0, MatchSession.TimerMinutes * 60 - Elapsed) : 0;

    // For the HUD: "Time left 29:41", or "" with no limit
    public static string Label => !HasLimit ? "" : Remaining <= 0 ? "Time up" : "Time left " + Format(Remaining);

    // "23:15", or "1:02:09" from an hour
    public static string Format(double seconds)
    {
        int total = (int)seconds;
        return total >= 3600 ? (total / 3600) + ":" + (total / 60 % 60).ToString("00") + ":" + (total % 60).ToString("00")
            : (total / 60) + ":" + (total % 60).ToString("00");
    }

    void Awake()
    {
        sStart = -1;
        sEnd = -1;
    }

    void OnEnable() => GameState.OnGameOver += HandleGameOver;
    void OnDisable() => GameState.OnGameOver -= HandleGameOver;

    void HandleGameOver()
    {
        if (sStart >= 0 && sEnd < 0) sEnd = Time.realtimeSinceStartupAsDouble;
    }

    void Update()
    {
        GameState gs = GameState.Instance;
        if (gs == null || gs.gameOver) return;

        if (sStart < 0)
        {
            if (gs.turnNumber > 0) sStart = Time.realtimeSinceStartupAsDouble;
            return;
        }

        if (HasLimit && Remaining <= 0)
            gs.EndGame(null, "time ran out after " + MatchSession.TimerMinutes + " minutes");
    }
}

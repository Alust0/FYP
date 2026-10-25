using System;

/* The online match this PC is in, set by the lobby before Ingame loads. When Ingame is opened
 without the lobby it stays offline and everything runs as the one-PC game. */
public static class MatchSession
{
    public static bool IsOnline => RoomCode != null;
    public static string RoomCode { get; private set; }

    // 0 = the room's host, who goes first; 1 = the guest. Always 0 offline
    public static int LocalSeat { get; private set; }
    public static int RemoteSeat => 1 - LocalSeat;

    public static readonly string[] PlayerNames = { "Player 1", "Player 2" };

    // Set code of the deck picked in the lobby, e.g. "D-SD01"; null for the Inspector test deck. Kept between matches
    public static string DeckSet;

    // New every run rather than per account, so two editors on one PC with the same login still count as two players
    public static readonly string ClientId = Guid.NewGuid().ToString("N");

    public static void Begin(string roomCode, int localSeat, string hostName, string guestName)
    {
        RoomCode = roomCode;
        LocalSeat = localSeat;
        PlayerNames[0] = hostName;
        PlayerNames[1] = guestName;
    }

    public static void End()
    {
        RoomCode = null;
        LocalSeat = 0;
        PlayerNames[0] = "Player 1";
        PlayerNames[1] = "Player 2";
    }

    // Whether this PC tracks and keeps count for that seat; offline it does both
    public static bool IsLocalSeat(int seat) => !IsOnline || seat == LocalSeat;
    public static bool IsLocal(PlayerState player) => player != null && IsLocalSeat(player.playerIndex);
}

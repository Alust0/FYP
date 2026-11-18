using System;

// The online match this PC is in (set by the lobby)
public static class MatchSession
{
    public static bool IsOnline => RoomCode != null;
    public static string RoomCode { get; private set; }
    public static string RoomPath { get; private set; } // "rooms/CODE", or a private room's hidden "privateRooms/<hash>"

    // Test Solo: one player, no room
    public static bool IsSolo { get; private set; }

    // 0 = the room's host, who goes first; 1 = the guest. Always 0 offline
    public static int LocalSeat { get; private set; }
    public static int RemoteSeat => 1 - LocalSeat;

    public static readonly string[] PlayerNames = { "Player 1", "Player 2" };

    // The lobby's Timer: minutes until the match ends in a draw (see MatchTimer); 0 for no limit
    public static int TimerMinutes { get; private set; }

    // The lobby's Card skills switch: off plays the basic rules only, no [AUTO]/[ACT]/order costs (SkillRules)
    public static bool SkillsOn { get; private set; } = true;

    // Deck id picked in the lobby
    public static string DeckId;

    // New every run rather than per account, so two editors on one PC with the same login still count as two players
    public static readonly string ClientId = Guid.NewGuid().ToString("N");

    public static void Begin(string roomCode, string roomPath, int localSeat, string hostName, string guestName, int timerMinutes, bool skills)
    {
        RoomCode = roomCode;
        RoomPath = roomPath;
        TimerMinutes = timerMinutes;
        SkillsOn = skills;
        LocalSeat = localSeat;
        PlayerNames[0] = hostName;
        PlayerNames[1] = guestName;
    }

    public static void BeginSolo(string name, int timerMinutes, bool skills)
    {
        End();
        IsSolo = true;
        TimerMinutes = timerMinutes;
        SkillsOn = skills;
        PlayerNames[0] = name;
        PlayerNames[1] = "No opponent";
    }

    public static void End()
    {
        RoomCode = null;
        RoomPath = null;
        IsSolo = false;
        LocalSeat = 0;
        TimerMinutes = 0;
        SkillsOn = true;
        PlayerNames[0] = "Player 1";
        PlayerNames[1] = "Player 2";
    }

    // Whether this PC tracks and keeps count for that seat; offline it does both
    public static bool IsLocalSeat(int seat) => !IsOnline || seat == LocalSeat;
    public static bool IsLocal(PlayerState player) => player != null && IsLocalSeat(player.playerIndex);

    // Only this PC's player on its board and camera: online or a solo test
    public static bool IsOneSide => IsOnline || IsSolo;
}

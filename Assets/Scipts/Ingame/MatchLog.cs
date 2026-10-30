using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/* The match's log: every action (cards placed, moved, rested and flipped, counts changed, keywords
 and buttons), every notification, and where every card is at the end of each Ride, Main and Battle
 Phase. Goes to the Console and to a text file per match in persistentDataPath/MatchLogs; the
 notifications are also kept for the in-match log (MatchLogPanel). Created by UISetup. */
public class MatchLog : MonoBehaviour
{
    public static string FilePath { get; private set; }

    // "[T3 Ride] Title: body" lines, oldest first, for MatchLogPanel
    public static readonly List<string> Notifications = new List<string>();
    public static event Action<string> OnNotification;

    static MatchLog sInstance;
    StreamWriter mWriter;
    int mTurn; // turn and phase last seen, to tell when a phase ends
    TurnPhase mPhase;
    string mTurnPlayer;
    bool mLoggedGameOver;

    static readonly string[] UnitCircles = { "LeftFront", "Vanguard", "RightFront", "LeftBack", "CenterBack", "RightBack" };
    static readonly string[] PileZones = { "Guardian", "Trigger", "Order", "Crest", "Soul", "Drop", "Damage", "Bind", "OOG" };

    void Awake()
    {
        sInstance = this;
        Notifications.Clear();

        string seat = MatchSession.IsOnline ? " P" + (MatchSession.LocalSeat + 1) : ""; // two editors on one PC get a file each
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "MatchLogs");
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, "Match " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + seat + ".txt");
            mWriter = new StreamWriter(FilePath, false, Encoding.UTF8) { AutoFlush = true };
            Debug.Log("[MatchLog] Writing to " + FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MatchLog] Can't write the log file, so it's Console only: " + e.Message);
            mWriter = null;
        }

        WriteLine("Match started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
            (MatchSession.IsOnline ? ", online room " + MatchSession.RoomCode : ", one PC") + ": " + string.Join(" vs ", MatchSession.PlayerNames));
    }

    void OnEnable()
    {
        GameState.OnPhaseChanged += HandlePhase;
        GameState.OnRuleNotice += HandleNotice;
        GameState.OnPersonaRide += HandlePersonaRide;
        GameState.OnTriggerActivated += HandleTrigger;
        GameState.OnGiftReceived += HandleGift;
    }

    void OnDisable()
    {
        GameState.OnPhaseChanged -= HandlePhase;
        GameState.OnRuleNotice -= HandleNotice;
        GameState.OnPersonaRide -= HandlePersonaRide;
        GameState.OnTriggerActivated -= HandleTrigger;
        GameState.OnGiftReceived -= HandleGift;
    }

    void OnDestroy()
    {
        if (sInstance == this) sInstance = null;
        WriteLine("Match log closed.");
        mWriter?.Dispose();
        mWriter = null;
    }

    // An action that isn't a card the tracker saw, e.g. a keyword, a button or a count fixed by hand
    public static void Action(string text)
    {
        if (sInstance != null) sInstance.Write("Action", text);
        else Debug.Log("[MatchLog] Action  " + text);
    }

    /* Card changes, called by GameState before its events fire so each action is logged before
     the rule notices it sets off. Online, the opponent's cards arrive through here too. */
    public static void CardMoved(CardInstance card, PlayerState fromOwner, string fromZone)
    {
        string text = CardName(card) + ": " + Where(fromOwner, fromZone) + " -> " + Where(card.zoneOwner, card.zone);
        if (card.zone == "Vanguard" && card.zoneOwner.previousVanguard != null)
            text += " (over " + CardName(card.zoneOwner.previousVanguard) + ")";
        Action(text);
    }

    public static void CardTurned(CardInstance card) =>
        Action(CardName(card) + (card.orientation == CardOrientation.Rest ? " rested" : " stood") + " on " + Where(card.zoneOwner, card.zone));

    public static void CardFlipped(CardInstance card) =>
        Action(CardName(card) + " turned " + (card.facing == CardFacing.FaceDown ? "face down" : "face up") + " in " + Where(card.zoneOwner, card.zone));

    // Phases

    // Fires on every broadcast; a new turn or phase means the last one ended
    void HandlePhase(TurnPhase phase, string turnPlayer)
    {
        GameState gs = GameState.Instance;
        if (gs == null || (gs.turnNumber == mTurn && phase == mPhase)) return;

        if (mTurn > 0 && (mPhase == TurnPhase.RidePhase || mPhase == TurnPhase.MainPhase || mPhase == TurnPhase.BattlePhase))
            WriteBoard("End of " + mTurnPlayer + "'s turn " + mTurn + " " + PhaseName(mPhase));

        mTurn = gs.turnNumber;
        mPhase = phase;
        mTurnPlayer = turnPlayer;
        if (mTurn > 0) Write("Phase", "--- " + turnPlayer + "'s turn " + mTurn + ": " + PhaseName(phase) + " ---");
    }

    // Notifications

    void HandleNotice(string title, string body)
    {
        AddNotification(title, body);

        GameState gs = GameState.Instance;
        if (gs != null && gs.gameOver && !mLoggedGameOver)
        {
            mLoggedGameOver = true;
            WriteBoard("Game over");
        }
    }

    void HandlePersonaRide(string cardName) => AddNotification("Persona Ride!", cardName + ": draw 1, vanguard Power +10000.");

    void HandleTrigger(TriggerIcon icon, string context) => AddNotification(icon + " trigger", "Revealed in a " + context + " check.");

    void HandleGift(ImaginaryGift gift, GiftType type) => AddNotification("Imaginary Gift", gift + " " + type);

    // The same notice again straight after itself (e.g. a recount) isn't repeated
    void AddNotification(string title, string body)
    {
        string entry = "[" + Stamp() + "] " + title + ": " + body;
        if (Notifications.Count > 0 && Notifications[Notifications.Count - 1] == entry) return;

        Notifications.Add(entry);
        Write("Notice", title + ": " + body);
        OnNotification?.Invoke(entry);
    }

    // Board snapshot

    // Every card on both players' boards, circle by circle, then the piles
    void WriteBoard(string heading)
    {
        GameState gs = GameState.Instance;
        if (gs == null || gs.players[0] == null || gs.players[1] == null) return;

        var sb = new StringBuilder();
        sb.Append("=== Board: ").Append(heading).Append(" ===");
        foreach (PlayerState player in gs.players)
        {
            sb.Append("\n  ").Append(player.playerName).Append(':');
            foreach (string circle in UnitCircles)
            {
                CardInstance card = player.CardOnCircle(circle);
                sb.Append("\n    ").Append(circle.PadRight(11)).Append(card != null ? Describe(card) : "-");
            }

            foreach (string zone in PileZones)
            {
                int count = player.CountInZone(zone);
                List<CardInstance> seen = player.ZoneList(zone);
                if (count == 0 && seen.Count == 0) continue;

                sb.Append("\n    ").Append(zone.PadRight(11)).Append(count);
                if (zone == "Damage") sb.Append(" (").Append(player.FaceUpDamage).Append(" face up)");
                if (seen.Count > 0) sb.Append(": ").Append(string.Join(", ", seen.Select(c => CardName(c) + (c.facing == CardFacing.FaceDown ? " (face down)" : ""))));
            }

            sb.Append("\n    ").Append("Deck".PadRight(11)).Append(player.CountInZone("Deck"))
              .Append(", ride deck ").Append(player.CountInZone("RideDeck"));
        }
        WriteLine(sb.ToString());
    }

    // e.g. "Blaster Blade (G2, 10000+10000, rested)"
    static string Describe(CardInstance card)
    {
        string power = card.data.power + (card.powerModifier != 0 ? card.powerModifier.ToString("+0;-0") : "");
        return CardName(card) + " (G" + card.data.grade + ", " + power +
            (card.TotalCritical != 1 ? ", C" + card.TotalCritical : "") +
            (card.orientation == CardOrientation.Rest ? ", rested" : "") +
            (card.facing == CardFacing.FaceDown ? ", face down" : "") + ")";
    }

    // Writing

    void Write(string category, string text) => WriteLine(DateTime.Now.ToString("HH:mm:ss") + " [" + Stamp() + "] " + category.PadRight(6) + " " + text);

    void WriteLine(string line)
    {
        Debug.Log("[MatchLog] " + line);
        if (mWriter == null) return;
        try { mWriter.WriteLine(line); }
        catch (Exception e)
        {
            Debug.LogWarning("[MatchLog] Stopped writing the log file: " + e.Message);
            mWriter = null;
        }
    }

    // e.g. "T3 Ride"; "Setup" before the first turn
    static string Stamp()
    {
        GameState gs = GameState.Instance;
        if (gs == null || gs.turnNumber == 0) return "Setup";
        return "T" + gs.turnNumber + " " + gs.currentPhase.ToString().Replace("Phase", "");
    }

    static string PhaseName(TurnPhase phase) => phase.ToString().Replace("Phase", " Phase");

    static string CardName(CardInstance card) => card.data != null ? card.data.cardName : "Unknown card";

    static string Where(PlayerState owner, string zone) => owner != null && zone != null ? owner.playerName + "'s " + zone : "off the board";
}

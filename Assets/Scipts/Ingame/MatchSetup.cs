using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Turn 0: coin toss, opening hand and mulligan, then "Stand up, Vanguard!"
public class MatchSetup
{
    public enum Stage { LayOut, Mulligan, PickUp, Ready }

    public const string PhrasePrefix = "setup "; // ActionButtons' phrases, e.g. "setup keep 0"
    static readonly string[] Circles = { "LeftFront", "RightFront", "LeftBack", "CenterBack", "RightBack" };

    readonly GameState gs;
    readonly System.Action<string, string> mNotice;
    readonly System.Action<string, string, System.Func<bool>> mToDo;
    readonly Stage[] mStage = new Stage[2];
    readonly bool[] mHere = new bool[2]; // this PC does that player's setup
    readonly List<CardInstance>[] mHand = { new List<CardInstance>(), new List<CardInstance>() }; // as first laid out

    // The camera checks the hand; false face to face on one PC
    public bool CameraChecks { get; }

    // Seat of the player going first; -1 until the online host's toss arrives
    public int FirstPlayer { get; private set; } = -1;

    public bool Done => FirstPlayer >= 0 && mStage.All(s => s == Stage.Ready);

    // Online: the opening hand on the rear-guard circles is left out of the seat sync
    public bool HidesRearguards => MatchSession.IsOnline && !Done;

    public MatchSetup(GameState state, System.Action<string, string> notice, System.Action<string, string, System.Func<bool>> toDo)
    {
        gs = state;
        mNotice = notice;
        mToDo = toDo;
        CameraChecks = MatchSession.IsOneSide;

        for (int i = 0; i < 2; i++)
        {
            mHere[i] = MatchSession.IsSolo ? i == 0 : MatchSession.IsLocalSeat(i);
            mStage[i] = CameraChecks ? Stage.LayOut : Stage.Mulligan;
        }
        if (MatchSession.IsSolo) { mStage[1] = Stage.Ready; } // no opponent
        gs.handOnCircles = CameraChecks;

        // Online the host tosses and the guest hears it through the seat sync (ApplyRemote)
        if (MatchSession.IsSolo) { FirstPlayer = 0; }
        else if (!MatchSession.IsOnline || MatchSession.LocalSeat == 0) { FirstPlayer = Random.Range(0, 2); }
        if (FirstPlayer >= 0 && !MatchSession.IsSolo) { AnnounceFirst(); }

        for (int i = 0; i < 2; i++) if (mHere[i]) { Ask(i); }
    }

    // Each frame until Done: the camera's view of the hand on the circles
    public void Update()
    {
        if (!CameraChecks) { return; }
        for (int i = 0; i < 2; i++)
        {
            if (!mHere[i]) { continue; }
            int seen = OnCircles(i).Count;
            if (mStage[i] == Stage.LayOut && seen >= 5) { LaidOut(i); }
            else if (mStage[i] == Stage.PickUp && seen == 0) { SetStage(i, Stage.Ready); }
        }
    }

    // The game is starting: orders on circles are mistakes again
    public void Finish() => gs.handOnCircles = false;

    List<CardInstance> OnCircles(int seat) => gs.players[seat].AllRearguards().ToList();

    string Name(int seat) => gs.players[seat].playerName;

    void AnnounceFirst()
    {
        MatchLogger.Action("Coin toss: " + Name(FirstPlayer) + " goes first");
        mNotice("Coin toss", Name(FirstPlayer) + " goes first. " + Name(1 - FirstPlayer) + " goes second.");
    }

    // The to-do for this player's stage
    void Ask(int seat)
    {
        string name = Name(seat);
        Stage stage = mStage[seat];
        System.Func<bool> still = () => mStage[seat] == stage;
        switch (stage)
        {
            case Stage.LayOut:
                // One to-do for laying out and the mulligan, so it stays up while the hand is swapped
                mToDo("Opening hand", name + ": put your grade 0 starting vanguard face down on the vanguard circle, shuffle your deck and draw 5.\n" +
                    "Mulligan (optional, once): put the cards you don't want back in your deck, draw that many, then shuffle.\n" +
                    "Press Keep hand when your hand is final.",
                    () => mStage[seat] == Stage.LayOut || mStage[seat] == Stage.Mulligan);
                break;
            case Stage.Mulligan:
                if (CameraChecks) { break; } // the lay-out to-do already explains it
                mToDo("Opening hand", name + ": put your grade 0 starting vanguard face down on the vanguard circle, shuffle your deck and draw 5. " +
                    "You may mulligan once: put any cards back in your deck, draw that many, then shuffle. Then press Ready.", still);
                break;
            case Stage.PickUp:
                mToDo("Opening hand", name + ": pick your hand up off the rear-guard circles.", still);
                break;
            case Stage.Ready:
                string waiting = string.Join(" and ", Enumerable.Range(0, 2).Where(s => mStage[s] != Stage.Ready).Select(Name));
                mNotice("Ready", name + " is ready." + (waiting.Length > 0 ? " Waiting for " + waiting + "." : ""));
                break;
        }
    }

    void SetStage(int seat, Stage stage)
    {
        if (mStage[seat] == stage) { return; }
        mStage[seat] = stage;
        if (mHere[seat] || stage == Stage.Ready) { Ask(seat); } // the opponent's own to-dos are on their PC
    }

    // The hand as laid out, from which a mulligan's swaps are counted
    void LaidOut(int seat)
    {
        mHand[seat] = OnCircles(seat);
        MatchLogger.Action(Name(seat) + "'s opening hand (" + mHand[seat].Count + " seen): " + string.Join(", ", mHand[seat].Select(c => c.data.DisplayName)));
        SetStage(seat, Stage.Mulligan);
    }

    // Keep hand: the hand is final; cards seen swapped since it was laid out were a mulligan
    void KeepHand(int seat)
    {
        List<CardInstance> now = OnCircles(seat);
        int returned = mHand[seat].Count(c => !now.Contains(c));
        int drawn = now.Count(c => !mHand[seat].Contains(c));
        int count = Mathf.Max(returned, drawn);
        if (count > 0)
        {
            MatchLogger.Action(Name(seat) + " mulliganed " + count + ": " + string.Join(", ", mHand[seat].Where(c => !now.Contains(c)).Select(c => c.data.DisplayName)) +
                " for " + string.Join(", ", now.Where(c => !mHand[seat].Contains(c)).Select(c => c.data.DisplayName)));
            mNotice("Mulligan", Name(seat) + " put back and redrew " + count + " card(s). Make sure your deck is shuffled.");
        }
        else
        {
            MatchLogger.Action(Name(seat) + " kept their hand");
            mNotice("Opening hand", Name(seat) + " kept their hand.");
        }
        SetStage(seat, Stage.PickUp);
    }

    // The buttons for this PC's players, as (label, phrase) for ActionButtons
    public List<(string label, string phrase)> Buttons()
    {
        var buttons = new List<(string, string)>();
        bool names = !MatchSession.IsOneSide; // one PC: whose button it is
        for (int seat = 0; seat < 2; seat++)
        {
            if (!mHere[seat]) { continue; }
            string who = names ? Name(seat) + ":\n" : "";
            switch (mStage[seat])
            {
                case Stage.LayOut: // not all 5 seen yet: the same buttons
                case Stage.Mulligan:
                    if (!CameraChecks) { buttons.Add((who + "Ready", PhrasePrefix + "ready " + seat)); }
                    else { buttons.Add((who + "Keep hand", PhrasePrefix + "keep " + seat)); }
                    break;
                case Stage.PickUp: buttons.Add((who + "Hand picked up", PhrasePrefix + "up " + seat)); break;
            }
        }
        return buttons;
    }

    // An ActionButtons press, e.g. "setup keep 0"
    public void Press(string phrase)
    {
        string[] parts = phrase.Substring(PhrasePrefix.Length).Split(' ');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int seat) || seat < 0 || seat > 1 || !mHere[seat]) { return; }

        switch (parts[0])
        {
            case "keep":
                if (mStage[seat] == Stage.LayOut) { LaidOut(seat); }
                if (mStage[seat] == Stage.Mulligan) { KeepHand(seat); }
                break;
            case "up":
                if (mStage[seat] != Stage.PickUp) { return; }
                // Still seen there: the camera holding on to them. Off the board, and seen again only once moved
                foreach (CardInstance card in OnCircles(seat)) gs.PutBackQuietly(card, null, null);
                gs.BroadcastState();
                SetStage(seat, Stage.Ready);
                break;
            case "ready":
                if (mStage[seat] != Stage.Mulligan) { return; }
                MatchLogger.Action(Name(seat) + " is ready");
                SetStage(seat, Stage.Ready);
                break;
        }
    }

    // For the HUD's Waiting line
    public string Waiting
    {
        get
        {
            for (int seat = 0; seat < 2; seat++)
            {
                if (!mHere[seat]) { continue; }
                string who = MatchSession.IsOneSide ? "" : Name(seat) + ": ";
                switch (mStage[seat])
                {
                    case Stage.LayOut: return who + "draw 5, mulligan if you want, then Keep hand";
                    case Stage.Mulligan: return who + (CameraChecks ? "mulligan if you want, then Keep hand" : "draw 5, mulligan if you want, then Ready");
                    case Stage.PickUp: return who + "pick your hand up";
                }
            }
            for (int seat = 0; seat < 2; seat++)
                if (mStage[seat] != Stage.Ready) { return Name(seat) + " to finish their opening hand"; }
            return FirstPlayer < 0 ? "the coin toss" : null;
        }
    }

    // Online: this PC's stage, and the host's toss, for the seat sync (OnlineMatch)
    public Dictionary<string, object> SeatField()
    {
        var field = new Dictionary<string, object> { ["stage"] = (long)mStage[MatchSession.LocalSeat] };
        if (MatchSession.LocalSeat == 0 && FirstPlayer >= 0) { field["first"] = (long)FirstPlayer; }
        return field;
    }

    // The opponent's stage, and on the guest's PC the host's toss
    public void ApplyRemote(Dictionary<string, object> field)
    {
        if (field == null || Done) { return; }
        int remote = MatchSession.RemoteSeat;
        if (FirstPlayer < 0 && remote == 0 && field.ContainsKey("first"))
        {
            FirstPlayer = (int)field.GetLong("first") == 1 ? 1 : 0;
            AnnounceFirst();
        }
        Stage stage = (Stage)Mathf.Clamp((int)field.GetLong("stage"), 0, (int)Stage.Ready);
        if (stage == Stage.Ready && mStage[remote] != Stage.Ready) { MatchLogger.Action(Name(remote) + " is ready"); }
        SetStage(remote, stage);
    }

    // Online: the opponent started turn 1, so their toss stands even if this PC hasn't heard it
    public void TakeFirst(int seat)
    {
        if (FirstPlayer >= 0 || seat < 0 || seat > 1) { return; }
        FirstPlayer = seat;
        AnnounceFirst();
    }
}

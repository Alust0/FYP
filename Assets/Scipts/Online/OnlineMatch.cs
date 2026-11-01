using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/* Keeps the two PCs of an online match in step through the room entry (see LobbyController).
 Each PC's camera only watches its own half of the board (BoardLayout's Multiplayer mode), so it
 uploads that half and mirrors the opponent's half from theirs:
   seat0 / seat1  {cards: [{id, number, zone, rest, faceDown}], counts: {zone: n}, attackTarget, said, saidN} —
                  counts are untrackedCards; attackTarget is the circle the turn player's current attack
                  targets; said is that player's latest voice keyword and saidN how many they've said;
                  pick / pickN the same for where they gave a trigger's power, critical or stand;
                  turned whether their view is turned (RotatedView)
   turn           {player, number, phase} — written by whichever PC moved the turn on, or undid
   undo           {at, seat, sinceMs, player, number, phase} — the latest undo: when it was sent, by
                  whom, and when the state it went back to began, so the other PC goes back there too
 The opponent's cards go through the same GameState calls the trackers use, so the rules, HUD and
 battle overlay treat them like cards seen by a camera. Added by GameSetup when the match is online. */
public class OnlineMatch : MonoBehaviour
{
    [Tooltip("Seconds between syncs. Each sync is one database read, plus a write when this PC's board changed.")]
    public float syncSeconds = 1f;
    public float heartbeatSeconds = 5f;
    [Tooltip("The opponent counts as disconnected after this long without a heartbeat.")]
    public float opponentTimeoutSeconds = 20f;

    // Every zone a card can be in, single-card circles first
    static readonly string[] Zones =
    {
        "Vanguard", "LeftFront", "RightFront", "LeftBack", "RightBack", "CenterBack",
        "Guardian", "Damage", "Drop", "Deck", "RideDeck", "Order", "Crest", "Bind", "Trigger", "OOG", "Soul",
    };

    class RemoteCard
    {
        public CardInstance instance; // null while its card data loads
        public Dictionary<string, object> wanted; // latest state from the opponent, null once off their board
    }

    GameState gs;
    RulesEngine rules;

    string RoomPath => "rooms/" + MatchSession.RoomCode;
    static string SeatField(int seat) => "seat" + seat;
    static string RoleField(int seat) => seat == 0 ? "host" : "guest";

    int mSyncedTurnKey; // TurnKey last uploaded or applied
    string mUploadedSeat; // this PC's seat as last uploaded, to skip unchanged writes
    string mAppliedSeat; // the opponent's seat as last applied
    float mLastBeat = float.NegativeInfinity;
    bool mOpponentGone;
    bool mReadFailing;
    long mAppliedSaidN = -1; // the opponent's keyword count last shown; -1 until their seat is first seen
    long mAppliedPickN = -1; // likewise their trigger picks
    long mAppliedSkillN = -1; // and the skills they used or skipped
    int mSentUndos; // RulesEngine.UndoCount already uploaded
    long mAppliedUndoAt = -1; // the room's undo last handled; -1 until the room is first read

    // Keyed by the CardInstance.id the opponent's PC gave the card
    readonly Dictionary<string, RemoteCard> mRemoteCards = new Dictionary<string, RemoteCard>();

    static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // The opponent's view is turned (RotatedView), so the picture sent to them turns its zone labels to suit
    public static bool OpponentTurned { get; private set; }

    /* A step of the opponent's skill that's this PC's player to do (their OpponentStep), as they're told;
     null for none. ActionButtons shows its Done, whose "skill done" reaches their PC */
    public static string OpponentStepForYou { get; private set; }
    static string sDoneStep; // the one already answered, until their PC moves on

    public static void DoneOpponentStep()
    {
        sDoneStep = OpponentStepForYou;
        OpponentStepForYou = null;
    }

    // Turn and phase as one number that only goes up as the game goes on
    int TurnKey() => gs.turnNumber * 10 + (int)gs.currentPhase;

    IEnumerator Start()
    {
        if (!MatchSession.IsOnline)
        {
            enabled = false;
            yield break;
        }

        gs = GameState.Instance;
        OpponentTurned = false;
        OpponentStepForYou = sDoneStep = null;
        rules = FindFirstObjectByType<RulesEngine>();
        if (rules == null)
        {
            Debug.LogError("[OnlineMatch] No RulesEngine in the scene.");
            yield break;
        }

        yield return new WaitUntil(() => rules.IsStarted);
        mSyncedTurnKey = TurnKey();
        mSentUndos = RulesEngine.UndoCount;

        int remote = MatchSession.RemoteSeat;
        Notice("Online match", "Room " + MatchSession.RoomCode + ": you are " + gs.players[MatchSession.LocalSeat].playerName +
            " vs " + gs.players[remote].playerName + ". " + gs.players[0].playerName + " goes first.");

        while (true)
        {
            yield return Upload();
            yield return Download();
            yield return new WaitForSeconds(syncSeconds);
        }
    }

    // Tells the opponent this PC has left, so they aren't left waiting
    void OnDestroy()
    {
        if (!MatchSession.IsOnline) return;
        RtdbRest.PatchWithoutWaiting(RoomPath, new Dictionary<string, object> { ["status"] = "closed" }, new[] { "status" });
        MatchSession.End();
    }

    // Upload: this PC's half, the turn if it moved it on, and a heartbeat

    IEnumerator Upload()
    {
        var fields = new Dictionary<string, object>();
        var mask = new List<string>();
        int local = MatchSession.LocalSeat;

        Dictionary<string, object> seat = SeatSnapshot(gs.players[local]);
        if (gs.currentPlayerIndex == local && gs.attackTarget != null) seat["attackTarget"] = gs.attackTarget;
        if (RulesEngine.PickCount > 0)
        {
            seat["pick"] = RulesEngine.LastPick;
            seat["pickN"] = (long)RulesEngine.PickCount;
        }
        if (VoiceCommands.SaidCount > 0)
        {
            seat["said"] = VoiceCommands.LastSaid;
            seat["saidN"] = (long)VoiceCommands.SaidCount;
        }
        if (SkillRules.SentCount > 0)
        {
            seat["skill"] = SkillRules.LastTitle;
            seat["skillText"] = SkillRules.LastBody;
            seat["skillN"] = (long)SkillRules.SentCount;
        }
        // A step of this PC's skill that's the opponent's to do: their PC shows it and its Done
        string opponentStep = rules.Skills?.OpponentStepWaiting;
        if (opponentStep != null) seat["oppStep"] = opponentStep;
        // Their cards this PC's rules want moved, e.g. retired by a skill: arrows on their PC (CardMoves)
        string moves = rules.Moves.ForPlayer(gs.players[MatchSession.RemoteSeat]);
        if (moves != null) seat["moves"] = moves;
        seat["turned"] = GameSettings.RotateView;
        string seatJson = RtdbRest.ToJson(seat);
        if (seatJson != mUploadedSeat)
        {
            fields[SeatField(local)] = seat;
            mask.Add(SeatField(local));
        }

        // An undo goes back, so it writes the turn even though it's behind the one written before
        int undos = RulesEngine.UndoCount;
        bool undid = undos > mSentUndos;
        if (undid)
        {
            var undo = RulesEngine.LastUndo;
            fields["undo"] = new Dictionary<string, object>
            {
                ["at"] = NowMs,
                ["seat"] = local,
                ["sinceMs"] = undo.sinceMs,
                ["player"] = undo.player,
                ["number"] = undo.turn,
                ["phase"] = (int)undo.phase,
            };
            mask.Add("undo");
        }

        int turnKey = TurnKey();
        if (turnKey > mSyncedTurnKey || undid)
        {
            fields["turn"] = new Dictionary<string, object>
            {
                ["player"] = gs.currentPlayerIndex,
                ["number"] = gs.turnNumber,
                ["phase"] = (int)gs.currentPhase,
            };
            mask.Add("turn");
        }

        bool beat = Time.time - mLastBeat >= heartbeatSeconds;
        if (beat)
        {
            fields[RoleField(local)] = new Dictionary<string, object> { ["beatMs"] = NowMs };
            mask.Add(RoleField(local) + ".beatMs");
        }

        if (mask.Count == 0) yield break;

        RtdbRest.Result result = null;
        yield return RtdbRest.Patch(RoomPath, fields, mask, r => result = r);
        if (!result.succeeded) yield break; // kept as unsent, so the next sync tries again

        if (mask.Contains(SeatField(local))) mUploadedSeat = seatJson;
        if (mask.Contains("turn")) mSyncedTurnKey = undid ? turnKey : Mathf.Max(mSyncedTurnKey, turnKey);
        if (undid) mSentUndos = undos;
        if (beat) mLastBeat = Time.time;
    }

    static Dictionary<string, object> SeatSnapshot(PlayerState player)
    {
        var cards = new List<object>();
        foreach (string zone in Zones)
        {
            IEnumerable<CardInstance> inZone = player.ZoneList(zone) ?? new List<CardInstance> { player.CardOnCircle(zone) };
            foreach (CardInstance card in inZone.Where(c => c != null))
            {
                cards.Add(new Dictionary<string, object>
                {
                    ["id"] = card.id,
                    ["number"] = card.data.cardNumber,
                    ["zone"] = zone,
                    ["rest"] = card.orientation == CardOrientation.Rest,
                    ["faceDown"] = card.facing == CardFacing.FaceDown,
                });
            }
        }

        var counts = new Dictionary<string, object>();
        foreach (KeyValuePair<string, int> pair in player.untrackedCards)
            if (pair.Value != 0) counts[pair.Key] = (long)pair.Value;

        return new Dictionary<string, object> { ["cards"] = cards, ["counts"] = counts };
    }

    // Download: the opponent's turn moves, their half, and whether they're still there

    IEnumerator Download()
    {
        RtdbRest.Result room = null;
        yield return RtdbRest.Get(RoomPath, r => room = r);

        if (!room.succeeded)
        {
            if (!mReadFailing) Debug.LogWarning("[OnlineMatch] Couldn't read the room: " + room.error);
            mReadFailing = true;
            yield break;
        }
        mReadFailing = false;

        CheckOpponent(room.fields);
        ApplyUndo(room.fields.GetMap("undo")); // first: the turn written with it is behind this PC's
        ApplyTurn(room.fields.GetMap("turn"));

        Dictionary<string, object> seat = room.fields.GetMap(SeatField(MatchSession.RemoteSeat));
        if (seat != null)
        {
            string seatJson = RtdbRest.ToJson(seat);
            if (seatJson != mAppliedSeat && !gs.gameOver) // the board is final once the game is over
            {
                mAppliedSeat = seatJson;
                // An error in the rules must not end this coroutine, which would stop syncing for good
                try { ApplySeat(seat); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    void CheckOpponent(Dictionary<string, object> room)
    {
        Dictionary<string, object> opponent = room.GetMap(RoleField(MatchSession.RemoteSeat));
        bool closed = room.GetString("status") == "closed";
        bool silent = opponent == null || NowMs - opponent.GetLong("beatMs") > opponentTimeoutSeconds * 1000f;

        if ((closed || silent) && !mOpponentGone)
            Notice("Opponent disconnected", gs.players[MatchSession.RemoteSeat].playerName +
                (closed ? " left the match." : " hasn't been heard from for a while."));
        else if (!closed && !silent && mOpponentGone)
            Notice("Opponent reconnected", gs.players[MatchSession.RemoteSeat].playerName + " is back.");

        mOpponentGone = closed || silent;
    }

    // The opponent undid: this PC goes back to the same moment. Undos from before this PC joined aren't news
    void ApplyUndo(Dictionary<string, object> undo)
    {
        long at = undo?.GetLong("at") ?? 0;
        if (mAppliedUndoAt < 0 || at <= mAppliedUndoAt)
        {
            mAppliedUndoAt = Math.Max(mAppliedUndoAt, at);
            return;
        }
        mAppliedUndoAt = at;
        if ((int)undo.GetLong("seat") == MatchSession.LocalSeat) return; // this PC's own

        rules.ApplyRemoteUndo(undo.GetLong("sinceMs"), (int)undo.GetLong("player"), (int)undo.GetLong("number"),
            (TurnPhase)undo.GetLong("phase"), gs.players[MatchSession.RemoteSeat].playerName);
        mSyncedTurnKey = TurnKey();
        mAppliedSeat = null; // their half again, over the counts the rollback put back
    }

    void ApplyTurn(Dictionary<string, object> turn)
    {
        if (turn == null) return;

        int number = (int)turn.GetLong("number");
        TurnPhase phase = (TurnPhase)turn.GetLong("phase");
        int key = number * 10 + (int)phase;
        if (key <= mSyncedTurnKey) return; // already here, or this PC is ahead

        mSyncedTurnKey = key;
        rules.ApplyRemoteTurn((int)turn.GetLong("player"), number, phase);
    }

    void ApplySeat(Dictionary<string, object> seat)
    {
        OpponentTurned = seat.GetBool("turned");
        rules.Moves.ApplyRemote(seat.GetString("moves"), gs.players[MatchSession.LocalSeat]);

        // Their skill waiting on this PC's player: a to-do here, answered with ActionButtons' Done
        string step = seat.GetString("oppStep");
        if (step != OpponentStepForYou && step != sDoneStep)
        {
            OpponentStepForYou = step;
            if (step != null)
            {
                GameState.OnRuleToDo?.Invoke("Your part", step, () => OpponentStepForYou == step);
                Notice("Your part", step);
            }
        }

        /* Their latest keyword, before their cards: a guard or damage check is said before the
         cards it's about are put down. Keywords from before this PC joined aren't news. */
        long saidN = seat.GetLong("saidN");
        if (mAppliedSaidN < 0) mAppliedSaidN = saidN;
        else if (saidN > mAppliedSaidN)
        {
            mAppliedSaidN = saidN;
            VoiceCommands.Instance?.HeardRemote(seat.GetString("said"), gs.players[MatchSession.RemoteSeat]);
        }

        long pickN = seat.GetLong("pickN");
        if (mAppliedPickN < 0) mAppliedPickN = pickN;
        else if (pickN > mAppliedPickN)
        {
            mAppliedPickN = pickN;
            rules.ApplyRemotePick(seat.GetString("pick"));
        }

        // The skill they last used or skipped, for this PC's notices and log
        long skillN = seat.GetLong("skillN");
        if (mAppliedSkillN < 0) mAppliedSkillN = skillN;
        else if (skillN > mAppliedSkillN)
        {
            mAppliedSkillN = skillN;
            rules.Skills?.ApplyRemote(seat.GetString("skill"), seat.GetString("skillText"));
        }

        var seen = new HashSet<string>();
        foreach (Dictionary<string, object> card in (seat.GetList("cards") ?? new List<object>()).OfType<Dictionary<string, object>>())
        {
            string id = card.GetString("id");
            if (id == null) continue;
            seen.Add(id);

            if (!mRemoteCards.TryGetValue(id, out RemoteCard remote))
            {
                remote = new RemoteCard();
                mRemoteCards[id] = remote;
                LoadCard(remote, card.GetString("number"));
            }
            remote.wanted = card;
        }

        foreach (KeyValuePair<string, RemoteCard> pair in mRemoteCards)
            if (!seen.Contains(pair.Key)) pair.Value.wanted = null;

        PlayerState player = gs.players[MatchSession.RemoteSeat];
        player.untrackedCards.Clear();
        foreach (KeyValuePair<string, object> pair in seat.GetMap("counts") ?? new Dictionary<string, object>())
            if (pair.Value is long n) player.untrackedCards[pair.Key] = (int)n;

        ApplyRemoteCards();

        // Their attack's target, once its attack has arrived through ApplyRemoteCards
        string target = seat.GetString("attackTarget");
        if (target != null && target != gs.attackTarget && gs.currentPlayerIndex == MatchSession.RemoteSeat) rules.ChooseTarget(target);

        gs.CheckWinCondition();
        gs.BroadcastState();
    }

    /* Zones first, in Zones order: a new vanguard lands before the old one is seen in the soul, so
     it counts as a ride (and a Persona Ride) like on the opponent's PC. Then stand/rest, front row
     before back row, since a boost only counts once the unit in front is attacking. */
    void ApplyRemoteCards()
    {
        PlayerState player = gs.players[MatchSession.RemoteSeat];
        List<RemoteCard> loaded = mRemoteCards.Values.Where(c => c.instance != null).ToList();

        foreach (RemoteCard card in loaded.OrderBy(c => Array.IndexOf(Zones, c.wanted?.GetString("zone"))))
        {
            string zone = card.wanted?.GetString("zone");
            gs.MoveCard(card.instance, zone != null ? player : null, zone);
        }

        foreach (RemoteCard card in loaded.Where(c => c.wanted != null).OrderBy(c => Array.IndexOf(Zones, c.instance.zone)))
        {
            gs.SetOrientation(card.instance, card.wanted.GetBool("rest") ? CardOrientation.Rest : CardOrientation.Stand);
            gs.SetFacing(card.instance, card.wanted.GetBool("faceDown") ? CardFacing.FaceDown : CardFacing.FaceUp);
        }
    }

    // Same lookup order as the trackers: Firestore first, local API cache as fallback
    void LoadCard(RemoteCard remote, string cardNumber)
    {
        void Apply(CardData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[OnlineMatch] No CardData found for the opponent's card " + cardNumber);
                return;
            }
            remote.instance = new CardInstance(data);
            ApplyRemoteCards();
            gs.BroadcastState();
        }
        CardData FromLocalCache() => ApiCardDatabase.Instance != null ? ApiCardDatabase.Instance.GetByNumber(cardNumber) : null;

        if (CardFirestoreDatabase.Instance != null)
            CardFirestoreDatabase.Instance.GetCardByNumber(cardNumber, Apply, _ => Apply(FromLocalCache()));
        else
            Apply(FromLocalCache());
    }

    // Logged by MatchLogger
    static void Notice(string title, string body) => GameState.OnRuleNotice?.Invoke(title, body);
}

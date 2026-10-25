using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/* Keeps the two PCs of an online match in step through the room document (see LobbyController).
 Each PC's camera only watches its own half of the board (BoardLayout's Multiplayer mode), so it
 uploads that half and mirrors the opponent's half from theirs:
   seat0 / seat1  {cards: [{id, number, zone, rest, faceDown}], counts: {zone: n}} — counts are untrackedCards
   turn           {player, number, phase} — written by whichever PC moved the turn on
 The opponent's cards go through the same GameState calls the trackers use, so the rules, HUD and
 battle overlay treat them like cards seen by a camera. Added by GameSetup when the match is online. */
public class OnlineMatch : MonoBehaviour
{
    [Tooltip("Seconds between syncs. Each sync is one Firestore read, plus a write when this PC's board changed.")]
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

    // Keyed by the CardInstance.id the opponent's PC gave the card
    readonly Dictionary<string, RemoteCard> mRemoteCards = new Dictionary<string, RemoteCard>();

    static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
        rules = FindFirstObjectByType<RulesEngine>();
        if (rules == null)
        {
            Debug.LogError("[OnlineMatch] No RulesEngine in the scene.");
            yield break;
        }

        yield return new WaitUntil(() => rules.IsStarted);
        mSyncedTurnKey = TurnKey();

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
        FirestoreRest.PatchWithoutWaiting(RoomPath, new Dictionary<string, object> { ["status"] = "closed" }, new[] { "status" });
        MatchSession.End();
    }

    // Upload: this PC's half, the turn if it moved it on, and a heartbeat

    IEnumerator Upload()
    {
        var fields = new Dictionary<string, object>();
        var mask = new List<string>();
        int local = MatchSession.LocalSeat;

        Dictionary<string, object> seat = SeatSnapshot(gs.players[local]);
        string seatJson = FirestoreRest.ToJson(seat);
        if (seatJson != mUploadedSeat)
        {
            fields[SeatField(local)] = seat;
            mask.Add(SeatField(local));
        }

        int turnKey = TurnKey();
        if (turnKey > mSyncedTurnKey)
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

        FirestoreRest.Result result = null;
        yield return FirestoreRest.Patch(RoomPath, fields, mask, r => result = r);
        if (!result.ok) yield break; // kept as unsent, so the next sync tries again

        if (mask.Contains(SeatField(local))) mUploadedSeat = seatJson;
        if (mask.Contains("turn")) mSyncedTurnKey = Mathf.Max(mSyncedTurnKey, turnKey);
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
        FirestoreRest.Result room = null;
        yield return FirestoreRest.Get(RoomPath, r => room = r);

        if (!room.ok)
        {
            if (!mReadFailing) Debug.LogWarning("[OnlineMatch] Couldn't read the room: " + room.error);
            mReadFailing = true;
            yield break;
        }
        mReadFailing = false;

        CheckOpponent(room.fields);
        ApplyTurn(room.fields.GetMap("turn"));

        Dictionary<string, object> seat = room.fields.GetMap(SeatField(MatchSession.RemoteSeat));
        if (seat != null)
        {
            string seatJson = FirestoreRest.ToJson(seat);
            if (seatJson != mAppliedSeat)
            {
                mAppliedSeat = seatJson;
                ApplySeat(seat);
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

    static void Notice(string title, string body)
    {
        Debug.Log("[OnlineMatch] " + title + ": " + body);
        GameState.OnRuleNotice?.Invoke(title, body);
    }
}

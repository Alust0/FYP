using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/* Cards the rules want moved, each shown as an arrow from where it is to where it goes (ZoneHighlighter):
 a rear-guard the attack hit and the guardians after the battle, to the drop zone; a unit a skill retires,
 puts on the bottom of the deck or into the soul; a Soul Blast's soul. Anything that wants a card moved
 calls Expect, and the arrow goes once it has.
 - With a card: done once the camera sees it leave. One that leaves the field without being seen land
   (cards overlap in the drop zone) is counted where it was going, and taken back off if it shows up
   after all.
 - Without one (a count, e.g. the soul under the vanguard): shown for a few seconds.
 Owned by RulesEngine; online the moves for the opponent's cards go to their PC (OnlineMatch). */
public class CardMoves
{
    class Move
    {
        public PlayerState owner;
        public CardInstance card;
        public string from, to;
        public float until; // a move without a card
    }

    readonly List<Move> mMoves = new List<Move>();
    readonly Dictionary<CardInstance, Move> mCounted = new Dictionary<CardInstance, Move>(); // counted in where it went, unseen
    const float CountOnlySeconds = 8f;

    public void Expect(PlayerState owner, string from, string to, CardInstance card)
    {
        if (owner == null || from == null || to == null || (card != null && mMoves.Any(m => m.card == card))) return;
        mMoves.Add(new Move { owner = owner, card = card, from = from, to = to, until = Time.time + CountOnlySeconds });
    }

    public bool Expects(CardInstance card) => card != null && mMoves.Any(m => m.card == card);

    // RulesEngine.HandleCardMoved: a card the camera saw move, its zone null if it left the field
    public void CardMoved(CardInstance card)
    {
        // Counted where it went without being seen there, and now it's seen: it's that card, not the count
        if (card.zone != null && mCounted.TryGetValue(card, out Move counted))
        {
            mCounted.Remove(card);
            string count = CountZone(counted.to);
            if (MatchSession.IsLocal(counted.owner)) GameState.Instance.AdjustCount(counted.owner, count, -1);
            if (card.zone == counted.from && card.zoneOwner == counted.owner) mMoves.Add(counted); // never went: still to move
            return;
        }

        Move move = mMoves.FirstOrDefault(m => m.card == card);
        if (move == null || (card.zone == move.from && card.zoneOwner == move.owner)) return;
        mMoves.Remove(move);

        string zone = CountZone(move.to);
        if (card.zone != null || zone == null) return;
        mCounted[card] = move;
        if (MatchSession.IsLocal(move.owner)) GameState.Instance.AdjustCount(move.owner, zone, 1); // its owner's PC keeps that count
    }

    // The count a card going there and not seen lands in: the soul is under the vanguard circle
    static string CountZone(string to) => to == "Drop" || to == "Deck" ? to : to == "Vanguard" ? "Soul" : null;

    public List<(PlayerState owner, string from, string to, RulesEngine.ArrowKind kind)> Arrows()
    {
        float now = Time.time;
        mMoves.RemoveAll(m => m.card == null ? m.until < now : m.card.zone != m.from || m.card.zoneOwner != m.owner);
        return mMoves.Select(m => (m.owner, m.from, m.to,
            m.card != null && m.to == "Drop" ? RulesEngine.ArrowKind.ToDrop : RulesEngine.ArrowKind.Cost)).ToList();
    }

    // Online: the moves this PC asked of the opponent's cards, "LeftFront>Drop;Guardian>Drop", or null
    public string ForPlayer(PlayerState player)
    {
        var moves = mMoves.Where(m => m.owner == player && m.card != null).Select(m => m.from + ">" + m.to).ToList();
        return moves.Count > 0 ? string.Join(";", moves) : null;
    }

    // Their list arriving: each new one on this PC's own card on that circle
    readonly HashSet<string> mApplied = new HashSet<string>();

    public void ApplyRemote(string moves, PlayerState player)
    {
        var now = new HashSet<string>(moves?.Split(';') ?? new string[0]);
        foreach (string move in now)
        {
            if (mApplied.Contains(move)) continue;
            string[] parts = move.Split('>');
            if (parts.Length != 2) continue;
            // A guardian circle holds several cards: each one there
            IEnumerable<CardInstance> cards = parts[0] == "Guardian" ? player.guardianCircle : new[] { player.CardOnCircle(parts[0]) };
            foreach (CardInstance card in cards.Where(c => c != null)) Expect(player, parts[0], parts[1], card);
        }
        mApplied.Clear();
        mApplied.UnionWith(now);
    }

    /* A new turn: the arrows go. The cards counted unseen are still watched for, unless the counts
     themselves went back (Undo) */
    public void Clear(bool counted)
    {
        mMoves.Clear();
        if (counted) mCounted.Clear();
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Arrows for cards the rules want moved; done when the card leaves
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
        if (owner == null || from == null || to == null || (card != null && mMoves.Any(m => m.card == card))) { return; }
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
            if (MatchSession.IsLocal(counted.owner)) { GameState.Instance.AdjustCount(counted.owner, count, -1); }
            if (card.zone == counted.from && card.zoneOwner == counted.owner) { mMoves.Add(counted); } // never went: still to move
            return;
        }

        Move move = mMoves.FirstOrDefault(m => m.card == card);
        if (move == null || (card.zone == move.from && card.zoneOwner == move.owner)) { return; }
        mMoves.Remove(move);

        string zone = CountZone(move.to);
        if (card.zone != null || zone == null) { return; }
        mCounted[card] = move;
        if (MatchSession.IsLocal(move.owner)) { GameState.Instance.AdjustCount(move.owner, zone, 1); } // its owner's PC keeps that count
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
            if (mApplied.Contains(move)) { continue; }
            string[] parts = move.Split('>');
            if (parts.Length != 2) { continue; }
            // A guardian circle holds several cards: each one there
            IEnumerable<CardInstance> cards = parts[0] == "Guardian" ? player.guardianCircle : new[] { player.CardOnCircle(parts[0]) };
            foreach (CardInstance card in cards.Where(c => c != null)) Expect(player, parts[0], parts[1], card);
        }
        mApplied.Clear();
        mApplied.UnionWith(now);
    }

    // New turn: clear the arrows
    public void Clear(bool counted)
    {
        mMoves.Clear();
        if (counted) { mCounted.Clear(); }
    }

    // Undo: a copy of the arrows now
    public object Save() => mMoves.Where(m => m.card != null || m.until > Time.time).Select(m => new Move { owner = m.owner, card = m.card, from = m.from, to = m.to, until = m.until }).ToList();

    // Undo: a state's arrows plus any added while it lasted
    public static object Merge(object older, object newer)
    {
        var moves = older as List<Move> ?? new List<Move>();
        if (!(newer is List<Move> added)) { return moves; }
        foreach (Move m in added)
        {
            if (!moves.Any(o => o.owner == m.owner && o.card == m.card && o.from == m.from && o.to == m.to)) { moves.Add(m); }
        }
        return moves;
    }

    // Undo: a saved state's arrows back; those whose card already went drop out in Arrows
    public void Load(object saved)
    {
        if (!(saved is List<Move> moves)) { return; }
        foreach (Move m in moves)
        {
            if (m.card != null && mMoves.Any(o => o.card == m.card)) { continue; }
            mMoves.Add(new Move { owner = m.owner, card = m.card, from = m.from, to = m.to, until = Time.time + CountOnlySeconds });
        }
    }
}

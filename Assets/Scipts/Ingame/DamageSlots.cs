using System.Collections.Generic;
using System.Linq;

/* Each player's damage zone as cards, oldest first, kept in step with the Damage and FaceDownDamage
 counts (the counts are the truth, see CLAUDE.md): which card each one is, when the engine saw it as a
 damage check card (RulesEngine), and which are face down. A Counter Blast or Counter Charge on this PC's
 player asks which cards to flip (CounterBlastPicker); anything else (the opponent's, Undo) flips the
 oldest face-up or newest face-down. Shown by StatBars. */
public static class DamageSlots
{
    public class Slot
    {
        public CardData card; // null when the engine didn't see which card it was
        public bool faceDown;
        public int turn;
    }

    static readonly List<Slot>[] sSlots = { new List<Slot>(), new List<Slot>() };
    static readonly CardData[] sNext = new CardData[2]; // the next damage's card, seen before its count goes up
    static readonly List<Slot>[] sPicked = { new List<Slot>(), new List<Slot>() }; // chosen in the picker so far

    // Undo: the counts jumped back, so the cards follow without asking
    public static bool Quiet;

    public static IReadOnlyList<Slot> Of(PlayerState player) => sSlots[player.playerIndex];

    // How many cards this PC's player still has to choose to flip: + face down (Counter Blast), - face up (Counter Charge)
    public static int FlipsDue { get; private set; }
    public static PlayerState FlipOwner { get; private set; }
    public static IReadOnlyList<Slot> Picked => FlipOwner != null ? sPicked[FlipOwner.playerIndex] : new List<Slot>();

    public static void Reset()
    {
        foreach (var slots in sSlots) slots.Clear();
        foreach (var picked in sPicked) picked.Clear();
        sNext[0] = sNext[1] = null;
        FlipsDue = 0;
        FlipOwner = null;
    }

    // A damage check card: the next damage counted is this card
    public static void Name(PlayerState player, CardData card) => sNext[player.playerIndex] = card;

    // After any count change: adds or takes away cards to match, then flips to match
    public static void Reconcile(GameState gs, PlayerState player)
    {
        if (player == null) return;
        List<Slot> slots = sSlots[player.playerIndex];
        int damage = player.CountInZone("Damage");
        int faceDown = damage - player.FaceUpDamage;

        while (slots.Count < damage)
        {
            slots.Add(new Slot { card = sNext[player.playerIndex], turn = gs.turnNumber });
            sNext[player.playerIndex] = null;
        }
        while (slots.Count > damage)
        {
            // A heal takes a face-down card if the face-down count went down with it
            bool down = slots.Count(s => s.faceDown) > faceDown;
            Slot gone = slots.LastOrDefault(s => s.faceDown == down) ?? slots.Last();
            slots.Remove(gone);
            sPicked[player.playerIndex].Remove(gone);
        }

        int need = faceDown - slots.Count(s => s.faceDown);
        if (need == 0)
        {
            if (FlipOwner == player) EndPicking();
            return;
        }
        if (Quiet || !MatchSession.IsLocal(player) || gs.gameOver)
        {
            Flip(slots, need);
            if (FlipOwner == player) EndPicking();
            return;
        }
        // This PC's player picks which (CounterBlastPicker); the bars show the count until then
        if (FlipOwner != player) sPicked[player.playerIndex].Clear();
        FlipOwner = player;
        FlipsDue = need;
    }

    // need > 0: the oldest face-up cards go face down; need < 0: the newest face-down go face up
    static void Flip(List<Slot> slots, int need)
    {
        for (; need > 0; need--)
        {
            Slot slot = slots.FirstOrDefault(s => !s.faceDown);
            if (slot == null) break;
            slot.faceDown = true;
        }
        for (; need < 0; need++)
        {
            Slot slot = slots.LastOrDefault(s => s.faceDown);
            if (slot == null) break;
            slot.faceDown = false;
        }
    }

    // The picker: a card can be picked if it's the right way up to flip
    public static bool CanPick(Slot slot) => FlipOwner != null && slot.faceDown == FlipsDue < 0;

    public static void TogglePick(Slot slot)
    {
        if (FlipOwner == null) return;
        List<Slot> picked = sPicked[FlipOwner.playerIndex];
        if (picked.Contains(slot)) picked.Remove(slot);
        else if (CanPick(slot) && picked.Count < System.Math.Abs(FlipsDue)) picked.Add(slot);
    }

    // Done: the picked cards flip; any not picked are taken in order
    public static void FinishPicking()
    {
        if (FlipOwner == null) return;
        List<Slot> slots = sSlots[FlipOwner.playerIndex];
        foreach (Slot slot in sPicked[FlipOwner.playerIndex]) slot.faceDown = FlipsDue > 0;
        int left = FlipsDue - System.Math.Sign(FlipsDue) * sPicked[FlipOwner.playerIndex].Count;
        Flip(slots, left);
        EndPicking();
    }

    static void EndPicking()
    {
        if (FlipOwner != null) sPicked[FlipOwner.playerIndex].Clear();
        FlipOwner = null;
        FlipsDue = 0;
    }

    // For a segment's hover text
    public static string Describe(Slot slot) =>
        (slot.card != null ? slot.card.DisplayName : "Unknown card") + (slot.faceDown ? " (face down)" : " (face up)") + ", damage from turn " + slot.turn;
}

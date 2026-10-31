/* "Draw 1 card": done once the deck count goes down by that many, seen the same ways as the Draw
 Phase's draw (HandZones, the Draw button, "draw card", Deck -). Done counts any not seen. */
public class DrawStep : Step
{
    public int Amount { get; }

    public DrawStep(int amount)
    {
        Amount = amount;
    }

    public override bool Begin(SkillFrame f)
    {
        f.Left = Amount;
        f.Mark = f.Owner.CountInZone("Deck");
        return false;
    }

    public override bool Recheck(SkillFrame f)
    {
        int deck = f.Owner.CountInZone("Deck");
        if (deck >= f.Mark) return false;

        f.Left -= f.Mark - deck;
        f.Mark = deck;
        if (f.Left > 0) f.Rules.StepNotice(f, "Drew 1 card. " + f.Left + " more to draw.");
        return f.Left <= 0;
    }

    public override void Confirm(SkillFrame f)
    {
        if (f.Left > 0) GameState.Instance.AdjustCount(f.Owner, "Deck", -f.Left);
    }

    public override string Instruction(SkillFrame f) => "draw " + (f.Left > 0 ? f.Left : Amount) + " card(s) from your deck";
}

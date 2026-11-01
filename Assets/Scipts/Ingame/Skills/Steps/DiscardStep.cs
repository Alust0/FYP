/* "手札から1枚選び、捨てる" in an effect, not a cost: waits for the owner's drop zone to go up by that
 many, as the camera sees the cards land there or Drop + counts them. The drop zone is lit meanwhile. */
public class DiscardStep : Step
{
    public int Amount { get; }

    public DiscardStep(int amount)
    {
        Amount = amount;
    }

    public override bool Begin(SkillFrame f)
    {
        f.Mark = f.Owner.CountInZone("Drop");
        return false;
    }

    public override bool Recheck(SkillFrame f) => f.Owner.CountInZone("Drop") >= f.Mark + Amount;

    public override System.Collections.Generic.IEnumerable<string> Zones(SkillFrame f) => new[] { "Drop" };

    public override string Instruction(SkillFrame f) => "discard " + Amount + " card(s) from your hand to the drop zone (Drop + if it isn't seen)";
}

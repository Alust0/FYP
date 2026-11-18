// Discard from hand (always payable)
public class DiscardCost : Cost
{
    public DiscardCost(int amount) : base(amount) { }

    public override string Name => "Discard";

    public override void Pay(PlayerState owner, CardInstance card) { }

    public override System.Collections.Generic.IEnumerable<string> Zones(CardInstance card) => new[] { "Drop" };

    public override string Instruction(CardInstance card) => "discard " + Amount + " card(s) from your hand to the drop zone (Drop + if it isn't seen)";
}

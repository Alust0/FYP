/* Cards from hand to the drop zone. The camera can't see the hand, so it can always be paid; the
 discarded card counts when it's seen in the drop zone, or with Drop + */
public class DiscardCost : Cost
{
    public DiscardCost(int amount) : base(amount) { }

    public override string Name => "Discard";

    public override void Pay(PlayerState owner, CardInstance card) { }

    public override string Instruction(CardInstance card) => "discard " + Amount + " card(s) from your hand to the drop zone (Drop + if it isn't seen)";
}

// Turns face-up damage face down, the same count as the HUD's CB - button
public class CounterBlastCost : Cost
{
    public CounterBlastCost(int amount) : base(amount) { }

    public override string Name => "Counter Blast";
    public override string ShortText => "CB " + Amount;

    public override bool CanPay(PlayerState owner, CardInstance card) => owner.FaceUpDamage >= Amount;

    public override void Pay(PlayerState owner, CardInstance card) => Adjust(owner, "FaceDownDamage", Amount);

    public override System.Collections.Generic.IEnumerable<string> Zones(CardInstance card) => new[] { "Damage" };

    public override string Instruction(CardInstance card) => "turn " + Amount + " damage face down";
}

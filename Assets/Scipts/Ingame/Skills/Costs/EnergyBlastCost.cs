// Energy off the Energy Generator's count; nothing to do with the real cards
public class EnergyBlastCost : Cost
{
    public EnergyBlastCost(int amount) : base(amount) { }

    public override string Name => "Energy Blast";
    public override string ShortText => "EB " + Amount;

    public override bool CanPay(PlayerState owner, CardInstance card) => owner.CountInZone("Energy") >= Amount;

    public override void Pay(PlayerState owner, CardInstance card) => Adjust(owner, "Energy", -Amount);
}

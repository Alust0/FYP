// Soul cards to the drop zone; one the camera then sees land there takes over from the count (SkillRules.SeenInDrop)
public class SoulBlastCost : Cost
{
    public SoulBlastCost(int amount) : base(amount) { }

    public override string Name => "Soul Blast";
    public override string ShortText => "SB " + Amount;

    public override bool CanPay(PlayerState owner, CardInstance card) => owner.CountInZone("Soul") >= Amount;

    public override void Pay(PlayerState owner, CardInstance card)
    {
        Adjust(owner, "Soul", -Amount);
        Adjust(owner, "Drop", Amount);
    }

    public override string Instruction(CardInstance card) => "put " + Amount + " card(s) from your soul into the drop zone";
}

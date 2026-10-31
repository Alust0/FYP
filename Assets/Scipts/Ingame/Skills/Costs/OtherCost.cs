// A cost the engine doesn't know, e.g. [Retire a rear-guard]: shown as printed, and the player pays it by hand
public class OtherCost : Cost
{
    readonly string mName;

    public OtherCost(string name, int amount) : base(amount)
    {
        mName = name;
    }

    public override string Name => mName;

    public override void Pay(PlayerState owner, CardInstance card) { }

    public override string Instruction(CardInstance card) => "pay " + this + " by hand";

    public override string ToString() => Amount > 1 ? Name + " " + Amount : Name;
}

// Resting the skill's own unit; RulesEngine doesn't take that resting as an attack (SkillRules.TakeRestDue)
public class RestCost : Cost
{
    public RestCost() : base(1) { }

    public override string Name => "Rest this unit";
    public override string ShortText => "Rest";

    public override bool CanPay(PlayerState owner, CardInstance card) => card.orientation == CardOrientation.Stand;

    public override void Pay(PlayerState owner, CardInstance card) { }

    public override string Instruction(CardInstance card) => "rest " + card.data.DisplayName;

    public override string ToString() => Name;
}

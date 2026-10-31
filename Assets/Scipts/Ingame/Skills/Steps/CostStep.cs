using System.Linq;

/* Pays one of the skill's costs off the counts. An "A or B" cost (ChoiceCost) waits for the player
 to pick, unless only one of them can be paid. */
public class CostStep : Step
{
    public Cost Cost { get; }

    public CostStep(Cost cost)
    {
        Cost = cost;
    }

    // A cost the engine doesn't know is paid by hand
    public override bool Automated =>
        !(Cost is OtherCost) && !(Cost is ChoiceCost choice && choice.Options.Any(o => o is OtherCost));

    public override bool Begin(SkillFrame f)
    {
        if (!(Cost is ChoiceCost choice)) return Pay(f, Cost);

        int payable = -1, count = 0;
        for (int i = 0; i < choice.Options.Count; i++)
            if (choice.Options[i].CanPay(f.Owner, f.Card)) { payable = i; count++; }
        if (count == 1) f.Choice = payable;
        return Recheck(f);
    }

    // A choice made with its button or "pay first"/"pay second"
    public override bool Recheck(SkillFrame f) =>
        Cost is ChoiceCost choice && f.Choice >= 0 && f.Choice < choice.Options.Count && Pay(f, choice.Options[f.Choice]);

    // Done without picking: the first option that can be paid
    public override void Confirm(SkillFrame f)
    {
        if (Cost is ChoiceCost choice) Pay(f, choice.Options.FirstOrDefault(o => o.CanPay(f.Owner, f.Card)) ?? choice.Options[0]);
    }

    public override string Instruction(SkillFrame f) => "choose which cost to pay: " + Cost;

    static bool Pay(SkillFrame f, Cost cost)
    {
        cost.Pay(f.Owner, f.Card);
        f.Rules.NotePaid(f, cost);
        return true;
    }
}

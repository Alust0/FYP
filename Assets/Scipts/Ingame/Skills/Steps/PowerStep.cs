/* "このユニットのパワー+5000", "このユニットのクリティカル+1": the skill's own unit gets it straight
 away. "そのバトル中" before it: it goes when the battle ends; otherwise at the end of the turn
 (RulesEngine.AddBonus). */
public class PowerStep : Step
{
    public bool Critical { get; }
    public int Amount { get; }
    public bool BattleOnly { get; }

    public PowerStep(bool critical, int amount, bool battleOnly)
    {
        Critical = critical;
        Amount = amount;
        BattleOnly = battleOnly;
    }

    public override bool Begin(SkillFrame f)
    {
        CardInstance unit = f.Card;
        if (!Skill.IsUnitCircle(unit.zone))
        {
            f.Rules.StepNotice(f, unit.data.DisplayName + " isn't on a circle, so it gets no " + Bonus + ".");
            return true;
        }
        f.Rules.AddBonus?.Invoke(unit, Critical ? 0 : Amount, Critical ? Amount : 0, BattleOnly);
        f.Rules.StepNotice(f, unit.data.DisplayName + " gets " + Bonus + (BattleOnly ? " for this battle." : " until end of turn."));
        return true;
    }

    string Bonus => (Critical ? "Critical +" : "Power +") + Amount;

    public override string Summary => Bonus + " to this unit";
}

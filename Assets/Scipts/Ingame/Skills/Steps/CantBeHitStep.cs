/* "あなたのユニットを1枚選び、そのバトル中、ヒットされない" (a Sentinel's perfect guard): the attacked
 unit can't be hit this battle (RulesEngine's perfect guard). The unit chosen is taken to be the one
 attacked, the only one it helps. */
public class CantBeHitStep : Step
{
    public override bool Begin(SkillFrame f)
    {
        f.Rules.PerfectGuard?.Invoke(f.Owner);
        return true;
    }

    public override string Summary => "the attacked unit can't be hit";
}

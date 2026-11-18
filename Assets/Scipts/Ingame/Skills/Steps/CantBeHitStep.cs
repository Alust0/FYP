// Sentinel perfect guard: attacked unit can't be hit this battle
public class CantBeHitStep : Step
{
    public override bool Begin(SkillFrame f)
    {
        f.Rules.PerfectGuard?.Invoke(f.Owner);
        return true;
    }

    public override string Summary => "the attacked unit can't be hit";
}

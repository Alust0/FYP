// Shuffle: the player is told to
public class ShuffleStep : Step
{
    public override bool Begin(SkillFrame f)
    {
        f.Rules.StepNotice(f, "Shuffle your deck.");
        return true;
    }

    public override string Summary => "shuffle";
}

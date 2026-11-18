using System.Linq;

// A part the engine can't do: the player presses Done
public class ManualStep : Step
{
    public override bool Automated => false;

    public override bool Begin(SkillFrame f) => false;

    public override string Instruction(SkillFrame f)
    {
        var done = f.Skill.Steps.Select(s => s.Summary).Where(s => s != null).ToList();
        return "do the rest yourself, then Done" + (done.Count > 0 ? " (the engine does " + string.Join(", ", done) + ")" : "") + ": " + f.Skill.Effect;
    }
}

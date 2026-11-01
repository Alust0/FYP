using System.Linq;

/* The parts of an effect the engine can't read or check, e.g. "retire one of your opponent's
 rear-guards": the player does them and says Done. Japanese and English sentences don't line up
 one to one, so it shows the skill's whole English effect, saying which parts the engine does itself. */
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

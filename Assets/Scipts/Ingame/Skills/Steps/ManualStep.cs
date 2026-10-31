/* The parts of an effect the engine can't read or check, e.g. "retire one of your opponent's
 rear-guards": the player does them and says Done. Japanese and English sentences don't line up
 one to one, so it shows the skill's whole English effect. */
public class ManualStep : Step
{
    public override bool Automated => false;

    public override bool Begin(SkillFrame f) => false;

    public override string Instruction(SkillFrame f) => "do the rest yourself, then Done: " + f.Skill.Effect;
}

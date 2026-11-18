using System.Linq;

// The opponent's part of the skill (can't be skipped)
public class OpponentStep : Step
{
    public override bool Automated => false;

    public override bool Begin(SkillFrame f) => false;

    public override string Instruction(SkillFrame f) =>
        Opponent(f).playerName + " does their part, then presses Done on their side: " + f.Skill.Effect;

    public static PlayerState Opponent(SkillFrame f) => GameState.Instance.players.First(p => p != f.Owner);
}

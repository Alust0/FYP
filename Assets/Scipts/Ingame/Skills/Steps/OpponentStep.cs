using System.Linq;

/* "相手は手札から1枚選び、あなたの監獄に収容する": the opponent's part of the skill. They do it with their
 real cards and press Done on their side; online their Done comes from their PC (OnlineMatch sends
 them the step, VoiceCommands.HeardRemote brings it back). It can't be skipped: it isn't the skill
 owner's choice. */
public class OpponentStep : Step
{
    public override bool Automated => false;

    public override bool Begin(SkillFrame f) => false;

    public override string Instruction(SkillFrame f) =>
        Opponent(f).playerName + " does their part, then presses Done on their side: " + f.Skill.Effect;

    public static PlayerState Opponent(SkillFrame f) => GameState.Instance.players.First(p => p != f.Owner);
}

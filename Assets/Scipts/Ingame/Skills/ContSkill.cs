using System.Collections.Generic;

// [CONT]: always on while its condition holds, so never activated; it's only shown on the card's panel
public class ContSkill : Skill
{
    public override string Marker => "[CONT]";

    public ContSkill(string text, string effect, string circle, int perTurn, List<Cost> costs)
        : base(text, effect, circle, perTurn, costs) { }

    public override bool UsableNow(GameState gs, CardInstance card, SkillTiming happened) => false;

    // Nothing enforces it yet: the player keeps it in mind
    public override bool Supported => false;
}

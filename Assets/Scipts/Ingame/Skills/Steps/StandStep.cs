using System.Collections.Generic;

/* "このユニットを【スタンド】させる": done once the camera sees the skill's unit standing (RulesEngine
 rechecks the steps when a card turns), or straight away if it already is. Done counts it as stood. */
public class StandStep : Step
{
    public override bool Begin(SkillFrame f) => Recheck(f);

    public override bool Recheck(SkillFrame f) => f.Card.orientation == CardOrientation.Stand || !Skill.IsUnitCircle(f.Card.zone);

    public override IEnumerable<string> Zones(SkillFrame f) => f.Card.zone != null ? new[] { f.Card.zone } : new string[0];

    public override string Instruction(SkillFrame f) => "stand " + f.Card.data.DisplayName + " (Done if the camera misses it)";

    public override string Summary => "watch for it standing";
}

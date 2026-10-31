using System.Collections.Generic;

// [ACT]: the turn player uses it in their Main Phase, from a unit on its circle
public class ActSkill : Skill
{
    public override string Marker => "[ACT]";

    public ActSkill(string text, string effect, string circle, int perTurn, List<Cost> costs)
        : base(text, effect, circle, perTurn, costs) { }

    public override bool UsableNow(GameState gs, CardInstance card, SkillTiming happened) =>
        happened == SkillTiming.None && gs.currentPhase == TurnPhase.MainPhase &&
        card.zoneOwner == gs.CurrentPlayer && IsUnitCircle(card.zone) && OnItsCircle(card) && OrderHolds(card);
}

using System.Collections.Generic;

/* Card text that runs like a skill's steps but isn't one of its [AUTO]/[ACT] skills, so it's never
 offered as one: an order's cost to play it (SkillParser.PlayCost, SkillRules.PayToPlay), and an
 Over trigger's additional effect (SkillParser.OverTriggerEffect, SkillRules.OverTriggerEffect). */
public class ExtraSkill : Skill
{
    readonly string mMarker;

    public ExtraSkill(string marker, string text, List<Step> steps) : base(text, text, null, 0, new List<Cost>())
    {
        mMarker = marker;
        Steps = steps;
    }

    public override string Marker => mMarker;

    public override bool UsableNow(GameState gs, CardInstance card, SkillTiming happened) => false;
}

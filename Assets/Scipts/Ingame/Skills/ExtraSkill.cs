using System.Collections.Generic;

// Order play costs and Over trigger effects, run like skill steps
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

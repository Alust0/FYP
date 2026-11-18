using System.Collections.Generic;

// [AUTO]: goes off by itself when its timing happens, and its owner is asked to activate or skip it
public class AutoSkill : Skill
{
    public SkillTiming Timing { get; }
    // Part of the other card's name its timing names, or null for any
    public string Named { get; internal set; }

    // The other card has the name the skill asks for (any card, if it asks for none)
    public bool OtherFits(CardInstance other)
    {
        if (Named == null) return true;
        if (other == null) return false;
        return CardFilter.Has(other.data.cardNameEn, Named) || CardFilter.Has(other.data.cardName, Named);
    }

    public override string Marker => "[AUTO]";

    // Also needs a timing the engine sees, or it's never asked about
    public override bool Supported => base.Supported && Timing != SkillTiming.Other && Timing != SkillTiming.None;

    public AutoSkill(SkillTiming timing, string text, string effect, string circle, int perTurn, List<Cost> costs)
        : base(text, effect, circle, perTurn, costs)
    {
        Timing = timing;
    }

    // Its own timing, with the card on its circle; a ridden-upon card is in the soul by then
    public override bool UsableNow(GameState gs, CardInstance card, SkillTiming happened) =>
        happened == Timing && Timing != SkillTiming.None && Timing != SkillTiming.Other &&
        (Timing == SkillTiming.RiddenUpon || OnItsCircle(card)) && OrderHolds(card);
}

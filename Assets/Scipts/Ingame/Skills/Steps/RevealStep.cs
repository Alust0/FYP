// Reveal = put the card on your trigger zone
public class RevealStep : Step
{
    public CardFilter Filter { get; }

    public RevealStep(CardFilter filter)
    {
        Filter = filter;
    }

    public override bool Begin(SkillFrame f) => false;

    // A card put on the owner's trigger zone while this waits: true if it's the one asked for
    public bool Offer(SkillFrame f, CardInstance card)
    {
        if (!Filter.Matches(card.data))
        {
            f.Rules.StepNotice(f, card.data.DisplayName + " isn't " + Filter + ". Show " + Filter + ", or Skip.");
            return false;
        }
        f.Locals["revealed"] = card;
        bool toHand = f.Cursor + 1 < f.Skill.Steps.Count && f.Skill.Steps[f.Cursor + 1] is ToHandStep; // that step says where it goes
        f.Rules.StepNotice(f, "Revealed " + card.data.DisplayName + "." + (toHand ? "" : " Take it back."));
        return true;
    }

    public override System.Collections.Generic.IEnumerable<string> Zones(SkillFrame f) => new[] { "Trigger" };

    public override string Summary => "check the card shown";

    public override string Instruction(SkillFrame f) =>
        "show " + Filter + " by putting it on your trigger zone (or any zone) for the camera (Done if it can't be read)";
}

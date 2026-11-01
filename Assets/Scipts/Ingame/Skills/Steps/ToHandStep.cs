/* "Reveal it, add it to your hand": the revealed card leaving the board (picked up off the zone it was
 shown on) is it going into the hand. From a deck search, the deck count goes down by 1 then.
 Done counts it as taken. */
public class ToHandStep : Step
{
    public bool FromDeck { get; }

    public ToHandStep(bool fromDeck)
    {
        FromDeck = fromDeck;
    }

    public override bool Begin(SkillFrame f) => Recheck(f);

    public override bool Recheck(SkillFrame f)
    {
        if (!(f.Locals.TryGetValue("revealed", out object card) && card is CardInstance revealed) || revealed.zone != null) return false;
        Confirm(f);
        return true;
    }

    public override void Confirm(SkillFrame f)
    {
        if (FromDeck) GameState.Instance.AdjustCount(f.Owner, "Deck", -1);
    }

    public override string Summary => FromDeck ? "count it off the deck" : null;

    // Where the revealed card lies, to be picked up
    public override System.Collections.Generic.IEnumerable<string> Zones(SkillFrame f) =>
        f.Locals.TryGetValue("revealed", out object card) && card is CardInstance revealed && revealed.zone != null ? new[] { revealed.zone } : new string[0];

    public override string Instruction(SkillFrame f) =>
        "put " + (f.Locals.TryGetValue("revealed", out object card) && card is CardInstance revealed ? revealed.data.DisplayName : "the revealed card") +
        " into your hand (Done if the camera misses it)";
}

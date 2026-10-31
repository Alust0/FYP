/* "Shuffle your deck", or the end of an effect that searched the deck without saying so: the
 camera can't see a shuffle, so the player is told to do it and the skill carries on */
public class ShuffleStep : Step
{
    public override bool Begin(SkillFrame f)
    {
        f.Rules.StepNotice(f, "Shuffle your deck.");
        return true;
    }
}

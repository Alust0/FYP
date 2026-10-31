/* One thing a skill does, in the order its text says (SkillParser). The engine runs a skill's
 steps one at a time (SkillRules.Pump), stopping at one that waits for the camera or the player:
 - Begin is called once when it's reached, Recheck after every count change while it waits
 - Either returns true once it's done
 - Done (button or "skill done") ends it as done; "skip step" ends it as not done */
public abstract class Step
{
    // The engine or camera does it; false makes the card only partly supported (SkillParser.Supported)
    public virtual bool Automated => true;

    public abstract bool Begin(SkillFrame f);

    public virtual bool Recheck(SkillFrame f) => false;

    // The player says it's done, e.g. a draw the camera didn't see
    public virtual void Confirm(SkillFrame f) { }

    // What the player does while it waits, e.g. "draw 1 card from your deck"
    public virtual string Instruction(SkillFrame f) => null;
}

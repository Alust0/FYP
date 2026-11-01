using System.Collections.Generic;
using System.Linq;

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

    // Board zones the player moves a card on while it waits, lit up (ZoneHighlighter)
    public virtual IEnumerable<string> Zones(SkillFrame f) => Enumerable.Empty<string>();

    // What the engine does for the player, e.g. "draw 1", listed by a ManualStep so it isn't done twice; null for none
    public virtual string Summary => null;

    // What the player does while it waits, e.g. "draw 1 card from your deck"
    public virtual string Instruction(SkillFrame f) => null;
}

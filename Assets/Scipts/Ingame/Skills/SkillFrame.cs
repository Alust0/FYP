using System.Collections.Generic;

/* One skill being resolved, step by step (SkillRules.Pump): whose it is, which step it's on, and
 what earlier steps left for later ones, e.g. the card a Reveal step was shown */
public class SkillFrame
{
    public SkillRules Rules { get; }
    public PlayerState Owner { get; }
    public CardInstance Card { get; }
    public Skill Skill { get; }
    public int Cursor { get; private set; }
    public bool Begun; // the current step has started and is waiting
    public int Left; // the current step's count still to come, e.g. cards to draw
    public int Mark; // the current step's starting point, e.g. the deck count it began at
    public int Choice = -1; // a ChoiceCost's option the player picked
    public readonly Dictionary<string, object> Locals = new Dictionary<string, object>();

    public SkillFrame(SkillRules rules, PlayerState owner, CardInstance card, Skill skill)
    {
        Rules = rules;
        Owner = owner;
        Card = card;
        Skill = skill;
    }

    // Null once the last step is done
    public Step Current => Cursor < Skill.Steps.Count ? Skill.Steps[Cursor] : null;

    public void Next()
    {
        Cursor++;
        Begun = false;
        Left = Mark = 0;
        Choice = -1;
    }
}

using System.Collections.Generic;
using System.Linq;

/* One part of a skill's cost and how much of it, e.g. Counter Blast (1). Each kind checks and
 pays itself its own way, off the counts the engine keeps (GameState.AdjustCount). */
public abstract class Cost
{
    public int Amount { get; }

    protected Cost(int amount)
    {
        Amount = amount;
    }

    public abstract string Name { get; }

    // For a button, e.g. "CB 1"
    public virtual string ShortText => ToString();

    public virtual bool CanPay(PlayerState owner, CardInstance card) => true;

    // Takes it off the counts; a cost the camera has to see (a discard) only says what to do
    public abstract void Pay(PlayerState owner, CardInstance card);

    // Board zones the player moves cards on to pay it, e.g. "Damage" for a Counter Blast, lit up (ZoneHighlighter)
    public virtual IEnumerable<string> Zones(CardInstance card) => Enumerable.Empty<string>();

    // Where its cards go, for an arrow between two of those zones (ZoneHighlighter), or null
    public virtual (string from, string to)? Arrow => null;

    // What the player does with the real cards once it's paid, or null
    public virtual string Instruction(CardInstance card) => null;

    public override string ToString() => Name + " " + Amount;

    protected static void Adjust(PlayerState owner, string zone, int delta) => GameState.Instance.AdjustCount(owner, zone, delta);
}

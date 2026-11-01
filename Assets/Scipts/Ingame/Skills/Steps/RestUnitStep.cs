using System.Collections.Generic;
using System.Linq;

/* "Rest one of your units", e.g. to play a set order: done once the camera sees that many of the
 player's units rest (SkillRules.TakeRestDue), which then isn't an attack. Done counts them as rested. */
public class RestUnitStep : Step
{
    public int Amount { get; }

    public RestUnitStep(int amount)
    {
        Amount = System.Math.Max(1, amount);
    }

    public override bool Begin(SkillFrame f)
    {
        f.Left = Amount;
        return false;
    }

    // One of the owner's units rested: true once that's all of them
    public bool Take(SkillFrame f) => --f.Left <= 0;

    // The owner's standing units, any of which can be rested
    public override IEnumerable<string> Zones(SkillFrame f) =>
        new[] { f.Owner.vanguardCircle }.Concat(f.Owner.AllRearguards())
            .Where(u => u != null && u.orientation == CardOrientation.Stand && u.zone != null).Select(u => u.zone);

    public override string Instruction(SkillFrame f) => "rest " + (f.Left > 0 ? f.Left : Amount) + " of your units (Done if the camera misses it)";

    public override string ToString() => "rest " + Amount + " of your units";
}

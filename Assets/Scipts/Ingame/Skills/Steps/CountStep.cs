/* Soul Charge, Counter Charge and Energy Charge: they only change counts the camera can't see, so
 the engine does them and says what to do with the real cards */
public class CountStep : Step
{
    public enum Kind { SoulCharge, CounterCharge, EnergyCharge }

    public Kind What { get; }
    public int Amount { get; }

    public CountStep(Kind what, int amount)
    {
        What = what;
        Amount = amount;
    }

    public override bool Begin(SkillFrame f)
    {
        GameState gs = GameState.Instance;
        if (What == Kind.SoulCharge)
        {
            gs.AdjustCount(f.Owner, "Deck", -Amount);
            gs.AdjustCount(f.Owner, "Soul", Amount);
            f.Rules.StepNotice(f, "Soul Charge " + Amount + " counted: put the top " + Amount + " card(s) of your deck into your soul.");
        }
        else if (What == Kind.EnergyCharge)
        {
            // Never past the limit (7.48.2)
            int added = System.Math.Max(0, System.Math.Min(Amount, RulesEngine.MaxEnergy - f.Owner.CountInZone("Energy")));
            gs.AdjustCount(f.Owner, "Energy", added);
            f.Rules.StepNotice(f, "Energy Charge " + Amount + ": +" + added + " energy.");
        }
        else
        {
            gs.AdjustCount(f.Owner, "FaceDownDamage", -Amount);
            f.Rules.StepNotice(f, "Counter Charge " + Amount + " counted: turn " + Amount + " damage face up.");
        }
        return true;
    }
}

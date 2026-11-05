using System.Collections.Generic;
using System.Linq;

// When an [AUTO] skill goes off, as far as its text says and the camera can see
public enum SkillTiming
{
    None, // no timing: a player choosing to use an [ACT] skill
    Other, // one the engine can't see, e.g. "when a card is put into your soul": no prompt for it
    PlacedOnVC, PlacedOnRC, PlacedOnGC, PlacedInOrderZone, RiddenUpon,
    Attacks, Boosts, AttackHits, EndOfBattle, BoostEndOfBattle,
    VanguardAttacked, // its owner's vanguard: for the units, soul and drop zone
    StartOfRidePhase, StartOfMainPhase, StartOfBattlePhase, EndOfTurn,
}

// "if you are the first/second player": the one condition in a skill's text the engine checks
public enum PlayerOrder { Any, First, Second }

/* One skill in a card's text (SkillParser): the circle it works from, how often it can be used, what
 it costs and what it does, as steps (Steps/). The engine checks when it can be used, then runs its
 steps; the ones it can't do or see are left to the player (ManualStep). */
public abstract class Skill
{
    // Shown to players,English even when parsed from the Japanese cards
    public string Text { get; internal set; } // everything after the marker, as the card database has it
    public string Effect { get; internal set; } // the text without its costs: what the player does once they're paid
    public string Circle { get; } // "VC", "RC", "GC", or a zone ("Soul", "Drop", "Hand"...) from its header; null for any
    public int PerTurn { get; } // e.g. 1 for [1/Turn]; 0 for no limit
    public bool PerGame { get; internal set; } // once a game, e.g. a Divine Skill or 【ファイト1回】
    public string GameGroup { get; internal set; } // "Divine" or "Unique": using one uses up all of them that game (12.3.3, 12.3.4)
    public SkillGate Gate { get; internal set; } // a keyword's condition, e.g. Limit Break (4); null for none
    public IReadOnlyList<Cost> Costs { get; }
    public PlayerOrder Order { get; internal set; }
    public bool Optional { get; internal set; } // its text says the player may ("してよい")

    // An [AUTO] with no cost that doesn't say "may" has to be used: it can't be skipped
    public bool Mandatory => this is AutoSkill && Costs.Count == 0 && !Optional;
    public List<Step> Steps { get; internal set; } = new List<Step>(); // its costs, then its effect
    internal string CostLabel; // the English costs, when the costs were read from the Japanese

    public abstract string Marker { get; }

    protected Skill(string text, string effect, string circle, int perTurn, List<Cost> costs)
    {
        Text = text;
        Effect = effect;
        Circle = circle;
        PerTurn = perTurn;
        Costs = costs;
    }

    // Whether it can be used on this card now that `happened` has (None: a player asking to use it)
    public abstract bool UsableNow(GameState gs, CardInstance card, SkillTiming happened);

    public bool CanPay(PlayerState owner, CardInstance card) => Costs.All(c => c.CanPay(owner, card));

    // The engine runs it all without asking the player to do part of it by hand
    public virtual bool Supported => Steps.All(s => s.Automated);

    // e.g. "Soul Blast 1, Energy Blast 3"
    public string CostText => Costs.Count == 0 ? "no cost" : CostLabel ?? string.Join(", ", Costs);

    // e.g. "SB 1, EB 3", for a button
    public string ShortCostText => string.Join(", ", Costs.Select(c => c.ShortText));

    // Player 1 always goes first (RulesEngine.Start), and in a solo test is the only player
    protected bool OrderHolds(CardInstance card) =>
        Order == PlayerOrder.Any || (card.zoneOwner != null && (card.zoneOwner.playerIndex == 0) == (Order == PlayerOrder.First));

    protected bool OnItsCircle(CardInstance card)
    {
        switch (Circle)
        {
            case "VC": return card.zone == "Vanguard";
            case "RC": return IsRearguardCircle(card.zone);
            case "GC": return card.zone == "Guardian";
            default: return Circle == null || Circle.Split('/').Contains(card.zone); // "Soul", "Soul/Drop"; never "Hand" or "RideDeck", which the camera doesn't see
        }
    }

    public static bool IsUnitCircle(string zone) => zone == "Vanguard" || IsRearguardCircle(zone);

    static bool IsRearguardCircle(string zone) =>
        zone == "LeftFront" || zone == "RightFront" || zone == "LeftBack" || zone == "RightBack" || zone == "CenterBack";
}

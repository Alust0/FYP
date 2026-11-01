using System.Collections.Generic;
using System.Linq;

/* "相手のリアガードを1枚選び、退却させる": the skill's owner picks the opponent's unit on screen, lit up
 like an attack target (OpponentUnitPicker), and the opponent is told what happens to it, e.g. retire it
 (with an arrow to their drop zone). The real card is theirs to move; a power change is applied here. */
public class ChooseOpponentStep : Step
{
    public enum Where { Rearguards, FrontRow, BackRow, Units }
    public enum Act { Retire, Power, Other }

    public Where Circles { get; }
    public int Count { get; }
    public bool UpTo { get; }
    public CardFilter Filter { get; }
    public Act What { get; }
    public string ActText { get; } // English, e.g. "retire it"
    public string TheirPart { get; } // what the opponent does, e.g. "put {card} into your drop zone"
    public string To { get; } // the opponent's zone it goes to, for an arrow (CardMoves); null for none
    public int Power { get; }
    public bool BattleOnly { get; }

    public ChooseOpponentStep(Where circles, int count, bool upTo, CardFilter filter, Act what, string actText, string theirPart, int power = 0, bool battleOnly = false, string to = null)
    {
        Circles = circles;
        Count = count;
        UpTo = upTo;
        Filter = filter;
        What = what;
        ActText = actText;
        TheirPart = theirPart;
        To = to;
        Power = power;
        BattleOnly = battleOnly;
    }

    static readonly string[] Rear = { "LeftFront", "RightFront", "LeftBack", "CenterBack", "RightBack" };

    IEnumerable<string> CircleNames()
    {
        switch (Circles)
        {
            case Where.FrontRow: return new[] { "LeftFront", "RightFront" };
            case Where.BackRow: return new[] { "LeftBack", "CenterBack", "RightBack" };
            case Where.Units: return new[] { "Vanguard" }.Concat(Rear);
            default: return Rear;
        }
    }

    // The opponent's circles that can be picked now: a unit the filter allows, not already picked
    public List<string> Choices(SkillFrame f)
    {
        PlayerState opponent = OpponentStep.Opponent(f);
        var chosen = Chosen(f);
        return CircleNames().Where(zone =>
        {
            CardInstance unit = opponent.CardOnCircle(zone);
            return unit != null && Filter.Matches(unit.data) && !chosen.Contains(unit);
        }).ToList();
    }

    static List<CardInstance> Chosen(SkillFrame f)
    {
        if (!f.Locals.TryGetValue("chosen", out object list)) f.Locals["chosen"] = list = new List<CardInstance>();
        return (List<CardInstance>)list;
    }

    public override bool Begin(SkillFrame f)
    {
        Chosen(f).Clear();
        f.Left = Count;
        if (Choices(f).Count > 0) return false;
        f.Rules.StepNotice(f, OpponentStep.Opponent(f).playerName + " has no " + WhatUnit + " to choose, so this part does nothing.");
        return true;
    }

    // A circle picked on screen (or with keys): true once all are picked
    public bool Choose(SkillFrame f, string zone)
    {
        if (!Choices(f).Contains(zone)) return false;
        PlayerState opponent = OpponentStep.Opponent(f);
        CardInstance unit = opponent.CardOnCircle(zone);
        Chosen(f).Add(unit);
        string name = unit.data.DisplayName + " (" + Circle(zone) + ")";

        if (What == Act.Power)
        {
            f.Rules.AddBonus?.Invoke(unit, Power, 0, BattleOnly);
            f.Rules.Announce(f, name + " gets Power " + (Power > 0 ? "+" : "") + Power + (BattleOnly ? " for this battle." : " until end of turn."));
        }
        else
        {
            f.Rules.Announce(f, "chose " + name + ". " + opponent.playerName + ": " + TheirPart.Replace("{card}", unit.data.DisplayName) + ".");
            if (To != null) f.Rules.Moves?.Expect(opponent, zone, To, unit); // an arrow until it's gone there
        }
        f.Left--;
        return f.Left <= 0 || Choices(f).Count == 0;
    }

    string WhatUnit => (Filter.Grade >= 0 ? "grade " + Filter.Grade + (Filter.OrGreater ? " or greater " : " ") : "") +
        (Circles == Where.FrontRow ? "front-row rear-guard" : Circles == Where.BackRow ? "back-row rear-guard" : Circles == Where.Units ? "unit" : "rear-guard");

    static string Circle(string zone) =>
        zone == "Vanguard" ? "vanguard" : zone.Replace("Left", "left ").Replace("Right", "right ").Replace("Center", "center ").Replace("Front", "front").Replace("Back", "back");

    public override string Summary => "choosing the opponent's " + WhatUnit + (What == Act.Power ? " and its power" : What == Act.Retire ? " to retire" : "");

    public override string Instruction(SkillFrame f) =>
        "choose " + (UpTo ? "up to " : "") + f.Left + " of " + OpponentStep.Opponent(f).playerName + "'s " + WhatUnit + "s (click its lit circle)" +
        (What == Act.Power ? " for Power " + (Power > 0 ? "+" : "") + Power : " to " + ActText);
}

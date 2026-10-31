using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/* Reads a card's skills. The Japanese text (JpSkillParser) when the card has it, as its wording is
 fixed; each skill then shows its English text, paired by position. The English, read here, when
 it hasn't, or as each skill's text for players:
 - The text is as the card database stores it, e.g.
   "[ACT](RC)[1/Turn]: If ..., [COST][Soul Blast (1)], [Energy Blast (3)], search your deck ..."
 - Each skill starts at its [AUTO], [ACT] or [CONT] marker
 - Its header (circle, 1/Turn) runs to the colon
 - Its costs run from [COST] to the first "]" the effect follows, and are found in there by name, so
   uneven brackets like "[COST] [Counter Blast (1), [Soul Blast] (1)]" still read right
 - Timings are matched by wording; one it doesn't know is logged, so it can be added
 - The effect becomes steps clause by clause, where the engine knows the wording (Steps/)
 - Parsed once per card */
public static class SkillParser
{
    // Some cards spell the markers out, e.g. "[Continuous]"; a keyword before one, e.g. "[Divine Skill]-", belongs to it
    static readonly Regex MarkerRx = new Regex(@"(?:\[([^\]]+)\]\s*[-–—]\s*)?\[(AUTO|Automatic|ACT|Activated?|CONT|Continuous)\]", RegexOptions.IgnoreCase);
    static readonly Regex CircleRx = new Regex(@"[\(\[](VC|RC|GC|V|R|G)[\)\]]", RegexOptions.IgnoreCase); // "(RC)", or "[R]" on some cards
    static readonly Regex RiddenByRx = new Regex(@"ridden (?:upon )?by [""“”]([^""“”]+)[""“”]", RegexOptions.IgnoreCase); // ridden by "Knight of ..."
    static readonly Regex PerTurnRx = new Regex(@"(\d+)\s*/\s*Turn", RegexOptions.IgnoreCase);
    // The "]" a cost ends at: the effect follows it, after ", " (not ", [" for another cost), " and " or "."
    static readonly Regex CostEndRx = new Regex(@"\](?=\s*,(?!\s*\[)|\s+and\s|\s*\.|\s*$)");
    static readonly Regex BlastRx = new Regex(@"(counter blast|soul blast|energy blast)\W*(\d+)", RegexOptions.IgnoreCase); // "Counter Blast (1)", "Soul Blast] (1)"
    static readonly Regex DiscardRx = new Regex(@"discard (a|an|one|two|three|\d+) cards?( from your hand)?", RegexOptions.IgnoreCase);
    static readonly Regex RestRx = new Regex(@"rest this unit", RegexOptions.IgnoreCase);
    static readonly Regex DrawRx = new Regex(@"\bdraw (a|an|one|two|three|\d+) cards?\b", RegexOptions.IgnoreCase);
    static readonly Regex SoulChargeRx = new Regex(@"soul charge\W*(\d+)", RegexOptions.IgnoreCase); // "[Soul Charge] (3)"
    static readonly Regex CounterChargeRx = new Regex(@"counter charge\W*(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex EnergyChargeRx = new Regex(@"energy charge\W*(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex RevealRx = new Regex(@"\breveal\b(?!ed)", RegexOptions.IgnoreCase);
    static readonly Regex SearchRx = new Regex(@"\bsearch(?:es)? (?:your|their) deck\b", RegexOptions.IgnoreCase);
    static readonly Regex ShuffleRx = new Regex(@"^shuffle (?:your|their|the) deck\b", RegexOptions.IgnoreCase); // not "shuffle X into your deck", which moves a card
    static readonly Regex GradeRx = new Regex(@"grade (\d+)( or greater)?", RegexOptions.IgnoreCase);
    static readonly Regex NameRx = new Regex(@"[""“]([^""”]+)[""”]");
    // "up to 1 Prison card": a capitalised word before "card", other than a card type
    static readonly Regex KeywordRx = new Regex(@"\b(?:a|an|one|two|three|\d+) ((?!(?:Trigger|Order|Unit|Normal|Set|Blitz)\b)[A-Z][\w'-]*(?: [A-Z][\w'-]*)*) cards?\b");
    static readonly Regex ClauseRx = new Regex(@"[,.;]|\band\b|\bthen\b", RegexOptions.IgnoreCase);
    static readonly Regex OrderZoneRx = new Regex(@"this card (?:is placed|is put|is set) (?:in|into|on) (?:the |your )?order zone", RegexOptions.IgnoreCase);
    // "When this unit is placed on (VC)", "appears on (RC)", "is put on (GC)", "is called to (RC)"
    static readonly Regex PlacedRx = new Regex(@"this unit (?:is placed|appears|is put|is called|is played) (?:on|to|onto) \((vc|rc|gc)\)", RegexOptions.IgnoreCase);
    const string CostMarker = "[COST]";
    const int MaxHeaderLength = 30; // a colon further in is the effect's own

    static readonly Dictionary<CardData, List<Skill>> Cache = new Dictionary<CardData, List<Skill>>();

    public static List<Skill> Parse(CardData data)
    {
        if (data == null) return new List<Skill>();
        if (!Cache.TryGetValue(data, out List<Skill> skills))
            Cache[data] = skills = ParseCard(data.abilityTextJa, data.abilityText);
        return skills;
    }

    // Every skill on the card runs without the player doing part of it by hand; for its info panel
    public static bool Supported(CardData data) => Parse(data).All(s => s.Supported);

    /* The Japanese skills, each showing the English one in the same place when the two have the same
     skills in the same order, or else the whole English text; the English alone without Japanese */
    public static List<Skill> ParseCard(string ja, string en)
    {
        List<Skill> english = Parse(en);
        List<Skill> skills = JpSkillParser.Parse(ja);
        if (skills.Count == 0) skills = english;
        else
        {
            bool paired = english.Count == skills.Count && english.Select(s => s.Marker).SequenceEqual(skills.Select(s => s.Marker));
            for (int i = 0; i < skills.Count; i++)
            {
                if (paired)
                {
                    skills[i].Text = english[i].Text;
                    skills[i].Effect = english[i].Effect;
                    if (english[i].Costs.Count > 0) skills[i].CostLabel = english[i].CostText;
                }
                else if (!string.IsNullOrWhiteSpace(en) && en != ja) skills[i].Text = skills[i].Effect = en; // some sets' English is the Japanese again
            }
        }

        foreach (Skill skill in skills)
            if (skill is AutoSkill auto && auto.Timing == SkillTiming.Other)
                UnityEngine.Debug.LogWarning("[SkillParser] No timing recognised, so this [AUTO] won't be asked about: " + skill.Text);
        return skills;
    }

    // Its costs, then its effect's steps, with the first clause the engine doesn't know (null) as one ManualStep
    internal static List<Step> WithCosts(IEnumerable<Cost> costs, IEnumerable<Step> effect)
    {
        var steps = costs.Select(c => (Step)new CostStep(c)).ToList();
        bool manual = false;
        foreach (Step step in effect)
        {
            if (step != null) steps.Add(step);
            else if (!manual)
            {
                steps.Add(new ManualStep());
                manual = true;
            }
        }
        return steps;
    }

    public static List<Skill> Parse(string abilityText)
    {
        var skills = new List<Skill>();
        if (string.IsNullOrWhiteSpace(abilityText)) return skills;

        MatchCollection markers = MarkerRx.Matches(abilityText);
        for (int i = 0; i < markers.Count; i++)
        {
            int start = markers[i].Index + markers[i].Length;
            int end = i + 1 < markers.Count ? markers[i + 1].Index : abilityText.Length;
            Skill skill = ParseSkill(markers[i].Groups[2].Value.ToUpperInvariant().Substring(0, 3), abilityText.Substring(start, end - start).Trim()); // "AUT", "ACT" or "CON"
            string keyword = markers[i].Groups[1].Value;
            if (keyword.IndexOf("Divine Skill", System.StringComparison.OrdinalIgnoreCase) >= 0) skill.GameGroup = "Divine";
            else if (keyword.IndexOf("Unique Skill", System.StringComparison.OrdinalIgnoreCase) >= 0) skill.GameGroup = "Unique";
            if (skill.GameGroup != null) skill.PerGame = true;
            skills.Add(skill);
        }
        return skills;
    }

    static Skill ParseSkill(string kind, string text)
    {
        // The header comes before the colon, e.g. "(RC)[1/Turn]"; it's empty when the colon comes first
        int colon = text.IndexOf(':');
        bool hasColon = colon >= 0 && colon <= MaxHeaderLength;
        string header = hasColon ? text.Substring(0, colon) : "";
        string body = hasColon ? text.Substring(colon + 1).Trim() : text;

        Match circle = CircleRx.Match(header);
        Match perTurn = PerTurnRx.Match(header);

        var costs = new List<Cost>();
        string effect = body;
        int costAt = body.IndexOf(CostMarker, System.StringComparison.OrdinalIgnoreCase); // "[COST]" or "[Cost]"
        if (costAt >= 0)
        {
            int from = costAt + CostMarker.Length;
            Match end = CostEndRx.Match(body, from);
            int costEnd = end.Success ? end.Index + 1 : body.Length;
            costs = ParseCosts(body.Substring(from, costEnd - from));
            string after = body.Substring(costEnd).TrimStart(' ', ',');
            if (after.StartsWith("and ")) after = after.Substring(4);
            effect = body.Substring(0, costAt) + after; // the text with its costs taken out
        }

        // Its timing is in the opening clause, e.g. "When this unit is placed on (RC) from your hand"
        string when = body;
        int comma = body.IndexOf(',');
        if (comma >= 0) when = body.Substring(0, comma);
        if (costAt >= 0 && costAt < when.Length) when = body.Substring(0, costAt);

        string circleName = circle.Success ? circle.Groups[1].Value.ToUpperInvariant().Substring(0, 1) + "C" : null; // "R" -> "RC"
        int limit = perTurn.Success ? int.Parse(perTurn.Groups[1].Value) : 0;
        text = text.TrimStart(':', ' '); // no header: the colon is just the marker's
        Skill skill;
        switch (kind)
        {
            case "AUT": skill = new AutoSkill(ParseTiming(when), text, effect, circleName, limit, costs); break;
            case "ACT": skill = new ActSkill(text, effect, circleName, limit, costs); break;
            default: skill = new ContSkill(text, effect, circleName, limit, costs); break;
        }
        skill.Order = ParseOrder(body);
        skill.PerGame = Regex.IsMatch(header, @"1\s*/\s*Fight", RegexOptions.IgnoreCase);
        skill.Steps = WithCosts(costs, EffectSteps(effect));
        Match riddenBy = RiddenByRx.Match(when);
        if (skill is AutoSkill ridden && riddenBy.Success) ridden.Named = riddenBy.Groups[1].Value.Trim();
        return skill;
    }

    /* A step per clause the engine knows ("draw a card", "Soul Charge (1)", "reveal ...", "shuffle"),
     null for one it doesn't. "Reveal it" after a search is the card searched for, so the search
     clause says which card. A search with no shuffle of its own ends with one. */
    static IEnumerable<Step> EffectSteps(string effect)
    {
        string before = "";
        bool searched = false, shuffled = false;
        foreach (string clause in ClauseRx.Split(effect))
        {
            string c = clause.Trim(' ', '[', ']');
            if (c.Length == 0) continue;

            Match m;
            if ((m = DrawRx.Match(c)).Success) yield return new DrawStep(ParseCount(m.Groups[1].Value));
            else if ((m = SoulChargeRx.Match(c)).Success) yield return new CountStep(CountStep.Kind.SoulCharge, int.Parse(m.Groups[1].Value));
            else if ((m = CounterChargeRx.Match(c)).Success) yield return new CountStep(CountStep.Kind.CounterCharge, int.Parse(m.Groups[1].Value));
            else if ((m = EnergyChargeRx.Match(c)).Success) yield return new CountStep(CountStep.Kind.EnergyCharge, int.Parse(m.Groups[1].Value));
            else if (RevealRx.IsMatch(c)) yield return new RevealStep(Filter((SearchRx.IsMatch(before) ? before + " " : "") + c));
            else if (ShuffleRx.IsMatch(c))
            {
                shuffled = true;
                yield return new ShuffleStep();
            }
            else yield return null;

            searched |= SearchRx.IsMatch(c);
            before = c;
        }
        if (searched && !shuffled) yield return new ShuffleStep();
    }

    // "reveal a grade 3 or greater card named "X"", or "search your deck for up to 1 Prison card"
    static CardFilter Filter(string text)
    {
        var filter = new CardFilter();
        Match grade = GradeRx.Match(text);
        if (grade.Success)
        {
            filter.Grade = int.Parse(grade.Groups[1].Value);
            filter.OrGreater = grade.Groups[2].Success;
        }
        Match name = NameRx.Match(text);
        if (name.Success) filter.Name = name.Groups[1].Value;
        Match keyword = KeywordRx.Match(text);
        if (keyword.Success) filter.Keyword = keyword.Groups[1].Value;
        return filter;
    }

    static int ParseCount(string n)
    {
        switch (n.ToLowerInvariant())
        {
            case "a": case "an": case "one": return 1;
            case "two": return 2;
            case "three": return 3;
            default: return int.Parse(n);
        }
    }

    static PlayerOrder ParseOrder(string body)
    {
        string s = body.ToLowerInvariant();
        if (s.Contains("you are the second player") || s.Contains("you are going second")) return PlayerOrder.Second;
        if (s.Contains("you are the first player") || s.Contains("you are going first")) return PlayerOrder.First;
        return PlayerOrder.Any;
    }

    static SkillTiming ParseTiming(string when)
    {
        string s = when.ToLowerInvariant();
        if (s.Contains("is ridden") || s.Contains("ridden upon") || s.Contains("rode upon")) return SkillTiming.RiddenUpon;
        Match placed = PlacedRx.Match(s);
        if (placed.Success)
            return placed.Groups[1].Value == "vc" ? SkillTiming.PlacedOnVC : placed.Groups[1].Value == "rc" ? SkillTiming.PlacedOnRC : SkillTiming.PlacedOnGC;
        if (OrderZoneRx.IsMatch(s)) return SkillTiming.PlacedInOrderZone;
        if (s.Contains("this unit's attack hits")) return SkillTiming.AttackHits;
        if (s.Contains("this unit attacks")) return SkillTiming.Attacks;
        if (s.Contains("this unit boosts")) return SkillTiming.Boosts;
        if (s.Contains("end of that battle") || s.Contains("end of the battle")) return SkillTiming.EndOfBattle;
        if (s.Contains("beginning of your ride phase")) return SkillTiming.StartOfRidePhase;
        if (s.Contains("beginning of your main phase")) return SkillTiming.StartOfMainPhase;
        if (s.Contains("beginning of your battle phase")) return SkillTiming.StartOfBattlePhase;
        if (s.Contains("end of your turn") || s.Contains("end of the turn")) return SkillTiming.EndOfTurn;
        return SkillTiming.Other;
    }

    /* The costs between [COST] and the effect, by name. Whatever words are left once the known
     ones are taken out, e.g. "Retire a rear-guard", are one cost the player pays by hand. */
    static List<Cost> ParseCosts(string part)
    {
        var costs = new List<Cost>();
        foreach (Match m in BlastRx.Matches(part))
        {
            int amount = int.Parse(m.Groups[2].Value);
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "counter blast": costs.Add(new CounterBlastCost(amount)); break;
                case "soul blast": costs.Add(new SoulBlastCost(amount)); break;
                default: costs.Add(new EnergyBlastCost(amount)); break;
            }
        }
        foreach (Match m in DiscardRx.Matches(part)) costs.Add(new DiscardCost(ParseCount(m.Groups[1].Value)));
        if (RestRx.IsMatch(part)) costs.Add(new RestCost());

        string left = RestRx.Replace(DiscardRx.Replace(BlastRx.Replace(part, " "), " "), " ");
        left = Regex.Replace(Regex.Replace(left, @"[\[\](),&]|\band\b", " "), @"\s+", " ").Trim();
        if (Regex.IsMatch(left, "[A-Za-z]")) costs.Add(new OtherCost(left, 1));
        return costs;
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// Parses skills from the JP text; EN text paired by position for display
public static class SkillParser
{
    // Some cards spell the markers out, e.g. "[Continuous]"; a keyword before one, e.g. "[Divine Skill]-", belongs to it
    static readonly Regex MarkerRx = new Regex(@"(?:\[([^\]]+)\]\s*[-–—]\s*)?\[(AUTO|Automatic|ACT|Activated?|CONT|Continuous)\]", RegexOptions.IgnoreCase);
    // The "]" a cost ends at: the effect follows it, after ", " (not ", [" for another cost), " and " or "."
    static readonly Regex CostEndRx = new Regex(@"\](?=\s*,(?!\s*\[)|\s+and\s|\s*\.|\s*$)");
    const string CostMarker = "[COST]";
    const int MaxHeaderLength = 30; // a colon further in is the effect's own

    static readonly Regex JpPlayCostRx = new Regex(@"【コスト】\[(.+?)\]ことでプレイ"); // "【コスト】[ユニットを１枚【レスト】させる]ことでプレイできる！"
    static readonly Regex JpRestUnitsRx = new Regex(@"ユニットを([0-9]+)枚【レスト】");

    static readonly Dictionary<CardData, List<Skill>> Cache = new Dictionary<CardData, List<Skill>>();

    public static List<Skill> Parse(CardData data)
    {
        if (data == null) { return new List<Skill>(); }
        if (!Cache.TryGetValue(data, out List<Skill> skills))
        {
            Cache[data] = skills = ParseCard(data.abilityTextJa, data.abilityText);
        }
        return skills;
    }

    // Every skill on the card runs without the player doing part of it by hand; for its info panel
    public static bool Supported(CardData data) => Parse(data).All(s => s.Supported);

    // JP skills with their EN text where they line up
    public static List<Skill> ParseCard(string ja, string en)
    {
        List<Skill> skills = JpSkillParser.Parse(ja);
        List<EnglishSkill> english = English(en);
        bool paired = english.Count == skills.Count && english.Select(s => s.marker).SequenceEqual(skills.Select(s => s.Marker));
        for (int i = 0; i < skills.Count; i++)
        {
            if (paired)
            {
                skills[i].Text = english[i].text;
                skills[i].Effect = english[i].effect;
                if (english[i].costs != null && skills[i].Costs.Count > 0) { skills[i].CostLabel = english[i].costs; }
            }
            else if (!string.IsNullOrWhiteSpace(en) && en != ja) { skills[i].Text = skills[i].Effect = en; } // some sets' English is the Japanese again
        }

        foreach (Skill skill in skills)
            if (skill is AutoSkill auto && auto.Timing == SkillTiming.Other)
            {
                UnityEngine.Debug.LogWarning("[SkillParser] No timing recognised, so this [AUTO] won't be asked about: " + skill.Text);
            }
        return skills;
    }

    // Costs, then effect steps (leading conditions first)
    internal static List<Step> WithCosts(IEnumerable<Cost> costs, IEnumerable<Step> effect)
    {
        List<Step> effectSteps = effect.ToList();
        int leading = effectSteps.TakeWhile(s => s is ConditionStep).Count();
        var steps = effectSteps.Take(leading).Concat(costs.Select(c => (Step)new CostStep(c))).ToList();
        bool manual = false;
        foreach (Step step in effectSteps.Skip(leading))
        {
            if (step != null) { steps.Add(step); }
            else if (!manual)
            {
                steps.Add(new ManualStep());
                manual = true;
            }
        }
        return steps;
    }

    // An order's play cost as steps
    public static ExtraSkill PlayCost(CardData data)
    {
        if (data == null || !data.isOrderCard) { return null; }
        Match jp = JpPlayCostRx.Match((data.abilityTextJa ?? "").Normalize(System.Text.NormalizationForm.FormKC)); // full-width digits and brackets, as JpSkillParser reads them
        if (!jp.Success) { return null; }

        var steps = new List<Step>();
        foreach (string item in Regex.Split(jp.Groups[1].Value, "[、，,]").Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            Match rest = JpRestUnitsRx.Match(item);
            steps.Add(rest.Success ? new RestUnitStep(int.Parse(rest.Groups[1].Value)) : (Step)new CostStep(JpSkillParser.ParseCost(item)));
        }
        return new ExtraSkill("(play cost)", string.Join(", ", steps.Select(s => s is CostStep cost ? cost.Cost.ToString() : s.ToString())), steps);
    }

    static readonly Regex JpOverExtraRx = new Regex(@"追加効果\s*[-－ー]\s*(.+?)[!！]?\s*$", RegexOptions.Multiline); // "追加効果－あなたのヴァンガードを1枚選び、【スタンド】させる！"
    static readonly Regex EnOverExtraRx = new Regex(@"Additional Effect\s*[-–—:]\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // An Over trigger's additional effect as steps
    public static ExtraSkill OverTriggerEffect(CardData data)
    {
        if (data == null) { return null; }
        Match jp = JpOverExtraRx.Match((data.abilityTextJa ?? "").Normalize(System.Text.NormalizationForm.FormKC));
        if (!jp.Success) { return null; }
        Match en = EnOverExtraRx.Match(data.abilityText ?? "");
        string text = en.Success ? en.Groups[1].Value : jp.Groups[1].Value;
        return new ExtraSkill("(Over trigger)", text, WithCosts(new List<Cost>(), JpSkillParser.EffectSteps(jp.Groups[1].Value)));
    }

    // One skill's English, for players: its marker to pair it by, its whole text, the effect without the costs, and the costs
    struct EnglishSkill
    {
        public string marker, text, effect, costs;
    }

    // EN text split at each [AUTO], [ACT] or [CONT]
    static List<EnglishSkill> English(string en)
    {
        var skills = new List<EnglishSkill>();
        if (string.IsNullOrWhiteSpace(en)) { return skills; }

        MatchCollection markers = MarkerRx.Matches(en);
        for (int i = 0; i < markers.Count; i++)
        {
            int start = markers[i].Index + markers[i].Length;
            int end = i + 1 < markers.Count ? markers[i + 1].Index : en.Length;
            string text = en.Substring(start, end - start).Trim();

            int colon = text.IndexOf(':');
            string body = colon >= 0 && colon <= MaxHeaderLength ? text.Substring(colon + 1).Trim() : text;
            string effect = body, costs = null;
            int costAt = body.IndexOf(CostMarker, System.StringComparison.OrdinalIgnoreCase); // "[COST]" or "[Cost]"
            if (costAt >= 0)
            {
                int from = costAt + CostMarker.Length;
                Match costEnd = CostEndRx.Match(body, from);
                int to = costEnd.Success ? costEnd.Index + 1 : body.Length;
                costs = Regex.Replace(Regex.Replace(body.Substring(from, to - from), @"[\[\]]", " "), @"\s+", " ").Trim(); // "Counter Blast (1), Soul Blast (1)"
                string after = body.Substring(to).TrimStart(' ', ',');
                if (after.StartsWith("and ")) { after = after.Substring(4); }
                effect = body.Substring(0, costAt) + after;
            }

            string kind = markers[i].Groups[2].Value.ToUpperInvariant().Substring(0, 3);
            skills.Add(new EnglishSkill
            {
                marker = kind == "AUT" ? "[AUTO]" : kind == "ACT" ? "[ACT]" : "[CONT]",
                text = text.TrimStart(':', ' '), // no header: the colon is just the marker's
                effect = effect,
                costs = costs,
            });
        }
        return skills;
    }
}

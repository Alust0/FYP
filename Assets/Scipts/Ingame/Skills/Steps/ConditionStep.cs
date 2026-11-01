using System.Linq;
using System.Text.RegularExpressions;

/* "相手のヴァンガードがグレード3以上なら" and other conditions in an effect: checked from the board and
 the counts when the engine can (either vanguard's grade, the damage, soul or drop count, cards in
 the Prison, a Persona Ride this turn), else the player says: Done if it's met, Skip step if not. Not met, the skill does
 nothing more. One at the start comes before the costs (SkillParser.WithCosts), so nothing is paid
 for a skill that won't go on. */
public class ConditionStep : Step
{
    static readonly Regex VanguardGradeRx = new Regex(@"^(相手|あなた)のヴァンガードがグレード(\d+)以上なら$");
    static readonly Regex CountRx = new Regex(@"^あなたの(ダメージゾーン|ソウル|ドロップ)が(\d+)枚以上なら$");
    static readonly Regex ImprisonedRx = new Regex(@"^あなたの監獄に収容されている相手のカードが(\d+)枚以上なら$");
    const string PersonaRodeText = "このターンにあなたがペルソナライドしているなら";

    public string Text { get; } // the condition as the card has it

    public ConditionStep(string text)
    {
        Text = text;
    }

    public override bool Automated => VanguardGradeRx.IsMatch(Text) || CountRx.IsMatch(Text) || ImprisonedRx.IsMatch(Text) || Text == PersonaRodeText;

    public override bool Begin(SkillFrame f)
    {
        bool? met = Check(f);
        if (met == null) return false; // the player says
        if (met == false) f.Rules.Stop(f, "its condition isn't met, so it does nothing more.");
        return true;
    }

    // Null when the engine can't tell, e.g. an unseen vanguard or a condition it doesn't read
    bool? Check(SkillFrame f)
    {
        Match grade = VanguardGradeRx.Match(Text);
        if (grade.Success)
        {
            PlayerState player = grade.Groups[1].Value == "あなた" ? f.Owner : GameState.Instance.players.FirstOrDefault(p => p != f.Owner);
            CardInstance vanguard = player?.vanguardCircle;
            return vanguard == null ? (bool?)null : vanguard.data.grade >= int.Parse(grade.Groups[2].Value);
        }

        Match count = CountRx.Match(Text);
        if (count.Success)
        {
            string zone = count.Groups[1].Value == "ダメージゾーン" ? "Damage" : count.Groups[1].Value == "ソウル" ? "Soul" : "Drop";
            return f.Owner.CountInZone(zone) >= int.Parse(count.Groups[2].Value);
        }

        Match imprisoned = ImprisonedRx.Match(Text);
        if (imprisoned.Success) return Prison.Imprisoned(f.Owner) >= int.Parse(imprisoned.Groups[1].Value);

        if (Text == PersonaRodeText) return f.Rules.HasPersonaRode(f.Owner);
        return null;
    }

    public override string Instruction(SkillFrame f) => "is its condition met? Done if it is, Skip step if not: " + f.Skill.Effect;
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/* Reads a card's official Japanese text into skills, the same kinds SkillParser makes from the
 English. The Japanese wording is fixed, so each part has one marker:
 - 【自】【起】【永】 start an AUTO, ACT or CONT skill; 【ディヴァインスキル】- or 【ユニークスキル】- before
   one makes it once a game for all of that kind, 白翼-/黒翼- gate it (SkillGate); any other
   keyword before it (ペルソナ-, 抜錨-, 3つ以上-) is kept in front of its text for the player
 - The header runs to the colon: 【(V)】/【(R)】/【(G)】 or a zone (【ソウル】【ドロップ】...) for where it
   works, 【ターン1回】 for 1/Turn, 【ファイト1回】 for once a game, and a keyword like 【リミットブレイク】(4)
 - An AUTO's timing is its opening clause, up to "...時、"
 - 【コスト】[...] is its cost: "," between costs, "か" between options to pick from, "ことで" after it
 - The effect is split into clauses at 、 and 。, and the ones the engine knows become steps
 - Abilities given to a unit, in 『』, aren't the card's own: no markers, costs or steps are read
   from inside them
 - 罪能- (堕獄囚) is no skill of its own: it's what "このユニットの罪能を使う" does
 The text is normalised (NFKC) first, so full-width digits, brackets and colons read as plain ones. */
public static class JpSkillParser
{
    /* A keyword before the marker, e.g. "【ディヴァインスキル】-" or "フレンド-", belongs to the skill after it.
     A skill starts a line or a "・" item; "...同名のカードの【自】能力" in the middle of one is a mention. */
    static readonly Regex MarkerRx = new Regex(@"(?<=^|\n|・|。|\s)(?:(【[^】]+】|[^\s、。【】:・]+)-)?【(自|起|永)】");
    static readonly Regex CircleRx = new Regex(@"\((V|R|G)\)");
    static readonly Regex PerTurnRx = new Regex(@"ターン(\d+)回");
    static readonly Regex TimingRx = new Regex(@"^(.*?時)[、,]"); // "あなたのメインフェイズ開始時、"
    static readonly Regex PlacedRx = new Regex(@"\((V|R|G)\)に(?:登場|置かれ|コールされ)");
    static readonly Regex OnVorRRx = new Regex(@"\(V\)か\(R\)に登場"); // one skill for each
    // "「X」にライドされた", "「X」を含むグレード3にライドされた"
    static readonly Regex RiddenByRx = new Regex(@"「([^」]+)」(?:を含む[^、。「」]*?)?にライドされた");
    static readonly Regex RodeFromRx = new Regex(@"「([^」]+)」からライドして登場");
    static readonly Regex AttackedNameRx = new Regex(@"「([^」]+)」を含む[^、。「」]*?ヴァンガードがアタックされた");
    static readonly Regex BlastRx = new Regex(@"【(カウンターブラスト|ソウルブラスト|エネルギーブラスト)】\((\d+)\)");
    static readonly Regex DiscardRx = new Regex(@"手札から.*?(\d+)枚捨て");
    static readonly Regex RestRx = new Regex(@"このユニットを【レスト】");
    static readonly Regex OrRx = new Regex(@"(?<=[)\]】])か[、,]?"); // "(2)か手札から", "(3)か、あなたの"
    static readonly Regex AfterCostRx = new Regex(@"^\s*(?:する)?ことで[、,]?");
    static readonly Regex ClauseRx = new Regex(@"[、。,]");
    static readonly Regex DrawRx = new Regex(@"(\d+)枚引");
    static readonly Regex SoulChargeRx = new Regex(@"ソウルチャージ】?\((\d+)\)"); // "【ソウルチャージ】(3)"
    static readonly Regex CounterChargeRx = new Regex(@"カウンターチャージ】?\((\d+)\)");
    static readonly Regex EnergyChargeRx = new Regex(@"エネルギーチャージ】?\((\d+)\)");
    // Reminder text: brackets with kana in, one level of brackets inside, e.g. "(あなたの(V)かGゾーンに…で有効)"
    static readonly Regex ReminderRx = new Regex(@"\((?:[^()]|\([^()]*\))*\)");
    static readonly Regex LimitBreakRx = new Regex(@"【リミットブレイク】\((\d+)\)");
    static readonly Regex RitualRx = new Regex(@"儀式(\d+)");
    static readonly Regex RevealRx = new Regex(@"公開(?!した|され|しな)"); // "公開してよい", not "公開したか" or "公開された中から"
    static readonly Regex SearchRx = new Regex(@"山札から.*探し"); // "あなたの山札から監獄カードを1枚まで探し"
    static readonly Regex ShuffleRx = new Regex(@"^山札をシャッフル");
    static readonly Regex GradeRx = new Regex(@"グレード(\d+)(以上)?");
    static readonly Regex RaceRx = new Regex(@"〈([^〉]+)〉");
    static readonly Regex NameRx = new Regex(@"「([^」]+)」");
    static readonly Regex KeywordRx = new Regex(@"([\p{IsCJKUnifiedIdeographs}\p{IsKatakana}ー]+)カード"); // 監獄カード; not "グレード3以上のカード"
    static readonly Regex SinRx = new Regex(@"罪能-"); // the 罪能 text, after the card's skills
    const string CostMarker = "【コスト】";
    const string SinUse = "罪能を使";
    const int MaxHeaderLength = 30; // a colon further in is the effect's own
    const char Masked = '＿'; // a 『』 ability's text, kept the same length

    /* [AUTO] keywords whose timing is only in their reminder text (removed), from 総合ルール 14:
     the keyword alone stands for "...時", which ParseTiming reads */
    static readonly Dictionary<string, string> KeywordTimings = new Dictionary<string, string>
    {
        ["先駆"] = "このユニットがライドされた時", // 14.8: ridden by a unit of the same clan
        ["暴喰"] = "このユニットがアタックした時", // 14.26
        ["亡霊"] = "このユニットが(R)に登場した時", // 14.35
    };

    // Header zones other than the circles, for a skill that works from there
    static readonly (string jp, string zone)[] HeaderZones =
    {
        ("ソウル", "Soul"), ("ドロップ", "Drop"), ("ダメージゾーン", "Damage"), ("オーダーゾーン", "Order"),
        ("バインドゾーン", "Bind"), ("手札", "Hand"), ("ライドデッキ", "RideDeck"),
    };

    // Costs the engine can't pay, named for the player to pay by hand
    static readonly (Regex rx, string name)[] OtherCosts =
    {
        (new Regex(@"この(?:カード|ユニット)をソウルに置く"), "put this card into your soul"),
        (new Regex(@"この(?:カード|ユニット)を山札の下に置く"), "put this card on the bottom of your deck"),
        (new Regex(@"このユニットを退却させる"), "retire this unit"),
        (new Regex(@"この(?:カード|ユニット)をバインドする"), "bind this card"),
        (new Regex(@"このカードをドロップに置く"), "put this card into the drop zone"),
        (new Regex(@"手札から.*ソウルに置く"), "put a card from your hand into your soul"),
        (new Regex(@"ライドデッキから.*公開する"), "reveal the named card from your ride deck"),
        (new Regex(@"ペルソナと言う"), "say \"Persona\""),
        (new Regex(@"バインドする"), "bind the cards named"),
        (new Regex(@"ソウルに置く"), "put the cards named into your soul"),
        (new Regex(@"山札の下に"), "put the cards named on the bottom of your deck"),
    };

    /* The text is read without its reminder text (総合ルール 2.11.3: it explains, it doesn't do
     anything), so a keyword's "(あなたのダメージが4枚以上で有効)" doesn't push the colon past the
     header. Brackets without kana, e.g. (V) or (3), are kept. */
    public static List<Skill> Parse(string text)
    {
        var skills = new List<Skill>();
        if (string.IsNullOrWhiteSpace(text)) return skills;
        text = text.Normalize(NormalizationForm.FormKC);
        text = ReminderRx.Replace(text, m => Regex.IsMatch(m.Value, @"\p{IsHiragana}") ? "" : m.Value);

        // 罪能 comes after the skills that use it
        string masked = MaskGranted(text);
        string sin = null;
        Match sinAt = SinRx.Match(masked);
        if (sinAt.Success)
        {
            sin = text.Substring(sinAt.Index + sinAt.Length).Trim();
            text = text.Substring(0, sinAt.Index);
            masked = masked.Substring(0, sinAt.Index);
        }

        MatchCollection markers = MarkerRx.Matches(masked);
        for (int i = 0; i < markers.Count; i++)
        {
            Match m = markers[i];
            int start = m.Index + m.Length;
            int end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
            string body = text.Substring(start, end - start).Trim();
            string maskedBody = MaskGranted(body);
            string kind = m.Groups[2].Value;
            string keyword = m.Groups[1].Value;

            skills.Add(WithKeyword(ParseSkill(kind, body, maskedBody, sin, false), keyword));
            // "(V)か(R)に登場した時": a second skill for the vanguard circle
            if (kind == "自" && OnVorRRx.IsMatch(maskedBody))
                skills.Add(WithKeyword(ParseSkill(kind, body, maskedBody, sin, true), keyword));
        }
        return skills;
    }

    // A keyword before the marker: a once-a-game group (12.3), a gate (14.43), or a label for the player
    static Skill WithKeyword(Skill skill, string keyword)
    {
        if (keyword.Contains("ディヴァインスキル")) skill.GameGroup = "Divine";
        else if (keyword.Contains("ユニークスキル")) skill.GameGroup = "Unique";
        else if (keyword == "白翼") skill.Gate = new SkillGate(SkillGate.Kind.WhiteWings);
        else if (keyword == "黒翼") skill.Gate = new SkillGate(SkillGate.Kind.BlackWings);
        else if (keyword.Length > 0) skill.Text = keyword + "-" + skill.Text; // e.g. "3つ以上-": arcana markers aren't counted
        if (skill.GameGroup != null) skill.PerGame = true;
        return skill;
    }

    // The text inside 『』 (an ability given to a unit) blanked out, so nothing is read from it
    static string MaskGranted(string s)
    {
        var sb = new StringBuilder(s);
        int depth = 0;
        for (int i = 0; i < sb.Length; i++)
        {
            if (sb[i] == '『') depth++;
            else if (sb[i] == '』') depth = System.Math.Max(0, depth - 1);
            else if (depth > 0) sb[i] = Masked;
        }
        return sb.ToString();
    }

    // A keyword in the header that turns the skill on only while its condition holds (14.2)
    static SkillGate HeaderGate(string header)
    {
        Match m;
        if ((m = LimitBreakRx.Match(header)).Success) return new SkillGate(SkillGate.Kind.LimitBreak, int.Parse(m.Groups[1].Value));
        if ((m = RitualRx.Match(header)).Success) return new SkillGate(SkillGate.Kind.Ritual, int.Parse(m.Groups[1].Value));
        if (header.Contains("真剣必殺")) return new SkillGate(SkillGate.Kind.Shinken);
        if (header.Contains("開眼")) return new SkillGate(SkillGate.Kind.Kaigan);
        return null;
    }

    // Where it works: a circle, or another zone; null for anywhere
    static string HeaderCircle(string header)
    {
        Match circle = CircleRx.Match(header);
        if (circle.Success) return circle.Groups[1].Value + "C";
        // "【ソウル/ドロップ】" works from either: "Soul/Drop"
        string zones = string.Join("/", HeaderZones.Where(z => header.Contains(z.jp)).Select(z => z.zone));
        return zones.Length > 0 ? zones : null;
    }

    /* text and masked are the same text, masked with 『』 blanked out, so an index into one is an
     index into the other. onVanguard: the (V) half of a "(V)か(R)に登場した時" skill. */
    static Skill ParseSkill(string kind, string text, string masked, string sin, bool onVanguard)
    {
        int colon = masked.IndexOf(':');
        bool hasColon = colon >= 0 && colon <= MaxHeaderLength;
        string header = hasColon ? text.Substring(0, colon) : "";
        int bodyAt = hasColon ? colon + 1 : 0;
        while (bodyAt < text.Length && char.IsWhiteSpace(text[bodyAt])) bodyAt++;
        string body = text.Substring(bodyAt), mBody = masked.Substring(bodyAt);

        string circle = HeaderCircle(header);
        Match perTurn = PerTurnRx.Match(header);
        int limit = perTurn.Success ? int.Parse(perTurn.Groups[1].Value) : 0;

        // An AUTO's timing is its opening clause; what follows is its conditions, cost and effect
        string rest = body, mRest = mBody;
        string when = null;
        if (kind == "自")
        {
            Match timing = TimingRx.Match(mBody);
            if (timing.Success)
            {
                when = body.Substring(0, timing.Groups[1].Length);
                rest = body.Substring(timing.Length);
                mRest = mBody.Substring(timing.Length);
            }
            else if (KeywordTimings.TryGetValue(body.Trim(), out string keywordWhen)) when = keywordWhen;
        }

        // "このユニットの罪能を使う" does the 罪能, cost and all; "使ってよい" leaves it to the player
        string shown = text.TrimStart(':', ' '); // no header: the colon is just the marker's
        string sinShown = null;
        if (sin != null && mRest.Contains(SinUse))
        {
            sinShown = "罪能-" + sin;
            shown += "\n" + sinShown;
            if (mRest.Contains(SinUse + "う"))
            {
                rest += "。" + sin;
                mRest += "。" + MaskGranted(sin);
                sinShown = null;
            }
        }

        var costs = new List<Cost>();
        string effect = rest, mEffect = mRest;
        int costAt = mRest.IndexOf(CostMarker, System.StringComparison.Ordinal);
        int open = costAt >= 0 ? mRest.IndexOf('[', costAt) : -1;
        int close = open >= 0 ? ClosingBracket(mRest, open) : -1;
        if (close > open)
        {
            costs = ParseCosts(rest.Substring(open + 1, close - open - 1));
            int after = close + 1 + AfterCostRx.Match(mRest.Substring(close + 1)).Length;
            effect = rest.Substring(0, costAt) + rest.Substring(after);
            mEffect = mRest.Substring(0, costAt) + mRest.Substring(after);
        }
        if (onVanguard && body.Contains("(V)に登場したなら、このコストはなくなる")) costs.Clear();
        if (sinShown != null) effect += "\n" + sinShown; // for the player, not read as steps

        Skill skill;
        switch (kind)
        {
            case "自":
                SkillTiming at = onVanguard ? SkillTiming.PlacedOnVC : when != null ? ParseTiming(when) : SkillTiming.Other;
                skill = new AutoSkill(at, shown, effect, circle, limit, costs);
                break;
            case "起": skill = new ActSkill(shown, effect, circle, limit, costs); break;
            default: skill = new ContSkill(shown, effect, circle, limit, costs); break;
        }
        skill.Order = body.Contains("あなたが後攻なら") ? PlayerOrder.Second : body.Contains("あなたが先攻なら") ? PlayerOrder.First : PlayerOrder.Any;
        skill.Gate = HeaderGate(header);
        // 【D-双闘】 can be done once a fight (its reminder text, removed)
        skill.PerGame = header.Contains("ファイト1回") || mBody.StartsWith("【D-双闘】");
        skill.Steps = SkillParser.WithCosts(costs, EffectSteps(mEffect));
        if (skill is AutoSkill auto && when != null) auto.Named = NamedInTiming(when);
        return skill;
    }

    // The other card a timing names: the rider, the vanguard ridden from, or the vanguard attacked
    static string NamedInTiming(string when)
    {
        Match m;
        if ((m = RiddenByRx.Match(when)).Success) return m.Groups[1].Value;
        if ((m = RodeFromRx.Match(when)).Success) return m.Groups[1].Value;
        if ((m = AttackedNameRx.Match(when)).Success) return m.Groups[1].Value;
        return null;
    }

    // The "]" that closes the "[" at open, past any brackets inside it
    static int ClosingBracket(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '[') depth++;
            else if (s[i] == ']' && --depth == 0) return i;
        }
        return -1;
    }

    // Only "this unit" timings, your vanguard being attacked, and the owner's own phases: the ones RulesEngine reports
    static SkillTiming ParseTiming(string when)
    {
        bool self = when.Contains("このユニット") || when.Contains("このカード");
        bool theirs = when.Contains("相手の");
        if (when.Contains("ライドされた")) return SkillTiming.RiddenUpon;
        if (self && when.Contains("ライドして登場")) return SkillTiming.PlacedOnVC; // "「X」からライドして登場した時"

        Match placed = PlacedRx.Match(when);
        if (self && placed.Success)
            return placed.Groups[1].Value == "V" ? SkillTiming.PlacedOnVC : placed.Groups[1].Value == "R" ? SkillTiming.PlacedOnRC : SkillTiming.PlacedOnGC;
        if (self && when.Contains("オーダーゾーンに置かれた")) return SkillTiming.PlacedInOrderZone;
        if (self && when.Contains("アタックがヒットした")) return SkillTiming.AttackHits;
        if (self && when.Contains("ブーストしたバトル終了時")) return SkillTiming.BoostEndOfBattle;
        if (self && when.Contains("バトル終了時")) return SkillTiming.EndOfBattle; // "このユニットがアタックしたバトル終了時"
        if (self && when.Contains("アタックした")) return SkillTiming.Attacks;
        if (self && when.Contains("ブーストした")) return SkillTiming.Boosts;
        if (theirs) return SkillTiming.Other;
        // "「X」を含むあなたのヴァンガードがアタックされた時", also its "...バトルのガードステップ開始時"
        if (when.Contains("ヴァンガードがアタックされた")) return SkillTiming.VanguardAttacked;
        if (when.Contains("ライドフェイズ開始時")) return SkillTiming.StartOfRidePhase;
        if (when.Contains("メインフェイズ開始時")) return SkillTiming.StartOfMainPhase;
        if (when.Contains("バトルフェイズ開始時")) return SkillTiming.StartOfBattlePhase;
        if (when.Contains("ターン終了時")) return SkillTiming.EndOfTurn;
        return SkillTiming.Other;
    }

    /* "," between costs that are all paid; "か" between options, one of which is paid (ChoiceCost).
     One the engine doesn't know is paid by hand (OtherCost). */
    static List<Cost> ParseCosts(string part)
    {
        var costs = new List<Cost>();
        foreach (string item in part.Split(','))
        {
            List<Cost> options = OrRx.Split(item).Select(o => o.Trim()).Where(o => o.Length > 0).Select(ParseCost).ToList();
            if (options.Count == 1) costs.Add(options[0]);
            else if (options.Count > 1) costs.Add(new ChoiceCost(options));
        }
        return costs;
    }

    static Cost ParseCost(string item)
    {
        Match blast = BlastRx.Match(item);
        if (blast.Success)
        {
            int amount = int.Parse(blast.Groups[2].Value);
            switch (blast.Groups[1].Value)
            {
                case "カウンターブラスト": return new CounterBlastCost(amount);
                case "ソウルブラスト": return new SoulBlastCost(amount);
                default: return new EnergyBlastCost(amount);
            }
        }
        Match discard = DiscardRx.Match(item);
        if (discard.Success) return new DiscardCost(int.Parse(discard.Groups[1].Value));
        if (RestRx.IsMatch(item)) return new RestCost();
        foreach ((Regex rx, string name) in OtherCosts)
            if (rx.IsMatch(item)) return new OtherCost(name, 1);
        return new OtherCost("the printed cost", 1);
    }

    /* A step per clause the engine knows, null for one it doesn't. "...を1枚選び、公開してよい":
     the choosing clause belongs to the reveal after it, and says which card it is. A search
     ("...探し、公開して") says which card too, but stays the player's to do. A search with no
     "山札をシャッフル" of its own ends with one. A choice ("以下を1つ行う", "1枚引くか、...") and
     what the opponent reveals are the player's. */
    static IEnumerable<Step> EffectSteps(string effect)
    {
        string[] clauses = ClauseRx.Split(effect).Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        bool searched = false, shuffled = false;
        for (int i = 0; i < clauses.Length; i++)
        {
            string c = clauses[i];
            if (c.Contains("以下を1つ"))
            {
                yield return null;
                yield break;
            }
            if (c.EndsWith("か") || c.Contains(")か【"))
            {
                yield return null;
                if (c.EndsWith("か")) i++; // the other option
                continue;
            }
            searched |= SearchRx.IsMatch(c);
            if (c.EndsWith("選び") && i + 1 < clauses.Length && RevealRx.IsMatch(clauses[i + 1])) continue;

            string before = i > 0 && (clauses[i - 1].EndsWith("選び") || SearchRx.IsMatch(clauses[i - 1])) ? clauses[i - 1] : "";
            Match m;
            if ((m = DrawRx.Match(c)).Success) yield return new DrawStep(int.Parse(m.Groups[1].Value));
            else if ((m = SoulChargeRx.Match(c)).Success) yield return new CountStep(CountStep.Kind.SoulCharge, int.Parse(m.Groups[1].Value));
            else if ((m = CounterChargeRx.Match(c)).Success) yield return new CountStep(CountStep.Kind.CounterCharge, int.Parse(m.Groups[1].Value));
            else if ((m = EnergyChargeRx.Match(c)).Success) yield return new CountStep(CountStep.Kind.EnergyCharge, int.Parse(m.Groups[1].Value));
            else if (RevealRx.IsMatch(c)) yield return before.StartsWith("相手") ? null : new RevealStep(Filter(before + c)); // "相手は自分の手札から1枚選び、公開する"
            else if (ShuffleRx.IsMatch(c))
            {
                shuffled = true;
                yield return new ShuffleStep();
            }
            else yield return null;
        }
        if (searched && !shuffled) yield return new ShuffleStep();
    }

    static CardFilter Filter(string text)
    {
        var filter = new CardFilter();
        Match grade = GradeRx.Match(text);
        if (grade.Success)
        {
            filter.Grade = int.Parse(grade.Groups[1].Value);
            filter.OrGreater = grade.Groups[2].Success;
        }
        Match race = RaceRx.Match(text);
        if (race.Success) filter.Race = race.Groups[1].Value;
        Match name = NameRx.Match(text);
        if (name.Success) filter.Name = name.Groups[1].Value;
        Match keyword = KeywordRx.Match(text);
        if (keyword.Success) filter.Keyword = keyword.Groups[1].Value;
        return filter;
    }
}

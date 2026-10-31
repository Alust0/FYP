using System.Linq;

/* Which card a skill asks for, e.g. "a grade 3 or greater <Gear Dragon>" or a card named "Favnir".
 Empty parts match anything. Names and races match in English or Japanese, ignoring punctuation. */
public class CardFilter
{
    public int Grade = -1; // -1 for any
    public bool OrGreater;
    public string Race; // e.g. "ギアドラゴン" from 〈ギアドラゴン〉
    public string Name; // part of the name, e.g. "ファヴルニール" from 「ファヴルニール」
    public string Keyword; // e.g. "監獄" from 監獄カード or "Prison" from "a Prison card": in its name, race, nation or type

    public bool Matches(CardData data)
    {
        if (data == null) return false;
        if (Grade >= 0 && (OrGreater ? data.grade < Grade : data.grade != Grade)) return false;
        if (Race != null && !Has(data.race, Race) && !Has(data.raceJa, Race)) return false;
        if (Name != null && !Has(data.cardName, Name) && !Has(data.cardNameEn, Name)) return false;
        if (Keyword != null && !new[] { data.cardName, data.cardNameEn, data.race, data.raceJa, data.nation, data.clan, data.cardType }.Any(s => Has(s, Keyword))) return false;
        return true;
    }

    public static bool Has(string text, string part) => text != null && Plain(text).Contains(Plain(part));

    // Letters and digits only, as names are written with or without commas and spaces
    public static string Plain(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // e.g. "a grade 3 or greater 〈ギアドラゴン〉 card", for notices
    public override string ToString()
    {
        string s = "a" + (Grade >= 0 ? " grade " + Grade + (OrGreater ? " or greater" : "") : "") +
                   (Race != null ? " 〈" + Race + "〉" : "") + (Keyword != null ? " " + Keyword : "") + " card";
        return Name != null ? s + " named \"" + Name + "\"" : s;
    }
}

using System.Linq;

/* A keyword that turns a skill on only while its condition holds (総合ルール 14.2), e.g.
 【リミットブレイク】(4). An [AUTO] whose gate is shut doesn't go off, and an [ACT] can't be used;
 one that went off resolves even if the condition stops holding (14.2.3.2). Cards the camera
 couldn't tell apart (untracked counts) get the benefit of the doubt. */
public class SkillGate
{
    public enum Kind
    {
        LimitBreak, // 【リミットブレイク】(N): N or more damage (14.7)
        Ritual, // 儀式N: N or more grade 1 cards in the drop zone (14.20)
        Shinken, // 真剣必殺: 3 or more damage (17.14.1)
        Kaigan, // 【開眼】: Persona Rode this turn, or 4 or more damage (17.16.3)
        WhiteWings, // 白翼: a vanguard named アレスティエル, and only odd grades in the bind zone (14.43)
        BlackWings, // 黒翼: the same with even grades
    }

    public Kind What { get; }
    public int Amount { get; }

    public SkillGate(Kind what, int amount = 0)
    {
        What = what;
        Amount = amount;
    }

    public bool Open(PlayerState owner, bool personaRodeThisTurn)
    {
        int damage = owner.CountInZone("Damage");
        switch (What)
        {
            case Kind.LimitBreak: return damage >= Amount;
            case Kind.Shinken: return damage >= 3;
            case Kind.Kaigan: return personaRodeThisTurn || damage >= 4;
            case Kind.Ritual:
                return owner.dropZone.Count(c => c.data.grade == 1) + System.Math.Max(0, owner.Untracked("Drop")) >= Amount;
            default:
                CardInstance vanguard = owner.vanguardCircle;
                if (vanguard == null || !CardFilter.Has(vanguard.data.cardName, "アレスティエル") && !CardFilter.Has(vanguard.data.cardNameEn, "Arestiel")) return false;
                if (owner.Untracked("Bind") > 0) return true;
                var faceUp = owner.bindZone.Where(c => c.facing == CardFacing.FaceUp).ToList();
                int odd = What == Kind.WhiteWings ? 1 : 0;
                return faceUp.Count > 0 && faceUp.All(c => c.data.grade % 2 == odd);
        }
    }

    // For notices, e.g. "Limit Break (4)"
    public override string ToString()
    {
        switch (What)
        {
            case Kind.LimitBreak: return "Limit Break (" + Amount + ")";
            case Kind.Ritual: return "Ritual " + Amount;
            case Kind.Shinken: return "Shinken Hissatsu";
            case Kind.Kaigan: return "Kaigan";
            case Kind.WhiteWings: return "White Wings";
            default: return "Black Wings";
        }
    }
}

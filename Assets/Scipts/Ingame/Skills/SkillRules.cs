using System.Collections.Generic;
using System.Linq;

/* Card skills during a game, for RulesEngine:
 - An [AUTO] skill whose timing the engine sees (a ride, a call, an attack, a phase starting) is
   queued on its owner's PC and asked about one at a time: Activate, Skip, or Later to pick the order
 - [ACT] skills are offered in the Main Phase once their circle, limit and costs allow
 - A skill used opens a frame that runs its steps in order (Pump): costs come off the counts,
   count-only effects are done, a draw or reveal waits for the camera, and anything else waits for
   the player's Done. "Skip step" passes one by.
 - Nothing moves on to the next phase until it's all done; Next Phase or turn end finishes the
   steps left as done, e.g. counting a draw that wasn't seen
 - A queued [AUTO] whose card was seen moving to another zone before it was answered does nothing
 - Online, what was used or skipped goes to the other PC too, for its notices and log
 - Of the conditions in the text only "if you are the first/second player" is checked, and the
   keywords that gate a skill (SkillGate, e.g. Limit Break); others, e.g. "if your vanguard
   is ...", are left to the player in a ManualStep */
public class SkillRules
{
    class Prompt
    {
        public CardInstance card;
        public Skill skill;
        public PlayerState owner; // as it went off: the card may leave the board before it's answered
        public string zone; // where the card was when it went off
    }

    readonly GameState gs;
    readonly System.Action<string, string> mNotice;
    readonly System.Action<string, string, System.Func<bool>> mToDo; // a notice that stays up until it's done
    readonly List<Prompt> mPrompts = new List<Prompt>(); // the first is the one being asked about
    readonly Dictionary<(CardInstance, Skill), int> mUsed = new Dictionary<(CardInstance, Skill), int>(); // this turn
    readonly HashSet<(PlayerState, object)> mUsedThisGame = new HashSet<(PlayerState, object)>(); // once-a-game skills, or their group, e.g. "Divine"
    PlayerState mPersonaRode; // Persona Rode this turn, for 【開眼】
    readonly Dictionary<PlayerState, int> mSoulBlastUnseen = new Dictionary<PlayerState, int>(); // Soul Blast counted, its cards not yet seen in the drop zone
    CardInstance mRestDue; // a Rest cost's unit, whose resting isn't an attack
    SkillFrame mFrame; // the skill being resolved, on its owner's PC
    SkillFrame mLastFrame; // the last one resolved, as the camera sees the cards it moved a moment after
    float mLastFrameEnded;
    const float AllowSeconds = 5f;
    // A paid cost's zones (Cost.Zones), lit up while the player moves the real cards
    readonly List<(PlayerState owner, string zone, float until)> mPaidZones = new List<(PlayerState, string, float)>();
    const float PaidZoneSeconds = 6f;
    // Arrows for cards to move, e.g. a paid cost's (Cost.Arrow) or a retired unit's: RulesEngine's
    public CardMoves Moves;
    // A Discard cost's drop zone stays lit until the owner's drop count reaches this
    readonly List<(PlayerState owner, int drop)> mDiscardsDue = new List<(PlayerState, int)>();
    readonly HashSet<(PlayerState, string, string, SkillTiming)> mFired = new HashSet<(PlayerState, string, string, SkillTiming)>(); // Repeat's, this turn
    bool mPumping; // a step's count change calls Recheck, which mustn't run the steps again from inside

    // This PC's latest skill used or skipped, and how many, uploaded by OnlineMatch for the other PC
    public static string LastTitle { get; private set; }
    public static string LastBody { get; private set; }
    public static int SentCount { get; private set; }

    public SkillRules(GameState state, System.Action<string, string> notice, System.Action<string, string, System.Func<bool>> toDo)
    {
        gs = state;
        mNotice = notice;
        mToDo = toDo;
    }

    // For RulesEngine's undo: what's been used and what's waiting to be asked about
    class State
    {
        public List<Prompt> prompts;
        public Dictionary<(CardInstance, Skill), int> used;
        public HashSet<(PlayerState, object)> usedThisGame;
        public PlayerState personaRode;
        public Dictionary<PlayerState, int> soulBlastUnseen;
        public CardInstance restDue;
        public SkillFrame frame;
        public HashSet<(PlayerState, string, string, SkillTiming)> fired;
        public List<(PlayerState, int)> discardsDue;
    }

    public object SaveState() => new State
    {
        prompts = new List<Prompt>(mPrompts),
        used = new Dictionary<(CardInstance, Skill), int>(mUsed),
        usedThisGame = new HashSet<(PlayerState, object)>(mUsedThisGame),
        personaRode = mPersonaRode,
        soulBlastUnseen = new Dictionary<PlayerState, int>(mSoulBlastUnseen),
        restDue = mRestDue,
        frame = mFrame,
        fired = new HashSet<(PlayerState, string, string, SkillTiming)>(mFired),
        discardsDue = new List<(PlayerState, int)>(mDiscardsDue),
    };

    /* A skill part-way through its steps can't be wound back, so one started since the save is
     dropped and one still going keeps the steps it has done */
    public void LoadState(object saved)
    {
        var state = (State)saved;
        mPrompts.Clear();
        mPrompts.AddRange(state.prompts);
        mUsed.Clear();
        foreach (var pair in state.used) mUsed[pair.Key] = pair.Value;
        mUsedThisGame.Clear();
        mUsedThisGame.UnionWith(state.usedThisGame);
        mPersonaRode = state.personaRode;
        mSoulBlastUnseen.Clear();
        foreach (var pair in state.soulBlastUnseen) mSoulBlastUnseen[pair.Key] = pair.Value;
        mRestDue = state.restDue;
        mFired.Clear();
        mFired.UnionWith(state.fired);
        mDiscardsDue.Clear();
        mDiscardsDue.AddRange(state.discardsDue);
        mFrame = state.frame != null && state.frame == mFrame ? mFrame : null;
        mPumping = false;
        if (Busy) Ask();
    }

    // Changes whenever anything SaveState keeps does, so RulesEngine can tell when to save
    public string StateKey =>
        mPrompts.Count + "|" + mUsed.Values.Sum() + "|" + mUsedThisGame.Count + "|" + mFired.Count + "|" + mPersonaRode?.playerIndex + "|" +
        mSoulBlastUnseen.Values.Sum() + "|" + mRestDue?.id + "|" + mDiscardsDue.Count + "|" + (mFrame != null ? mFrame.GetHashCode() + ":" + mFrame.Current?.GetHashCode() : "");

    // An [AUTO] skill is waiting for Activate or Skip; one waits while another skill resolves
    public bool Busy => mFrame == null && mPrompts.Count > 0;

    // A skill used is still going through its steps
    public bool Stepping => mFrame != null;

    // Nothing moves on to the next phase by itself until both are done
    public bool Resolving => mPrompts.Count > 0 || mFrame != null;

    // A skill's draw still to come, for HandZones to watch for like the Draw Phase's
    public bool DrawDue => mFrame?.Current is DrawStep;

    // The step waiting is a cost to choose, e.g. Counter Blast (2) or the printed cost
    public ChoiceCost ChoiceDue => mFrame?.Current is CostStep step ? step.Cost as ChoiceCost : null;

    public int PromptCount => mPrompts.Count;
    public PlayerState PromptOwner => Busy ? mPrompts[0].owner : mFrame?.Owner;
    public string PromptCost => Busy ? mPrompts[0].skill.ShortCostText : null;

    // For the HUD's Waiting line
    public string PromptWaiting => Busy
        ? mPrompts[0].owner.playerName + ": activate or skip " + mPrompts[0].card.data.DisplayName + "'s [AUTO]"
        : null;

    public string StepWaiting => mFrame == null ? null
        : mFrame.Owner.playerName + ": " + mFrame.Current?.Instruction(mFrame) + " (" + mFrame.Card.data.DisplayName + ")";

    // The lobby's Card skills switch is off: the basic rules only, no skill is asked about or paid for
    static bool Off => !MatchSession.SkillsOn;

    // timing has just happened to card: its [AUTO] skills for that timing get asked about (other: the card the timing names, e.g. the rider)
    public void Happened(SkillTiming timing, CardInstance card, CardInstance other = null)
    {
        if (Off || card == null || card.zoneOwner == null) return;
        PlayerState owner = card.zoneOwner;

        foreach (Skill skill in SkillParser.Parse(card.data))
        {
            if (!skill.UsableNow(gs, card, timing) || UsedUp(owner, card, skill) || !GateOpen(owner, skill) || !PrisonReady(owner, skill)) continue;
            if (skill is AutoSkill auto && !auto.OtherFits(other)) continue; // e.g. "ridden by" another card
            if (Repeat(owner, card, skill, timing)) continue;
            if (!MatchSession.IsLocal(owner))
            {
                mNotice("Skill", owner.playerName + "'s " + card.data.DisplayName + ": its " + skill.Marker + " can activate.");
                continue; // asked on their PC
            }
            mPrompts.Add(new Prompt { card = card, skill = skill, owner = owner, zone = card.zone });
            if (Busy && mPrompts.Count == 1) Ask();
        }
    }

    // A phase starting or the turn ending: every one of the player's units, and the soul's cards for skills that work from there
    public void PhaseTiming(SkillTiming timing, PlayerState player)
    {
        foreach (CardInstance unit in Units(player).ToList()) Happened(timing, unit);
        foreach (CardInstance card in player.soul.ToList())
            if (SkillParser.Parse(card.data).Any(s => s.Circle != null && s.Circle.Split('/').Contains("Soul"))) Happened(timing, card);
    }

    // The player's vanguard is attacked: their units', soul's and drop zone's skills for it, e.g. a soul card's "+20000"
    public void VanguardAttacked(PlayerState defender)
    {
        CardInstance vanguard = defender.vanguardCircle;
        foreach (CardInstance card in Units(defender).Concat(defender.soul).Concat(defender.dropZone).ToList())
            Happened(SkillTiming.VanguardAttacked, card, vanguard);
    }

    void Ask()
    {
        Prompt p = mPrompts[0];
        if (Fizzled(p))
        {
            mPrompts.RemoveAt(0);
            Tell(p.card.data.DisplayName + " [AUTO]", p.card.data.DisplayName + " moved to another zone before its [AUTO] was answered, so it does nothing.");
            if (Busy) Ask();
            return;
        }
        mToDo(p.card.data.DisplayName + " [AUTO]", p.owner.playerName + ": " + p.skill.Text +
            "\nCost: " + p.skill.CostText + "." + (p.skill.CanPay(p.owner, p.card) ? "" : " Can't be paid right now.") +
            (p.skill.Mandatory ? " It has to be used: Activate" : " Activate or Skip") + (mPrompts.Count > 1 ? ", or Later to do another first" : "") + ".",
            () => Busy && mPrompts[0] == p);
    }

    /* Seen somewhere else since it went off, from a rear-guard or guardian circle or the order zone.
     A card the camera has just lost sight of hasn't moved, and nor has a vanguard: the camera swaps
     it with the card under it as they peek out. */
    static bool Fizzled(Prompt p) =>
        p.zone != null && p.zone != "Vanguard" && p.zone != "Soul" && p.card.zone != null && p.card.zone != p.zone;

    // "activate skill": the [AUTO] being asked about, or else the only [ACT] that's ready
    public void ActivateSkill()
    {
        if (Busy)
        {
            Prompt p = mPrompts[0];
            if (!p.skill.CanPay(p.owner, p.card))
            {
                mNotice("Skill", p.owner.playerName + " can't pay " + p.skill.CostText + " for " + p.card.data.DisplayName + ". Skip it instead.");
                return;
            }
            mPrompts.RemoveAt(0);
            Use(p.owner, p.card, p.skill);
            return;
        }

        List<(CardInstance card, Skill skill)> ready = ReadyActs();
        if (ready.Count == 1) Use(gs.CurrentPlayer, ready[0].card, ready[0].skill);
        else mNotice("Skill", ready.Count == 0
            ? "No skill can be activated right now."
            : "More than one skill is ready: press its button, or say its circle, e.g. \"activate vanguard\".");
    }

    public void SkipSkill()
    {
        if (!Busy) return;
        Prompt p = mPrompts[0];
        if (p.skill.Mandatory)
        {
            mNotice("Skill", p.card.data.DisplayName + "'s [AUTO] has no cost and doesn't say \"may\", so it has to be used. Activate it.");
            return;
        }
        mPrompts.RemoveAt(0);
        MatchLogger.Action(p.owner.playerName + ": skipped " + p.card.data.DisplayName + "'s [AUTO]");
        Tell(p.card.data.DisplayName + " [AUTO]", p.owner.playerName + " skipped it: " + p.skill.Text);
        if (Busy) Ask();
    }

    // Puts the skill being asked about after the others, so the turn player picks the order
    public void Later()
    {
        if (!Busy || mPrompts.Count < 2) return;
        Prompt p = mPrompts[0];
        mPrompts.RemoveAt(0);
        mPrompts.Add(p);
        Ask();
    }

    // [ACT] skills this PC's turn player could use now, for ActionButtons
    public List<(CardInstance card, Skill skill)> ReadyActs()
    {
        var ready = new List<(CardInstance, Skill)>();
        PlayerState cp = gs.CurrentPlayer;
        if (Off || Resolving || cp == null || !MatchSession.IsLocal(cp)) return ready;

        foreach (CardInstance unit in Units(cp))
            foreach (Skill skill in SkillParser.Parse(unit.data))
                if (skill.UsableNow(gs, unit, SkillTiming.None) && !UsedUp(cp, unit, skill) && GateOpen(cp, skill) && PrisonReady(cp, skill) && skill.CanPay(cp, unit))
                    ready.Add((unit, skill));
        return ready;
    }

    // "activate vanguard" and the like, or that skill's button
    public void ActivateOn(string zone)
    {
        foreach ((CardInstance card, Skill skill) in ReadyActs())
        {
            if (card.zone != zone) continue;
            Use(gs.CurrentPlayer, card, skill);
            return;
        }
        mNotice("Skill", "No [ACT] skill is ready on that circle.");
    }

    // Says what it costs and does, then runs its steps
    void Use(PlayerState owner, CardInstance card, Skill skill)
    {
        mUsed.TryGetValue((card, skill), out int used);
        mUsed[(card, skill)] = used + 1;
        if (skill.PerGame) mUsedThisGame.Add((owner, GameKey(skill)));

        // What to do with the real cards for the costs paid now; a choice of costs says it once picked
        var todo = skill.Costs.Where(c => !(c is ChoiceCost)).Select(c => c.Instruction(card)).Where(s => s != null).ToList();
        string paid = skill.Costs.Count == 0 ? ""
            : " Cost: " + skill.CostText + (todo.Count > 0 ? " (" + string.Join(", ", todo) + ")" : "") + ".";

        MatchLogger.Action(owner.playerName + ": activated " + card.data.DisplayName + "'s " + skill.Marker + " (" + skill.CostText + ")");
        // Stays up until the costs are paid and the effect is done
        SkillFrame frame = new SkillFrame(this, owner, card, skill);
        Tell(card.data.DisplayName + " " + skill.Marker, owner.playerName + " activated it." + paid + " Effect: " + skill.Effect, () => mFrame == frame);

        mFrame = frame;
        Pump();
    }

    /* Runs the skill's steps until one waits (for the camera, a count, or the player) or they're all
     done; then the next [AUTO] in the queue is asked about */
    void Pump()
    {
        if (mPumping) return;
        mPumping = true;
        try { RunSteps(); }
        finally { mPumping = false; }
        if (Busy) Ask();
    }

    void RunSteps()
    {
        while (mFrame != null)
        {
            Step step = mFrame.Current;
            if (step == null)
            {
                MatchLogger.Action(mFrame.Owner.playerName + ": " + mFrame.Card.data.DisplayName + "'s " + mFrame.Skill.Marker + " resolved");
                mLastFrame = mFrame;
                mLastFrameEnded = UnityEngine.Time.time;
                mFrame = null;
                break;
            }

            bool begun = mFrame.Begun;
            mFrame.Begun = true;
            bool done = begun ? step.Recheck(mFrame) : step.Begin(mFrame);
            if (!done)
            {
                if (!begun)
                {
                    SkillFrame f = mFrame;
                    int cursor = f.Cursor;
                    StepNotice(f, step.Instruction(f) + ".", () => mFrame == f && f.Cursor == cursor); // until this step is done
                }
                return;
            }
            mFrame.Next();
        }
    }

    // "skill done": the step waiting is done, e.g. the rest of an effect done by hand
    public void Done()
    {
        if (mFrame?.Current == null) return;
        mFrame.Current.Confirm(mFrame);
        mFrame.Next();
        Pump();
    }

    // "skip step": the step waiting isn't done, e.g. a "you may" the player doesn't want, or a condition that isn't met
    public void SkipStep()
    {
        if (mFrame?.Current == null) return;
        if (mFrame.Current is OpponentStep)
        {
            StepNotice(mFrame, "this part is " + OpponentStep.Opponent(mFrame).playerName + "'s to do, so it can't be skipped.");
            return;
        }
        MatchLogger.Action(mFrame.Owner.playerName + ": skipped a step of " + mFrame.Card.data.DisplayName + "'s " + mFrame.Skill.Marker);
        if (mFrame.Current is ConditionStep) Stop(mFrame, "its condition isn't met, so it does nothing more.");
        mFrame.Next();
        Pump();
    }

    // A condition not met (ConditionStep): the skill ends without its other steps
    internal void Stop(SkillFrame f, string why)
    {
        f.Stopped = true;
        StepNotice(f, why);
    }

    // For ConditionStep's "このターンにあなたがペルソナライドしているなら"
    internal bool HasPersonaRode(PlayerState player) => mPersonaRode == player;

    /* A step gives a unit power and critical (PowerStep): RulesEngine adds them, battle-only ones to be
     taken off when the battle ends (card, power, critical, battle only) */
    public System.Action<CardInstance, int, int, bool> AddBonus;

    // A step makes the attacked unit unhittable this battle (CantBeHitStep): RulesEngine's perfect guard, for its owner
    public System.Action<PlayerState> PerfectGuard;

    // "pay first" / "pay second": which option of a choice of costs
    public void Choose(int option)
    {
        ChoiceCost choice = ChoiceDue;
        if (choice == null || option < 0 || option >= choice.Options.Count) return;
        if (!choice.Options[option].CanPay(mFrame.Owner, mFrame.Card))
        {
            mNotice("Skill", mFrame.Owner.playerName + " can't pay " + choice.Options[option] + ".");
            return;
        }
        mFrame.Choice = option;
        Pump();
    }

    // Next Phase or turn end with a skill still resolving: its steps left count as done
    public void FinishSteps()
    {
        mPumping = true;
        try
        {
            while (mFrame?.Current != null)
            {
                if (mFrame.Begun) mFrame.Current.Confirm(mFrame);
                else if (!mFrame.Current.Begin(mFrame)) mFrame.Current.Confirm(mFrame);
                mFrame.Next();
            }
        }
        finally { mPumping = false; }
        Pump();
    }

    /* After every count change: a waiting step may be done now, e.g. a draw once the deck goes down
     (the Draw button, "draw card", HandZones seeing it, or Deck -) */
    public void Recheck()
    {
        if (mFrame != null && mFrame.Begun) Pump();
    }

    /* A card put on its owner's trigger zone while a Reveal step waits: that's it being shown, not a
     drive or damage check. On another zone only the card asked for counts, e.g. a set order shown on
     the order zone, not played. True when it's taken as the reveal. */
    public bool TakeShown(CardInstance card)
    {
        if (!(mFrame?.Current is RevealStep reveal) || card.zoneOwner != mFrame.Owner) return false;
        if (card.zone != "Trigger" && !reveal.Filter.Matches(card.data)) return false;
        if (reveal.Offer(mFrame, card))
        {
            mFrame.Next();
            Pump();
        }
        return true;
    }

    // A cost just paid by a CostStep: what to watch for afterwards
    internal void NotePaid(SkillFrame f, Cost cost)
    {
        if (cost is SoulBlastCost)
        {
            mSoulBlastUnseen.TryGetValue(f.Owner, out int unseen);
            mSoulBlastUnseen[f.Owner] = unseen + cost.Amount;
        }
        if (cost is RestCost) mRestDue = f.Card;
        if (cost is DiscardCost)
        {
            // After any discards still to come
            int from = mDiscardsDue.Where(d => d.owner == f.Owner).Select(d => d.drop).Append(f.Owner.CountInZone("Drop")).Max();
            mDiscardsDue.Add((f.Owner, from + cost.Amount));
        }
        else foreach (string zone in cost.Zones(f.Card)) mPaidZones.Add((f.Owner, zone, UnityEngine.Time.time + PaidZoneSeconds));
        (string from, string to)? arrow = cost.Arrow;
        if (arrow != null) Moves?.Expect(f.Owner, arrow.Value.from, arrow.Value.to, null);
        if (f.Skill.Costs.Contains(cost)) return; // said when it was activated

        string todo = cost.Instruction(f.Card); // an option picked from a choice
        StepNotice(f, "Paid " + cost + (todo != null ? ": " + todo : "") + ".");
    }

    /* Where the owner moves cards now: the waiting step's zones, a cost's for a few seconds after paying
     it, and the drop zone until a discard paid is in it */
    public List<(PlayerState owner, string zone)> AttentionZones()
    {
        mPaidZones.RemoveAll(z => z.until < UnityEngine.Time.time);
        mDiscardsDue.RemoveAll(d => d.owner.CountInZone("Drop") >= d.drop);
        var zones = mPaidZones.Select(z => (z.owner, z.zone)).ToList();
        zones.AddRange(mDiscardsDue.Select(d => (d.owner, "Drop")));
        if (mFrame != null && mFrame.Begun && mFrame.Current != null)
            zones.AddRange(mFrame.Current.Zones(mFrame).Select(zone => (mFrame.Owner, zone)));
        return zones;
    }

    // A step's message that the opponent needs too: online it goes to their PC as well
    internal void Announce(SkillFrame f, string body) =>
        Tell(f.Card.data.DisplayName + " " + f.Skill.Marker, f.Owner.playerName + ": " + body);

    // A ChooseOpponentStep waiting on this PC: the opponent's circles to pick from (OpponentUnitPicker)
    public ChooseOpponentStep OpponentChoiceDue => mFrame != null && mFrame.Begun && MatchSession.IsLocal(mFrame.Owner) ? mFrame.Current as ChooseOpponentStep : null;
    public PlayerState ChoiceOpponent => mFrame != null ? OpponentStep.Opponent(mFrame) : null;
    public List<string> OpponentChoices() => OpponentChoiceDue?.Choices(mFrame) ?? new List<string>();

    public void ChooseOpponent(string zone)
    {
        ChooseOpponentStep step = OpponentChoiceDue;
        if (step == null || !step.Choose(mFrame, zone)) return;
        mFrame.Next();
        Pump();
    }

    // A step's message, under the skill's card name; with stillToDo, it stays up until that's false
    internal void StepNotice(SkillFrame f, string body, System.Func<bool> stillToDo = null)
    {
        string title = f.Card.data.DisplayName + " " + f.Skill.Marker;
        if (stillToDo != null) mToDo(title, f.Owner.playerName + ": " + body, stillToDo);
        else mNotice(title, f.Owner.playerName + ": " + body);
    }

    // Shown here, and online sent on for the other PC (OnlineMatch)
    void Tell(string title, string body, System.Func<bool> stillToDo = null)
    {
        if (stillToDo != null) mToDo(title, body, stillToDo);
        else mNotice(title, body);
        LastTitle = title;
        LastBody = body;
        SentCount++;
    }

    // Online: the opponent used or skipped a skill on their PC
    public void ApplyRemote(string title, string body)
    {
        if (title != null) mNotice(title, body + " (on their PC)");
    }

    bool UsedUp(PlayerState owner, CardInstance card, Skill skill) =>
        (skill.PerGame && mUsedThisGame.Contains((owner, GameKey(skill)))) ||
        (skill.PerTurn > 0 && mUsed.TryGetValue((card, skill), out int used) && used >= skill.PerTurn);

    // A Divine or Unique Skill used uses up all of that kind; any other once-a-game skill only itself
    static object GameKey(Skill skill) => skill.GameGroup ?? (object)skill;

    // A keyword's condition, e.g. Limit Break (4), checked as it goes off or is offered (14.2.3)
    bool GateOpen(PlayerState owner, Skill skill) => skill.Gate == null || skill.Gate.Open(owner, mPersonaRode == owner);

    public enum Move { Ride, Call, Order, Guard }

    // Words in a skill's effect, English or Japanese, that say it does the move
    static readonly Dictionary<Move, string[]> MoveWords = new Dictionary<Move, string[]>
    {
        [Move.Ride] = new[] { "ride", "ライド" },
        [Move.Call] = new[] { "call", "コール", "登場" },
        [Move.Order] = new[] { "order", "オーダー" },
        [Move.Guard] = new[] { "guardian", "(gc)", "ガーディアン", "(g)" },
    };

    /* The skill that lets one of owner's cards make this move against the usual rules, as "Card's
     [ACT]": one resolving now, or resolved in the last few seconds, whose effect does that move.
     Null if none, and the rule break is a mistake */
    public string Allowing(PlayerState owner, Move move)
    {
        SkillFrame f = mFrame ?? (UnityEngine.Time.time - mLastFrameEnded < AllowSeconds ? mLastFrame : null);
        if (f == null || f.Owner != owner) return null;
        string effect = (f.Skill.Effect ?? f.Skill.Text ?? "").ToLowerInvariant();
        return MoveWords[move].Any(effect.Contains) ? f.Card.data.DisplayName + "'s " + f.Skill.Marker : null;
    }

    // "...stores it in your prison", "if your opponent has cards in your prison": only with a Prison in its owner's order zone
    static bool PrisonReady(PlayerState owner, Skill skill)
    {
        string text = skill.Text ?? "";
        bool needsPrison = text.IndexOf("your prison", System.StringComparison.OrdinalIgnoreCase) >= 0 || text.Contains("あなたの監獄");
        return !needsPrison || Prison.Has(owner);
    }

    /* The camera reading one card twice (a second target of it, or it flickering out and back) isn't it
     happening again: not while the same card's skill is still waiting, and once a turn for timings
     that can't come twice, e.g. a ride or an order played */
    bool Repeat(PlayerState owner, CardInstance card, Skill skill, SkillTiming timing)
    {
        string number = card.data.cardNumber;
        if (mPrompts.Any(p => p.owner == owner && p.card.data.cardNumber == number && p.skill.Text == skill.Text)) return true;
        bool onceATurn = timing == SkillTiming.PlacedOnVC || timing == SkillTiming.RiddenUpon || timing == SkillTiming.PlacedInOrderZone;
        return onceATurn && !mFired.Add((owner, number, skill.Text, timing));
    }

    // RulesEngine saw a Persona Ride
    public void PersonaRode(PlayerState player) => mPersonaRode = player;

    /* A card the camera saw go from the soul to the drop zone while a Soul Blast is counted: the
     seen card takes over from the count, as the camera's own zones now hold it */
    public void SeenInDrop(CardInstance card, string fromZone)
    {
        PlayerState owner = card.zoneOwner;
        if (fromZone != "Soul" || owner == null || !mSoulBlastUnseen.TryGetValue(owner, out int unseen) || unseen <= 0) return;
        mSoulBlastUnseen[owner] = unseen - 1;
        gs.AdjustCount(owner, "Soul", 1);
        gs.AdjustCount(owner, "Drop", -1);
    }

    // An order just played: its cost to play it first, e.g. resting a unit, then its skills
    public void PayToPlay(CardInstance card)
    {
        PlayerState owner = card.zoneOwner;
        ExtraSkill cost = Off ? null : SkillParser.PlayCost(card.data);
        if (cost == null || owner == null || !MatchSession.IsLocal(owner) || mFrame != null) return;

        SkillFrame frame = new SkillFrame(this, owner, card, cost);
        Tell(card.data.DisplayName + " " + cost.Marker, owner.playerName + " played it. Pay its cost: " + cost.Text + ".", () => mFrame == frame);
        mFrame = frame;
        Pump();
    }

    // An Over trigger revealed in a drive check: its additional effect, run like a skill
    public void OverTriggerEffect(CardInstance card, PlayerState owner)
    {
        ExtraSkill effect = Off ? null : SkillParser.OverTriggerEffect(card.data);
        if (effect == null || owner == null || !MatchSession.IsLocal(owner)) return;
        if (mFrame != null)
        {
            mNotice(card.data.DisplayName + " " + effect.Marker, owner.playerName + ": additional effect, do it by hand: " + effect.Text);
            return;
        }

        SkillFrame frame = new SkillFrame(this, owner, card, effect);
        Tell(card.data.DisplayName + " " + effect.Marker, owner.playerName + ": additional effect: " + effect.Text, () => mFrame == frame);
        mFrame = frame;
        Pump();
    }

    // A step waiting for the opponent (OpponentStep), as they're told; online OnlineMatch sends it to their PC for its Done
    public string OpponentStepWaiting => mFrame != null && mFrame.Begun && mFrame.Current is OpponentStep
        ? mFrame.Card.data.DisplayName + " " + mFrame.Skill.Marker + ": " + mFrame.Current.Instruction(mFrame) : null;

    // The opponent's Done, from their PC (VoiceCommands.HeardRemote)
    public void OpponentDone()
    {
        if (OpponentStepWaiting != null) Done();
    }

    // The [AUTO] being asked about has to be used (Skill.Mandatory): no Skip
    public bool PromptMandatory => Busy && mPrompts[0].skill.Mandatory;

    // A Rest cost's unit resting, or any unit for a "rest one of your units" step: paying the cost, not attacking
    public bool TakeRestDue(CardInstance card)
    {
        if (mFrame != null && mFrame.Begun && mFrame.Current is RestUnitStep rest && card.zoneOwner == mFrame.Owner && Skill.IsUnitCircle(card.zone))
        {
            if (rest.Take(mFrame))
            {
                mFrame.Next();
                Pump();
            }
            else StepNotice(mFrame, "Rested " + card.data.DisplayName + ". " + rest.Instruction(mFrame) + ".");
            return true;
        }
        if (card == null || card != mRestDue) return false;
        mRestDue = null;
        return true;
    }

    public void NewTurn()
    {
        mPrompts.Clear();
        mUsed.Clear();
        mSoulBlastUnseen.Clear();
        mRestDue = null;
        mFrame = null;
        mLastFrame = null;
        mPaidZones.Clear();
        mDiscardsDue.Clear();
        mFired.Clear();
        mPersonaRode = null;
    }

    static IEnumerable<CardInstance> Units(PlayerState player)
    {
        if (player.vanguardCircle != null) yield return player.vanguardCircle;
        foreach (CardInstance rg in player.AllRearguards()) yield return rg;
    }
}

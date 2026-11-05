using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/* Referee for a physical D format game:
 - Players move real cards; the tracker reports each move through GameState
 - Checks each move against the current phase and warns about illegal plays
 - Keeps score of battles, triggers, damage and Counter Blast
 - Warnings never undo anything, as card effects can legally break the usual timing
 - Phases advance through AdvancePhase: Space, the HUD's Next Phase button, or "next phase" (VoiceCommands)
 - Guard, drive check and damage check start when announced by voice (Declare); the cards
   that follow are checked against the zones they should go to */
public class RulesEngine : MonoBehaviour
{
    [Tooltip("Ends the current phase. Gestures should call AdvancePhase() as well.")]
    public Key advancePhaseKey = Key.Space;
    [Tooltip("Seconds the End Phase notice stays up before the next player's Stand Phase starts.")]
    public float endPhaseSeconds = 2f;
    [Tooltip("Each player's face-down deck when the game starts: 54 cards minus the 4-card ride deck and the 5-card opening hand.")]
    public int startingDeckCount = 45;
    [Tooltip("Cards left in each ride deck once the grade 0 starting vanguard is out: grades 1 to 3.")]
    public int startingRideDeckCount = 3;
    [Tooltip("Seconds a finished Stand, Draw or Ride Phase stays up before the next phase starts by itself.")]
    public float autoAdvanceSeconds = 1.5f;
    [Tooltip("The same once the Draw Phase's card is drawn.")]
    public float drawAdvanceSeconds = 0.5f;
    [Tooltip("Strict: Next Phase is refused until the Stand Phase's units are standing, the Draw Phase's card is drawn and the Ride Phase's ride is done. Off: Next Phase goes on anyway (the camera may have it wrong).")]
    public bool strictPhases;

    const int TriggerPower = 10000;

    // Energy Generator (GameSettings.EnergyEnabled): 3 energy each Stand Phase from turn 2, 10 at most; Energy Blast 7 draws 1
    public const int MaxEnergy = 10;
    const int EnergyPerTurn = 3;
    public const int EnergyDrawCost = 7;

    GameState gs;
    bool mStarted;
    bool mPassingTurn; // End Phase showing, next turn about to start
    bool mRodeThisTurn;
    bool mStandWarned; // Next Phase was pressed once with units still rested; a second press goes on anyway
    bool mRideCostDue; // rode from the ride deck and the hand card isn't in the drop zone yet
    int mDropBeforeRide;
    int mRideDeckAfterRide;
    int mDeckAtDrawPhase; // the deck count going down in the Draw Phase means the draw happened
    bool mDrewThisTurn;
    int mEnergyCharged; // energy this turn's Ride Phase added, for its notice
    bool mEnergyChargeDue; // this turn's Energy Charge 3 hasn't happened yet
    bool mEnergyDrawn; // the Energy Blast draw is once a turn
    bool mEnergyDrawDue; // Energy Blast paid, its card not yet seen drawn
    int mDeckAtEnergyDraw; // the deck count going down after the Energy Blast means its draw happened
    bool mRideLegal; // this turn's ride broke no rule, so the Ride Phase can end by itself
    bool mRideNeedsFix; // this turn's ride broke a rule; the next card put on the vanguard circle is checked as its fix
    bool mRideDeckCharged; // this turn's ride already took a card off the ride deck
    bool mRideDeckTouched; // a hand was seen taking a card off the ride deck this turn (HandZones)
    bool mRideDiscardNoted; // the discard for riding from the ride deck was seen before the ride, and said so
    string mRodeNumber; // card number of this turn's ride
    bool mPersonaDrawDue; // Persona Ride's draw, its card not yet seen drawn
    int mDeckAtPersonaDraw; // the deck count going down after the Persona Ride means its draw happened
    float mGuardedAt = -100f; // when the latest guardian was placed, for its arrow (GuardArrow)
    PlayerState mPersonaFrontRow; // Persona Rode this turn: their front row units get +10000 (PersonaBonus)
    const int PersonaRidePower = 10000;
    CardInstance mDamageCheckCard; // the latest damage check card, highlighted on its way to the Damage zone
    readonly List<(CardInstance card, int power, int critical)> mBattleBonuses = new List<(CardInstance, int, int)>(); // skills' bonuses for this battle only
    readonly HashSet<(PlayerState, string)> mSetOrders = new HashSet<(PlayerState, string)>(); // set orders played this game, by card number
    readonly HashSet<string> mAttackedFrom = new HashSet<string>(); // circles that have attacked this turn
    readonly Dictionary<string, float> mAttackAgainSaid = new Dictionary<string, float>(); // when "already attacked" was last said, by circle
    const float AttackAgainSeconds = 10f;

    // Cards to move, shown as arrows until they have: a hit rear-guard, guardians, a skill's retire (CardMoves)
    public CardMoves Moves { get; } = new CardMoves();
    public bool ExpectsMove(CardInstance card) => Moves.Expects(card);

    static bool IsSetOrder(CardData data) =>
        data.isOrderCard && ((data.cardType ?? "").Contains("セット") || (data.cardType ?? "").IndexOf("Set", System.StringComparison.OrdinalIgnoreCase) >= 0);
    // Each player's ride deck from the deck builder (CardKeys); empty when not known, e.g. an Inspector test deck without one
    readonly HashSet<string>[] mRideDecks = { new HashSet<string>(), new HashSet<string>() };
    readonly int[] mRideDeckSizes = { -1, -1 }; // cards left after the starting vanguard; -1 uses startingRideDeckCount
    int mGradeBeforeRide; // the vanguard's grade as the Ride Phase began
    CardInstance mVanguardBeforeRide;
    readonly HashSet<CardInstance> mBeenVanguard = new HashSet<CardInstance>(); // every card seen as a vanguard this game
    // Cards that went off the board: where from and when, so one coming back there (soon, or this turn) isn't a new call
    readonly Dictionary<CardInstance, (PlayerState owner, string zone, float time, int turn)> mLeftFrom =
        new Dictionary<CardInstance, (PlayerState owner, string zone, float time, int turn)>();
    const float ReturnSeconds = 20f;
    Coroutine mAutoAdvance;
    CardInstance mAttacker; // unit attacking in the current battle, null outside one
    CardInstance mBooster;
    float mBoostedAt = -100f; // when the boost started, for its arrow (BoostArrow)
    const float BoostArrowSeconds = 3f;
    CardInstance mPendingBooster; // a back-row unit rested before the unit in front of it attacked
    /* Who attacked, from which circle, and whether with the vanguard, as the attack started: the
     camera can lose or swap the attacking card mid-battle, and the battle mustn't lose track */
    PlayerState mAttackerOwner;
    string mAttackerZone;
    bool mAttackByVanguard;

    /* Where the current battle is. Attack: picking the target. Guard: the defender guards or not.
     Drive: the attacking vanguard's drive checks. Damage: the hit is waiting for, or getting,
     its damage checks. Close: nothing left to check. */
    public enum BattleStep { None, Attack, Guard, Drive, Damage, Close }
    public enum Declaration { Guard, NoGuard, DriveCheck, DamageCheck, PerfectGuard }
    BattleStep mStep;
    bool mGuardSaid; // "guard" or "no guard" this battle
    bool mNoGuard;
    readonly HashSet<string> mCantBeHit = new HashSet<string>(); // the defender's circles a perfect guard (Sentinel) protects this battle
    int mDriveLeft; // drive check cards still to come
    int mDamageLeft; // damage check cards still to reach the Damage zone; 0 before "damage check"
    bool mVanguardAttacked; // the turn player's vanguard has attacked this turn
    /* Damage check cards counted as damage when revealed: a damage zone's cards overlap, so the
     camera seldom recognises them there. Seen there later, the card takes over from the count. */
    readonly HashSet<CardInstance> mCountedDamage = new HashSet<CardInstance>();

    /* A Heal trigger's heal, done on the counts when the trigger resolves. The healed card seen
     landing in the drop zone takes over from them; whether it was face down comes from the camera
     if it knew the card, from the damage if it's all one way, or else from the player (ActionButtons). */
    class Heal
    {
        public PlayerState owner;
        public bool dropCounted; // Drop +1 still on the counts, not yet a seen card
        public bool damageCounted; // Damage -1 still on the counts
        public bool faceDownCounted; // FaceDownDamage -1 on the counts: a face-down card was healed
        public bool facingUnknown; // waiting for the player to say which
        public float time; // Time.time of the heal
    }
    Heal mHeal;

    /* A trigger's bonuses waiting for their owner to say where they go: power first, then the
     critical (Critical trigger) or the rear-guard to stand (Stand trigger). The power and critical
     start on the usual unit (the attacker on a drive check, the vanguard on a damage check), so the
     battle is right even if nobody picks; picking another circle moves them there. */
    public enum TriggerPick { None, Power, Critical, Stand }
    class PendingPick
    {
        public PlayerState owner;
        public TriggerIcon icon;
        public int power; // the trigger's power bonus
        public CardInstance powerOn, criticalOn;
        public readonly Queue<TriggerPick> picks = new Queue<TriggerPick>();
    }
    PendingPick mPick;
    string mDecideAfterPick; // the drive check is over but the hit waits for the picks; DecideHit's prefix

    // This PC's latest pick, e.g. "Power:LeftFront", and how many it has made, uploaded by OnlineMatch
    public static string LastPick { get; private set; }
    public static int PickCount { get; private set; }

    public PlayerState PickOwner => mPick?.owner;
    public TriggerPick PickNow => mPick != null && mPick.picks.Count > 0 ? mPick.picks.Peek() : TriggerPick.None;
    public TriggerIcon PickTrigger => mPick != null ? mPick.icon : TriggerIcon.None;

    // The circle the bonus being picked is on now (Power, Critical), or null (Stand)
    public string PickCurrentZone
    {
        get
        {
            switch (PickNow)
            {
                case TriggerPick.Power: return mPick.powerOn?.zone;
                case TriggerPick.Critical: return mPick.criticalOn?.zone;
                default: return null;
            }
        }
    }

    // The owner's circles the current pick can go to: any unit for power and critical, a rested rear-guard to stand
    public List<string> PickZones()
    {
        var zones = new List<string>();
        if (mPick == null) return zones;
        foreach (string zone in UnitCircles)
        {
            CardInstance unit = mPick.owner.CardOnCircle(zone);
            if (unit == null) continue;
            if (PickNow == TriggerPick.Stand && (zone == "Vanguard" || unit.orientation != CardOrientation.Rest)) continue;
            zones.Add(zone);
        }
        return zones;
    }

    static readonly string[] UnitCircles = { "LeftFront", "Vanguard", "RightFront", "LeftBack", "CenterBack", "RightBack" };

    // A heal whose healed card's facing the player has to give (ActionButtons)
    public PlayerState HealFacingAsked => mHeal != null && mHeal.facingUnknown ? mHeal.owner : null;
    TriggerIcon mCheckTrigger; // trigger on the check card just revealed: what it affects may be touched too
    PlayerState mCheckTriggerOwner;

    public bool IsStarted => mStarted;

    // Card skills: [AUTO] prompts, [ACT] skills ready to use, and their costs
    public SkillRules Skills { get; private set; }

    // The attack still needs its target picked (AttackTargetPicker, HandZones)
    public bool IsChoosingTarget => mAttacker != null && gs.attackTarget == null && !gs.gameOver;

    // The current battle, for ActionButtons: its step, who's in it, and what's been said
    public BattleStep Step => mStep;
    public bool GuardSaid => mGuardSaid;
    public int DamageLeft => mDamageLeft;
    public PlayerState AttackingPlayer => mAttacker != null ? mAttackerOwner : null;
    public bool VanguardAttacking => mAttacker != null && mAttackByVanguard;
    public CardInstance Attacker => mAttacker;
    public CardInstance Booster => mAttacker != null ? mBooster : null;

    /* A unit's power as the battle has it: the attacker's includes its booster's, the attacked unit's
     its guardians' shields (CardOverlayUI) */
    public int ShownPower(CardInstance card) =>
        card.TotalPower + (card == mAttacker && mBooster != null ? mBooster.TotalPower : 0) + GuardShield(card);

    // Persona Ride's +10000 on a front-row unit of the player who did it this turn (CardInstance.AreaBonus)
    int PersonaBonus(CardInstance card) =>
        mPersonaFrontRow != null && card.zoneOwner == mPersonaFrontRow && System.Array.IndexOf(FrontRow, card.zone) >= 0 ? PersonaRidePower : 0;

    // The guardians' shields if this unit is the one the current attack targets, else 0
    public int GuardShield(CardInstance card)
    {
        if (mAttacker == null || card == null || gs.attackTarget == null) return 0;
        PlayerState defender = gs.Opponent;
        if (card != defender.CardOnCircle(gs.attackTarget)) return 0;
        return defender.guardianCircle.Where(g => g != null).Sum(g => g.data.shield);
    }

    // For a few seconds after a boost, from the booster's circle to the unit it boosts (ZoneHighlighter)
    public (PlayerState owner, string from, string to)? BoostArrow =>
        mAttacker != null && mBooster != null && mBooster.zone != null && Time.time - mBoostedAt < BoostArrowSeconds
            ? (mAttackerOwner, mBooster.zone, mAttackerZone) : ((PlayerState, string, string)?)null;

    // The same after a guardian is placed, from the Guardian circle to the unit it guards
    public (PlayerState owner, string from, string to)? GuardArrow =>
        mAttacker != null && gs.attackTarget != null && Time.time - mGuardedAt < BoostArrowSeconds && gs.Opponent.guardianCircle.Any(g => g != null)
            ? (gs.Opponent, "Guardian", gs.attackTarget) : ((PlayerState, string, string)?)null;

    // The attack's power meets the target's power plus the guardians' shields, as things stand
    public bool AttackHits
    {
        get
        {
            if (mAttacker == null) return false;
            BattlePowers(out int attack, out int defence);
            return attack >= defence && !TargetCantBeHit;
        }
    }

    // A perfect guard (Sentinel) protects the attack's target this battle
    public bool TargetCantBeHit => mAttacker != null && mCantBeHit.Contains(gs.attackTarget ?? "Vanguard");
    public bool NoGuardSaid => mNoGuard;
    public bool VanguardAttackedThisTurn => mVanguardAttacked;

    /* What the current phase or battle is waiting for, for the HUD, so it isn't lost when a
     newer notice replaces the one that asked for it. Null when nothing is outstanding. */
    public string Waiting
    {
        get
        {
            if (!mStarted || gs.gameOver || mPassingTurn) return null;
            if (Skills.Busy) return Skills.PromptWaiting;
            if (Skills.Stepping) return Skills.StepWaiting;
            PlayerState cp = gs.CurrentPlayer;
            List<CardInstance> overGrade = OverGradeUnits(cp);
            if (overGrade.Count > 0) return "Put " + overGrade[0].data.DisplayName + " back in your hand";
            switch (gs.currentPhase)
            {
                case TurnPhase.StandPhase:
                    int rested = RestedUnits(cp);
                    return rested > 0 ? "Stand " + rested + " rested unit(s)" : null;
                case TurnPhase.DrawPhase:
                    return mDrewThisTurn ? null : "Draw 1 card from your deck";
                case TurnPhase.MainPhase:
                    if (mPersonaDrawDue) return "Persona Ride: draw 1 card from your deck";
                    return mEnergyDrawDue ? "Energy Blast: draw 1 card from your deck" : null;
                case TurnPhase.RidePhase:
                    if (mPersonaDrawDue) return "Persona Ride: draw 1 card from your deck";
                    if (mRideCostDue) return "Discard 1 card to the drop zone (or Drop +; rode from hand? Ride +)";
                    if (!mRodeThisTurn) return strictPhases ? "Ride a unit on your vanguard" : "Ride, or Next Phase to skip";
                    return mRideLegal ? null : "Ride a grade " + RideFixGrade() + " unit instead" + (strictPhases ? "" : " (or Next Phase)");
                case TurnPhase.BattlePhase:
                    if (HealFacingAsked != null) return HealFacingAsked.playerName + ": was the healed damage face up or face down?";
                    if (PickNow != TriggerPick.None) return mPick.owner.playerName + ": hold a finger on the circle to give the " + mPick.icon + " trigger's " + PickName(PickNow) + " to";
                    PlayerState defender = gs.Opponent;
                    switch (mStep)
                    {
                        case BattleStep.Attack: return "Pick the attack target";
                        case BattleStep.Guard:
                            if (!mGuardSaid) return defender.playerName + ": \"guard\" or \"no guard\"";
                            if (mAttackByVanguard) return mAttackerOwner.playerName + ": \"drive check\"";
                            if (TargetCantBeHit) return "Perfect guard: the attack can't hit. Attack with another unit, or Next Phase";
                            BattlePowers(out int attack, out int defence);
                            return attack >= defence
                                ? "Hitting (" + attack + " vs " + defence + "): " + defender.playerName + " can still guard, or \"damage check\""
                                : "Not hitting (" + attack + " vs " + defence + "): attack with another unit, or Next Phase";
                        case BattleStep.Drive: return "Drive check: " + mDriveLeft + " card(s) to the Trigger zone";
                        case BattleStep.Damage: return mDamageLeft > 0 ? "Damage check: " + mDamageLeft + " card(s) to the Trigger zone, then Damage" : defender.playerName + ": \"damage check\"";
                    }
                    return null;
            }
            return null;
        }
    }

    // The defender's circles an attack can target
    public static readonly string[] FrontRow = { "LeftFront", "Vanguard", "RightFront" };

    void OnEnable()
    {
        GameState.OnCardMoved += HandleCardMoved;
        GameState.OnCardOrientationChanged += HandleOrientationChanged;
        GameState.OnCardFacingChanged += HandleFacingChanged;
        GameState.OnGameStateUpdated += HandleStateUpdated;
        CardInstance.AreaBonus = PersonaBonus;
    }

    void OnDisable()
    {
        GameState.OnCardMoved -= HandleCardMoved;
        GameState.OnCardOrientationChanged -= HandleOrientationChanged;
        GameState.OnCardFacingChanged -= HandleFacingChanged;
        GameState.OnGameStateUpdated -= HandleStateUpdated;
        if (CardInstance.AreaBonus == (System.Func<CardInstance, int>)PersonaBonus) CardInstance.AreaBonus = null;
    }

    IEnumerator Start()
    {
        gs = GameState.Instance;
        yield return new WaitUntil(() => gs.players[0] != null && gs.players[1] != null);

        gs.turnNumber = 1;
        gs.currentPlayerIndex = 0;
        gs.isFirstTurn = true;
        foreach (PlayerState player in gs.players)
        {
            if (!KeepsCountFor(player)) continue;
            player.AdjustCount("Deck", startingDeckCount);
            player.AdjustCount("RideDeck", StartingRideDeck(player.playerIndex));
        }
        Skills = new SkillRules(gs, Notice, ToDo);
        DamageSlots.Reset();
        Skills.AddBonus = AddBonus;
        Skills.PerfectGuard = PerfectGuardFromSkill;
        Skills.Moves = Moves;
        mStarted = true;
        EnterPhase(TurnPhase.StandPhase);
        if (!MatchSession.SkillsOn) Notice("Card skills off", "This match plays the basic rules only: no card skills are asked about or paid for.");
    }

    int StartingRideDeck(int playerIndex) => mRideDeckSizes[playerIndex] >= 0 ? mRideDeckSizes[playerIndex] : startingRideDeckCount;

    /* A player's deck, from DeckTargetLoader. Its ride deck decides which rides come from the ride
     deck, and how many cards that starts with (all but the grade 0 starting vanguard). The decks can
     arrive after Start, so the count is put right then too, if nobody has ridden yet. */
    public void SetDeck(int playerIndex, Deck deck)
    {
        if (deck == null || playerIndex < 0 || playerIndex >= mRideDecks.Length) return;
        List<DeckEntry> rideDeck = (deck.rideDeck ?? new List<DeckEntry>()).Where(e => !string.IsNullOrWhiteSpace(e.cardNumber)).ToList();
        if (rideDeck.Count == 0) return; // keeps the old guess: any ride up a grade is from the ride deck

        mRideDecks[playerIndex].Clear();
        foreach (DeckEntry entry in rideDeck) mRideDecks[playerIndex].Add(CardFirestoreDatabase.CardKey(entry.cardNumber));

        int before = StartingRideDeck(playerIndex);
        mRideDeckSizes[playerIndex] = Mathf.Max(0, rideDeck.Sum(e => e.count) - 1);
        PlayerState player = gs != null ? gs.players[playerIndex] : null;
        if (mStarted && player != null && KeepsCountFor(player) && gs.turnNumber == 1 && !mRodeThisTurn)
            player.AdjustCount("RideDeck", mRideDeckSizes[playerIndex] - before);
        MatchLogger.Action((player?.playerName ?? "Player " + (playerIndex + 1)) + "'s ride deck: " + string.Join(", ", mRideDecks[playerIndex]));
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[advancePhaseKey].wasPressedThisFrame)
            AdvancePhase();
        if (keyboard != null && keyboard.ctrlKey.isPressed && keyboard[undoKey].wasPressedThisFrame)
            Undo();

        if (Time.time >= mNextUndoCheck)
        {
            mNextUndoCheck = Time.time + UndoCheckSeconds;
            TrackUndo();
        }
    }

    // Ends the current phase; ending the Battle Phase runs the End Phase and passes the turn
    public void AdvancePhase()
    {
        if (!mStarted || gs.gameOver || mPassingTurn) return;

        if (!MatchSession.IsLocal(gs.CurrentPlayer))
        {
            Notice("Not your turn", gs.CurrentPlayer.playerName + " ends their own phases.");
            return;
        }
        // An effect is resolved before the game moves on
        if (Skills.Busy)
        {
            Notice("Skill", Skills.PromptWaiting + " first.");
            return;
        }
        // A unit called over the vanguard's grade by mistake goes back to the hand first
        PlayerState turnPlayer = gs.CurrentPlayer;
        List<CardInstance> overGrade = OverGradeUnits(turnPlayer);
        if (overGrade.Count > 0)
        {
            ToDo("Rule check", turnPlayer.playerName + ": put " + string.Join(", ", overGrade.Select(c => c.data.DisplayName)) +
                " back in your hand before moving on: a unit can't be called over your vanguard's grade.", () => OverGradeUnits(turnPlayer).Count > 0);
            return;
        }
        MatchLogger.Action(gs.CurrentPlayer.playerName + ": Next Phase in the " + PhaseName(gs.currentPhase));

        if (gs.currentPhase == TurnPhase.StandPhase)
        {
            int rested = RestedUnits(gs.CurrentPlayer);
            if (rested > 0 && strictPhases)
            {
                ToDo("Stand Phase", gs.CurrentPlayer.playerName + ": stand your " + rested + " rested unit(s) before moving on.", () => gs.currentPhase == TurnPhase.StandPhase && !AllStanding());
                return;
            }
            if (rested > 0 && !mStandWarned)
            {
                mStandWarned = true;
                Notice("Stand Phase", gs.CurrentPlayer.playerName + ": " + rested + " unit(s) still rested. Stand them, or press Next Phase again if the camera has it wrong.");
                return;
            }
        }

        // The cost has to be paid; Drop + counts as paying it, Ride Deck + as having ridden from hand instead
        if (gs.currentPhase == TurnPhase.RidePhase && mRideCostDue)
        {
            ToDo("Ride Deck cost", gs.CurrentPlayer.playerName + ": discard 1 card from your hand to the drop zone before moving on. Rode from your hand instead? Press + on Ride.", () => mRideCostDue);
            return;
        }

        if (strictPhases && gs.currentPhase == TurnPhase.DrawPhase && !mDrewThisTurn)
        {
            ToDo("Draw Phase", gs.CurrentPlayer.playerName + ": draw 1 card before moving on (the camera missed it? the Draw button).", () => gs.currentPhase == TurnPhase.DrawPhase && !mDrewThisTurn);
            return;
        }
        if (strictPhases && gs.currentPhase == TurnPhase.RidePhase && !RideDone())
        {
            ToDo("Ride Phase", gs.CurrentPlayer.playerName + ": " + (mPersonaDrawDue ? "draw 1 card for the Persona Ride" : mRodeThisTurn ? "ride a grade " + RideFixGrade() + " unit instead" : "ride a unit on your vanguard") + " before moving on.",
                () => gs.currentPhase == TurnPhase.RidePhase && !RideDone());
            return;
        }

        // Ending the Draw Phase without the draw being seen counts it now
        if (gs.currentPhase == TurnPhase.DrawPhase && !mDrewThisTurn)
        {
            mDrewThisTurn = true;
            gs.AdjustCount(gs.CurrentPlayer, "Deck", -1);
        }

        // The same for the Energy Blast's draw when the Main Phase ends
        if (gs.currentPhase == TurnPhase.MainPhase && mEnergyDrawDue)
        {
            mEnergyDrawDue = false;
            gs.AdjustCount(gs.CurrentPlayer, "Deck", -1);
        }

        // And a skill's steps, e.g. its draw, in any phase
        Skills.FinishSteps();

        // The first player can't attack on turn 1, so their Main Phase goes straight to the End Phase
        if (gs.currentPhase == TurnPhase.MainPhase && gs.isFirstTurn)
        {
            Notice("Battle Phase skipped", "The first player can't attack on their first turn.");
            StartCoroutine(EndTurn());
            return;
        }

        if (gs.currentPhase >= TurnPhase.BattlePhase) StartCoroutine(EndTurn());
        else EnterPhase(gs.currentPhase + 1);
    }

    /* Stand, Draw and Ride end by themselves once they're done: after autoAdvanceSeconds, if the
     game is still in that phase and stillDone holds. Only the turn player's PC does it; online,
     the other PC follows through OnlineMatch. */
    void ScheduleAdvance(System.Func<bool> stillDone, float seconds = -1f)
    {
        if (!MatchSession.IsLocal(gs.CurrentPlayer)) return;
        if (gs.currentPhase > TurnPhase.RidePhase) return; // Main and Battle only end when the player says so
        if (mAutoAdvance != null) StopCoroutine(mAutoAdvance);
        mAutoAdvance = StartCoroutine(AutoAdvance(gs.turnNumber, gs.currentPhase, stillDone, seconds < 0f ? autoAdvanceSeconds : seconds));
    }

    IEnumerator AutoAdvance(int turn, TurnPhase phase, System.Func<bool> stillDone, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        while (Skills.Resolving) // a skill to answer or its draw to come first, then the usual pause
        {
            yield return new WaitWhile(() => Skills.Resolving);
            yield return new WaitForSeconds(seconds);
        }
        mAutoAdvance = null;
        if (gs.gameOver || mPassingTurn || gs.turnNumber != turn || gs.currentPhase != phase || !stillDone()) yield break;
        EnterPhase(phase + 1);
    }

    // Shows the End Phase for endPhaseSeconds, then starts the other player's Stand Phase
    IEnumerator EndTurn()
    {
        mPassingTurn = true;
        EnterPhase(TurnPhase.EndPhase);
        Skills.PhaseTiming(SkillTiming.EndOfTurn, gs.CurrentPlayer);
        yield return new WaitWhile(() => Skills.Resolving);
        yield return new WaitForSeconds(endPhaseSeconds);
        mPassingTurn = false;

        if (!gs.gameOver) StartNextTurn();
    }

    /* A solo test skips the opponent's turn: this player again, two turns on, so grade limits and
     energy go up as they would for the first player */
    void StartNextTurn()
    {
        if (MatchSession.IsSolo) StartTurn(gs.currentPlayerIndex, gs.turnNumber + 2, TurnPhase.StandPhase);
        else StartTurn(1 - gs.currentPlayerIndex, gs.turnNumber + 1, TurnPhase.StandPhase);
    }

    void StartTurn(int playerIndex, int turnNumber, TurnPhase phase)
    {
        // "Until end of turn" power and critical bonuses wear off
        gs.ClearTurnModifiers();
        foreach (PlayerState player in gs.players)
            foreach (CardInstance card in player.AllFieldCards())
                GameState.OnCardPowerChanged?.Invoke(card.data.cardName, card.data.power, card.powerModifier);

        gs.currentPlayerIndex = playerIndex;
        gs.turnNumber = turnNumber;
        gs.isFirstTurn = turnNumber == 1;
        mRodeThisTurn = false;
        mStandWarned = false;
        mRideCostDue = false;
        mDrewThisTurn = false;
        mRideLegal = false;
        mRideNeedsFix = false;
        mRideDeckCharged = false;
        mRideDeckTouched = false;
        mRideDiscardNoted = false;
        mRodeNumber = null;
        mDamageCheckCard = null;
        Moves.Clear(false);
        mCantBeHit.Clear();
        mBattleBonuses.Clear(); // the turn's modifiers are cleared all together
        mAttackedFrom.Clear();
        mVanguardAttacked = false;
        mHeal = null;
        mPick = null;
        mDecideAfterPick = null;
        mEnergyDrawn = false;
        mEnergyDrawDue = false;
        mPersonaDrawDue = false;
        mPersonaFrontRow = null;
        mEnergyChargeDue = true;
        Skills.NewTurn();
        EnterPhase(phase);
    }

    /* Energy Generator's Energy Charge 3, as the Ride Phase starts. Once a turn; online only the turn
     player's own PC keeps their count, and it always goes through its Ride Phase. */
    void ChargeEnergy()
    {
        mEnergyCharged = 0;
        if (!mEnergyChargeDue) return;
        mEnergyChargeDue = false;
        PlayerState cp = gs.CurrentPlayer;
        if (!GameSettings.EnergyEnabled || gs.turnNumber < 2 || !KeepsCountFor(cp)) return;

        mEnergyCharged = Mathf.Min(EnergyPerTurn, MaxEnergy - cp.CountInZone("Energy"));
        if (mEnergyCharged > 0) gs.AdjustCount(cp, "Energy", mEnergyCharged);
    }

    // The Energy Blast draw is offered (ActionButtons): this PC's turn, Main Phase, 7 energy, not used yet this turn
    public bool CanEnergyDraw => mStarted && !gs.gameOver && !mPassingTurn && GameSettings.EnergyEnabled &&
        gs.currentPhase == TurnPhase.MainPhase && MatchSession.IsLocal(gs.CurrentPlayer) &&
        !mEnergyDrawn && gs.CurrentPlayer.CountInZone("Energy") >= EnergyDrawCost;

    // While the Energy Blast's card is still to be drawn, HandZones watches the deck as in the Draw Phase
    public bool EnergyDrawDue => mEnergyDrawDue;

    // The same while a skill's draw is still to come on this PC (SkillRules)
    public bool SkillDrawDue => Skills != null && Skills.DrawDue;

    /* Pays Energy Blast 7; the draw itself counts once the deck goes down (CheckDrawn), seen the same
     ways as the Draw Phase's, or when the Main Phase ends without it */
    public void EnergyDraw()
    {
        if (!CanEnergyDraw) return;
        PlayerState cp = gs.CurrentPlayer;
        mEnergyDrawn = true;
        mEnergyDrawDue = true;
        mDeckAtEnergyDraw = cp.CountInZone("Deck");
        MatchLogger.Action(cp.playerName + ": Energy Blast " + EnergyDrawCost + " to draw 1");
        gs.AdjustCount(cp, "Energy", -EnergyDrawCost);
        ToDo("Energy Blast", cp.playerName + " paid " + EnergyDrawCost + " energy: draw 1 card from your deck.", () => mEnergyDrawDue);
    }

    /* Online: the other PC ended a phase or passed the turn (OnlineMatch), so follow it here.
     That player's own counts, e.g. drawing from their deck, were already done on their PC. */
    public void ApplyRemoteTurn(int playerIndex, int turnNumber, TurnPhase phase)
    {
        if (!mStarted || gs.gameOver) return;

        if (playerIndex != gs.currentPlayerIndex || turnNumber != gs.turnNumber)
            StartTurn(playerIndex, turnNumber, phase);
        else if (phase != gs.currentPhase)
            EnterPhase(phase);
    }

    // Online, each PC keeps its own player's zone counts and the opponent's arrive with their board
    static bool KeepsCountFor(PlayerState player) => MatchSession.IsLocal(player);

    void EnterPhase(TurnPhase phase)
    {
        if (mAttacker != null) EndBattle();
        mPendingBooster = null;

        gs.currentPhase = phase;
        if (phase == TurnPhase.DrawPhase) mDeckAtDrawPhase = gs.CurrentPlayer.CountInZone("Deck"); // before the broadcast checks it
        gs.BroadcastState();

        PlayerState cp = gs.CurrentPlayer;
        int vgGrade = VanguardGrade(cp);

        switch (phase)
        {
            case TurnPhase.StandPhase:
                int rested = RestedUnits(cp);
                Notice("Stand Phase", cp.playerName + ": " + (rested > 0 ? "stand your " + rested + " rested unit(s)." : "all your units are standing."));
                if (rested == 0) ScheduleAdvance(AllStanding);
                break;

            case TurnPhase.DrawPhase:
                Notice("Draw Phase", cp.playerName + ": draw 1 card from your deck.");
                break;

            case TurnPhase.RidePhase:
                mDropBeforeRide = cp.CountInZone("Drop"); // the discard may come before or after the ride
                mGradeBeforeRide = vgGrade;
                mVanguardBeforeRide = cp.vanguardCircle;
                int rideTo = Mathf.Min(vgGrade + 1, MaxRideGrade());
                ChargeEnergy();
                string energy = mEnergyCharged > 0 ? "+" + mEnergyCharged + " energy (now " + cp.CountInZone("Energy") + "). " : "";
                string grades = "grade " + (rideTo > vgGrade ? vgGrade + " or " + rideTo : vgGrade.ToString());
                // The camera sees one copy of a card, so another copy laid on the vanguard needs saying
                string persona = PersonaRideVanguard(cp) != null
                    ? " Persona Ride: put another " + cp.vanguardCircle.data.DisplayName + " on it and press Persona Ride (or say \"persona ride\")."
                    : "";
                // An unseen vanguard is still there, e.g. the starting one put down before the camera saw it
                if (RideDiscardDue)
                    ToDo("Ride Phase", energy + cp.playerName + ": to ride from your ride deck, first discard 1 card from your hand to the drop zone. " +
                        "Or ride a " + grades + " unit from your hand.", () => RideDiscardDue);
                else Notice("Ride Phase", energy + cp.playerName + ": you may ride 1 " + grades + " unit." + persona);
                break;

            case TurnPhase.MainPhase:
                gs.orderPlayedThisTurn = 0;
                Notice("Main Phase", cp.playerName + ": call rear-guards (grade " + vgGrade + " or lower), play up to " + gs.orderLimit + " order, move units.");
                break;

            case TurnPhase.BattlePhase:
                /* An attack is seen as a unit turning from standing to rested, so one already rested
                 (or that the camera thinks is) has to be stood first */
                string alreadyRested = string.Join(", ", Units(cp)
                    .Where(c => c.orientation == CardOrientation.Rest && (c.zone == "Vanguard" || c.zone == "LeftFront" || c.zone == "RightFront"))
                    .Select(c => c.data.DisplayName));
                Notice("Battle Phase", gs.isFirstTurn
                    ? "The first player can't attack on their first turn."
                    : cp.playerName + ": rest a front-row unit to attack, and the unit behind it to boost." +
                      (alreadyRested.Length > 0 ? " Already rested, so stand it before attacking: " + alreadyRested + "." : ""));
                break;

            case TurnPhase.EndPhase:
                Notice("End Phase", cp.playerName + "'s turn ends. " + (MatchSession.IsSolo
                    ? "Test mode: no opponent's turn, so your next turn starts."
                    : gs.Opponent.playerName + "'s turn is next."));
                break;
        }

        // "At the beginning of your ... phase" skills, after the phase's own notice
        if (phase == TurnPhase.RidePhase) Skills.PhaseTiming(SkillTiming.StartOfRidePhase, cp);
        else if (phase == TurnPhase.MainPhase) Skills.PhaseTiming(SkillTiming.StartOfMainPhase, cp);
        else if (phase == TurnPhase.BattlePhase && !gs.isFirstTurn) Skills.PhaseTiming(SkillTiming.StartOfBattlePhase, cp);
    }

    // Card movement

    void HandleCardMoved(CardInstance card, PlayerState fromOwner, string fromZone)
    {
        if (mStarted && !gs.gameOver) Moves.CardMoved(card); // an arrow's card gone where it was going
        if (card.zone == null)
        {
            if (fromZone != null) mLeftFrom[card] = (fromOwner, fromZone, Time.time, gs.turnNumber);
            Skills?.Recheck(); // e.g. a revealed card picked up into the hand (ToHandStep)
            return;
        }
        /* Back where it left from: the camera lost it under a hand, or it was lifted and put back.
         It never left, so it's not a call or a play */
        bool back = mLeftFrom.TryGetValue(card, out var left) && left.owner == card.zoneOwner && left.zone == card.zone &&
            (Time.time - left.time < ReturnSeconds || left.turn == gs.turnNumber);
        mLeftFrom.Remove(card);
        if (back || !mStarted || gs.gameOver) return;
        if (Skills.TakeShown(card)) return; // shown for a skill's reveal, not a check or a play
        if (!CheckTouch(card, fromZone, false)) return;

        PlayerState owner = card.zoneOwner;
        bool isTurnPlayer = owner == gs.CurrentPlayer;

        switch (card.zone)
        {
            case "Vanguard": CheckRide(card, owner, isTurnPlayer, fromOwner == owner && fromZone == "Soul"); break;
            case "LeftFront":
            case "RightFront":
            case "LeftBack":
            case "RightBack":
            case "CenterBack": CheckCall(card, isTurnPlayer, fromOwner == owner && IsRearguardCircle(fromZone)); break;
            case "Order": CheckOrder(card, isTurnPlayer); break;
            case "Guardian": CheckGuard(card, isTurnPlayer, fromZone); break;
            case "Trigger":
                if (fromZone != "Deck" && KeepsCountFor(owner)) owner.AdjustCount("Deck", -1); // drive/damage check off the face-down deck
                bool lastDrive = CheckTriggerCard(card, owner);
                ResolveTrigger(card, owner);
                if (lastDrive)
                {
                    if (mPick != null) mDecideAfterPick = "Drive check done. ";
                    else DecideHit("Drive check done. ");
                }
                break;
            case "Drop":
                CheckHealedCard(card, fromZone);
                Skills.SeenInDrop(card, fromZone);
                Skills.Recheck(); // e.g. a skill's discard (DiscardStep)
                break;
            case "Damage":
                if (fromZone == null && KeepsCountFor(owner)) owner.AdjustCount("Deck", -1); // damage check placed straight from the deck
                CheckDamageCard(card, owner);
                gs.CheckWinCondition();
                RefreshBattle();
                break;
        }
    }

    /* fromSoul: a card that was under the vanguard showing on top again, e.g. the old vanguard
     peeking out from under the new one. The camera swapping them back and forth isn't a ride. */
    void CheckRide(CardInstance card, PlayerState owner, bool isTurnPlayer, bool fromSoul)
    {
        /* A card that's been a vanguard before, back on the circle: the camera losing and finding
         it, or seeing it peek out from under the new one. Not a ride. */
        bool seenBefore = !mBeenVanguard.Add(card);
        if (fromSoul || seenBefore) return;
        // Another target of this turn's ridden card (the camera reading one card as two): not a second ride
        if (mRodeThisTurn && card.data.cardNumber == mRodeNumber) return;

        if (mRideNeedsFix && isTurnPlayer && gs.currentPhase == TurnPhase.RidePhase)
        {
            FixRide(card, owner);
            return;
        }

        /* Nothing seen under it: the first vanguard of the game, or a card placed back on an empty circle.
         In the turn player's Ride Phase, before they've ridden, it's a ride over a vanguard the camera
         never saw, e.g. the face-down starting one, of the grade the Ride Phase began with */
        CardInstance previous = owner.previousVanguard;
        bool overUnseen = previous == null && isTurnPlayer && gs.currentPhase == TurnPhase.RidePhase && !mRodeThisTurn;
        if (previous == null && !overUnseen) return;
        if (overUnseen && card.data.grade <= mGradeBeforeRide) return; // that vanguard itself, seen at last

        if (!isTurnPlayer)
        {
            Breaks(card, SkillRules.Move.Ride, "Only the turn player rides.");
            return;
        }

        bool legal = false;
        if (gs.currentPhase != TurnPhase.RidePhase) Breaks(card, SkillRules.Move.Ride, "Riding normally happens in the Ride Phase.");
        else if (mRodeThisTurn) Breaks(card, SkillRules.Move.Ride, "You've already ridden this turn.");
        else legal = true;
        // Outside the Ride Phase, e.g. the mat set back after Undo, it isn't this turn's ride
        if (gs.currentPhase == TurnPhase.RidePhase) mRodeThisTurn = true;
        mRodeNumber = card.data.cardNumber;

        int grade = previous != null ? previous.data.grade : mGradeBeforeRide;
        int maxGrade = MaxRideGrade();
        bool gradeOk = true;
        if (card.data.grade != grade && card.data.grade != grade + 1)
            gradeOk = Breaks(card, SkillRules.Move.Ride, "Can only ride grade " + grade + " or " + (grade + 1) + " over a grade " + grade + " vanguard.");
        else if (card.data.grade > maxGrade)
            gradeOk = Breaks(card, SkillRules.Move.Ride, "Can't ride grade " + card.data.grade + " yet. Turn " + gs.turnNumber + " allows up to grade " + maxGrade + ".");
        if (legal && gradeOk) mRideLegal = true; // a later ride that breaks a rule doesn't undo an earlier legal one

        // The wrong grade in the Ride Phase: the next card on the vanguard circle is checked again
        if (!mRideLegal && gs.currentPhase == TurnPhase.RidePhase && legal)
        {
            mRideNeedsFix = true;
            ToDo("Wrong ride", card.data.DisplayName + " is grade " + card.data.grade + ". Take it back and ride a grade " + RideFixGrade() + " unit.", () => mRideNeedsFix);
            return;
        }

        ChargeRideDeck(card, owner, grade);

        // A ride that broke a rule waits for Next Phase, so the player can sort it out first
        if (RideDone()) ScheduleAdvance(RideDone);

        if (previous != null) CheckPersonaRide(card, previous);

        /* Ride skills only where rides happen, the Ride Phase or a skill's ride in the Main Phase: in a
         battle, a new card on the vanguard circle is nearly always a check card passing over it */
        if (gs.currentPhase == TurnPhase.RidePhase || gs.currentPhase == TurnPhase.MainPhase)
        {
            Skills.Happened(SkillTiming.RiddenUpon, previous, card);
            Skills.Happened(SkillTiming.PlacedOnVC, card, previous); // "「X」からライドして登場"
        }
    }

    /* After a wrong-grade ride: whatever goes on the vanguard circle next should be one grade up from
     the vanguard the Ride Phase started with (the same grade if this turn can't go higher). Once it
     is, the ride counts and the Ride Phase moves on as for any ride. */
    void FixRide(CardInstance card, PlayerState owner)
    {
        if (card == mVanguardBeforeRide) return; // the old vanguard showing again once the wrong card is lifted off

        int wanted = RideFixGrade();
        if (card.data.grade != wanted)
        {
            Warn(card, "Still the wrong grade: ride a grade " + wanted + " unit over your grade " + mGradeBeforeRide + " vanguard.");
            return;
        }

        mRideNeedsFix = false;
        mRideLegal = true;
        Notice("Ride", card.data.DisplayName + " is the right grade.");
        ChargeRideDeck(card, owner, mGradeBeforeRide);
        if (RideDone()) ScheduleAdvance(RideDone);
        if (mVanguardBeforeRide != null) CheckPersonaRide(card, mVanguardBeforeRide);
        Skills.Happened(SkillTiming.RiddenUpon, mVanguardBeforeRide, card);
        Skills.Happened(SkillTiming.PlacedOnVC, card, mVanguardBeforeRide);
    }

    // The grade this turn's ride should be: one up from the vanguard, unless the turn's limit stops it
    int RideFixGrade() => Mathf.Min(mGradeBeforeRide + 1, Mathf.Max(mGradeBeforeRide, MaxRideGrade()));

    // This turn's Draw Phase draw is counted (HandZones); Undo puts it back
    public bool DrewThisTurn => mDrewThisTurn;

    /* This turn's ride is still to come and the ride deck has cards. Its last card is a grade 3, so
     from a grade 3 vanguard on, rides are from hand and cost nothing, whatever the count says */
    public bool RideDeckRideDue => mStarted && !gs.gameOver && gs.currentPhase == TurnPhase.RidePhase && !mRodeThisTurn &&
        mGradeBeforeRide < 3 && gs.CurrentPlayer.CountInZone("RideDeck") > 0;

    // Riding from the ride deck costs a card from hand, discarded first: until it's in the drop zone, that's lit, not the ride deck
    public bool RideDiscardDue => RideDeckRideDue && gs.CurrentPlayer.CountInZone("Drop") <= mDropBeforeRide;

    // HandZones saw a hand go onto the ride deck and away: this turn's ride is from the ride deck
    public void RideDeckTouched()
    {
        if (!RideDeckRideDue || mRideDeckTouched) return;
        mRideDeckTouched = true;
        MatchLogger.Action(gs.CurrentPlayer.playerName + ": took a card from the ride deck");
        Notice("Ride Deck", gs.CurrentPlayer.playerName + " took a card from the ride deck. Ride it on your vanguard.");
    }

    /* The camera can't see the face-down ride deck, so a Ride Phase ride up a grade counts as from
     the ride deck when a hand was seen taking a card off it (HandZones), or else when the card is in
     the deck builder's ride deck; while it has cards left, once a turn. Any other card is ridden from
     hand. Without a known ride deck, every ride up a grade counts. It costs a card from hand to the
     drop zone. */
    void ChargeRideDeck(CardInstance card, PlayerState owner, int overGrade)
    {
        if (gs.currentPhase != TurnPhase.RidePhase || !KeepsCountFor(owner) || mRideDeckCharged) return;
        if (card.data.grade != overGrade + 1 || overGrade >= 3 || owner.CountInZone("RideDeck") <= 0) return;
        HashSet<string> rideDeck = mRideDecks[owner.playerIndex];
        if (!mRideDeckTouched && rideDeck.Count > 0 && !rideDeck.Contains(CardFirestoreDatabase.CardKey(card.data.cardNumber ?? ""))) return;

        mRideDeckCharged = true;
        owner.AdjustCount("RideDeck", -1);
        mRideDeckAfterRide = owner.CountInZone("RideDeck");
        mRideCostDue = owner.CountInZone("Drop") <= mDropBeforeRide;
        if (mRideCostDue)
            ToDo("Ride Deck cost", owner.playerName + ": rode from the ride deck. Discard 1 card from your hand to the drop zone.", () => mRideCostDue);
    }

    // Persona Ride: same name, both with the icon
    void CheckPersonaRide(CardInstance card, CardInstance over)
    {
        if (card.data.cardName != over.data.cardName || !card.data.hasPersonaRideIcon || !over.data.hasPersonaRideIcon) return;
        PersonaRide(card);
    }

    /* Front row +10000 for the turn, its draw to come, and skills that ask "if you Persona Rode this
     turn". The bonus goes with the circles, so units called or moved to the front row later get it too */
    void PersonaRide(CardInstance card)
    {
        PlayerState owner = card.zoneOwner;
        mPersonaFrontRow = owner;
        Skills.PersonaRode(owner);
        GameState.OnPersonaRide?.Invoke(card.data.DisplayName);
        if (!KeepsCountFor(owner)) return;
        mPersonaDrawDue = true;
        mDeckAtPersonaDraw = owner.CountInZone("Deck");
        ToDo("Persona Ride", owner.playerName + ": draw 1 card from your deck.", () => mPersonaDrawDue);
    }

    // The vanguard a Persona Ride could go on: a grade 3 or higher with the icon
    CardInstance PersonaRideVanguard(PlayerState player)
    {
        CardInstance vanguard = player?.vanguardCircle;
        return vanguard != null && vanguard.data.grade >= 3 && vanguard.data.hasPersonaRideIcon ? vanguard : null;
    }

    // The Persona Ride button is worth showing: Ride Phase, not ridden yet, a vanguard that can be
    public bool CanPersonaRide => mStarted && !gs.gameOver && gs.currentPhase == TurnPhase.RidePhase && !mRodeThisTurn &&
        PersonaRideVanguard(gs.CurrentPlayer) != null;

    public bool PersonaDrawDue => mPersonaDrawDue;

    /* "persona ride" (voice or ActionButtons). Vuforia tracks one copy of a card, so another copy laid
     on the vanguard is the same target in the same place: no move is seen. remote: the opponent's,
     relayed online, whose phase sync may lag, so the phase isn't checked. */
    public void DeclarePersonaRide(PlayerState who, bool remote = false)
    {
        if (!mStarted || gs.gameOver || who == null) return;
        CardInstance vanguard = PersonaRideVanguard(who);
        string wrong = who != gs.CurrentPlayer ? "Only the turn player rides."
            : !remote && gs.currentPhase != TurnPhase.RidePhase ? "Persona Ride happens in the Ride Phase."
            : mRodeThisTurn ? who.playerName + " has already ridden this turn."
            : vanguard == null ? "The vanguard has to be a grade 3 or higher with the Persona Ride icon."
            : null;
        if (wrong != null)
        {
            Notice("Persona Ride", wrong);
            return;
        }

        mRodeThisTurn = true;
        mRideLegal = true;
        mRideNeedsFix = false;
        mRodeNumber = vanguard.data.cardNumber;
        MatchLogger.Action(who.playerName + ": Persona Ride with " + vanguard.data.DisplayName);
        PersonaRide(vanguard);
        if (RideDone()) ScheduleAdvance(RideDone);
        Skills.Happened(SkillTiming.RiddenUpon, vanguard, vanguard);
        Skills.Happened(SkillTiming.PlacedOnVC, vanguard, vanguard);
    }

    void HandleStateUpdated(GameState state)
    {
        if (!mStarted || gs.gameOver) return;
        bool extraDrawDue = mPersonaDrawDue || mEnergyDrawDue; // one card drawn is one draw: Persona Ride's, then the Energy Blast's, first
        CheckDrawn();
        if (!extraDrawDue) Skills.Recheck();
        CheckRideCostPaid();
    }

    // Draw Phase or Energy Blast: the deck count going down (HandZones seeing the draw, the Draw button or pad, Deck -)
    void CheckDrawn()
    {
        if (mPersonaDrawDue && KeepsCountFor(gs.CurrentPlayer) && gs.CurrentPlayer.CountInZone("Deck") < mDeckAtPersonaDraw)
        {
            mPersonaDrawDue = false;
            Notice("Persona Ride", gs.CurrentPlayer.playerName + " drew 1 card.");
            if (gs.currentPhase == TurnPhase.RidePhase && RideDone()) ScheduleAdvance(RideDone);
            return;
        }

        if (mEnergyDrawDue && KeepsCountFor(gs.CurrentPlayer) && gs.CurrentPlayer.CountInZone("Deck") < mDeckAtEnergyDraw)
        {
            mEnergyDrawDue = false;
            Notice("Energy Blast", gs.CurrentPlayer.playerName + " drew 1 card. Carry on with your Main Phase.");
            return;
        }

        if (gs.currentPhase != TurnPhase.DrawPhase || mDrewThisTurn || !KeepsCountFor(gs.CurrentPlayer)) return;
        if (gs.CurrentPlayer.CountInZone("Deck") >= mDeckAtDrawPhase) return;

        mDrewThisTurn = true;
        ScheduleAdvance(() => true, drawAdvanceSeconds); // a draw needs no reading, so on quickly
    }

    bool AllStanding() => RestedUnits(gs.CurrentPlayer) == 0;

    bool RideDone() => mRideLegal && !mRideCostDue && !mPersonaDrawDue;

    /* After every board or count change while the ride deck cost is owed: a new card in the drop
     zone (seen, or Drop +) pays it, and Ride Deck + means the ride was from hand after all */
    void CheckRideCostPaid()
    {
        // Discarded before riding: the ride deck is next
        if (!mRideDiscardNoted && RideDeckRideDue && !RideDiscardDue)
        {
            mRideDiscardNoted = true;
            ToDo("Ride Deck", gs.CurrentPlayer.playerName + " discarded 1 card. Now ride the next card of your ride deck.", () => RideDeckRideDue);
        }
        if (!mRideCostDue) return;

        PlayerState cp = gs.CurrentPlayer;
        if (cp.CountInZone("Drop") > mDropBeforeRide)
        {
            mRideCostDue = false;
            Notice("Ride Deck cost", cp.playerName + " discarded 1 card.");
        }
        else if (cp.CountInZone("RideDeck") > mRideDeckAfterRide)
        {
            mRideCostDue = false;
            Notice("Ride Deck cost", cp.playerName + " rode from hand, so no discard is needed.");
        }
        else return;

        if (RideDone()) ScheduleAdvance(RideDone);
    }

    void CheckCall(CardInstance card, bool isTurnPlayer, bool movedBetweenCircles)
    {
        if (movedBetweenCircles) return; // repositioning a unit already on the field, not a call

        bool call = true;
        if (!isTurnPlayer) call = Breaks(card, SkillRules.Move.Call, "Only the turn player calls rear-guards.");
        else if (gs.currentPhase != TurnPhase.MainPhase) call = Breaks(card, SkillRules.Move.Call, "Rear-guards are normally called in the Main Phase.");

        mAllowedOverGrade.Remove(card);
        PlayerState owner = card.zoneOwner;
        int vgGrade = VanguardGrade(owner);
        if (card.data.grade > vgGrade)
        {
            string rule = "Can't call a grade " + card.data.grade + " unit over a grade " + vgGrade + " vanguard.";
            if (owner.vanguardCircle == null) call &= Breaks(card, SkillRules.Move.Call, rule); // vanguard not seen yet: its grade is a guess, so only a warning
            else if (Skills.Allowing(owner, SkillRules.Move.Call) != null)
            {
                Breaks(card, SkillRules.Move.Call, rule); // notes the skill allowing it
                mAllowedOverGrade.Add(card);
            }
            else
            {
                call = false;
                GameState.OnRulePrompt?.Invoke(card.data.cardName, rule);
                ToDo("Rule check", owner.playerName + ": " + card.data.DisplayName + ": " + rule + " Put it back in your hand.",
                    () => OverGradeUnits(owner).Contains(card));
            }
        }

        /* "When this unit appears on (R)" only for a real call: not a unit the camera finds again out of the
         Main Phase, or a call against the rules that no skill allows */
        if (call) Skills.Happened(SkillTiming.PlacedOnRC, card); // "from hand" can't be seen: a call counts as from hand
    }

    void CheckOrder(CardInstance card, bool isTurnPlayer)
    {
        if (!card.data.isOrderCard)
        {
            // A unit with a Prison there is the opponent's card being imprisoned, not an order played
            PlayerState owner = card.zoneOwner;
            if (Prison.Has(owner))
            {
                MatchLogger.Action(card.data.DisplayName + " imprisoned in " + owner.playerName + "'s Prison");
                Notice("Prison", card.data.DisplayName + " is imprisoned in " + owner.playerName + "'s Prison (" + Prison.Imprisoned(owner) + " card(s) now).");
            }
            else Warn(card, "Only order cards go in the order zone.");
            return;
        }

        /* A set order stays in the order zone: once played, seeing it there again (the camera losing and
         finding it, or reading it as another copy) isn't another play, nor its skill again */
        PlayerState player = card.zoneOwner;
        bool setOrder = IsSetOrder(card.data);
        if (setOrder && mSetOrders.Contains((player, card.data.cardNumber))) return;
        if (Prison.IsPrison(card.data) && player.orderZone.Any(c => c != card && Prison.IsPrison(c.data)))
        {
            Warn(card, "There's already a Prison in the order zone; only one can be there.");
            return;
        }

        // Blitz orders are played by the defender during a battle
        bool blitz = !isTurnPlayer && card.data.isBlitzOrder && gs.currentPhase == TurnPhase.BattlePhase;
        if (!blitz)
        {
            string wrong = !isTurnPlayer ? "Only the turn player plays orders (except Blitz Orders while guarding)."
                : gs.currentPhase != TurnPhase.MainPhase ? "Orders are played in the Main Phase."
                : gs.orderPlayedThisTurn >= gs.orderLimit ? "Only " + gs.orderLimit + " order per turn."
                : null;
            /* Not played (unless a skill allows it), so it neither counts toward the limit nor triggers:
             placed again once it's allowed, it's seen as a new play */
            if (wrong != null && !Breaks(card, SkillRules.Move.Order, wrong)) return;
        }

        if (isTurnPlayer) gs.orderPlayedThisTurn++;
        if (setOrder) mSetOrders.Add((player, card.data.cardNumber));
        Skills.PayToPlay(card); // e.g. a set order's "rest one of your units", before its skills are asked about
        Skills.Happened(SkillTiming.PlacedInOrderZone, card); // e.g. a set order's "when this card is placed in the order zone"
    }

    void CheckGuard(CardInstance card, bool isTurnPlayer, string fromZone)
    {
        if (!isTurnPlayer && mAttacker != null) mGuardedAt = Time.time; // its shield's arrow (GuardArrow)
        if (isTurnPlayer) Breaks(card, SkillRules.Move.Guard, "The attacking player doesn't guard.");
        else if (gs.currentPhase != TurnPhase.BattlePhase) Breaks(card, SkillRules.Move.Guard, "Guardians are only called during a battle.");
        else if (mNoGuard) Breaks(card, SkillRules.Move.Guard, "You said no guard for this attack.");
        else if (mStep == BattleStep.Drive || mStep == BattleStep.Damage) Breaks(card, SkillRules.Move.Guard, "Guardians are called before the drive check.");

        // Intercept: a rear-guard moving from the field to the guardian circle
        if (fromZone == "LeftBack" || fromZone == "RightBack" || fromZone == "CenterBack")
            Warn(card, "Only front-row rear-guards can intercept.");
        else if ((fromZone == "LeftFront" || fromZone == "RightFront") && !card.data.hasIntercept)
            Warn(card, "This unit doesn't have Intercept.");

        if (!isTurnPlayer) Skills.Happened(SkillTiming.PlacedOnGC, card);

        /* A Sentinel whose perfect guard skill wasn't read from its text (no CantBeHitStep): it perfect
         guards as it's placed, and its cost is paid by hand */
        if (MatchSession.SkillsOn && !isTurnPlayer && card.data.hasSentinel && mStep == BattleStep.Guard && !mNoGuard && !TargetCantBeHit &&
            MatchSession.IsLocal(card.zoneOwner) && !SkillParser.Parse(card.data).Any(s => s.Steps.Any(step => step is CantBeHitStep)))
        {
            Notice("Sentinel", card.data.DisplayName + ": pay its perfect guard's cost by hand.");
            PerfectGuardFromSkill(card.zoneOwner);
        }
        RefreshBattle();
    }

    /* A card in a trigger zone is a drive check (turn player) or a damage check (the other
     player). The power/critical goes to the attacker or the checking player's vanguard,
     the usual choice; a different target has to be applied by hand for now. */
    void ResolveTrigger(CardInstance card, PlayerState owner) =>
        ResolveTrigger(card, owner, card.data.triggerIcon, mCountedDamage.Contains(card));

    /* card is null for a check card the camera didn't scan (UnscannedCheck). damageCounted: it's a
     damage check card already counted as damage. */
    void ResolveTrigger(CardInstance card, PlayerState owner, TriggerIcon icon, bool damageCounted)
    {
        if (icon == TriggerIcon.None) return;

        bool isDrive = owner == gs.CurrentPlayer;
        GameState.OnTriggerActivated?.Invoke(icon, isDrive ? "drive" : "damage");
        if (InCheck)
        {
            mCheckTrigger = icon;
            mCheckTriggerOwner = owner;
        }

        CardInstance target = isDrive && mAttacker != null ? mAttacker : owner.vanguardCircle;
        if (icon != TriggerIcon.Front) StartPicks(owner, icon, target);

        switch (icon)
        {
            case TriggerIcon.Critical:
                if (target != null) target.criticalModifier += 1;
                ApplyPower(target, TriggerPower);
                break;

            case TriggerIcon.Front:
                ApplyPower(owner.vanguardCircle, TriggerPower);
                foreach (CardInstance unit in owner.frontRowRearguards.Where(c => c != null))
                    ApplyPower(unit, TriggerPower);
                break;

            case TriggerIcon.Heal:
                ApplyPower(target, TriggerPower);
                PlayerState other = gs.players[1 - owner.playerIndex];
                // A damage check card is already counted (CheckTriggerCard), but heals before it lands
                int ownDamage = owner.CountInZone("Damage") - (damageCounted ? 1 : 0);
                if (ownDamage < other.CountInZone("Damage"))
                {
                    if (card != null) Warn(card, "No heal: your damage is lower than your opponent's.");
                    else Notice("Heal", "No heal: " + owner.playerName + "'s damage is lower than their opponent's.");
                }
                else if (ownDamage == 0)
                    Notice("Heal", "No damage to heal.");
                else
                    HealOne(owner);
                break;

            case TriggerIcon.Over:
                ApplyPower(target, 100000000);
                if (KeepsCountFor(owner)) owner.AdjustCount("Deck", -1); // its draw comes off the deck
                if (isDrive && card != null) Skills.OverTriggerEffect(card, owner); // the card's "追加効果", only from a drive check
                else if (isDrive) Notice("Over trigger", "Its extra effect can't be read from an unscanned card: do it by hand.");
                break;

            case TriggerIcon.Draw:
                ApplyPower(target, TriggerPower);
                if (KeepsCountFor(owner)) owner.AdjustCount("Deck", -1); // its draw comes off the deck
                break;

            default: // Stand
                ApplyPower(target, TriggerPower);
                break;
        }

        RefreshBattle();
    }

    /* Once a check is coming (the guard is settled) and until it's done, only that check's cards
     may move: the attacker's deck to their Trigger zone for a drive check, the defender's deck to
     their Trigger and Damage zones for a damage check, and whatever a trigger just revealed
     affects. Anything else gets a warning (it isn't undone) and isn't otherwise ruled on. */
    bool InCheck =>
        (mStep == BattleStep.Guard && mGuardSaid) || mStep == BattleStep.Drive || mStep == BattleStep.Damage;

    bool CheckTouch(CardInstance card, string fromZone, bool turned)
    {
        if (!InCheck || mAttacker == null || gs.currentPhase != TurnPhase.BattlePhase) return true;
        // Turning only means something for units on circles: check cards often lie sideways in the Trigger zone
        if (turned && card.zone != "Vanguard" && !IsRearguardCircle(card.zone)) return true;

        PlayerState attacker = mAttackerOwner;
        bool drive = VanguardAttacking && mStep != BattleStep.Damage;
        PlayerState checker = drive ? attacker : gs.Opponent;
        PlayerState owner = card.zoneOwner;

        // The check's own cards: off the deck into the Trigger zone, and for damage on to the Damage zone
        if (!turned && owner == checker && fromZone != card.zone &&
            (card.zone == "Trigger" || (!drive && card.zone == "Damage" && (fromZone == null || fromZone == "Deck" || fromZone == "Trigger"))))
            return true;

        if (TriggerAllows(card, fromZone, turned)) return true;

        string allowed = drive
            ? "only " + checker.playerName + "'s deck to their Trigger zone"
            : "only " + checker.playerName + "'s deck to their Trigger zone, then their Damage zone";
        if (mCheckTrigger != TriggerIcon.None) allowed += ", and what the " + mCheckTrigger + " trigger affects";
        Warn(card, "During the " + (drive ? "drive" : "damage") + " check, " + allowed + ".");
        return false;
    }

    // Heals 1 damage on the counts: Damage -1, Drop +1, and face down or not as far as can be told
    void HealOne(PlayerState owner)
    {
        mHeal = new Heal { owner = owner, time = Time.time };
        if (!KeepsCountFor(owner))
        {
            Notice("Heal", owner.playerName + " heals 1 damage.");
            return; // online, their PC does the counts
        }

        int faceUp = owner.FaceUpDamage;
        int faceDown = owner.CountInZone("Damage") - faceUp;

        owner.AdjustCount("Damage", -1);
        owner.AdjustCount("Drop", 1);
        mHeal.damageCounted = mHeal.dropCounted = true;

        if (faceUp == 0) SetHealedFacing(true);
        else if (faceDown > 0) mHeal.facingUnknown = true;

        Heal heal = mHeal;
        ToDo("Heal", owner.playerName + " heals 1 damage: put it in the drop zone." +
            (heal.facingUnknown ? " Was it face up or face down? (battle buttons)" : ""), () => mHeal == heal && heal.facingUnknown);
        gs.BroadcastState();
    }

    // The player's answer, or the only possibility: a face-down healed card also takes 1 off the Counter Blast paid
    public void SetHealedFacing(bool faceDown)
    {
        if (mHeal == null) return;
        mHeal.facingUnknown = false;
        if (faceDown && !mHeal.faceDownCounted)
        {
            mHeal.faceDownCounted = true;
            mHeal.owner.AdjustCount("FaceDownDamage", -1);
        }
        gs.BroadcastState();
    }

    /* A card landing in a drop zone while a heal waits for its card: that's the healed card, and the
     camera's drop zone now holds it, so the counted Drop +1 goes. The Damage -1 stays: damage is
     only ever a count (PlayerState.CountInZone). */
    void CheckHealedCard(CardInstance card, string fromZone)
    {
        if (mHeal == null || card.zoneOwner != mHeal.owner || !KeepsCountFor(mHeal.owner)) return;
        if (fromZone != "Damage" && fromZone != null) return;
        if (fromZone == null && Time.time - mHeal.time > 10f) return; // later, a card from hand is a discard, not the heal

        PlayerState owner = mHeal.owner;
        if (mHeal.dropCounted)
        {
            owner.AdjustCount("Drop", -1);
            mHeal.dropCounted = false;
        }
        if (fromZone == "Damage" || !mHeal.facingUnknown) mHeal = null;
    }

    /* What the trigger just revealed lets its owner touch: the column of the unit getting its power
     (the attacker for a drive check, the vanguard for a damage check), all front-row circles for a
     Front trigger, rear-guards standing for a Stand trigger, damage to the drop zone for a Heal,
     and the Over trigger itself going out of the game. */
    bool TriggerAllows(CardInstance card, string fromZone, bool turned)
    {
        PlayerState owner = card.zoneOwner;
        if (mCheckTrigger == TriggerIcon.None || owner != mCheckTriggerOwner) return false;

        string unit = owner == mAttackerOwner ? mAttackerZone : "Vanguard";
        string zone = card.zone;
        bool inColumn = zone == unit || FrontOf(zone) == unit || (fromZone != null && (fromZone == unit || FrontOf(fromZone) == unit));

        switch (mCheckTrigger)
        {
            case TriggerIcon.Front:
                return zone == "Vanguard" || zone == "LeftFront" || zone == "RightFront" || inColumn;
            case TriggerIcon.Stand:
                return (turned && card.orientation == CardOrientation.Stand && IsRearguardCircle(zone)) || inColumn;
            case TriggerIcon.Heal:
                return (!turned && (fromZone == "Damage" || fromZone == null) && zone == "Drop") || inColumn;
            case TriggerIcon.Over:
                return (!turned && fromZone == "Trigger" && zone == "OOG") || inColumn;
            default:
                return inColumn;
        }
    }

    /* A card turning up in a Trigger zone during a battle: the attacker's should come after "drive
     check", the defender's after "damage check". True when it was the last drive check card. */
    bool CheckTriggerCard(CardInstance card, PlayerState owner)
    {
        mCheckTrigger = TriggerIcon.None; // a new check card: the last one's trigger is done with
        if (mPick != null) FinishPicks("The next check card came, so the last trigger stays where it is.");
        if (mAttacker == null || gs.currentPhase != TurnPhase.BattlePhase) return false; // e.g. a skill's check outside a battle

        if (owner == mAttackerOwner)
        {
            if (mStep != BattleStep.Drive || mDriveLeft <= 0)
            {
                Warn(card, mStep == BattleStep.Drive ? "That's more drive checks than this vanguard gets." : "Say \"drive check\" before revealing drive check cards.");
                return false;
            }
            mDriveLeft--;
            if (mDriveLeft > 0) ToDo("Drive check", mDriveLeft + " more drive check card(s) to reveal into your Trigger zone.", DriveDue);
            return mDriveLeft == 0;
        }

        if (mStep != BattleStep.Damage || mDamageLeft <= 0)
        {
            Warn(card, "Say \"damage check\" before revealing damage check cards.");
            return false;
        }

        // Every damage check card becomes a damage, so it counts now, while it's clearly in view
        DamageSlots.Name(owner, card.data); // which card that damage is, for the HUD's damage bar
        if (KeepsCountFor(owner)) owner.AdjustCount("Damage", 1);
        mCountedDamage.Add(card);
        mDamageCheckCard = card;
        TookDamage(card.data.DisplayName + " counts as 1 damage: put it in your Damage zone.");
        gs.CheckWinCondition();
        return false;
    }

    // Whose check card is due now, drive or damage (the "not scanned" buttons); null when none is
    public PlayerState CheckingPlayer => mStarted && !gs.gameOver ? (DriveDue() ? mAttackerOwner : DamageDue() ? gs.Opponent : null) : null;

    /* A check card the camera didn't scan: its player says whether it was a trigger, and which
     (ActionButtons or the voice keyword, e.g. "critical trigger"). It's ruled on like a scanned
     one, just without its name. */
    public void UnscannedCheck(PlayerState who, TriggerIcon icon)
    {
        if (!mStarted || gs.gameOver || who == null) return;
        if (who != CheckingPlayer)
        {
            Notice("Check card", "No drive or damage check card is due from " + who.playerName + ".");
            return;
        }

        mCheckTrigger = TriggerIcon.None;
        if (mPick != null) FinishPicks("The next check card came, so the last trigger stays where it is.");
        if (KeepsCountFor(who)) who.AdjustCount("Deck", -1); // off the face-down deck, as a scanned one

        string what = icon == TriggerIcon.None ? "no trigger" : "a " + icon + " trigger";
        bool lastDrive = false, damage = who != mAttackerOwner;
        if (!damage)
        {
            mDriveLeft--;
            Notice("Drive check", who.playerName + "'s drive check card (not scanned): " + what + ".");
            if (mDriveLeft > 0) ToDo("Drive check", mDriveLeft + " more drive check card(s) to reveal into your Trigger zone.", DriveDue);
            lastDrive = mDriveLeft == 0;
        }
        else
        {
            DamageSlots.Name(who, null);
            if (KeepsCountFor(who)) who.AdjustCount("Damage", 1);
            TookDamage("Damage check card (not scanned): " + what + ". It counts as 1 damage: put it in your Damage zone.");
            gs.CheckWinCondition();
        }

        ResolveTrigger(null, who, icon, damage);
        if (lastDrive)
        {
            if (mPick != null) mDecideAfterPick = "Drive check done. ";
            else DecideHit("Drive check done. ");
        }
        RefreshBattle();
        gs.BroadcastState();
    }

    void TookDamage(string what)
    {
        mDamageLeft--;
        if (mDamageLeft > 0)
            ToDo("Damage check", what + " " + mDamageLeft + " more damage check card(s) to reveal into your Trigger zone.", DamageDue);
        else
        {
            mStep = BattleStep.Close;
            Notice("Damage check done", what + " " + gs.CurrentPlayer.playerName + ": attack with another unit, or say \"turn end\".");
        }
    }

    // A card reaching a Damage zone during a battle: only the defender's, and only after "damage check"
    void CheckDamageCard(CardInstance card, PlayerState owner)
    {
        // Already counted from the Trigger zone; damage is a count, not the cards seen (PlayerState.CountInZone)
        if (mCountedDamage.Remove(card)) return;

        if (mAttacker == null || gs.currentPhase != TurnPhase.BattlePhase) return;

        if (owner == mAttackerOwner)
        {
            Warn(card, mStep == BattleStep.Drive ? "Drive check cards go in your Trigger zone, then to your hand." : "Damage goes in the defending player's Damage zone.");
            return;
        }
        if (mStep != BattleStep.Damage || mDamageLeft <= 0)
        {
            Warn(card, "Say \"damage check\" before taking damage.");
            return;
        }

        // Put straight in the Damage zone, without passing the Trigger zone
        DamageSlots.Name(owner, card.data);
        if (KeepsCountFor(owner)) owner.AdjustCount("Damage", 1);
        TookDamage("");
        gs.CheckWinCondition();
    }

    /* A battle keyword, said into this PC's microphone or (online) the opponent's arriving through
     OnlineMatch. Starts that step and says what to do; the cards that follow are checked by
     CheckTriggerCard and CheckDamageCard. */
    public void Declare(Declaration what, PlayerState who)
    {
        if (!mStarted || gs.gameOver || who == null) return;
        if (what == Declaration.DriveCheck && !DriveCheckFallback(who)) return;
        if (mAttacker == null || gs.attackTarget == null)
        {
            Notice("Nothing to check", "There's no attack with a target yet.");
            return;
        }

        PlayerState attacker = mAttackerOwner;
        PlayerState defender = gs.Opponent;
        bool vanguardAttack = mAttackByVanguard;

        switch (what)
        {
            case Declaration.Guard:
            case Declaration.NoGuard:
                if (who != defender) Notice("Guard", "Only the defending player, " + defender.playerName + ", guards.");
                else if (mStep != BattleStep.Guard) Notice("Guard", "Too late to guard this attack.");
                else
                {
                    mGuardSaid = true;
                    mNoGuard = what == Declaration.NoGuard;
                    string next = vanguardAttack ? attacker.playerName + " says \"drive check\"." : "If it still hits, " + defender.playerName + " says \"damage check\".";
                    Notice(mNoGuard ? "No guard" : "Guard", mNoGuard
                        ? defender.playerName + " doesn't guard."
                        : defender.playerName + ": call guardians to your Guardian circle. Then " + next);

                    // With no guard nothing can change: a vanguard drive checks now, a rear-guard's attack is decided now
                    if (mNoGuard && vanguardAttack) Declare(Declaration.DriveCheck, attacker);
                    else if (mNoGuard) DecideHit("");
                }
                break;

            // The attacked unit can't be hit this battle: a Sentinel's skill, said or done by CantBeHitStep
            case Declaration.PerfectGuard:
                if (who != defender) Notice("Perfect guard", "Only the defending player, " + defender.playerName + ", guards.");
                else if (mStep != BattleStep.Guard) Notice("Perfect guard", "Too late to guard this attack.");
                else if (mNoGuard) Notice("Perfect guard", defender.playerName + " said no guard for this attack.");
                else if (!defender.guardianCircle.Any(c => c != null && c.data.hasSentinel))
                    Notice("Perfect guard", "Only a Sentinel on " + defender.playerName + "'s Guardian circle perfect guards.");
                else
                {
                    mGuardSaid = true;
                    string zone = gs.attackTarget ?? "Vanguard";
                    mCantBeHit.Add(zone);
                    CardInstance target = defender.CardOnCircle(zone);
                    string name = target != null ? target.data.DisplayName : zone == "Vanguard" ? "The vanguard" : "The attacked unit";
                    Notice("Perfect guard", name + " can't be hit this battle." + (vanguardAttack ? " " + attacker.playerName + " still drive checks; there's no damage check." : ""));
                    RefreshBattle();
                    if (!vanguardAttack) DecideHit(""); // no drive check to wait for: the battle's over, no hit
                }
                break;

            case Declaration.DriveCheck:
                if (who != attacker) Notice("Drive check", "Only the attacking player, " + attacker.playerName + ", drive checks.");
                else if (mStep == BattleStep.Drive) Notice("Drive check", mDriveLeft + " drive check card(s) still to reveal.");
                else if (mStep != BattleStep.Guard) Notice("Drive check", "This attack is past its drive check.");
                else if (!vanguardAttack)
                {
                    Notice("No drive check", "Only a vanguard's attack drive checks.");
                    DecideHit("");
                }
                else
                {
                    if (!mGuardSaid) Notice("Drive check", defender.playerName + " didn't say \"guard\" or \"no guard\"; going on without guarding.");
                    mNoGuard |= !mGuardSaid;
                    mDriveLeft = Mathf.Max(1, mAttacker.data.drive);
                    mStep = BattleStep.Drive;
                    ToDo("Drive check", attacker.playerName + ": reveal " + mDriveLeft + " card(s) from the top of your deck into your Trigger zone, one at a time, then put them in your hand.", DriveDue);
                }
                break;

            case Declaration.DamageCheck:
                if (who != defender) Notice("Damage check", "Only the defending player, " + defender.playerName + ", damage checks.");
                else if (mStep == BattleStep.Guard && vanguardAttack) Notice("Damage check", attacker.playerName + " says \"drive check\" first.");
                else if (mStep == BattleStep.Drive) Notice("Damage check", "Wait for the drive check to finish.");
                else if (mStep == BattleStep.Damage && mDamageLeft > 0) Notice("Damage check", mDamageLeft + " damage check card(s) still to reveal.");
                else
                {
                    if (mStep == BattleStep.Guard) DecideHit(""); // a rear-guard's attack: no drive check to wait for; a hit starts the damage check
                    else if (mStep == BattleStep.Damage && mDamageLeft <= 0) StartDamageCheck("");
                }
                break;
        }
    }

    /* "drive check" (or its button) when the camera missed part of the attack: with no attack seen,
     the turn player's vanguard attacks; with no target picked, it attacks the vanguard. Then the
     drive check goes on as usual (without a guard if none was declared). False when it can't. */
    bool DriveCheckFallback(PlayerState who)
    {
        if (who != gs.CurrentPlayer || gs.currentPhase != TurnPhase.BattlePhase) return true; // Declare says why not

        if (mAttacker == null || mStep == BattleStep.Close)
        {
            CardInstance vanguard = who.vanguardCircle;
            if (vanguard == null)
            {
                Notice("Drive check", "No vanguard seen to attack with.");
                return false;
            }
            if (mVanguardAttacked)
            {
                Notice("Drive check", "Your vanguard has already attacked this turn.");
                return false;
            }

            StartAttack(vanguard);
            if (mAttacker == null) return false; // e.g. the first player's first turn
            Notice("Drive check", "The camera didn't see an attack, so " + vanguard.data.DisplayName + " (vanguard) attacks.");
        }

        if (gs.attackTarget == null && mAttackerOwner == who) ChooseTarget("Vanguard");
        return true;
    }

    void StartPicks(PlayerState owner, TriggerIcon icon, CardInstance target)
    {
        mPick = new PendingPick
        {
            owner = owner,
            icon = icon,
            power = icon == TriggerIcon.Over ? 100000000 : TriggerPower,
            powerOn = target,
            criticalOn = icon == TriggerIcon.Critical ? target : null,
        };
        mPick.picks.Enqueue(TriggerPick.Power);
        if (icon == TriggerIcon.Critical) mPick.picks.Enqueue(TriggerPick.Critical);
        if (icon == TriggerIcon.Stand && owner.AllRearguards().Any(c => c.orientation == CardOrientation.Rest))
            mPick.picks.Enqueue(TriggerPick.Stand);

        ToDo(icon + " trigger", owner.playerName + ": choose where its " + PickName(PickNow) + " goes. Hold a finger on a highlighted circle (or click it)." +
            (target != null ? " It's on " + target.data.DisplayName + " for now." : ""), PickDue());
    }

    /* Board zones a player has to move cards on now, lit up by ZoneHighlighter: a skill's step or cost,
     a check's cards to reveal, the ride deck cost's discard, a draw, a ride to redo */
    public List<(PlayerState owner, string zone)> AttentionZones()
    {
        var zones = new List<(PlayerState, string)>();
        if (!mStarted || gs.gameOver) return zones;
        zones.AddRange(Skills.AttentionZones());
        if (DriveDue()) zones.Add((mAttackerOwner, "Trigger"));

        // A damage check card is revealed on the Trigger zone, then, once its trigger is done, goes to the Damage zone
        PlayerState defender = gs.Opponent;
        bool placing = mDamageCheckCard != null && mDamageCheckCard.zone == "Trigger" && mDamageCheckCard.zoneOwner == defender;
        if (placing && mPick == null) zones.Add((defender, "Damage"));
        else if (!placing && DamageDue()) zones.Add((defender, "Trigger"));

        if (mRideCostDue || RideDiscardDue) zones.Add((gs.CurrentPlayer, "Drop"));
        foreach (CardInstance unit in OverGradeUnits(gs.CurrentPlayer)) zones.Add((gs.CurrentPlayer, unit.zone)); // to take back to the hand
        // A ride to redo, as long as the HUD asks for one
        if (mRideNeedsFix || (gs.currentPhase == TurnPhase.RidePhase && mRodeThisTurn && !mRideLegal)) zones.Add((gs.CurrentPlayer, "Vanguard"));
        return zones;
    }

    public enum ArrowKind { ToDrop, Ride, Cost }

    /* Cards to move, as arrows from where they are: CardMoves' (a hit rear-guard, guardians, a skill's
     retire or Soul Blast), each until the card has gone, and the ride deck's card to the vanguard
     circle until the ride is done */
    public List<(PlayerState owner, string from, string to, ArrowKind kind)> AttentionArrows()
    {
        var arrows = new List<(PlayerState, string, string, ArrowKind)>();
        if (!mStarted || gs.gameOver) return arrows;
        // One arrow per pair of zones, e.g. for all the guardians
        foreach (var move in Moves.Arrows())
            if (!arrows.Any(a => a.Item1 == move.owner && a.Item2 == move.from && a.Item3 == move.to)) arrows.Add(move);
        // The ride deck isn't read by the camera, so where its card goes is shown once the discard is in
        if (RideDeckRideDue && !RideDiscardDue)
            arrows.Add((gs.CurrentPlayer, "RideDeck", "Vanguard", ArrowKind.Ride));
        return arrows;
    }

    // To-dos' conditions: check cards still to reveal, and this pick still to make
    bool DriveDue() => mStep == BattleStep.Drive && mDriveLeft > 0;
    bool DamageDue() => mStep == BattleStep.Damage && mDamageLeft > 0;
    System.Func<bool> PickDue()
    {
        PendingPick pick = mPick;
        TriggerPick now = PickNow;
        return () => mPick == pick && PickNow == now;
    }

    static string PickName(TriggerPick pick) =>
        pick == TriggerPick.Critical ? "critical" : pick == TriggerPick.Stand ? "stand" : "power";

    /* Gives the bonus being picked to the unit on that circle of its owner's field, moving it off
     the unit it was on. Also the opponent's pick arriving online (remote). */
    public void Pick(string zone, bool remote = false, bool stood = false)
    {
        if (mPick == null || PickNow == TriggerPick.None) return;
        if (!(stood || remote) && !PickZones().Contains(zone)) return;
        if (mPick.owner.CardOnCircle(zone) == null) return;

        CardInstance unit = mPick.owner.CardOnCircle(zone);
        TriggerPick what = mPick.picks.Dequeue();
        switch (what)
        {
            case TriggerPick.Power:
                if (unit != mPick.powerOn)
                {
                    ApplyPower(mPick.powerOn, -mPick.power);
                    ApplyPower(unit, mPick.power);
                    mPick.powerOn = unit;
                }
                break;
            case TriggerPick.Critical:
                if (unit != mPick.criticalOn)
                {
                    if (mPick.criticalOn != null) mPick.criticalOn.criticalModifier -= 1;
                    unit.criticalModifier += 1;
                    mPick.criticalOn = unit;
                }
                break;
            case TriggerPick.Stand:
                mAttackedFrom.Remove(zone); // stood again, so it may attack again
                break;
        }

        if (!remote)
        {
            LastPick = what + ":" + zone;
            PickCount++;
        }

        string done = what == TriggerPick.Stand
            ? "Stand " + unit.data.DisplayName + ": turn it upright."
            : (what == TriggerPick.Power ? "Power +" + mPick.power : "Critical +1") + " to " + unit.data.DisplayName + ".";
        RefreshBattle();

        if (mPick.picks.Count > 0)
            ToDo(mPick.icon + " trigger", done + " Now choose where its " + PickName(PickNow) + " goes.", PickDue());
        else
            FinishPicks(done);
    }

    // The picks are over (or left as they are): carry on with the battle
    void FinishPicks(string notice)
    {
        mPick = null;
        Notice("Trigger", notice);
        if (mDecideAfterPick == null) return;

        string prefix = mDecideAfterPick;
        mDecideAfterPick = null;
        DecideHit(prefix);
    }

    // Leaves the bonuses being picked where they are, e.g. from ActionButtons
    public void KeepPicks()
    {
        if (mPick == null) return;
        LastPick = "Keep:";
        PickCount++;
        FinishPicks("The trigger's bonuses stay where they are.");
    }

    // Online: the trigger owner's pick from their PC
    public void ApplyRemotePick(string pick)
    {
        if (pick == null) return;
        int colon = pick.IndexOf(':');
        if (colon < 0) return;
        if (pick.StartsWith("Keep")) { if (mPick != null) FinishPicks("The trigger's bonuses stay where they are."); return; }
        Pick(pick.Substring(colon + 1), true);
    }

    // Once the drive check is over (or there isn't one): did the attack hit, and what happens now
    void DecideHit(string prefix)
    {
        mCheckTrigger = TriggerIcon.None;
        BattlePowers(out int attack, out int defence);
        PlayerState defender = gs.Opponent;
        CardInstance target = defender.CardOnCircle(gs.attackTarget ?? "Vanguard");
        string score = " (" + attack + " vs " + defence + ")";
        bool hit = attack >= defence && !TargetCantBeHit;

        if (!hit)
        {
            mStep = BattleStep.Close;
            Notice("No hit", prefix + (TargetCantBeHit ? "Perfect guard: the attack can't hit." : "The attack didn't hit" + score + "."));
        }
        else if (gs.attackTarget != "Vanguard")
        {
            mStep = BattleStep.Close;
            Notice("Hit", prefix + "Hit" + score + ": " + defender.playerName + " retires " + (target != null ? target.data.DisplayName : "that rear-guard") + " to the drop zone.");
        }
        else if (MatchSession.IsSolo)
        {
            mStep = BattleStep.Close; // no one to damage check
            Notice("Hit", prefix + "Hit. Test mode: no damage check. " + gs.CurrentPlayer.playerName + ": attack with another unit, or say \"turn end\".");
        }
        else StartDamageCheck(prefix + "Hit" + score + ". ");

        if (hit && gs.attackTarget != "Vanguard" && target != null) Moves.Expect(defender, gs.attackTarget, "Drop", target);
        foreach (CardInstance guardian in defender.guardianCircle) // the drive check is over: they're done
            Moves.Expect(defender, "Guardian", "Drop", guardian);
        if (hit) Skills.Happened(SkillTiming.AttackHits, mAttacker);
    }

    // The defender's damage check, straight after the drive check (or a rear-guard's hit); "damage check" is the fallback
    void StartDamageCheck(string prefix)
    {
        PlayerState defender = gs.Opponent;
        mStep = BattleStep.Damage;
        mDamageLeft = Mathf.Max(1, mAttacker.TotalCritical);
        mDamageCheckCard = null;
        ToDo("Damage check", prefix + defender.playerName + ": reveal " + mDamageLeft + " card(s) from the top of your deck into your Trigger zone, " +
            "then put each in your Damage zone.", DamageDue);
    }

    /* "turn end": straight to the End Phase from the Main or Battle Phase, unless a drive or
     damage check is still going */
    public void RequestTurnEnd()
    {
        if (!mStarted || gs.gameOver || mPassingTurn) return;

        if (!MatchSession.IsLocal(gs.CurrentPlayer)) Notice("Not your turn", gs.CurrentPlayer.playerName + " ends their own turn.");
        else if (gs.currentPhase < TurnPhase.MainPhase) Notice("Turn end", "Finish your Stand, Draw and Ride Phases first.");
        else if (mStep == BattleStep.Drive || (mStep == BattleStep.Damage && mDamageLeft > 0)) Notice("Turn end", "Finish this battle's checks first.");
        else if (Skills.Busy) Notice("Turn end", Skills.PromptWaiting + " first.");
        else
        {
            Skills.FinishSteps();
            StartCoroutine(EndTurn());
        }
    }

    // Stand / rest

    void HandleOrientationChanged(CardInstance card)
    {
        if (!mStarted || gs.gameOver || card.zoneOwner == null) return;
        if (!CheckTouch(card, card.zone, true)) return;

        if (card.orientation == CardOrientation.Stand)
        {
            // Stood by a Stand trigger or a skill as it resolves: it may attack again this turn
            if ((PickNow == TriggerPick.Stand && card.zoneOwner == mPick?.owner) || Skills.Stepping) mAttackedFrom.Remove(card.zone);
            Skills.Recheck(); // e.g. a skill's "stand this unit" (StandStep)
            // The camera seeing one of the owner's rear-guards stand settles a Stand pick (online, on the owner's PC)
            if (PickNow == TriggerPick.Stand && card.zoneOwner == mPick.owner && IsRearguardCircle(card.zone) && MatchSession.IsLocal(mPick.owner))
                Pick(card.zone, false, true);

            bool isUnit = card.zone == "Vanguard" || IsRearguardCircle(card.zone);
            if (gs.currentPhase == TurnPhase.StandPhase && card.zoneOwner == gs.CurrentPlayer && isUnit)
            {
                int rested = RestedUnits(card.zoneOwner);
                Notice("Stand Phase", card.zoneOwner.playerName + ": " + (rested > 0
                    ? rested + " rested unit(s) left to stand."
                    : "all your units are standing."));
                if (rested == 0) ScheduleAdvance(AllStanding);
            }
            return;
        }

        if (Skills.TakeRestDue(card)) return; // paying a skill's Rest cost, not attacking

        bool frontRow = card.zone == "Vanguard" || card.zone == "LeftFront" || card.zone == "RightFront";
        if (card.zoneOwner != gs.CurrentPlayer) return;

        // Resting outside the Battle Phase may be a cost, but say why it wasn't an attack
        if (gs.currentPhase != TurnPhase.BattlePhase)
        {
            if (frontRow && gs.currentPhase >= TurnPhase.RidePhase)
                Notice("Not an attack", card.data.DisplayName + " was rested in the " + PhaseName(gs.currentPhase) +
                    ". Attacks only count in the Battle Phase: stand it, press Next Phase, then rest it again.");
            return;
        }

        switch (card.zone)
        {
            case "Vanguard":
            case "LeftFront":
            case "RightFront": StartAttack(card); break;
            case "LeftBack":
            case "RightBack":
            case "CenterBack": Boost(card); break;
        }
    }

    void StartAttack(CardInstance attacker)
    {
        if (gs.isFirstTurn)
        {
            Warn(attacker, "The first player can't attack on their first turn.");
            return;
        }

        /* Once a turn from each circle, unless a Stand trigger or a skill stood the unit again: resting
         after the camera lost it, or it wobbling, isn't another attack */
        if (!mAttackedFrom.Add(attacker.zone))
        {
            MatchLogger.Action(attacker.data.DisplayName + " rested again on " + attacker.zone + ": it has already attacked this turn, so it's not an attack");
            // Once per circle a few seconds, as the camera wobbling a rested card reads as resting again
            if (!mAttackAgainSaid.TryGetValue(attacker.zone, out float said) || Time.time - said > AttackAgainSeconds)
            {
                mAttackAgainSaid[attacker.zone] = Time.time;
                Notice("Already attacked", attacker.data.DisplayName + " has already attacked this turn. A unit attacks again only if a Stand trigger or a skill stands it; this isn't an attack.");
            }
            return;
        }

        if (mAttacker != null) EndBattle(); // a new attack means the last battle is over
        gs.attackTarget = null; // its target is still to pick
        mAttacker = attacker;
        mBooster = null;
        mStep = BattleStep.Attack;
        mAttackerOwner = attacker.zoneOwner;
        mAttackerZone = attacker.zone;
        mAttackByVanguard = attacker == attacker.zoneOwner.vanguardCircle;
        if (mAttackByVanguard) mVanguardAttacked = true;

        // The booster may have been rested first
        if (mPendingBooster != null && mPendingBooster.orientation == CardOrientation.Rest &&
            mPendingBooster.zoneOwner == attacker.zoneOwner && FrontOf(mPendingBooster.zone) == attacker.zone)
        {
            mBooster = mPendingBooster;
            mBoostedAt = Time.time;
        }
        mPendingBooster = null;

        // Only the vanguard to attack: no need to ask
        PlayerState defender = gs.Opponent;
        if (defender.frontRowRearguards.All(c => c == null)) ChooseTarget("Vanguard");
        else
        {
            Notice("Attack", attacker.data.DisplayName + (mBooster != null ? ", boosted by " + mBooster.data.DisplayName + "," : "") + " is attacking. " +
                (MatchSession.IsLocal(attacker.zoneOwner)
                    ? "Choose which of " + defender.playerName + "'s front-row units it attacks."
                    : attacker.zoneOwner.playerName + " is choosing its target."));
            RefreshBattle();
        }

        Skills.Happened(SkillTiming.Attacks, attacker);
        Skills.Happened(SkillTiming.Boosts, mBooster);
    }

    /* Back-row units boost rather than attack. Resting one before the unit in front of it is fine:
     the boost waits for that unit's attack. */
    void Boost(CardInstance booster)
    {
        if (!booster.data.hasBoost && booster.data.grade > 1)
        {
            Warn(booster, "This unit doesn't have Boost.");
            return;
        }

        if (mAttacker != null && mAttackerZone == FrontOf(booster.zone) && mAttackerOwner == booster.zoneOwner)
        {
            mBooster = booster;
            mBoostedAt = Time.time;
            Notice("Boost", booster.data.DisplayName + " boosts " + mAttacker.data.DisplayName + ".");
            RefreshBattle();
            Skills.Happened(SkillTiming.Boosts, booster);
            return;
        }

        mPendingBooster = booster;
        Notice("Boost", booster.data.DisplayName + " will boost. Rest the unit in front of it to attack.");
    }

    // Picks which of the defender's front-row circles the attack targets; also the opponent's pick arriving online
    public void ChooseTarget(string zone)
    {
        // Only while picking: a late copy from the other PC mustn't restart a battle that's moved on
        if (mAttacker == null || mStep != BattleStep.Attack || System.Array.IndexOf(FrontRow, zone) < 0) return;

        CardInstance target = gs.Opponent.CardOnCircle(zone);
        if (target == null && zone != "Vanguard") return; // an empty rear-guard circle; the vanguard may just be unseen

        gs.attackTarget = zone;
        mStep = BattleStep.Guard;
        if (zone == "Vanguard") Skills.VanguardAttacked(gs.Opponent);
        string name = target != null ? target.data.DisplayName : "the vanguard";

        // A solo test has no one to guard
        if (MatchSession.IsSolo)
        {
            mGuardSaid = true;
            mNoGuard = true;
            RefreshBattle();
            if (mAttackByVanguard)
            {
                Notice("Attack", mAttacker.data.DisplayName + " attacks. Test mode: no opponent to guard.");
                Declare(Declaration.DriveCheck, mAttackerOwner);
            }
            else DecideHit("Test mode: no opponent to guard. ");
            return;
        }
        Notice("Attack", mAttacker.data.DisplayName + " attacks " + name + (zone == "Vanguard" ? " (vanguard)." : " (rear-guard): a hit retires it.") +
            " " + gs.Opponent.playerName + ": say \"guard\" or \"no guard\".");
        RefreshBattle();
    }

    /* Defence is the targeted unit's power plus all guardian shields; if the opponent's field
     isn't visible it's just the shields. Until a target is picked it's shown against the vanguard. */
    void RefreshBattle()
    {
        if (mAttacker == null) return;
        BattlePowers(out int attack, out int defence);
        GameState.OnBattleStarted?.Invoke(attack, defence);
    }

    void BattlePowers(out int attack, out int defence)
    {
        attack = mAttacker.TotalPower + (mBooster != null ? mBooster.TotalPower : 0);
        PlayerState defender = gs.Opponent;
        CardInstance target = defender.CardOnCircle(gs.attackTarget ?? "Vanguard");
        defence = (target != null ? target.TotalPower : 0) + defender.guardianCircle.Sum(g => g.data.shield);
    }

    void EndBattle()
    {
        // Skills' bonuses "during that battle" end with it
        foreach ((CardInstance card, int power, int critical) in mBattleBonuses)
        {
            card.criticalModifier -= critical;
            ApplyPower(card, -power);
        }
        mBattleBonuses.Clear();

        CardInstance attacker = mAttacker;
        CardInstance booster = mBooster;
        mAttacker = null;
        mBooster = null;
        gs.attackTarget = null;
        mStep = BattleStep.None;
        mGuardSaid = false;
        mNoGuard = false;
        mCantBeHit.Clear();
        mDriveLeft = 0;
        mDamageLeft = 0;
        mCheckTrigger = TriggerIcon.None;
        mPick = null;
        mDecideAfterPick = null;
        GameState.OnCloseStep?.Invoke();

        if (gs.Opponent.guardianCircle.Count > 0)
            Notice("Battle over", "Move the guardians to the drop zone.");
        Skills.Happened(SkillTiming.EndOfBattle, attacker);
        Skills.Happened(SkillTiming.BoostEndOfBattle, booster);
    }

    // Counter Blast

    void HandleFacingChanged(CardInstance card)
    {
        if (!mStarted || card.zone != "Damage" || card.zoneOwner == null) return;

        /* The camera losing a damage card under the others looks like a flip, so it isn't counted:
         Counter Blast is paid through skills' costs and the HUD's CB - (PlayerState.FaceUpDamage) */
    }

    // Returns whether the player can pay Counter Blast (amount), e.g. before an ability's cost is paid
    public bool CanCounterBlast(PlayerState player, int amount) => player.FaceUpDamage >= amount;

    // Helpers

    static IEnumerable<CardInstance> Units(PlayerState player)
    {
        if (player.vanguardCircle != null) yield return player.vanguardCircle;
        foreach (CardInstance rg in player.AllRearguards()) yield return rg;
    }

    // e.g. "Main Phase"
    static string PhaseName(TurnPhase phase) => phase.ToString().Replace("Phase", " Phase");

    static int RestedUnits(PlayerState player) => Units(player).Count(c => c.orientation == CardOrientation.Rest);

    static int VanguardGrade(PlayerState player) => player.vanguardCircle != null ? player.vanguardCircle.data.grade : 0;

    readonly HashSet<CardInstance> mAllowedOverGrade = new HashSet<CardInstance>(); // called over the vanguard's grade with a skill's leave

    /* A player's rear-guards of a higher grade than their vanguard that no skill allowed: called by
     mistake, so each must go back to the hand before the game moves on (AdvancePhase). Leaving the
     circle (the camera stops seeing it) is going back. None while the vanguard hasn't been seen */
    List<CardInstance> OverGradeUnits(PlayerState player) =>
        player == null || player.vanguardCircle == null ? new List<CardInstance>()
        : player.AllRearguards().Where(c => c.data.grade > VanguardGrade(player) && !mAllowedOverGrade.Contains(c)).ToList();

    // Each player's Nth turn allows up to grade N: turns 1-2 grade 1, turns 3-4 grade 2, ...
    int MaxRideGrade() => (gs.turnNumber + 1) / 2;

    static bool IsRearguardCircle(string zone) =>
        zone == "LeftFront" || zone == "RightFront" || zone == "LeftBack" || zone == "RightBack" || zone == "CenterBack";

    // The front-row circle a back-row circle boosts
    static string FrontOf(string backZone)
    {
        switch (backZone)
        {
            case "LeftBack": return "LeftFront";
            case "RightBack": return "RightFront";
            case "CenterBack": return "Vanguard";
            default: return null;
        }
    }

    void ApplyPower(CardInstance card, int amount)
    {
        if (card == null) return;
        card.powerModifier += amount;
        GameState.OnCardPowerChanged?.Invoke(card.data.cardName, card.data.power, card.powerModifier);
    }

    /* A skill's bonus (PowerStep). One "during that battle" is taken off when the battle ends
     (EndBattle); the rest go with the turn's (GameState.ClearTurnModifiers). Outside a battle it lasts the turn. */
    void AddBonus(CardInstance card, int power, int critical, bool battleOnly)
    {
        card.criticalModifier += critical;
        ApplyPower(card, power);
        if (battleOnly && mAttacker != null) mBattleBonuses.Add((card, power, critical));
        RefreshBattle();
    }

    /* A Sentinel's skill (CantBeHitStep): as if its owner said "perfect guard", so online it reaches
     the other PC's engine the same way */
    void PerfectGuardFromSkill(PlayerState owner)
    {
        if (VoiceCommands.Instance != null) VoiceCommands.Instance.Press("perfect guard");
        else Declare(Declaration.PerfectGuard, owner);
    }

    // Logged by MatchLogger
    void Notice(string title, string body)
    {
        if (gs != null && gs.gameOver) return; // nothing more once it's over
        GameState.OnRuleNotice?.Invoke(title, body);
    }

    // A notice for something the player has to do: it stays on screen until stillToDo turns false
    void ToDo(string title, string body, System.Func<bool> stillToDo)
    {
        if (gs != null && gs.gameOver) return;
        GameState.OnRuleToDo?.Invoke(title, body, stillToDo);
        Notice(title, body);
    }

    // A rule warning about one card: shown on screen and over the card when its overlay supports it
    void Warn(CardInstance card, string message)
    {
        Notice("Rule check", card.data.DisplayName + ": " + message);
        GameState.OnRulePrompt?.Invoke(card.data.cardName, message);
    }

    /* A move against the usual rules: a mistake (Warn), unless a skill resolving now does that move,
     e.g. a superior call in the Battle Phase. Then it's noted, naming the card whose skill allowed
     it. True when a skill allowed it */
    bool Breaks(CardInstance card, SkillRules.Move move, string rule)
    {
        string by = Skills.Allowing(card.zoneOwner, move);
        if (by == null)
        {
            Warn(card, rule);
            return false;
        }
        Notice("Allowed by a skill", card.data.DisplayName + ": " + rule + " " + by + " allows it here.");
        return true;
    }

    // Undo

    /* The referee's bookkeeping, for Undo: turn and phase, the zone counts, power and critical
     bonuses, the ride, the battle and its triggers, energy, and the skills used. Where the cards
     are isn't kept: that's the mat, which the camera keeps reading. */
    class Snapshot
    {
        public string key;
        public long sinceMs; // wall-clock time this state began, for matching the other PC's history online
        public int player, turn, ordersPlayed;
        public TurnPhase phase;
        public bool firstTurn;
        public string attackTarget;
        public Dictionary<string, int>[] counts;
        public Dictionary<CardInstance, (int power, int critical, bool noDrive)> bonuses;
        public List<(CardInstance card, int power, int critical)> battleBonuses;
        public HashSet<string> attackedFrom, cantBeHit;
        public HashSet<(PlayerState, string)> setOrders;
        public object skills;
        // Where the rules had each card, kept up to date until the change, so Undo can re-arm the cards it moved (not in the key)
        public Dictionary<CardInstance, (PlayerState owner, string zone)> zones;
        public CardInstance[] previousVanguards;

        public bool rode, standWarned, rideCostDue, drew, energyChargeDue, energyDrawn, energyDrawDue, personaDrawDue,
            rideLegal, rideNeedsFix, rideDeckCharged, attackByVanguard, guardSaid, noGuard, vanguardAttacked;
        public int dropBeforeRide, rideDeckAfterRide, deckAtDrawPhase, energyCharged, deckAtEnergyDraw, deckAtPersonaDraw,
            gradeBeforeRide, driveLeft, damageLeft;
        public CardInstance vanguardBeforeRide, attacker, booster, pendingBooster;
        public HashSet<CardInstance> beenVanguard, countedDamage;
        public PlayerState attackerOwner, checkTriggerOwner, personaFrontRow;
        public string attackerZone, decideAfterPick;
        public BattleStep step;
        public Heal heal;
        public PendingPick pick;
        public TriggerIcon checkTrigger;
    }

    const int MaxUndo = 30;
    const float UndoCheckSeconds = 0.1f;
    public Key undoKey = Key.Z; // with Ctrl
    readonly List<Snapshot> mHistory = new List<Snapshot>(); // states before each change, newest last
    Snapshot mLast; // the state as last checked
    float mNextUndoCheck;

    public bool CanUndo => mStarted && !gs.gameOver && !mPassingTurn && mHistory.Count > 0;

    /* This PC's latest undo, uploaded by OnlineMatch: when the state it went back to began (wall clock),
     and the turn and phase it's in, so the other PC goes back to the same moment */
    public static int UndoCount { get; private set; }
    public static (long sinceMs, int player, int turn, TurnPhase phase) LastUndo { get; private set; }

    static long NowMs => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /* Every UndoCheckSeconds: if the bookkeeping changed since the last check, the state before it
     goes on the history. Changes close together (one move and the rules it set off) undo as one. */
    void TrackUndo()
    {
        if (!mStarted || Skills == null || mPassingTurn) return;
        Snapshot now = Capture();
        if (mLast != null && now.key == mLast.key)
        {
            // Unchanged: it keeps the time it began, but where the cards are follows, so it's as just before the change
            mLast.zones = now.zones;
            mLast.previousVanguards = now.previousVanguards;
            return;
        }
        if (mLast != null)
        {
            mHistory.Add(mLast);
            if (mHistory.Count > MaxUndo) mHistory.RemoveAt(0);
        }
        mLast = now;
    }

    /* Ctrl+Z or the HUD's Undo: back to before the last change. Online the other PC goes back to the
     same moment too (OnlineMatch, ApplyRemoteUndo) */
    public void Undo()
    {
        if (!mStarted) return;
        if (gs.gameOver || mPassingTurn)
        {
            Notice("Undo", gs.gameOver ? "The game is over." : "Wait for the next turn to start, then undo.");
            return;
        }
        TrackUndo(); // anything changed since the last check is what gets undone
        if (mHistory.Count == 0)
        {
            Notice("Undo", "Nothing to undo.");
            return;
        }

        Snapshot back = mHistory[mHistory.Count - 1];
        mHistory.RemoveAt(mHistory.Count - 1);
        GoBackTo(back);

        if (MatchSession.IsOnline)
        {
            UndoCount++;
            LastUndo = (back.sinceMs, gs.currentPlayerIndex, gs.turnNumber, gs.currentPhase);
        }
        MatchLogger.Action("Undo: back to " + gs.CurrentPlayer.playerName + "'s " + PhaseName(gs.currentPhase) + ", turn " + gs.turnNumber);
        Notice("Undo", "Back to " + gs.CurrentPlayer.playerName + "'s " + PhaseName(gs.currentPhase) + " (turn " + gs.turnNumber + ")" +
            (MatchSession.IsOnline ? ", on both PCs" : "") + ". To redo a move, pick the card up and put it down again: it counts as new.");
    }

    /* Online: the other PC undid back to the state it had since sinceMs (wall clock). This PC goes back
     to its own state from that moment: what it did because of their undone move came a sync later, so
     that goes too. Their turn and phase win if this PC's history doesn't reach back that far. */
    public void ApplyRemoteUndo(long sinceMs, int player, int turn, TurnPhase phase, string by)
    {
        if (!mStarted || gs.gameOver) return;
        TrackUndo();
        if (mLast != null && mLast.sinceMs > sinceMs && mHistory.Count > 0)
        {
            int index = Mathf.Max(0, mHistory.FindLastIndex(s => s.sinceMs <= sinceMs)); // the oldest kept, if none is that old
            Snapshot back = mHistory[index];
            mHistory.RemoveRange(index, mHistory.Count - index);
            GoBackTo(back);
        }

        if (gs.currentPlayerIndex != player || gs.turnNumber != turn || gs.currentPhase != phase)
        {
            gs.currentPlayerIndex = player;
            gs.turnNumber = turn;
            gs.currentPhase = phase;
            gs.BroadcastState();
            mLast = Capture();
        }

        MatchLogger.Action("Undo by " + by + ": back to " + gs.CurrentPlayer.playerName + "'s " + PhaseName(gs.currentPhase) + ", turn " + gs.turnNumber);
        Notice("Undo", by + " undid: back to " + gs.CurrentPlayer.playerName + "'s " + PhaseName(gs.currentPhase) + " (turn " + gs.turnNumber +
            "). Move cards back on the mat too if they changed.");
    }

    void GoBackTo(Snapshot back)
    {
        if (mAutoAdvance != null)
        {
            StopCoroutine(mAutoAdvance);
            mAutoAdvance = null;
        }
        DamageSlots.Quiet = true; // the damage cards follow the counts back without asking which to flip
        Restore(back);
        PutCardsBack(back);
        Moves.Clear(true); // the counts went back too

        foreach (PlayerState player in gs.players)
            foreach (CardInstance card in player.AllFieldCards())
                GameState.OnCardPowerChanged?.Invoke(card.data.cardName, card.data.power, card.powerModifier);
        gs.BroadcastState();
        DamageSlots.Quiet = false;
        mLast = Capture(); // what the broadcast's listeners did isn't a new change
        mLast.sinceMs = back.sinceMs; // the same state, from the same moment
    }

    Snapshot Capture()
    {
        var s = new Snapshot
        {
            sinceMs = NowMs,
            player = gs.currentPlayerIndex, turn = gs.turnNumber, phase = gs.currentPhase, firstTurn = gs.isFirstTurn,
            ordersPlayed = gs.orderPlayedThisTurn, attackTarget = gs.attackTarget,
            counts = gs.players.Select(p => new Dictionary<string, int>(p.untrackedCards)).ToArray(),
            bonuses = new Dictionary<CardInstance, (int, int, bool)>(),
            battleBonuses = new List<(CardInstance, int, int)>(mBattleBonuses),
            attackedFrom = new HashSet<string>(mAttackedFrom),
            cantBeHit = new HashSet<string>(mCantBeHit),
            setOrders = new HashSet<(PlayerState, string)>(mSetOrders),
            skills = Skills.SaveState(),

            rode = mRodeThisTurn, standWarned = mStandWarned, rideCostDue = mRideCostDue, drew = mDrewThisTurn,
            energyChargeDue = mEnergyChargeDue, energyDrawn = mEnergyDrawn, energyDrawDue = mEnergyDrawDue,
            personaDrawDue = mPersonaDrawDue, deckAtPersonaDraw = mDeckAtPersonaDraw,
            rideLegal = mRideLegal, rideNeedsFix = mRideNeedsFix, rideDeckCharged = mRideDeckCharged,
            attackByVanguard = mAttackByVanguard, guardSaid = mGuardSaid, noGuard = mNoGuard, vanguardAttacked = mVanguardAttacked,
            dropBeforeRide = mDropBeforeRide, rideDeckAfterRide = mRideDeckAfterRide, deckAtDrawPhase = mDeckAtDrawPhase,
            energyCharged = mEnergyCharged, deckAtEnergyDraw = mDeckAtEnergyDraw, gradeBeforeRide = mGradeBeforeRide,
            driveLeft = mDriveLeft, damageLeft = mDamageLeft,
            vanguardBeforeRide = mVanguardBeforeRide, attacker = mAttacker, booster = mBooster, pendingBooster = mPendingBooster,
            beenVanguard = new HashSet<CardInstance>(mBeenVanguard), countedDamage = new HashSet<CardInstance>(mCountedDamage),
            attackerOwner = mAttackerOwner, checkTriggerOwner = mCheckTriggerOwner, personaFrontRow = mPersonaFrontRow,
            attackerZone = mAttackerZone, decideAfterPick = mDecideAfterPick,
            step = mStep, checkTrigger = mCheckTrigger,
            heal = mHeal == null ? null : new Heal
            {
                owner = mHeal.owner, dropCounted = mHeal.dropCounted, damageCounted = mHeal.damageCounted,
                faceDownCounted = mHeal.faceDownCounted, facingUnknown = mHeal.facingUnknown, time = mHeal.time,
            },
            pick = CopyPick(mPick),
            zones = CardZones(),
            previousVanguards = gs.players.Select(p => p.previousVanguard).ToArray(),
        };
        foreach (PlayerState player in gs.players)
            foreach (CardInstance card in player.AllFieldCards())
                s.bonuses[card] = (card.powerModifier, card.criticalModifier, card.cannotDriveCheckThisTurn);
        s.key = KeyOf(s);
        return s;
    }

    void Restore(Snapshot s)
    {
        gs.currentPlayerIndex = s.player;
        gs.turnNumber = s.turn;
        gs.currentPhase = s.phase;
        gs.isFirstTurn = s.firstTurn;
        gs.orderPlayedThisTurn = s.ordersPlayed;
        gs.attackTarget = s.attackTarget;
        for (int i = 0; i < gs.players.Length; i++)
            gs.players[i].untrackedCards = new Dictionary<string, int>(s.counts[i]);

        // Cards on the field since then had no bonus yet
        foreach (PlayerState player in gs.players)
            foreach (CardInstance card in player.AllFieldCards())
            {
                s.bonuses.TryGetValue(card, out var bonus);
                card.powerModifier = bonus.power;
                card.criticalModifier = bonus.critical;
                card.cannotDriveCheckThisTurn = bonus.noDrive;
            }
        Skills.LoadState(s.skills);
        mBattleBonuses.Clear();
        mBattleBonuses.AddRange(s.battleBonuses);
        mAttackedFrom.Clear();
        mAttackedFrom.UnionWith(s.attackedFrom);
        mCantBeHit.Clear();
        mCantBeHit.UnionWith(s.cantBeHit);
        mSetOrders.Clear();
        mSetOrders.UnionWith(s.setOrders);

        mRodeThisTurn = s.rode; mStandWarned = s.standWarned; mRideCostDue = s.rideCostDue; mDrewThisTurn = s.drew;
        mEnergyChargeDue = s.energyChargeDue; mEnergyDrawn = s.energyDrawn; mEnergyDrawDue = s.energyDrawDue;
        mPersonaDrawDue = s.personaDrawDue; mDeckAtPersonaDraw = s.deckAtPersonaDraw;
        mRideLegal = s.rideLegal; mRideNeedsFix = s.rideNeedsFix; mRideDeckCharged = s.rideDeckCharged;
        mAttackByVanguard = s.attackByVanguard; mGuardSaid = s.guardSaid; mNoGuard = s.noGuard; mVanguardAttacked = s.vanguardAttacked;
        mDropBeforeRide = s.dropBeforeRide; mRideDeckAfterRide = s.rideDeckAfterRide; mDeckAtDrawPhase = s.deckAtDrawPhase;
        mEnergyCharged = s.energyCharged; mDeckAtEnergyDraw = s.deckAtEnergyDraw; mGradeBeforeRide = s.gradeBeforeRide;
        mDriveLeft = s.driveLeft; mDamageLeft = s.damageLeft;
        mVanguardBeforeRide = s.vanguardBeforeRide; mAttacker = s.attacker; mBooster = s.booster; mPendingBooster = s.pendingBooster;
        mBeenVanguard.Clear(); mBeenVanguard.UnionWith(s.beenVanguard);
        mCountedDamage.Clear(); mCountedDamage.UnionWith(s.countedDamage);
        mAttackerOwner = s.attackerOwner; mCheckTriggerOwner = s.checkTriggerOwner; mPersonaFrontRow = s.personaFrontRow;
        mAttackerZone = s.attackerZone; mDecideAfterPick = s.decideAfterPick;
        mStep = s.step; mCheckTrigger = s.checkTrigger;
        mHeal = s.heal; // the snapshot's own copy; it isn't used again
        mPick = CopyPick(s.pick);
    }

    static readonly string[] CardZoneNames =
        { "Vanguard", "LeftFront", "RightFront", "LeftBack", "RightBack", "CenterBack",
          "Guardian", "Trigger", "Order", "Crest", "Soul", "Drop", "Damage", "Bind", "OOG", "Deck", "RideDeck" };

    // Where the rules have each tracked card now
    Dictionary<CardInstance, (PlayerState owner, string zone)> CardZones()
    {
        var zones = new Dictionary<CardInstance, (PlayerState, string)>();
        foreach (PlayerState player in gs.players)
            foreach (string zone in CardZoneNames)
            {
                CardInstance single = player.CardOnCircle(zone);
                if (single != null) zones[single] = (player, zone);
                List<CardInstance> list = player.ZoneList(zone);
                if (list != null) foreach (CardInstance card in list) zones[card] = (player, zone);
            }
        return zones;
    }

    /* The mat isn't undone, but the cards the undone moves put somewhere go back to where the rules
     had them, quietly: the camera then reports each again once it's lifted or moved, so doing the
     move again (riding, calling, guarding with the same card) counts as a new one instead of the
     card "never having left". Online, only this PC's own cards: the other PC sends its own. */
    void PutCardsBack(Snapshot back)
    {
        if (back.zones == null) return;
        bool Mine(PlayerState p) => p != null && (!MatchSession.IsOnline || p.playerIndex == MatchSession.LocalSeat);

        Dictionary<CardInstance, (PlayerState owner, string zone)> now = CardZones();
        var moved = new List<CardInstance>();
        foreach (var pair in now)
            if (!back.zones.TryGetValue(pair.Key, out var was) || was != pair.Value) moved.Add(pair.Key);
        foreach (var pair in back.zones)
            if (!now.ContainsKey(pair.Key)) moved.Add(pair.Key);
        moved.RemoveAll(card =>
        {
            now.TryGetValue(card, out var at);
            back.zones.TryGetValue(card, out var was);
            return !Mine(at.owner) && !Mine(was.owner);
        });
        if (moved.Count == 0) return;

        foreach (CardInstance card in moved) gs.TakeOffQuietly(card);
        foreach (CardInstance card in moved)
        {
            back.zones.TryGetValue(card, out var was);
            gs.PutBackQuietly(card, was.owner, was.zone);
            mLeftFrom.Remove(card); // not "lifted and put back": putting it down again is a new move
        }
        for (int i = 0; i < gs.players.Length; i++)
            if (Mine(gs.players[i])) gs.players[i].previousVanguard = back.previousVanguards[i];
        MatchLogger.Action("Undo: " + moved.Count + " card" + (moved.Count == 1 ? "" : "s") + " back where they were, waiting to be moved again");
    }

    static PendingPick CopyPick(PendingPick pick)
    {
        if (pick == null) return null;
        var copy = new PendingPick { owner = pick.owner, icon = pick.icon, power = pick.power, powerOn = pick.powerOn, criticalOn = pick.criticalOn };
        foreach (TriggerPick next in pick.picks) copy.picks.Enqueue(next);
        return copy;
    }

    // Every value in the snapshot as text, to tell whether anything changed
    string KeyOf(Snapshot s)
    {
        var key = new System.Text.StringBuilder();
        void Add(object value) => key.Append(value).Append('|');
        string Id(CardInstance card) => card?.id ?? "-";

        Add(s.player); Add(s.turn); Add(s.phase); Add(s.firstTurn); Add(s.ordersPlayed); Add(s.attackTarget);
        foreach (Dictionary<string, int> counts in s.counts)
            foreach (KeyValuePair<string, int> pair in counts.OrderBy(p => p.Key)) { Add(pair.Key); Add(pair.Value); }
        foreach (KeyValuePair<CardInstance, (int power, int critical, bool noDrive)> pair in s.bonuses)
            if (pair.Value.power != 0 || pair.Value.critical != 0 || pair.Value.noDrive) { Add(pair.Key.id); Add(pair.Value); }
        Add(Skills.StateKey);
        Add(s.battleBonuses.Count); Add(s.attackedFrom.Count); Add(s.setOrders.Count); Add(string.Join(",", s.cantBeHit.OrderBy(z => z)));

        Add(s.rode); Add(s.standWarned); Add(s.rideCostDue); Add(s.drew); Add(s.energyChargeDue); Add(s.energyDrawn); Add(s.energyDrawDue);
        Add(s.rideLegal); Add(s.rideNeedsFix); Add(s.rideDeckCharged); Add(s.attackByVanguard); Add(s.guardSaid); Add(s.noGuard);
        Add(s.vanguardAttacked); Add(s.dropBeforeRide); Add(s.rideDeckAfterRide); Add(s.deckAtDrawPhase); Add(s.energyCharged);
        Add(s.deckAtEnergyDraw); Add(s.personaDrawDue); Add(s.deckAtPersonaDraw); Add(s.gradeBeforeRide); Add(s.driveLeft); Add(s.damageLeft);
        Add(Id(s.vanguardBeforeRide)); Add(Id(s.attacker)); Add(Id(s.booster)); Add(Id(s.pendingBooster));
        Add(s.beenVanguard.Count); Add(s.countedDamage.Count);
        Add(s.attackerOwner?.playerIndex); Add(s.checkTriggerOwner?.playerIndex); Add(s.personaFrontRow?.playerIndex); Add(s.attackerZone); Add(s.decideAfterPick);
        Add(s.step); Add(s.checkTrigger);
        if (s.heal != null) { Add(s.heal.owner?.playerIndex); Add(s.heal.dropCounted); Add(s.heal.damageCounted); Add(s.heal.faceDownCounted); Add(s.heal.facingUnknown); }
        if (s.pick != null) { Add(s.pick.owner?.playerIndex); Add(s.pick.icon); Add(s.pick.power); Add(Id(s.pick.powerOn)); Add(Id(s.pick.criticalOn)); Add(string.Join(",", s.pick.picks)); }
        return key.ToString();
    }
}

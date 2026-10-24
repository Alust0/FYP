using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/* Referee for a physical D format game. Players move real cards; the tracker reports each
 move through GameState, and this checks it against the current phase, warns about illegal
 plays, and keeps score of battles, triggers, damage and Counter Blast. Warnings never undo
 anything, since card effects can legally break the usual timing. Phases advance through
 AdvancePhase: Space or the HUD's Next Phase button for now, hand gestures later. */
public class RulesEngine : MonoBehaviour
{
    [Tooltip("Ends the current phase. Gestures should call AdvancePhase() as well.")]
    public Key advancePhaseKey = Key.Space;
    [Tooltip("Seconds the End Phase notice stays up before the next player's Stand Phase starts.")]
    public float endPhaseSeconds = 2f;
    [Tooltip("Each player's face-down deck when the game starts: 54 cards minus the 4-card ride deck and the 5-card opening hand.")]
    public int startingDeckCount = 45;

    const int TriggerPower = 10000;

    GameState gs;
    bool mStarted;
    bool mPassingTurn; // End Phase showing, next turn about to start
    bool mRodeThisTurn;
    CardInstance mAttacker; // unit attacking in the current battle, null outside one
    CardInstance mBooster;

    void OnEnable()
    {
        GameState.OnCardMoved += HandleCardMoved;
        GameState.OnCardOrientationChanged += HandleOrientationChanged;
        GameState.OnCardFacingChanged += HandleFacingChanged;
    }

    void OnDisable()
    {
        GameState.OnCardMoved -= HandleCardMoved;
        GameState.OnCardOrientationChanged -= HandleOrientationChanged;
        GameState.OnCardFacingChanged -= HandleFacingChanged;
    }

    IEnumerator Start()
    {
        gs = GameState.Instance;
        yield return new WaitUntil(() => gs.players[0] != null && gs.players[1] != null);

        gs.turnNumber = 1;
        gs.currentPlayerIndex = 0;
        gs.isFirstTurn = true;
        foreach (PlayerState player in gs.players)
            player.AdjustCount("Deck", startingDeckCount);
        mStarted = true;
        EnterPhase(TurnPhase.StandPhase);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[advancePhaseKey].wasPressedThisFrame)
            AdvancePhase();
    }

    // Ends the current phase; ending the Battle Phase runs the End Phase and passes the turn
    public void AdvancePhase()
    {
        if (!mStarted || gs.gameOver || mPassingTurn) return;

        if (gs.currentPhase >= TurnPhase.BattlePhase) StartCoroutine(EndTurn());
        else EnterPhase(gs.currentPhase + 1);
    }

    // Shows the End Phase for endPhaseSeconds, then starts the other player's Stand Phase
    IEnumerator EndTurn()
    {
        mPassingTurn = true;
        EnterPhase(TurnPhase.EndPhase);
        yield return new WaitForSeconds(endPhaseSeconds);
        mPassingTurn = false;

        if (!gs.gameOver) StartNextTurn();
    }

    void StartNextTurn()
    {
        // "Until end of turn" power and critical bonuses wear off
        gs.ClearTurnModifiers();
        foreach (PlayerState player in gs.players)
            foreach (CardInstance card in player.AllFieldCards())
                GameState.OnCardPowerChanged?.Invoke(card.data.cardName, card.data.power, card.powerModifier);

        gs.SwapCurrentPlayer();
        gs.turnNumber++;
        gs.isFirstTurn = false;
        mRodeThisTurn = false;
        EnterPhase(TurnPhase.StandPhase);
    }

    void EnterPhase(TurnPhase phase)
    {
        if (mAttacker != null) EndBattle();

        gs.currentPhase = phase;
        gs.BroadcastState();

        PlayerState cp = gs.CurrentPlayer;
        int vgGrade = VanguardGrade(cp);

        switch (phase)
        {
            case TurnPhase.StandPhase:
                int rested = Units(cp).Count(c => c.orientation == CardOrientation.Rest);
                Notice("Stand Phase", cp.playerName + ": " + (rested > 0 ? "stand your " + rested + " rested unit(s)." : "all your units are standing."));
                break;

            case TurnPhase.DrawPhase:
                gs.AdjustCount(cp, "Deck", -1);
                Notice("Draw Phase", cp.playerName + ": draw 1 card.");
                break;

            case TurnPhase.RidePhase:
                Notice("Ride Phase", cp.vanguardCircle == null
                    ? cp.playerName + ": place your vanguard."
                    : cp.playerName + ": you may ride 1 grade " + vgGrade + " or " + (vgGrade + 1) + " unit. Riding from the ride deck costs 1 card from hand.");
                break;

            case TurnPhase.MainPhase:
                gs.orderPlayedThisTurn = 0;
                Notice("Main Phase", cp.playerName + ": call rear-guards (grade " + vgGrade + " or lower), play up to " + gs.orderLimit + " order, move units.");
                break;

            case TurnPhase.BattlePhase:
                Notice("Battle Phase", gs.isFirstTurn
                    ? "The first player can't attack on their first turn."
                    : cp.playerName + ": rest a front-row unit to attack, and the unit behind it to boost.");
                break;

            case TurnPhase.EndPhase:
                Notice("End Phase", cp.playerName + "'s turn ends. " + gs.Opponent.playerName + "'s turn is next.");
                break;
        }
    }

    // Card movement

    void HandleCardMoved(CardInstance card, PlayerState fromOwner, string fromZone)
    {
        if (!mStarted || gs.gameOver || card.zone == null) return;

        PlayerState owner = card.zoneOwner;
        bool isTurnPlayer = owner == gs.CurrentPlayer;

        switch (card.zone)
        {
            case "Vanguard": CheckRide(card, owner, isTurnPlayer); break;
            case "LeftFront":
            case "RightFront":
            case "LeftBack":
            case "RightBack":
            case "CenterBack": CheckCall(card, isTurnPlayer, fromOwner == owner && IsRearguardCircle(fromZone)); break;
            case "Order": CheckOrder(card, isTurnPlayer); break;
            case "Guardian": CheckGuard(card, isTurnPlayer, fromZone); break;
            case "Trigger":
                if (fromZone != "Deck") owner.AdjustCount("Deck", -1); // drive/damage check off the face-down deck
                ResolveTrigger(card, owner);
                break;
            case "Damage":
                if (fromZone == null) owner.AdjustCount("Deck", -1); // damage check placed straight from the deck
                gs.CheckWinCondition();
                RefreshBattle();
                break;
        }
    }

    void CheckRide(CardInstance card, PlayerState owner, bool isTurnPlayer)
    {
        CardInstance previous = owner.previousVanguard;
        if (previous == null) return; // first vanguard of the game, or placed back on an empty circle

        if (!isTurnPlayer)
        {
            Warn(card, "Only the turn player rides.");
            return;
        }

        if (gs.currentPhase != TurnPhase.RidePhase) Warn(card, "Riding normally happens in the Ride Phase.");
        else if (mRodeThisTurn) Warn(card, "You've already ridden this turn.");
        mRodeThisTurn = true;

        int grade = previous.data.grade;
        if (card.data.grade != grade && card.data.grade != grade + 1)
            Warn(card, "Can only ride grade " + grade + " or " + (grade + 1) + " over a grade " + grade + " vanguard.");

        // Persona Ride: same name, both with the icon
        if (card.data.cardName == previous.data.cardName && card.data.hasPersonaRideIcon && previous.data.hasPersonaRideIcon)
        {
            ApplyPower(card, 10000);
            GameState.OnPersonaRide?.Invoke(card.data.cardName);
        }
    }

    void CheckCall(CardInstance card, bool isTurnPlayer, bool movedBetweenCircles)
    {
        if (movedBetweenCircles) return; // repositioning a unit already on the field, not a call

        if (!isTurnPlayer) Warn(card, "Only the turn player calls rear-guards.");
        else if (gs.currentPhase != TurnPhase.MainPhase) Warn(card, "Rear-guards are normally called in the Main Phase.");

        int vgGrade = VanguardGrade(card.zoneOwner);
        if (card.data.grade > vgGrade)
            Warn(card, "Can't call a grade " + card.data.grade + " unit over a grade " + vgGrade + " vanguard.");
    }

    void CheckOrder(CardInstance card, bool isTurnPlayer)
    {
        if (!card.data.isOrderCard)
        {
            Warn(card, "Only order cards go in the order zone.");
            return;
        }

        // Blitz orders are played by the defender during a battle
        if (!isTurnPlayer && card.data.isBlitzOrder && gs.currentPhase == TurnPhase.BattlePhase) return;

        if (!isTurnPlayer) Warn(card, "Only the turn player plays orders (except Blitz Orders while guarding).");
        else if (gs.currentPhase != TurnPhase.MainPhase) Warn(card, "Orders are played in the Main Phase.");
        else if (gs.orderPlayedThisTurn >= gs.orderLimit) Warn(card, "Only " + gs.orderLimit + " order per turn.");

        if (isTurnPlayer) gs.orderPlayedThisTurn++;
    }

    void CheckGuard(CardInstance card, bool isTurnPlayer, string fromZone)
    {
        if (isTurnPlayer) Warn(card, "The attacking player doesn't guard.");
        else if (gs.currentPhase != TurnPhase.BattlePhase) Warn(card, "Guardians are only called during a battle.");

        // Intercept: a rear-guard moving from the field to the guardian circle
        if (fromZone == "LeftBack" || fromZone == "RightBack" || fromZone == "CenterBack")
            Warn(card, "Only front-row rear-guards can intercept.");
        else if ((fromZone == "LeftFront" || fromZone == "RightFront") && !card.data.hasIntercept)
            Warn(card, "This unit doesn't have Intercept.");

        RefreshBattle();
    }

    /* A card in a trigger zone is a drive check (turn player) or a damage check (the other
     player). The power/critical goes to the attacker or the checking player's vanguard,
     the usual choice; a different target has to be applied by hand for now. */
    void ResolveTrigger(CardInstance card, PlayerState owner)
    {
        TriggerIcon icon = card.data.triggerIcon;
        if (icon == TriggerIcon.None) return;

        bool isDrive = owner == gs.CurrentPlayer;
        GameState.OnTriggerActivated?.Invoke(icon, isDrive ? "drive" : "damage");

        CardInstance target = isDrive && mAttacker != null ? mAttacker : owner.vanguardCircle;

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
                if (owner.CountInZone("Damage") < other.CountInZone("Damage"))
                    Warn(card, "No heal: your damage is lower than your opponent's.");
                break;

            case TriggerIcon.Over:
                ApplyPower(target, 100000000);
                break;

            default: // Draw, Stand
                ApplyPower(target, TriggerPower);
                break;
        }

        RefreshBattle();
    }

    // Stand / rest

    void HandleOrientationChanged(CardInstance card)
    {
        if (!mStarted || gs.gameOver || card.zoneOwner == null) return;
        if (card.orientation != CardOrientation.Rest) return;
        if (gs.currentPhase != TurnPhase.BattlePhase || card.zoneOwner != gs.CurrentPlayer) return; // e.g. resting as a cost

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

        if (mAttacker != null) EndBattle(); // a new attack means the last battle is over
        mAttacker = attacker;
        mBooster = null;
        RefreshBattle();
    }

    void Boost(CardInstance booster)
    {
        if (mAttacker == null)
        {
            Warn(booster, "Back-row units can't attack.");
            return;
        }

        if (mAttacker.zone != FrontOf(booster.zone)) Warn(booster, "A unit can only boost the unit directly in front of it.");
        else if (!booster.data.hasBoost) Warn(booster, "This unit doesn't have Boost.");
        else
        {
            mBooster = booster;
            RefreshBattle();
        }
    }

    /* Attacks are assumed to target the opponent's vanguard. Defence is that vanguard's power
     plus all guardian shields; if the opponent's field isn't visible it's just the shields. */
    void RefreshBattle()
    {
        if (mAttacker == null) return;

        int attack = mAttacker.TotalPower + (mBooster != null ? mBooster.TotalPower : 0);
        PlayerState defender = gs.Opponent;
        int defence = (defender.vanguardCircle != null ? defender.vanguardCircle.TotalPower : 0)
                    + defender.guardianCircle.Sum(g => g.data.shield);

        GameState.OnBattleStarted?.Invoke(attack, defence);
    }

    void EndBattle()
    {
        mAttacker = null;
        mBooster = null;
        GameState.OnCloseStep?.Invoke();

        if (gs.Opponent.guardianCircle.Count > 0)
            Notice("Battle over", "Move the guardians to the drop zone.");
    }

    // Counter Blast

    void HandleFacingChanged(CardInstance card)
    {
        if (!mStarted || card.zone != "Damage" || card.zoneOwner == null) return;

        PlayerState owner = card.zoneOwner;
        if (card.facing == CardFacing.FaceDown)
            Notice("Counter Blast", owner.playerName + " flipped 1 damage face down. " + owner.FaceUpDamage + " face-up damage left.");
    }

    // Returns whether the player can pay Counter Blast (amount), e.g. before an ability's cost is paid
    public bool CanCounterBlast(PlayerState player, int amount) => player.FaceUpDamage >= amount;

    // Helpers

    static IEnumerable<CardInstance> Units(PlayerState player)
    {
        if (player.vanguardCircle != null) yield return player.vanguardCircle;
        foreach (CardInstance rg in player.AllRearguards()) yield return rg;
    }

    static int VanguardGrade(PlayerState player) => player.vanguardCircle != null ? player.vanguardCircle.data.grade : 0;

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

    void Notice(string title, string body)
    {
        Debug.Log("[RulesEngine] " + title + ": " + body);
        GameState.OnRuleNotice?.Invoke(title, body);
    }

    // A rule warning about one card: shown on screen and over the card when its overlay supports it
    void Warn(CardInstance card, string message)
    {
        Notice("Rule check", card.data.cardName + ": " + message);
        GameState.OnRulePrompt?.Invoke(card.data.cardName, message);
    }
}

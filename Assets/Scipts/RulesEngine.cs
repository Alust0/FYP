using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Executes all game phases and rule logic for D format Vanguard
public class RulesEngine : MonoBehaviour
{
    private GameState gs;

    // Pending player choices — replace the stubs below with real UI callbacks

    void Start()
    {
        gs = GameState.Instance;
        StartCoroutine(RunTurnLoop());
    }

    // Main loop that drives each player's turn until the game ends
    IEnumerator RunTurnLoop()
    {
        yield return new WaitUntil(() => gs.players[0] != null && gs.players[1] != null);

        gs.isFirstTurn = true;
        gs.turnNumber = 0;
        gs.currentPlayerIndex = 0;

        while (!gs.gameOver)
        {
            yield return StartCoroutine(ExecuteStandPhase());
            yield return StartCoroutine(ExecuteDrawPhase());
            yield return StartCoroutine(ExecuteRidePhase());
            yield return StartCoroutine(ExecuteMainPhase());
            yield return StartCoroutine(ExecuteBattlePhase());
            yield return StartCoroutine(ExecuteEndPhase());

            gs.SwapCurrentPlayer();
            gs.turnNumber++;
            gs.isFirstTurn = false;
        }
    }

    // Stands all cards and fires beginning-of-turn abilities
    IEnumerator ExecuteStandPhase()
    {
        gs.currentPhase = TurnPhase.StandPhase;
        gs.BroadcastState();

        ActivateAutoAbilities("StandUp");
        yield return StartCoroutine(ResolveCheckTiming());

        ActivateAutoAbilities("BeginningOfTurn");
        ActivateAutoAbilities("BeginningOfStandPhase");
        yield return StartCoroutine(ResolveCheckTiming());

        foreach (CardInstance card in gs.CurrentPlayer.AllFieldCards())
            card.orientation = CardOrientation.Stand; // stand all cards

        yield return StartCoroutine(ResolveCheckTiming());
    }

    // Fires draw phase abilities then draws one mandatory card
    IEnumerator ExecuteDrawPhase()
    {
        gs.currentPhase = TurnPhase.DrawPhase;
        gs.BroadcastState();

        ActivateAutoAbilities("BeginningOfDrawPhase");
        yield return StartCoroutine(ResolveCheckTiming());

        gs.CurrentPlayer.DrawCard(1); // always draw one

        yield return StartCoroutine(ResolveCheckTiming());

        // G ASSIST — not in D format; uncomment below if adding G format or older format support
        // yield return StartCoroutine(ExecuteGAssistStep());
    }

    // G ASSIST STEP — not in D format
    // Uncomment this entire method if adding G format or older format support
    // Players without a ride deck may search the top 5 cards for a unit one grade higher
    // IEnumerator ExecuteGAssistStep()
    // {
    // PlayerState cp = gs.CurrentPlayer;
    // int vgGrade = cp.vanguardCircle != null ? cp.vanguardCircle.data.grade : -1;
    // bool hasRideDeck = cp.rideDeck != null && cp.rideDeck.Count > 0;
    //
    // // Skip G Assist if player has a ride deck
    // if (hasRideDeck) yield break;
    //
    // // Skip if vanguard is already grade 3 or higher
    // if (vgGrade >= 3) yield break;
    //
    // // Skip if hand already has a unit of the next grade
    // bool handHasNextGrade = cp.hand.Exists(c => c.data.grade == vgGrade + 1);
    // if (handHasNextGrade) yield break;
    //
    // // Player may choose to perform G Assist
    // bool performGAssist = AskPlayerGAssistChoice();
    // if (!performGAssist) yield break;
    //
    // // Reveal hand to opponent then look at top 5 cards of deck
    // Debug.Log("G Assist: revealing hand and checking top 5 cards.");
    // List<CardInstance> top5 = new List<CardInstance>();
    // Stack<CardInstance> temp = new Stack<CardInstance>();
    // for (int i = 0; i < 5 && cp.mainDeck.Count > 0; i++)
    // { CardInstance c = cp.mainDeck.Pop(); top5.Add(c); temp.Push(c); }
    // while (temp.Count > 0) cp.mainDeck.Push(temp.Pop());
    //
    // // Player may take one unit of the next grade from those 5
    // CardInstance chosen = AskPlayerGAssistCardChoice(top5, vgGrade + 1);
    // if (chosen != null)
    // {
    // // Remove chosen card from deck and add to hand
    // List<CardInstance> deckList = new List<CardInstance>(cp.mainDeck);
    // deckList.Remove(chosen);
    // cp.mainDeck.Clear();
    // foreach (CardInstance c in deckList) cp.mainDeck.Push(c);
    // cp.hand.Add(chosen);
    //
    // // Player must remove 2 cards from hand from the game
    // List<CardInstance> toRemove = AskPlayerGAssistRemoveCards();
    // foreach (CardInstance c in toRemove) cp.hand.Remove(c);
    // }
    //
    // // Always shuffle after G Assist
    // List<CardInstance> shuffleList = new List<CardInstance>(cp.mainDeck);
    // for (int i = shuffleList.Count - 1; i > 0; i--)
    // { int j = Random.Range(0, i + 1); CardInstance t = shuffleList[i];
    // shuffleList[i] = shuffleList[j]; shuffleList[j] = t; }
    // cp.mainDeck.Clear();
    // foreach (CardInstance c in shuffleList) cp.mainDeck.Push(c);
    // yield return null;
    // }

    // Fires ride phase abilities, runs ride step then stride step
    IEnumerator ExecuteRidePhase()
    {
        gs.currentPhase = TurnPhase.RidePhase;
        gs.BroadcastState();

        ActivateAutoAbilities("BeginningOfRidePhase");
        yield return StartCoroutine(ResolveCheckTiming());
        yield return StartCoroutine(ExecuteRideStep());
        yield return StartCoroutine(ResolveCheckTiming());
        yield return StartCoroutine(ExecuteStrideStep()); // stride step after ride step
        yield return StartCoroutine(ResolveCheckTiming());
    }

    // Lets the player optionally ride from hand or ride deck
    IEnumerator ExecuteRideStep()
    {
        ActivateAutoAbilities("BeginningOfRideStep");
        yield return StartCoroutine(ResolveCheckTiming());

        PlayerState cp = gs.CurrentPlayer;
        int vgGrade = cp.vanguardCircle != null ? cp.vanguardCircle.data.grade : -1;

        // Ride is optional — stub defaults to false, replace with UI
        bool playerRides = AskPlayerRideChoice();
        if (!playerRides) yield break;

        CardInstance rideTarget = AskPlayerRideTarget();
        if (rideTarget == null) yield break;

        bool fromHand = cp.hand.Contains(rideTarget);
        bool fromRideDeck = cp.rideDeck.Contains(rideTarget);

        bool validFromHand = rideTarget.data.grade == vgGrade || rideTarget.data.grade == vgGrade + 1;
        bool validFromRideDeck = rideTarget.data.grade == vgGrade + 1; // exactly one higher

        if (fromHand && validFromHand)
        {
            cp.hand.Remove(rideTarget);
            yield return StartCoroutine(PerformRide(rideTarget, "hand"));
        }
        else if (fromRideDeck && validFromRideDeck)
        {
            CardInstance discarded = AskPlayerHandDiscard(); // pay discard cost
            if (discarded != null)
            {
                cp.hand.Remove(discarded);
                cp.dropZone.Add(discarded);
            }
            cp.rideDeck.Remove(rideTarget);
            yield return StartCoroutine(PerformRide(rideTarget, "rideDeck"));
        }
        else
        {
            Debug.LogWarning("Invalid ride target."); // cancel invalid ride
        }

        yield return StartCoroutine(ResolveCheckTiming());
    }

    // Places the ride card on vanguard circle and checks for persona ride and gift
    IEnumerator PerformRide(CardInstance rideCard, string source)
    {
        PlayerState cp = gs.CurrentPlayer;
        CardInstance previousVanguard = cp.vanguardCircle;

        if (source == "rideDeck")
            Debug.Log("Revealing from ride deck: " + rideCard.data.cardName); // reveal ride deck card

        cp.vanguardCircle = rideCard;
        rideCard.orientation = CardOrientation.Stand;

        if (previousVanguard != null)
            cp.soul.Add(previousVanguard); // old vanguard to soul

        ActivateAutoAbilities("WhenRodeUpon", previousVanguard);

        // Persona Ride: normal hand ride, same name, both have icon
        if (source == "hand"
            && previousVanguard != null
            && rideCard.data.cardName == previousVanguard.data.cardName
            && rideCard.data.hasPersonaRideIcon
            && previousVanguard.data.hasPersonaRideIcon)
        {
            PerformPersonaRide(); // D format exclusive
        }

        // IMAGINARY GIFTS — not in D format; uncomment below if adding G format support
        // if (rideCard.data.imaginaryGift != ImaginaryGift.None)
        // yield return StartCoroutine(ResolveImaginaryGift(rideCard.data.imaginaryGift));

        gs.BroadcastState();
        yield return null;
    }

    // Grants draw one and Power +10000 to the vanguard until end of turn
    void PerformPersonaRide()
    {
        gs.CurrentPlayer.DrawCard(1);
        gs.CurrentPlayer.vanguardCircle.powerModifier += 10000; // until end turn
        GameState.OnPersonaRide?.Invoke(gs.CurrentPlayer.vanguardCircle.data.cardName);
    }

    // IMAGINARY GIFTS — not in D format
    // Uncomment this entire method if adding G format or older format support
    // IEnumerator ResolveImaginaryGift(ImaginaryGift giftType)
    // {
    // PlayerState cp = gs.CurrentPlayer;
    // GiftType chosenType = GetOrChooseGiftType(cp, giftType);
    // GameState.OnGiftReceived?.Invoke(giftType, chosenType);
    // switch (giftType)
    // {
    // case ImaginaryGift.Force:
    // int forceCircle = AskPlayerCircleChoice();
    // cp.giftMarkers.Add(new GiftMarker(ImaginaryGift.Force, chosenType, forceCircle));
    // if (chosenType == GiftType.TypeI) ApplyForceI(cp, forceCircle);
    // else ApplyForceII(cp, forceCircle);
    // break;
    // case ImaginaryGift.Accel:
    // int accelCount = cp.giftMarkers.Count(m => m.clanType == ImaginaryGift.Accel);
    // int newCircle = cp.frontRowRearguards.Length + accelCount;
    // cp.giftMarkers.Add(new GiftMarker(ImaginaryGift.Accel, chosenType, newCircle));
    // if (chosenType == GiftType.TypeI) ApplyAccelI(cp, newCircle);
    // else { cp.DrawCard(1); ApplyAccelII(cp, newCircle); }
    // break;
    // case ImaginaryGift.Protect:
    // if (chosenType == GiftType.TypeI)
    // { CardInstance protect = CreateProtectPseudoCard(); cp.hand.Add(protect); }
    // else
    // { int rearCircle = AskPlayerRearguardCircleChoice();
    // cp.giftMarkers.Add(new GiftMarker(ImaginaryGift.Protect, GiftType.TypeII, rearCircle)); }
    // break;
    // }
    // gs.BroadcastState();
    // yield return null;
    // }

    // Stride step — player may stride if vanguard is grade 3+ and G zone has face down cards
    IEnumerator ExecuteStrideStep()
    {
        PlayerState cp = gs.CurrentPlayer;

        // Stride requires vanguard to be grade 3 or higher
        if (cp.vanguardCircle == null || cp.vanguardCircle.data.grade < 3)
            yield break;

        // Stride requires at least one face down G unit in the G zone
        bool hasGUnit = cp.gZone.Exists(c => c.facing == CardFacing.FaceDown);
        if (!hasGUnit) yield break;

        // Player chooses whether to stride — stub defaults to false, replace with UI
        bool playerStrides = AskPlayerStrideChoice();
        if (!playerStrides) yield break;

        // Player selects a face down G unit from G zone to stride with
        CardInstance strideTarget = AskPlayerStrideTarget();
        if (strideTarget == null) yield break;

        // Pay stride cost: discard cards from hand with total grade 3 or more
        List<CardInstance> strideCost = AskPlayerStrideCostCards();
        if (strideCost == null) yield break;

        int totalGrade = 0;
        foreach (CardInstance c in strideCost)
            totalGrade += c.data.grade;

        if (totalGrade < 3)
        {
            Debug.LogWarning("Stride cost not met — total grade must be 3 or more.");
            yield break;
        }

        // Pay the cost — discard chosen cards
        foreach (CardInstance c in strideCost)
        {
            cp.hand.Remove(c);
            cp.dropZone.Add(c);
        }

        // Perform stride — G unit goes to vanguard circle, previous vanguard becomes heart
        CardInstance previousVanguard = cp.vanguardCircle;
        cp.gZone.Remove(strideTarget);

        strideTarget.facing = CardFacing.FaceUp;
        strideTarget.orientation = CardOrientation.Stand;
        cp.vanguardCircle = strideTarget;

        // Previous vanguard enters heart state (stays under G unit)
        if (previousVanguard != null)
            cp.soul.Add(previousVanguard);

        ActivateAutoAbilities("WhenStrode", strideTarget);

        Debug.Log("Stride! " + strideTarget.data.cardName + " strode over " + previousVanguard?.data.cardName);
        gs.BroadcastState();
        yield return StartCoroutine(ResolveCheckTiming());
    }

    // Lets the player call units, play orders, use abilities, or pass
    IEnumerator ExecuteMainPhase()
    {
        gs.currentPhase = TurnPhase.MainPhase;
        gs.orderPlayedThisTurn = 0; // reset order count
        gs.BroadcastState();

        ActivateAutoAbilities("BeginningOfMainPhase");
        yield return StartCoroutine(ResolveCheckTiming());

        bool playerActing = true;

        while (playerActing)
        {
            string action = AskPlayerMainPhaseAction();

            switch (action)
            {
                case "CallUnit":
                    CardInstance unit = AskPlayerHandUnitChoice(gs.CurrentPlayer.vanguardCircle != null ? gs.CurrentPlayer.vanguardCircle.data.grade : 0);
                    int circle = AskPlayerRearguardCircleChoice();
                    if (unit != null) CallUnit(unit, circle); // triggers placed abilities
                    break;

                case "SwitchColumn":
                    int col = AskPlayerColumnChoice();
                    SwitchColumn(col); // swap one column
                    break;

                case "PlayOrder":
                    if (gs.orderPlayedThisTurn < gs.orderLimit) // limit one per turn
                    {
                        CardInstance order = AskPlayerOrderChoice(false);
                        if (order != null)
                        {
                            PlayOrder(order);
                            gs.orderPlayedThisTurn++;
                        }
                    }
                    break;

                case "OverDress":
                    CardInstance overCard = AskPlayerOverDressChoice();
                    CardInstance target = AskPlayerMatchingRearguard(overCard);
                    if (overCard != null && target != null)
                        PerformOverDress(overCard, target); // dress onto rearguard
                    break;

                case "Pass":
                    playerActing = false; // end main phase
                    break;
            }

            yield return StartCoroutine(ResolveCheckTiming());
            gs.BroadcastState();
        }
    }

    // Runs repeated attack sub-phases until the player passes or has no valid attackers
    IEnumerator ExecuteBattlePhase()
    {
        gs.currentPhase = TurnPhase.BattlePhase;
        gs.BroadcastState();

        if (gs.isFirstTurn && gs.currentPlayerIndex == 0) // first player no attack
            yield break;

        bool continueBattling = true;

        while (continueBattling)
        {
            bool result = false;
            yield return StartCoroutine(ExecuteAttackSubPhase(r => result = r));
            continueBattling = result;
        }
    }

    // Runs one full attack from start step through close step
    IEnumerator ExecuteAttackSubPhase(System.Action<bool> result)
    {
        ActivateAutoAbilities("BeginningOfStartStep");
        ActivateAutoAbilities("BeginningOfBattlePhase");
        yield return StartCoroutine(ResolveCheckTiming());

        if (!AskPlayerAttackChoice()) { result(false); yield break; } // end battle phase

        CardInstance attacker = AskPlayerAttackerChoice(); // front row standing unit
        if (attacker == null) { result(false); yield break; }

        ActivateAutoAbilities("BeginningOfAttackStep");
        yield return StartCoroutine(ResolveCheckTiming());

        attacker.orientation = CardOrientation.Rest;
        ActivateAutoAbilities("WhenUnitAttacks", attacker);

        CardInstance defender = AskPlayerDefenderChoice(); // opponent vanguard or front rearguard
        if (defender == null) { result(false); yield break; }

        CardInstance booster = GetBooster(attacker);
        if (booster != null && booster.orientation == CardOrientation.Stand)
        {
            booster.orientation = CardOrientation.Rest;
            attacker.powerModifier += booster.data.power; // add boost power
            ActivateAutoAbilities("WhenUnitBoosts", booster);
        }

        yield return StartCoroutine(ResolveCheckTiming());
        yield return StartCoroutine(ExecuteGuardStep(attacker, defender));

        bool isVanguardAttack = attacker == gs.CurrentPlayer.vanguardCircle || attacker.hasDriveByEffect;
        if (isVanguardAttack)
            yield return StartCoroutine(ExecuteDriveStep(attacker)); // vanguard performs drive

        yield return StartCoroutine(ExecuteDamageStep(attacker, defender));
        yield return StartCoroutine(ExecuteCloseStep(attacker));

        result(true); // may continue attacking
    }

    // Lets the opponent call guardians, intercept, or play blitz orders before drive
    IEnumerator ExecuteGuardStep(CardInstance attacker, CardInstance defender)
    {
        ActivateAutoAbilities("BeginningOfGuardStep");

        bool passCalled = false;

        while (!passCalled)
        {
            string guardAction = AskOpponentGuardAction();

            switch (guardAction)
            {
                case "Pass":
                    passCalled = true; // skip to drive step
                    break;

                case "CallGuardian":
                    List<CardInstance> guardians = AskOpponentGuardianChoice(); // no grade restriction
                    foreach (CardInstance g in guardians)
                    {
                        gs.Opponent.hand.Remove(g);
                        gs.Opponent.guardianCircle.Add(g);
                    }
                    break;

                case "Intercept":
                    CardInstance interceptor = AskOpponentInterceptorChoice(); // front row intercept
                    if (interceptor != null)
                    {
                        MoveToGuardianCircle(interceptor);
                        ActivateAutoAbilities("WhenUnitIntercepts", interceptor);
                    }
                    break;

                case "BlitzOrder":
                    if (gs.orderPlayedThisTurn < gs.orderLimit) // check order limit
                    {
                        CardInstance blitz = AskPlayerOrderChoice(true);
                        if (blitz != null)
                        {
                            PlayOrder(blitz);
                            gs.orderPlayedThisTurn++;
                        }
                    }
                    break;
            }

            yield return StartCoroutine(ResolveCheckTiming());
        }
    }

    // Flips drive cards one at a time and resolves any trigger icons
    IEnumerator ExecuteDriveStep(CardInstance driveUnit)
    {
        ActivateAutoAbilities("BeginningOfDriveStep");
        yield return StartCoroutine(ResolveCheckTiming());

        int drivesRemaining = driveUnit.data.drive; // grade 3 usually two drives

        while (drivesRemaining > 0)
        {
            if (gs.CurrentPlayer.mainDeck.Count == 0) break;

            CardInstance driveCard = gs.CurrentPlayer.mainDeck.Pop();
            Debug.Log("Drive check: " + driveCard.data.cardName);

            if (driveCard.data.triggerIcon != TriggerIcon.None)
            {
                GameState.OnTriggerActivated?.Invoke(driveCard.data.triggerIcon, "drive");
                yield return StartCoroutine(ResolveTriggerAbility(driveCard.data.triggerIcon, "drive", driveCard)); // drive trigger
            }

            yield return StartCoroutine(ResolveCheckTiming());
            gs.CurrentPlayer.hand.Add(driveCard); // add to hand
            drivesRemaining--;
        }

        yield return StartCoroutine(ResolveCheckTiming());
    }

    // Applies the two sub-abilities of the revealed trigger icon
    IEnumerator ResolveTriggerAbility(TriggerIcon icon, string context, CardInstance triggerCard)
    {
        // context is "drive" or "damage"

        switch (icon)
        {
            case TriggerIcon.Critical:
                CardInstance critTarget = AskPlayerUnitChoice();
                if (critTarget != null) critTarget.criticalModifier += 1; // plus one critical
                CardInstance powerTargC = AskPlayerUnitChoice();
                if (powerTargC != null) powerTargC.powerModifier += GetTriggerPowerValue(icon);
                break;

            case TriggerIcon.Draw:
                gs.CurrentPlayer.DrawCard(1); // draw one card
                CardInstance powerTargD = AskPlayerUnitChoice();
                if (powerTargD != null) powerTargD.powerModifier += GetTriggerPowerValue(icon);
                break;

            case TriggerIcon.Stand:
                CardInstance standTarget = AskPlayerRearguardChoice();
                if (standTarget != null)
                {
                    standTarget.orientation = CardOrientation.Stand;
                    standTarget.cannotDriveCheckThisTurn = true; // no drive this turn
                }
                CardInstance powerTargS = AskPlayerUnitChoice();
                if (powerTargS != null) powerTargS.powerModifier += GetTriggerPowerValue(icon);
                break;

            case TriggerIcon.Heal:
                if (gs.CurrentPlayer.damageZone.Count >= gs.Opponent.damageZone.Count) // heal condition met
                {
                    CardInstance healTarget = AskPlayerDamageZoneChoice();
                    if (healTarget != null)
                    {
                        gs.CurrentPlayer.damageZone.Remove(healTarget);
                        gs.CurrentPlayer.dropZone.Add(healTarget); // heal one damage
                    }
                }
                CardInstance powerTargH = AskPlayerUnitChoice();
                if (powerTargH != null) powerTargH.powerModifier += GetTriggerPowerValue(icon);
                break;

            case TriggerIcon.Front:
                foreach (CardInstance unit in gs.CurrentPlayer.frontRowRearguards.Where(c => c != null))
                    unit.powerModifier += GetTriggerPowerValue(icon); // all front row
                if (gs.CurrentPlayer.vanguardCircle != null)
                    gs.CurrentPlayer.vanguardCircle.powerModifier += GetTriggerPowerValue(icon);
                break;

            case TriggerIcon.Over:
                Debug.Log("Over Trigger: " + triggerCard.data.cardName + " removed from game."); // removed from game
                gs.CurrentPlayer.DrawCard(1);
                CardInstance overTarget = AskPlayerUnitChoice();
                if (overTarget != null) overTarget.powerModifier += 100000000; // effectively enormous power
                break;
        }

        gs.BroadcastState();
        yield return null;
    }

    // Compares attack and defend power then deals damage if the attack hits
    IEnumerator ExecuteDamageStep(CardInstance attacker, CardInstance defender)
    {
        ActivateAutoAbilities("BeginningOfDamageStep");
        yield return StartCoroutine(ResolveCheckTiming());

        int attackPower = attacker.TotalPower;
        int defendPower = defender.TotalPower + GetGuardianShield();
        bool attackHits = attackPower >= defendPower; // must meet or exceed

        if (!attacker.IsOnField(gs.CurrentPlayer) || !defender.IsOnField(gs.Opponent))
            attackHits = false; // skip if left field

        GameState.OnBattleStarted?.Invoke(attackPower, defendPower);
        yield return StartCoroutine(ResolveCheckTiming());

        if (attackHits && defender == gs.Opponent.vanguardCircle)
        {
            int damage = attacker.TotalCritical;
            yield return StartCoroutine(PerformDamageCheck(gs.Opponent, damage)); // deal critical damage
        }

        yield return StartCoroutine(ResolveCheckTiming());

        if (attackHits)
            ActivateAutoAbilities("WhenAttackHits", attacker);
        else
            ActivateAutoAbilities("WhenAttackDidNotHit", attacker); // missed attack

        yield return StartCoroutine(ResolveCheckTiming());

        foreach (CardInstance g in gs.Opponent.guardianCircle.ToList())
            RetireToDropZone(g, gs.Opponent); // retire all guardians
        gs.Opponent.guardianCircle.Clear();

        if (attackHits && defender != gs.Opponent.vanguardCircle)
            RetireToDropZone(defender, gs.Opponent); // retire hit rearguard

        yield return StartCoroutine(ResolveCheckTiming());
    }

    // Draws one card per point of damage and checks for triggers and win condition each time
    IEnumerator PerformDamageCheck(PlayerState damagedPlayer, int damageAmount)
    {
        for (int i = 0; i < damageAmount; i++)
        {
            if (damagedPlayer.damageZone.Count >= 6) // already at six
            {
                gs.CheckWinCondition();
                yield break;
            }

            if (damagedPlayer.mainDeck.Count == 0) { gs.CheckWinCondition(); yield break; }

            CardInstance damageCard = damagedPlayer.mainDeck.Pop();
            damagedPlayer.damageZone.Add(damageCard);
            Debug.Log("Damage check: " + damageCard.data.cardName); // damage zone public

            if (damageCard.data.triggerIcon != TriggerIcon.None)
            {
                GameState.OnTriggerActivated?.Invoke(damageCard.data.triggerIcon, "damage");
                yield return StartCoroutine(ResolveTriggerAbility(damageCard.data.triggerIcon, "damage", damageCard)); // damage trigger
            }

            gs.CheckWinCondition();
            yield return StartCoroutine(ResolveCheckTiming());
            gs.BroadcastState();
        }
    }

    // Clears all battle-duration effects and resets attacking unit status
    IEnumerator ExecuteCloseStep(CardInstance attacker)
    {
        ActivateAutoAbilities("BeginningOfCloseStep");
        ActivateAutoAbilities("AtTheEndOfBattle");
        yield return StartCoroutine(ResolveCheckTiming());

        gs.ClearBattleModifiers(attacker); // clear battle effects
        GameState.OnCloseStep?.Invoke();
        gs.BroadcastState();
    }

    // Unlocks cards, retires music orders, clears turn effects, then passes the turn
    IEnumerator ExecuteEndPhase()
    {
        gs.currentPhase = TurnPhase.EndPhase; // no G units
        gs.BroadcastState();

        foreach (CardInstance card in gs.CurrentPlayer.AllFieldCards().Where(c => c.isLocked).ToList())
        {
            card.isLocked = false;
            card.facing = CardFacing.FaceUp; // unlock and reveal
            ActivateAutoAbilities("WhenUnlocked", card);
            ActivateAutoAbilities("WhenTurnedFaceUp", card);
        }

        foreach (CardInstance order in gs.CurrentPlayer.orderZone.Where(c => c.isMusicOrder).ToList())
        {
            gs.CurrentPlayer.orderZone.Remove(order);
            gs.CurrentPlayer.dropZone.Add(order); // music orders retire
        }

        ActivateAutoAbilities("BeginningOfEndPhase");
        ActivateAutoAbilities("AtTheEndOfTurn");

        yield return StartCoroutine(ResolveCheckTiming());

        gs.ClearTurnModifiers(); // clear turn effects
        gs.CheckWinCondition(); // no hand limit
        gs.BroadcastState();
    }

    // Resolves all standing-by AUTO abilities in a loop until none remain
    IEnumerator ResolveCheckTiming()
    {
        gs.CheckWinCondition();
        yield return null;
    }

    // Moves a card from the field to the drop zone and fires retired abilities
    void RetireToDropZone(CardInstance card, PlayerState owner)
    {
        card.orientation = CardOrientation.Stand; // reset on retire

        for (int i = 0; i < owner.frontRowRearguards.Length; i++)
            if (owner.frontRowRearguards[i] == card) owner.frontRowRearguards[i] = null;

        for (int i = 0; i < owner.backRowRearguards.Length; i++)
            if (owner.backRowRearguards[i] == card) owner.backRowRearguards[i] = null;

        if (owner.centerBackRearguard == card) owner.centerBackRearguard = null;

        owner.dropZone.Add(card);
        ActivateAutoAbilities("WhenRetired", card);
    }

    // Returns the total shield value of all cards currently in the guardian circle
    int GetGuardianShield()
    {
        int total = 0;
        foreach (CardInstance g in gs.Opponent.guardianCircle)
            total += g.data.shield;
        return total;
    }

    // Returns the standing back-row unit in the same column as the attacker if it has Boost
    CardInstance GetBooster(CardInstance attacker)
    {
        PlayerState cp = gs.CurrentPlayer;

        if (attacker == cp.vanguardCircle && cp.centerBackRearguard != null
            && cp.centerBackRearguard.data.hasBoost
            && cp.centerBackRearguard.orientation == CardOrientation.Stand)
            return cp.centerBackRearguard;

        for (int i = 0; i < cp.frontRowRearguards.Length; i++)
        {
            if (cp.frontRowRearguards[i] == attacker)
            {
                CardInstance candidate = cp.backRowRearguards[i];
                if (candidate != null && candidate.data.hasBoost && candidate.orientation == CardOrientation.Stand)
                    return candidate;
            }
        }
        return null;
    }

    // Moves a front-row rearguard to the guardian circle as an intercept
    void MoveToGuardianCircle(CardInstance card)
    {
        PlayerState op = gs.Opponent;
        for (int i = 0; i < op.frontRowRearguards.Length; i++)
        {
            if (op.frontRowRearguards[i] == card)
            {
                op.frontRowRearguards[i] = null;
                card.orientation = CardOrientation.Rest;
                op.guardianCircle.Add(card);
                return;
            }
        }
    }

    // Places a unit card from hand onto a chosen rear-guard circle
    void CallUnit(CardInstance unit, int circleIndex)
    {
        PlayerState cp = gs.CurrentPlayer;
        int centerBackIdx = cp.frontRowRearguards.Length + cp.backRowRearguards.Length;
        cp.hand.Remove(unit);

        if (circleIndex < cp.frontRowRearguards.Length)
        {
            if (cp.frontRowRearguards[circleIndex] != null)
                RetireToDropZone(cp.frontRowRearguards[circleIndex], cp);
            cp.frontRowRearguards[circleIndex] = unit;
        }
        else if (circleIndex == centerBackIdx)
        {
            if (cp.centerBackRearguard != null)
                RetireToDropZone(cp.centerBackRearguard, cp);
            cp.centerBackRearguard = unit;
        }
        else
        {
            int backIndex = circleIndex - cp.frontRowRearguards.Length;
            if (cp.backRowRearguards[backIndex] != null)
                RetireToDropZone(cp.backRowRearguards[backIndex], cp);
            cp.backRowRearguards[backIndex] = unit;
        }

        unit.orientation = CardOrientation.Stand;
        ActivateAutoAbilities("WhenPlaced", unit);
    }

    // Swaps the front and back cards of one column
    void SwitchColumn(int colIndex)
    {
        PlayerState cp = gs.CurrentPlayer;
        CardInstance temp = cp.frontRowRearguards[colIndex];
        cp.frontRowRearguards[colIndex] = cp.backRowRearguards[colIndex];
        cp.backRowRearguards[colIndex] = temp;
    }

    // Sends an order card to the order zone and applies its effect
    void PlayOrder(CardInstance order)
    {
        PlayerState cp = gs.CurrentPlayer;
        cp.hand.Remove(order);
        cp.orderZone.Add(order);
        ActivateAutoAbilities("WhenOrderPlayed", order);
        Debug.Log("Order played: " + order.data.cardName);
    }

    // Places the OverDress card on top of an existing rear-guard
    void PerformOverDress(CardInstance overCard, CardInstance target)
    {
        PlayerState cp = gs.CurrentPlayer;
        cp.hand.Remove(overCard);
        cp.soul.Add(target);

        for (int i = 0; i < cp.frontRowRearguards.Length; i++)
            if (cp.frontRowRearguards[i] == target) { cp.frontRowRearguards[i] = overCard; return; }

        for (int i = 0; i < cp.backRowRearguards.Length; i++)
            if (cp.backRowRearguards[i] == target) { cp.backRowRearguards[i] = overCard; return; }
    }

    // Returns the power bonus granted by a given trigger icon
    int GetTriggerPowerValue(TriggerIcon icon)
    {
        return 10000;
    }

    // // Returns the previously chosen gift type or defaults to TypeI
    // GiftType GetOrChooseGiftType(PlayerState player, ImaginaryGift gift)
    // {
    // switch (gift)
    // {
    // case ImaginaryGift.Force:
    // if (player.chosenForceType == GiftType.None) player.chosenForceType = GiftType.TypeI;
    // return player.chosenForceType;
    // case ImaginaryGift.Accel:
    // if (player.chosenAccelType == GiftType.None) player.chosenAccelType = GiftType.TypeI;
    // return player.chosenAccelType;
    // case ImaginaryGift.Protect:
    // if (player.chosenProtectType == GiftType.None) player.chosenProtectType = GiftType.TypeI;
    // return player.chosenProtectType;
    // default:
    // return GiftType.None;
    // }
    // }

    // // Creates a Protect I pseudo-card that functions as a sentinel in hand
    // CardInstance CreateProtectPseudoCard()
    // {
    // CardData pseudo = ScriptableObject.CreateInstance<CardData>();
    // pseudo.cardName = "Protect Pseudo-Card";
    // pseudo.grade = 0;
    // pseudo.shield = 15000;
    // pseudo.hasSentinel = true;
    // return new CardInstance(pseudo);
    // }

    // // Applies Force I effect to the chosen circle
    // void ApplyForceI(PlayerState player, int circleIndex)
    // {
    // CardInstance unit = GetUnitOnCircle(player, circleIndex);
    // if (unit != null) unit.powerModifier += 10000;
    // }

    // // Applies Force II effect to the chosen circle
    // void ApplyForceII(PlayerState player, int circleIndex)
    // {
    // CardInstance unit = GetUnitOnCircle(player, circleIndex);
    // if (unit != null) unit.criticalModifier += 1; // critical becomes 2
    // }

    // // Applies Accel I effect to the new circle
    // void ApplyAccelI(PlayerState player, int circleIndex)
    // {
    // CardInstance unit = GetUnitOnCircle(player, circleIndex);
    // if (unit != null) unit.powerModifier += 10000;
    // }

    // // Applies Accel II effect to the new circle
    // void ApplyAccelII(PlayerState player, int circleIndex)
    // {
    // CardInstance unit = GetUnitOnCircle(player, circleIndex);
    // if (unit != null) unit.powerModifier += 5000;
    // }

    // Returns the unit on the given circle index or null if empty
    CardInstance GetUnitOnCircle(PlayerState player, int index)
    {
        if (index == -1) return player.vanguardCircle;
        if (index < player.frontRowRearguards.Length) return player.frontRowRearguards[index];
        int back = index - player.frontRowRearguards.Length;
        if (back < player.backRowRearguards.Length) return player.backRowRearguards[back];
        if (index == player.frontRowRearguards.Length + player.backRowRearguards.Length)
            return player.centerBackRearguard;
        return null;
    }

    // Fires all AUTO abilities matching the given trigger string
    void ActivateAutoAbilities(string trigger, CardInstance card = null)
    {
        Debug.Log("AUTO: " + trigger + (card != null ? " on " + card.data.cardName : ""));
    }

    // Player Input place holders will be replace with real UI callbacks

    bool AskPlayerRideChoice() { return false; }
    CardInstance AskPlayerRideTarget() { return null; }
    CardInstance AskPlayerHandDiscard() { return null; }
    int AskPlayerCircleChoice() { return -1; }
    int AskPlayerRearguardCircleChoice() { return 0; }
    int AskPlayerColumnChoice() { return 0; }
    string AskPlayerMainPhaseAction() { return "Pass"; }
    bool AskPlayerAttackChoice(){ return false; }
    CardInstance AskPlayerAttackerChoice() { return null; }
    CardInstance AskPlayerDefenderChoice() { return null; }
    string AskOpponentGuardAction() { return "Pass"; }
    List<CardInstance> AskOpponentGuardianChoice() { return new List<CardInstance>(); }
    CardInstance AskOpponentInterceptorChoice() { return null; }
    CardInstance AskPlayerUnitChoice() { return null; }
    CardInstance AskPlayerRearguardChoice() { return null; }
    CardInstance AskPlayerDamageZoneChoice() { return null; }
    CardInstance AskPlayerOrderChoice(bool blitz) { return null; }
    CardInstance AskPlayerOverDressChoice() { return null; }
    CardInstance AskPlayerMatchingRearguard(CardInstance c) { return null; }
    CardInstance AskPlayerHandUnitChoice(int maxGrade) { return null; }

    // Stride replace with real UI callbacks
    bool AskPlayerStrideChoice() { return false; }
    CardInstance AskPlayerStrideTarget() { return null; }
    List<CardInstance> AskPlayerStrideCostCards() { return new List<CardInstance>(); }

    // G Assist only needed if uncommenting G Assist step above
    // bool AskPlayerGAssistChoice() { return false; }
    // CardInstance AskPlayerGAssistCardChoice(List<CardInstance> top5, int grade) { return null; }
    // List<CardInstance> AskPlayerGAssistRemoveCards() { return new List<CardInstance>(); }
}
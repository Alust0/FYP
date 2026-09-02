using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Runs a fully automated test game between two AI players until someone wins
public class TestSetup : MonoBehaviour
{
    private GameState gs;
    private BoardLayout board;

    // How long each highlight stays on screen
    public float actionDelay = 1.0f;
    public float highlightDuration = 0.6f;

    // Zone colors
    public Color colorVanguard = Color.yellow;
    public Color colorFrontRow = Color.cyan;
    public Color colorBackRow = Color.blue;
    public Color colorGuardian = Color.green;
    public Color colorAttack = Color.red;
    public Color colorDamage = new Color(1f, 0.3f, 0f);
    public Color colorRide = new Color(0f, 1f, 0.5f);
    public Color colorStride = new Color(0.8f, 0f, 1f);
    public Color colorTrigger = Color.magenta;
    public Color colorGuard = new Color(0f, 0.8f, 0.2f);

    void Awake()
    {
        gs = GameState.Instance;
        board = FindFirstObjectByType<BoardLayout>();
    }

    void Start()
    {
        StartCoroutine(SetupAndRun());
    }

    // Waits for CardDatabase if present, builds players, then starts the game
    IEnumerator SetupAndRun()
    {
        if (ApiCardDatabase.Instance != null)
            yield return new WaitUntil(() => ApiCardDatabase.Instance.IsLoaded);

        gs.players[0] = CreatePlayer("Player 1");
        gs.players[0].playerIndex = 0;
        gs.players[1] = CreatePlayer("Player 2");
        gs.players[1].playerIndex = 1;

        gs.players[0].vanguardCircle = gs.players[0].rideDeck[0];
        gs.players[1].vanguardCircle = gs.players[1].rideDeck[0];

        gs.players[0].DrawCard(5);
        gs.players[1].DrawCard(5);

        LogSetupSummary();

        yield return StartCoroutine(RunTestGame());
    }

    // Runs the full test game turn by turn until a player loses
    IEnumerator RunTestGame()
    {
        yield return new WaitForSeconds(1f);
        Debug.Log("=== TEST GAME STARTING ===");

        gs.isFirstTurn = true;
        gs.currentPlayerIndex = 0;
        gs.turnNumber = 0;

        while (!gs.gameOver)
        {
            yield return StartCoroutine(RunTestTurn());

            if (gs.gameOver) break;

            gs.SwapCurrentPlayer();
            gs.turnNumber++;
            gs.isFirstTurn = false;
        }

        Debug.Log("=== GAME OVER ===");
    }

    // Runs one full turn for the current player
    IEnumerator RunTestTurn()
    {
        PlayerState cp = gs.CurrentPlayer;
        PlayerState opp = gs.Opponent;
        string pName = cp.playerName;

        Debug.Log("=== " + pName + " TURN " + gs.turnNumber + " ===");

        // STAND PHASE
        yield return StartCoroutine(TestStandPhase(cp));

        // DRAW PHASE
        yield return StartCoroutine(TestDrawPhase(cp));

        // RIDE PHASE
        yield return StartCoroutine(TestRidePhase(cp));

        // MAIN PHASE
        yield return StartCoroutine(TestMainPhase(cp));

        // BATTLE PHASE
        if (!(gs.isFirstTurn && gs.currentPlayerIndex == 0))
            yield return StartCoroutine(TestBattlePhase(cp, opp));

        // END PHASE
        yield return StartCoroutine(TestEndPhase(cp));

        gs.CheckWinCondition();
    }

    // Stands all cards and logs it
    IEnumerator TestStandPhase(PlayerState cp)
    {
        gs.currentPhase = TurnPhase.StandPhase;
        gs.BroadcastState();
        Debug.Log("[STAND] Standing all cards for " + cp.playerName);

        foreach (CardInstance card in cp.AllFieldCards())
            card.orientation = CardOrientation.Stand;

        HighlightAllFieldCircles(cp, Color.white);
        yield return new WaitForSeconds(highlightDuration);
    }

    // Draws one card and logs it
    IEnumerator TestDrawPhase(PlayerState cp)
    {
        gs.currentPhase = TurnPhase.DrawPhase;
        gs.BroadcastState();
        if (cp.mainDeck.Count == 0) { gs.gameOver = true; yield break; }

        cp.DrawCard(1);
        gs.BroadcastState();
        Debug.Log("[DRAW] " + cp.playerName + " draws. Hand: " + cp.hand.Count + " cards. Deck: " + cp.mainDeck.Count);
        yield return new WaitForSeconds(actionDelay * 0.5f);
    }

    // Rides up one grade from ride deck if possible, then strides if grade 3
    IEnumerator TestRidePhase(PlayerState cp)
    {
        gs.currentPhase = TurnPhase.RidePhase;
        gs.BroadcastState();
        int currentGrade = cp.vanguardCircle != null ? cp.vanguardCircle.data.grade : -1;
        int nextGrade = currentGrade + 1;

        // Try to ride from ride deck up to grade 3
        CardInstance rideTarget = cp.rideDeck.FirstOrDefault(c => c.data.grade == nextGrade);

        if (rideTarget != null && nextGrade <= 3)
        {
            // Pay ride cost — discard one card from hand
            if (cp.hand.Count > 0)
            {
                CardInstance discard = cp.hand[Random.Range(0, cp.hand.Count)];
                cp.hand.Remove(discard);
                cp.dropZone.Add(discard);
                Debug.Log("[RIDE COST] " + cp.playerName + " discards " + discard.data.cardName);
            }

            // Perform the ride
            CardInstance previous = cp.vanguardCircle;
            cp.rideDeck.Remove(rideTarget);

            if (previous != null) cp.soul.Add(previous);

            cp.vanguardCircle = rideTarget;
            rideTarget.orientation = CardOrientation.Stand;

            HighlightVanguardCircle(cp, colorRide);
            Debug.Log("[RIDE] " + cp.playerName + " rides " + rideTarget.data.cardName + " (Grade " + rideTarget.data.grade + ")");
            yield return new WaitForSeconds(highlightDuration);
            ResetPlayerCircles(cp);
        }
        else
        {
            Debug.Log("[RIDE] " + cp.playerName + " stays on " + (cp.vanguardCircle?.data.cardName ?? "none"));
        }

        // STRIDE — attempt if vanguard is grade 3 and G zone has face down cards
        if (cp.vanguardCircle != null && cp.vanguardCircle.data.grade >= 3)
        {
            CardInstance gUnit = cp.gZone.FirstOrDefault(c => c.facing == CardFacing.FaceDown);

            // Need at least one grade 3 card in hand to pay stride cost
            CardInstance strideCostCard = cp.hand.FirstOrDefault(c => c.data.grade >= 3);

            if (gUnit != null && strideCostCard != null)
            {
                cp.hand.Remove(strideCostCard);
                cp.dropZone.Add(strideCostCard);
                Debug.Log("[STRIDE COST] " + cp.playerName + " discards " + strideCostCard.data.cardName);

                CardInstance heart = cp.vanguardCircle;
                cp.gZone.Remove(gUnit);
                gUnit.facing = CardFacing.FaceUp;
                gUnit.orientation = CardOrientation.Stand;
                cp.vanguardCircle = gUnit;

                if (heart != null) cp.soul.Add(heart);

                HighlightVanguardCircle(cp, colorStride);
                Debug.Log("[STRIDE] " + cp.playerName + " strides " + gUnit.data.cardName + "!");
                yield return new WaitForSeconds(highlightDuration);
                ResetPlayerCircles(cp);
            }
        }
    }

    // Calls units to empty rear-guard circles from hand
    IEnumerator TestMainPhase(PlayerState cp)
    {
        gs.currentPhase = TurnPhase.MainPhase;
        gs.BroadcastState();
        int vgGrade = cp.vanguardCircle?.data.grade ?? 0;

        // Try to fill front row then back row
        for (int i = 0; i < cp.frontRowRearguards.Length; i++)
        {
            if (cp.frontRowRearguards[i] == null)
            {
                CardInstance unit = cp.hand.FirstOrDefault(c => c.data.grade <= vgGrade && !c.data.isTriggerUnit);
                if (unit == null) break;

                cp.hand.Remove(unit);
                cp.frontRowRearguards[i] = unit;
                unit.orientation = CardOrientation.Stand;

                string circleKey = cp.CirclePrefix + "_";
                circleKey += i == 0 ? "LeftFront" : "RightFront";

                board?.HighlightCircle(circleKey, colorFrontRow);
                Debug.Log("[CALL] " + cp.playerName + " calls " + unit.data.cardName + " to " + circleKey);
                yield return new WaitForSeconds(highlightDuration);
                board?.ResetCircleColor(circleKey);
            }
        }

        for (int i = 0; i < cp.backRowRearguards.Length; i++)
        {
            if (cp.backRowRearguards[i] == null)
            {
                CardInstance unit = cp.hand.FirstOrDefault(c => c.data.grade <= vgGrade && !c.data.isTriggerUnit);
                if (unit == null) break;

                cp.hand.Remove(unit);
                cp.backRowRearguards[i] = unit;
                unit.orientation = CardOrientation.Stand;

                string circleKey = cp.CirclePrefix + "_";
                circleKey += i == 0 ? "LeftBack" : "RightBack";

                board?.HighlightCircle(circleKey, colorBackRow);
                Debug.Log("[CALL] " + cp.playerName + " calls " + unit.data.cardName + " to " + circleKey);
                yield return new WaitForSeconds(highlightDuration);
                board?.ResetCircleColor(circleKey);
            }
        }

        // Fill center back (behind vanguard column)
        if (cp.centerBackRearguard == null)
        {
            CardInstance unit = cp.hand.FirstOrDefault(c => c.data.grade <= vgGrade && !c.data.isTriggerUnit);
            if (unit != null)
            {
                cp.hand.Remove(unit);
                cp.centerBackRearguard = unit;
                unit.orientation = CardOrientation.Stand;

                string circleKey = cp.CirclePrefix + "_CenterBack";
                board?.HighlightCircle(circleKey, colorBackRow);
                Debug.Log("[CALL] " + cp.playerName + " calls " + unit.data.cardName + " to " + circleKey);
                yield return new WaitForSeconds(highlightDuration);
                board?.ResetCircleColor(circleKey);
            }
        }

        yield return new WaitForSeconds(actionDelay * 0.5f);
    }

    // Attacks with vanguard and all standing front row rear-guards
    IEnumerator TestBattlePhase(PlayerState cp, PlayerState opp)
    {
        gs.currentPhase = TurnPhase.BattlePhase;
        gs.BroadcastState();
        Debug.Log("[BATTLE] " + cp.playerName + " starts battle phase");

        // Collect all attackers — vanguard first then front row rear-guards
        List<CardInstance> attackers = new List<CardInstance>();

        if (cp.vanguardCircle != null && cp.vanguardCircle.orientation == CardOrientation.Stand)
            attackers.Add(cp.vanguardCircle);

        foreach (CardInstance rg in cp.frontRowRearguards)
            if (rg != null && rg.orientation == CardOrientation.Stand)
                attackers.Add(rg);

        foreach (CardInstance attacker in attackers)
        {
            if (gs.gameOver) yield break;

            yield return StartCoroutine(TestAttack(cp, opp, attacker));
            yield return new WaitForSeconds(actionDelay * 0.3f);
        }
    }

    // Performs one full attack — boost, guard, drive/damage check
    IEnumerator TestAttack(PlayerState cp, PlayerState opp, CardInstance attacker)
    {
        // Rest the attacker
        attacker.orientation = CardOrientation.Rest;

        // Apply boost from back row same column
        CardInstance booster = GetBooster(cp, attacker);
        int totalPower = attacker.data.power + attacker.powerModifier;

        if (booster != null)
        {
            booster.orientation = CardOrientation.Rest;
            totalPower += booster.data.power;
            Debug.Log("[BOOST] " + booster.data.cardName + " boosts " + attacker.data.cardName + " → " + totalPower + " power");
        }

        // Defender is always the opponent vanguard
        CardInstance defender = opp.vanguardCircle;
        int defendPower = defender != null ? defender.data.power + defender.powerModifier : 0;

        // Highlight attacker and defender
        HighlightUnit(cp, attacker, colorAttack);
        HighlightUnit(opp, defender, colorGuard);
        Debug.Log("[ATTACK] " + attacker.data.cardName + " (" + totalPower + ") attacks " + defender?.data.cardName + " (" + defendPower + ")");
        yield return new WaitForSeconds(highlightDuration);

        // GUARD STEP — opponent randomly chooses to guard or not
        int guardShield = 0;
        if (opp.hand.Count > 0 && Random.value > 0.5f)
        {
            // Pick a random card from hand to guard with
            CardInstance guardian = opp.hand[Random.Range(0, opp.hand.Count)];
            opp.hand.Remove(guardian);
            opp.guardianCircle.Add(guardian);
            guardShield = guardian.data.shield;

            string guardKey = opp.CirclePrefix + "_Guardian";
            board?.HighlightCircle(guardKey, colorGuard);
            Debug.Log("[GUARD] " + opp.playerName + " guards with " + guardian.data.cardName + " (shield " + guardShield + ")");
            yield return new WaitForSeconds(highlightDuration);
            board?.ResetCircleColor(guardKey);
        }

        // DRIVE STEP — only if attacker is vanguard
        if (attacker == cp.vanguardCircle)
            yield return StartCoroutine(TestDriveCheck(cp, attacker));

        // DAMAGE STEP — check if attack hits
        int finalDefend = defendPower + guardShield;
        bool hits = totalPower >= finalDefend;

        Debug.Log("[DAMAGE] ATK " + totalPower + " vs DEF " + finalDefend + " → " + (hits ? "HIT!" : "NO HIT"));

        if (hits && defender == opp.vanguardCircle)
        {
            int damage = attacker.data.critical + attacker.criticalModifier;
            yield return StartCoroutine(TestDamageCheck(opp, damage));
        }

        // Retire guardians
        foreach (CardInstance g in opp.guardianCircle.ToList())
            opp.dropZone.Add(g);
        opp.guardianCircle.Clear();

        ResetPlayerCircles(cp);
        ResetPlayerCircles(opp);

        gs.CheckWinCondition();
        yield return new WaitForSeconds(actionDelay * 0.5f);
    }

    // Performs drive checks for the attacking vanguard
    IEnumerator TestDriveCheck(PlayerState cp, CardInstance attacker)
    {
        int drives = attacker.data.drive;
        Debug.Log("[DRIVE] " + attacker.data.cardName + " performs " + drives + " drive check(s)");

        for (int i = 0; i < drives; i++)
        {
            if (cp.mainDeck.Count == 0) yield break;

            CardInstance driveCard = cp.mainDeck.Pop();
            Debug.Log("[DRIVE CHECK] Revealed: " + driveCard.data.cardName +
                      (driveCard.data.isTriggerUnit ? " *** TRIGGER: " + driveCard.data.triggerIcon + " ***" : ""));

            if (driveCard.data.isTriggerUnit)
            {
                HighlightVanguardCircle(cp, colorTrigger);
                yield return StartCoroutine(TestResolveTrigger(cp, driveCard.data.triggerIcon, "drive"));
                yield return new WaitForSeconds(highlightDuration);
                ResetPlayerCircles(cp);
            }

            cp.hand.Add(driveCard);
            gs.BroadcastState();
            yield return new WaitForSeconds(actionDelay * 0.5f);
        }
    }

    // Performs damage checks — one card per damage point
    IEnumerator TestDamageCheck(PlayerState damagedPlayer, int damageAmount)
    {
        for (int i = 0; i < damageAmount; i++)
        {
            if (damagedPlayer.damageZone.Count >= 6)
            {
                gs.gameOver = true;
                Debug.Log("*** " + damagedPlayer.playerName + " has taken 6 damage — LOSES! ***");
                yield break;
            }

            if (damagedPlayer.mainDeck.Count == 0)
            {
                gs.gameOver = true;
                Debug.Log("*** " + damagedPlayer.playerName + " has no cards in deck — LOSES! ***");
                yield break;
            }

            CardInstance damageCard = damagedPlayer.mainDeck.Pop();
            damagedPlayer.damageZone.Add(damageCard);
            gs.BroadcastState();

            string dmgKey = damagedPlayer.CirclePrefix + "_Damage";
            board?.HighlightCircle(dmgKey, colorDamage);

            Debug.Log("[DAMAGE CHECK] " + damagedPlayer.playerName + " takes damage! Revealed: " + damageCard.data.cardName +
                      " | Damage: " + damagedPlayer.damageZone.Count + "/6");

            if (damageCard.data.isTriggerUnit)
            {
                Debug.Log("[DAMAGE TRIGGER] " + damageCard.data.triggerIcon + " trigger!");
                yield return StartCoroutine(TestResolveTrigger(damagedPlayer, damageCard.data.triggerIcon, "damage"));
            }

            yield return new WaitForSeconds(highlightDuration);
            board?.ResetCircleColor(dmgKey);

            gs.CheckWinCondition();
            if (gs.gameOver) yield break;
        }
    }

    // Resolves a trigger effect automatically
    IEnumerator TestResolveTrigger(PlayerState cp, TriggerIcon icon, string context)
    {
        switch (icon)
        {
            case TriggerIcon.Critical:
                if (cp.vanguardCircle != null)
                {
                    cp.vanguardCircle.criticalModifier += 1;
                    Debug.Log("[TRIGGER] Critical → Vanguard +1 critical, unit +10000 power");
                }
                GivePowerToRandomUnit(cp, 10000);
                break;

            case TriggerIcon.Draw:
                cp.DrawCard(1);
                GivePowerToRandomUnit(cp, 10000);
                Debug.Log("[TRIGGER] Draw → draw 1 card, unit +10000 power");
                break;

            case TriggerIcon.Stand:
                CardInstance standTarget = cp.AllRearguards().FirstOrDefault(c => c.orientation == CardOrientation.Rest);
                if (standTarget != null)
                {
                    standTarget.orientation = CardOrientation.Stand;
                    Debug.Log("[TRIGGER] Stand → " + standTarget.data.cardName + " stands");
                }
                GivePowerToRandomUnit(cp, 10000);
                break;

            case TriggerIcon.Heal:
                if (cp.damageZone.Count >= gs.Opponent.damageZone.Count && cp.damageZone.Count > 0)
                {
                    CardInstance healCard = cp.damageZone[cp.damageZone.Count - 1];
                    cp.damageZone.Remove(healCard);
                    cp.dropZone.Add(healCard);
                    Debug.Log("[TRIGGER] Heal → healed 1 damage. Damage now: " + cp.damageZone.Count + "/6");
                }
                GivePowerToRandomUnit(cp, 10000);
                break;

            case TriggerIcon.Front:
                foreach (CardInstance unit in cp.frontRowRearguards.Where(c => c != null))
                    unit.powerModifier += 10000;
                if (cp.vanguardCircle != null)
                    cp.vanguardCircle.powerModifier += 10000;
                Debug.Log("[TRIGGER] Front → all front row units +10000 power");
                break;

            case TriggerIcon.Over:
                GivePowerToRandomUnit(cp, 100000000);
                cp.DrawCard(1);
                Debug.Log("[TRIGGER] Over → unit +100000000 power, draw 1");
                break;
        }

        yield return null;
    }

    // Clears power and critical modifiers at end of turn
    IEnumerator TestEndPhase(PlayerState cp)
    {
        gs.currentPhase = TurnPhase.EndPhase;
        gs.BroadcastState();
        Debug.Log("[END] " + cp.playerName + " end phase. Damage: " + cp.damageZone.Count + "/6 | Hand: " + cp.hand.Count + " | Deck: " + cp.mainDeck.Count);

        // Clear turn modifiers
        foreach (CardInstance card in cp.AllFieldCards())
        {
            card.powerModifier = 0;
            card.criticalModifier = 0;
        }

        yield return new WaitForSeconds(actionDelay * 0.5f);
    }

    // Returns the booster in the same column behind the attacker
    CardInstance GetBooster(PlayerState cp, CardInstance attacker)
    {
        if (attacker == cp.vanguardCircle && cp.centerBackRearguard != null
            && cp.centerBackRearguard.data.hasBoost
            && cp.centerBackRearguard.orientation == CardOrientation.Stand)
            return cp.centerBackRearguard;

        for (int i = 0; i < cp.frontRowRearguards.Length; i++)
        {
            if (cp.frontRowRearguards[i] == attacker)
            {
                CardInstance candidate = i < cp.backRowRearguards.Length ? cp.backRowRearguards[i] : null;
                if (candidate != null && candidate.data.hasBoost && candidate.orientation == CardOrientation.Stand)
                    return candidate;
            }
        }
        return null;
    }

    // Gives +power to a random standing unit
    void GivePowerToRandomUnit(PlayerState cp, int power)
    {
        List<CardInstance> units = cp.AllFieldCards().ToList();
        if (units.Count == 0) return;
        units[Random.Range(0, units.Count)].powerModifier += power;
    }

    // Highlights the vanguard circle for the given player
    void HighlightVanguardCircle(PlayerState cp, Color color)
    {
        board?.HighlightCircle(cp.CirclePrefix + "_Vanguard", color);
    }

    // Highlights all field circles for a player
    void HighlightAllFieldCircles(PlayerState cp, Color color)
    {
        string[] zones = { "_Vanguard", "_LeftFront", "_RightFront", "_LeftBack", "_CenterBack", "_RightBack" };
        foreach (string z in zones) board?.HighlightCircle(cp.CirclePrefix + z, color);
    }

    // Highlights the circle a specific unit is on
    void HighlightUnit(PlayerState cp, CardInstance unit, Color color)
    {
        if (unit == null) return;
        string prefix = cp.CirclePrefix;

        if (unit == cp.vanguardCircle) { board?.HighlightCircle(prefix + "_Vanguard", color); return; }
        if (cp.frontRowRearguards[0] == unit) { board?.HighlightCircle(prefix + "_LeftFront", color); return; }
        if (cp.frontRowRearguards[1] == unit) { board?.HighlightCircle(prefix + "_RightFront", color); return; }
        if (cp.backRowRearguards[0] == unit) { board?.HighlightCircle(prefix + "_LeftBack", color); return; }
        if (cp.backRowRearguards[1] == unit) { board?.HighlightCircle(prefix + "_RightBack", color); return; }
        if (cp.centerBackRearguard == unit) { board?.HighlightCircle(prefix + "_CenterBack", color); return; }
    }

    // Resets all field circle colors for a player
    void ResetPlayerCircles(PlayerState cp)
    {
        string[] zones = { "_Vanguard", "_LeftFront", "_RightFront", "_LeftBack", "_CenterBack", "_RightBack", "_Guardian", "_Damage" };
        foreach (string z in zones) board?.ResetCircleColor(cp.CirclePrefix + z);
    }

    // Creates a player — uses CardDatabase if loaded, otherwise falls back to synthetic test cards
    PlayerState CreatePlayer(string name)
    {
        PlayerState player = new PlayerState();
        player.playerName = name;

        bool useDatabase = ApiCardDatabase.Instance != null
                        && ApiCardDatabase.Instance.IsLoaded
                        && ApiCardDatabase.Instance.CardCount > 0;

        if (useDatabase)
        {
            List<CardData> rideData = ApiCardDatabase.Instance.BuildRideDeck();
            player.rideDeck = rideData.Count >= 4
                ? rideData.Take(4).Select(d => new CardInstance(d)).ToList()
                : BuildFallbackRideDeck();

            List<CardInstance> deck = ApiCardDatabase.Instance.BuildMainDeck(vanguardGrade: 3)
                                        .Select(d => new CardInstance(d)).ToList();

            Debug.Log("[Setup] " + name + " using database deck (" + deck.Count + " cards)");
            ShuffleList(deck);
            foreach (CardInstance card in deck)
                player.mainDeck.Push(card);
        }
        else
        {
            player.rideDeck = BuildFallbackRideDeck();
            BuildFallbackMainDeck(player, name);
        }

        // G zone is always synthetic (grade 4 units are not available from the API)
        player.gZone = new List<CardInstance>
        {
            MakeGUnit("G Unit Alpha", power: 15000, drive: 2, facing: CardFacing.FaceDown),
            MakeGUnit("G Unit Beta", power: 15000, drive: 2, facing: CardFacing.FaceDown),
            MakeGUnit("G Unit Gamma", power: 15000, drive: 2, facing: CardFacing.FaceDown),
            MakeGUnit("G Unit Delta", power: 15000, drive: 2, facing: CardFacing.FaceDown)
        };

        return player;
    }

    // Fallback ride deck of synthetic test cards
    List<CardInstance> BuildFallbackRideDeck()
    {
        return new List<CardInstance>
        {
            new CardInstance(MakeCard("Test Unit G0", grade: 0, power: 5000, shield: 10000, drive: 1, hasBoost: true)),
            new CardInstance(MakeCard("Test Unit G1", grade: 1, power: 8000, shield: 5000, drive: 1, hasBoost: true)),
            new CardInstance(MakeCard("Test Unit G2", grade: 2, power: 10000, shield: 5000, drive: 1, hasIntercept: true)),
            new CardInstance(MakeCard("Test Unit G3", grade: 3, power: 13000, shield: 0, drive: 2, hasPersonaRide: true))
        };
    }

    // Fallback 50-card main deck of synthetic test cards
    void BuildFallbackMainDeck(PlayerState player, string name)
    {
        List<CardInstance> deck = new List<CardInstance>();

        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Critical Trigger", grade: 0, power: 5000, shield: 10000, drive: 1, trigger: TriggerIcon.Critical)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Draw Trigger", grade: 0, power: 5000, shield: 10000, drive: 1, trigger: TriggerIcon.Draw)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Heal Trigger", grade: 0, power: 5000, shield: 10000, drive: 1, trigger: TriggerIcon.Heal)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Stand Trigger", grade: 0, power: 5000, shield: 10000, drive: 1, trigger: TriggerIcon.Stand)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Sentinel Unit", grade: 1, power: 8000, shield: 15000, drive: 1, hasSentinel: true)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Booster G1-A", grade: 1, power: 8000, shield: 5000, drive: 1, hasBoost: true)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Booster G1-B", grade: 1, power: 7000, shield: 5000, drive: 1, hasBoost: true)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Interceptor G2", grade: 2, power: 10000, shield: 5000, drive: 1, hasIntercept: true)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Attacker G2", grade: 2, power: 12000, shield: 5000, drive: 1)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Vanguard G3-A", grade: 3, power: 13000, shield: 0, drive: 2, hasPersonaRide: true)));
        for (int i = 0; i < 4; i++) deck.Add(new CardInstance(MakeCard("Vanguard G3-B", grade: 3, power: 13000, shield: 0, drive: 2)));
        for (int i = 0; i < 6; i++) deck.Add(new CardInstance(MakeCard("Attacker G3", grade: 3, power: 12000, shield: 0, drive: 2)));

        Debug.Assert(deck.Count == 50, name + " fallback deck must be 50 cards, got: " + deck.Count);

        ShuffleList(deck);
        foreach (CardInstance card in deck)
            player.mainDeck.Push(card);
    }

    // Creates a CardData ScriptableObject
    CardData MakeCard(
        string cardName, int grade = 0, int power = 5000, int shield = 5000,
        int critical = 1, int drive = 1, TriggerIcon trigger = TriggerIcon.None,
        ImaginaryGift gift = ImaginaryGift.None, bool hasBoost = false,
        bool hasIntercept = false, bool hasSentinel = false, bool hasPersonaRide = false)
    {
        CardData data = ScriptableObject.CreateInstance<CardData>();
        data.cardName = cardName;
        data.grade = grade;
        data.power = power;
        data.shield = shield;
        data.critical = critical;
        data.drive = drive;
        data.triggerIcon = trigger;
        data.imaginaryGift = gift;
        data.hasBoost = hasBoost;
        data.hasIntercept = hasIntercept;
        data.hasSentinel = hasSentinel;
        data.hasPersonaRideIcon = hasPersonaRide;
        data.isTriggerUnit = trigger != TriggerIcon.None;
        return data;
    }

    // Creates a G unit CardInstance
    CardInstance MakeGUnit(string cardName, int power, int drive, CardFacing facing)
    {
        CardData data = ScriptableObject.CreateInstance<CardData>();
        data.cardName = cardName;
        data.grade = 4;
        data.power = power;
        data.critical = 1;
        data.drive = drive;

        CardInstance inst = new CardInstance(data);
        inst.facing = facing;
        return inst;
    }

    // Fisher-Yates shuffle
    void ShuffleList(List<CardInstance> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            CardInstance t = list[i];
            list[i] = list[j];
            list[j] = t;
        }
    }

    // Logs setup summary to Console
    void LogSetupSummary()
    {
        Debug.Log("=== TEST SETUP COMPLETE ===");
        for (int i = 0; i < 2; i++)
        {
            PlayerState p = gs.players[i];
            Debug.Log(p.playerName + " | Deck: " + p.mainDeck.Count + " | Ride deck: " + p.rideDeck.Count + " | G zone: " + p.gZone.Count + " | Hand: " + p.hand.Count);
        }
    }
}

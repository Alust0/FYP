using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// All possible turn phases in D format
public enum TurnPhase
{
    StandPhase,
    DrawPhase, // no G Assist
    RidePhase,
    MainPhase,
    BattlePhase,
    EndPhase
}

// Orientation and facing states for any card
public enum CardOrientation { Stand, Rest }
public enum CardFacing { FaceUp, FaceDown }

// Gift types available in D format clans
public enum ImaginaryGift { None, Force, Accel, Protect }
public enum GiftType { None, TypeI, TypeII }

// All trigger icons a card can have
public enum TriggerIcon { None, Critical, Draw, Stand, Heal, Front, Over }
public enum AbilityType { Activated, Automatic, Continuous, Order }

// Holds all static data for a single card
[CreateAssetMenu(fileName = "NewCard", menuName = "Vanguard/CardData")]
public class CardData : ScriptableObject
{
    public string cardName;
    public string cardNameEn; // English translated name, if available
    // English name if there is one, for notices and the match log; cardName stays the key for matching cards
    public string DisplayName => string.IsNullOrEmpty(cardNameEn) ? cardName : cardNameEn;
    public string cardNumber; // set/number code, e.g. "DZ-BT03-008" — matches Cloud Reco target metadata
    public string clan;
    public string nation;
    public string race;
    public string raceJa; // as printed, e.g. "ギアドラゴン", which Japanese skill text names in 〈〉
    public string rarity;
    public string illustrator;
    public string skillText; // skill badge, e.g. "Twin Drive!!" — distinct from the full abilityText
    public string cardType; // as the database names it, e.g. "Normal Unit", "Trigger Unit", "Normal Order"
    public string flavorText;
    public string imageUrl; // card art, for detail-panel display
    public int grade; // 0 through 4
    public int power;
    public int shield;
    public int critical = 1; // default one
    public int drive; // vanguard drive count
    public bool hasPersonaRideIcon;
    public ImaginaryGift imaginaryGift; // Force Accel Protect
    public TriggerIcon triggerIcon; // Critical Draw Stand Heal Front Over
    public AbilityType abilityType;
    public string abilityText;
    public string abilityTextJa; // the official Japanese text, which SkillParser reads; abilityText is what players see
    public bool isTriggerUnit;
    public bool hasSentinel;
    public bool isOrderCard;
    public bool isBlitzOrder;         
    public bool hasBoost;
    public bool hasOverDress;
    public bool hasIntercept;
}

// Represents a single imaginary gift marker placed on a circle
public class GiftMarker
{
    public ImaginaryGift clanType; // Force Accel Protect
    public GiftType giftType; // TypeI or TypeII
    public int circleIndex; // target circle index on field

    /* Force I → Power +10000 your turn
     Force II → Critical becomes 2
     Accel I → new circle Power +10000
     Accel II → new circle Power +5000 draw
     Protect I → Protect pseudo-card hand
     Protect II → intercept Shield +10000 */

    public GiftMarker(ImaginaryGift clan, GiftType type, int circle)
    {
        clanType = clan;
        giftType = type;
        circleIndex = circle;
    }
}

// Runtime instance of a card on the field with live modifiers
public class CardInstance
{
    public readonly string id = System.Guid.NewGuid().ToString("N").Substring(0, 12); // names this physical card to the other PC online
    public CardData data;
    public CardOrientation orientation = CardOrientation.Stand;
    public CardFacing facing = CardFacing.FaceUp;
    public int powerModifier;
    public int criticalModifier;
    public bool isLocked;
    public bool isMusicOrder;
    public bool cannotDriveCheckThisTurn;
    public bool hasDriveByEffect;
    public PlayerState zoneOwner; // whose side of the board it's on, null when off the board
    public string zone; // BoardLayout key suffix, e.g. "Vanguard", or "Soul"; null when off the board

    public CardInstance(CardData cardData)
    {
        data = cardData;
    }

    // Returns total power including modifiers
    public int TotalPower => data.power + powerModifier;

    // Returns total critical including modifiers
    public int TotalCritical => data.critical + criticalModifier;

    // Returns true if this card is currently on any field circle
    public bool IsOnField(PlayerState owner)
    {
        if (owner.vanguardCircle == this) return true;
        if (owner.centerBackRearguard == this) return true;
        foreach (var c in owner.frontRowRearguards) if (c == this) return true;
        foreach (var c in owner.backRowRearguards) if (c == this) return true;
        return false;
    }
}

// All zones belonging to one player
public class PlayerState
{
    public string playerName;
    public int playerIndex; // 0 for P1, 1 for P2
    public string CirclePrefix => playerIndex == 0 ? "P1" : "P2";
    public CardInstance vanguardCircle;
    public CardInstance[] frontRowRearguards = new CardInstance[2]; // left right
    public CardInstance[] backRowRearguards = new CardInstance[2]; // left right
    public CardInstance centerBackRearguard; // behind the vanguard column
    public List<CardInstance> guardianCircle = new List<CardInstance>(); // cleared each battle
    public List<CardInstance> hand = new List<CardInstance>();
    public List<CardInstance> mainDeck = new List<CardInstance>(); // only cards seen placed on it; the face-down deck is invisible
    public List<CardInstance> rideDeck = new List<CardInstance>(); // grade 0 to 3
    public List<CardInstance> dropZone = new List<CardInstance>();
    public List<CardInstance> soul = new List<CardInstance>(); // cards under vanguard
    public List<CardInstance> damageZone = new List<CardInstance>(); // lose at 6
    public List<CardInstance> orderZone = new List<CardInstance>(); // active set orders
    public List<CardInstance> crestZone = new List<CardInstance>(); // crests, e.g. Energy Generator
    public List<CardInstance> bindZone = new List<CardInstance>();
    public List<CardInstance> triggerZone = new List<CardInstance>(); // card being drive/damage checked
    public List<CardInstance> removedZone = new List<CardInstance>(); // Out of Game
    public List<CardInstance> gZone = new List<CardInstance>(); // shares space with order zone on playmat
    public CardInstance previousVanguard; // what the last vanguard placement rode over; null if the circle was empty
    public List<GiftMarker> giftMarkers = new List<GiftMarker>();
    public GiftType chosenForceType = GiftType.None;
    public GiftType chosenAccelType = GiftType.None;
    public GiftType chosenProtectType = GiftType.None;

    // Returns all cards currently on the field and order zone
    public IEnumerable<CardInstance> AllFieldCards()
    {
        if (vanguardCircle != null) yield return vanguardCircle;
        if (centerBackRearguard != null) yield return centerBackRearguard;
        foreach (var c in frontRowRearguards) if (c != null) yield return c;
        foreach (var c in backRowRearguards) if (c != null) yield return c;
        foreach (var c in orderZone) yield return c;
    }

    // Returns all rear-guard instances currently on field
    public IEnumerable<CardInstance> AllRearguards()
    {
        foreach (var c in frontRowRearguards) if (c != null) yield return c;
        foreach (var c in backRowRearguards) if (c != null) yield return c;
        if (centerBackRearguard != null) yield return centerBackRearguard;
    }

    /* Cards in a zone the camera can't count one by one: the face-down deck, soul charges, and
     extra copies of a card (Cloud Reco tracks only one copy per card). Added to the tracked
     cards, and can go negative to remove a tracked card that left unseen. "FaceDownDamage"
     counts Counter Blast paid with untracked damage. */
    public Dictionary<string, int> untrackedCards = new Dictionary<string, int>();

    public int Untracked(string zone) => untrackedCards.TryGetValue(zone, out int n) ? n : 0;

    public const int MaxDamage = 6;

    /* Damage still face up, i.e. what's left to pay Counter Blast with. Kept as counts, not read off
     the cards: damage cards overlap, and the camera losing one isn't it being flipped */
    public int FaceUpDamage => Mathf.Max(0, Untracked("Damage") - Untracked("FaceDownDamage"));

    /* Changes a zone's count without a tracked card, never below zero cards in total. Damage stops at
     6 (the game is over by then), and no more of it can be face down than there is */
    public void AdjustCount(string zone, int delta)
    {
        int tracked = CountInZone(zone) - Untracked(zone);
        int count = Mathf.Max(-tracked, Untracked(zone) + delta);
        if (zone == "Damage") count = Mathf.Min(count, MaxDamage);
        if (zone == "FaceDownDamage") count = Mathf.Clamp(count, 0, Untracked("Damage"));
        untrackedCards[zone] = count;
        if (zone == "Damage" && Untracked("FaceDownDamage") > count) untrackedCards["FaceDownDamage"] = count; // a heal takes face-down damage too
    }

    // Multi-card zone for a BoardLayout key suffix; null for single-card circles and unknown names
    public List<CardInstance> ZoneList(string zone)
    {
        switch (zone)
        {
            case "Guardian": return guardianCircle;
            case "Damage": return damageZone;
            case "Drop": return dropZone;
            case "Deck": return mainDeck;
            case "RideDeck": return rideDeck;
            case "Order": return orderZone;
            case "Crest": return crestZone;
            case "Bind": return bindZone;
            case "Trigger": return triggerZone;
            case "OOG": return removedZone;
            case "Soul": return soul;
            default: return null;
        }
    }

    // The card on a single-card circle; null if empty or not a circle
    public CardInstance CardOnCircle(string zone)
    {
        switch (zone)
        {
            case "Vanguard": return vanguardCircle;
            case "LeftFront": return frontRowRearguards[0];
            case "RightFront": return frontRowRearguards[1];
            case "LeftBack": return backRowRearguards[0];
            case "RightBack": return backRowRearguards[1];
            case "CenterBack": return centerBackRearguard;
            default: return null;
        }
    }

    /* Tracked cards in a zone plus its untracked count. Damage is the count alone (RulesEngine counts
     each damage check, heal and HUD +/-): damage cards overlap, so the camera both loses them and reads
     one card as two */
    public int CountInZone(string zone)
    {
        if (zone == "Damage") return Untracked(zone);
        List<CardInstance> list = ZoneList(zone);
        int tracked = list != null ? list.Count : (CardOnCircle(zone) != null ? 1 : 0);
        return tracked + Untracked(zone);
    }

    // Removes a card from whichever zone it occupies, e.g. when a scanned card moves zones
    public void RemoveCardFromAllZones(CardInstance card)
    {
        if (vanguardCircle == card) vanguardCircle = null;
        if (centerBackRearguard == card) centerBackRearguard = null;
        for (int i = 0; i < frontRowRearguards.Length; i++) if (frontRowRearguards[i] == card) frontRowRearguards[i] = null;
        for (int i = 0; i < backRowRearguards.Length; i++) if (backRowRearguards[i] == card) backRowRearguards[i] = null;

        if (card.zone != null) ZoneList(card.zone)?.Remove(card);
    }

    /* Places a card into the zone named by a BoardLayout circle-key suffix (the part after
     "P1_"/"P2_", e.g. "Vanguard"). Returns false for unknown zone names. */
    public bool PlaceCardInZone(CardInstance card, string zoneSuffix)
    {
        RemoveCardFromAllZones(card);

        switch (zoneSuffix)
        {
            case "Vanguard":
                // Riding: the old vanguard is now under the new one, i.e. in the soul
                previousVanguard = vanguardCircle; // null when placed on an empty circle
                if (previousVanguard != null)
                {
                    soul.Add(previousVanguard);
                    previousVanguard.zone = "Soul";
                }
                vanguardCircle = card;
                return true;
            case "LeftFront": frontRowRearguards[0] = card; return true;
            case "RightFront": frontRowRearguards[1] = card; return true;
            case "LeftBack": backRowRearguards[0] = card; return true;
            case "RightBack": backRowRearguards[1] = card; return true;
            case "CenterBack": centerBackRearguard = card; return true;
        }

        List<CardInstance> list = ZoneList(zoneSuffix);
        if (list == null) return false;
        list.Add(card);
        return true;
    }
}

// Singleton that tracks the full state of a running game
public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    public PlayerState[] players = new PlayerState[2]; // index 0 first
    public int currentPlayerIndex;
    public int turnNumber;
    public TurnPhase currentPhase;
    public bool isFirstTurn; // no attack turn1
    public int orderPlayedThisTurn; // limit one per turn
    public bool gameOver;
    public int winnerIndex = -1; // once gameOver: the winner's player index, or -1 for a draw
    public string endReason;     // e.g. "Bo reached 6 damage"
    public int orderLimit = 1;
    readonly bool[] mHadDeck = new bool[2]; // per player: the deck count has been above 0
    [System.NonSerialized] public string attackTarget; // not saved in the scene, which would load it as "" not null. Defender's circle under attack, e.g. "LeftFront"; null when there's no battle or it's not picked yet

    // Events that AROverlay and UI subscribe to
    public static System.Action<TurnPhase, string> OnPhaseChanged;
    public static System.Action<GameState> OnGameStateUpdated;
    public static System.Action<int, int> OnBattleStarted;
    public static System.Action OnCloseStep;
    public static System.Action<ImaginaryGift, GiftType> OnGiftReceived;
    public static System.Action<string> OnPersonaRide;
    public static System.Action<TriggerIcon, string> OnTriggerActivated;
    public static System.Action<string, int, int> OnCardPowerChanged;
    public static System.Action<string, string> OnRulePrompt;
    public static System.Action<string, string> OnRuleNotice; // title, body — board-wide referee messages
    // Just before a notice that's something to do (a skill's cost or step, a trigger pick...): it stays up while stillToDo is true
    public static System.Action<string, string, System.Func<bool>> OnRuleToDo; // title, body, stillToDo
    public static System.Action OnGameOver; // after EndGame's "Game Over" notice; winnerIndex and endReason are set

    // Tracker-driven card changes; the card's zone/orientation/facing is already updated when these fire
    public static System.Action<CardInstance, PlayerState, string> OnCardMoved; // card, previous owner, previous zone
    public static System.Action<CardInstance> OnCardOrientationChanged;
    public static System.Action<CardInstance> OnCardFacingChanged;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Returns the player whose turn it currently is
    public PlayerState CurrentPlayer => players[currentPlayerIndex];

    // Returns the opponent of the current player
    public PlayerState Opponent => players[1 - currentPlayerIndex];

    /* Moves a card to a zone, or off the board when owner/zone is null. Trackers (Vuforia, or
     OpenCV later) only ever call this, SetOrientation and SetFacing, so the rules don't care which one runs. */
    public void MoveCard(CardInstance card, PlayerState owner, string zone)
    {
        if (gameOver) return; // the board is final once the game is over
        // An order is never a unit: on the vanguard or a rear-guard circle it isn't counted as on the board
        if (card.data.isOrderCard && Skill.IsUnitCircle(zone))
        {
            OnRuleNotice?.Invoke("Rule check", card.data.DisplayName + " is an order, so it can't go on a circle. Put it in the order zone.");
            OnRulePrompt?.Invoke(card.data.cardName, "Orders go in the order zone.");
            owner = null;
            zone = null;
        }
        if (card.zoneOwner == owner && card.zone == zone) return;

        PlayerState fromOwner = card.zoneOwner;
        string fromZone = card.zone;
        fromOwner?.RemoveCardFromAllZones(card);
        card.zoneOwner = null;
        card.zone = null;

        if (owner != null && zone != null && owner.PlaceCardInZone(card, zone))
        {
            card.zoneOwner = owner;
            card.zone = zone;
        }

        MatchLogger.CardMoved(card, fromOwner, fromZone);
        OnCardMoved?.Invoke(card, fromOwner, fromZone);
        BroadcastState();
    }

    public void SetOrientation(CardInstance card, CardOrientation orientation)
    {
        if (gameOver || card.orientation == orientation) return;
        card.orientation = orientation;
        MatchLogger.CardTurned(card);
        OnCardOrientationChanged?.Invoke(card);
    }

    // Corrects a zone count by hand (HUD +/- now, gestures later) or from rules bookkeeping
    public void AdjustCount(PlayerState player, string zone, int delta)
    {
        if (gameOver) return;
        player.AdjustCount(zone, delta);
        MatchLogger.Action(player.playerName + ": " + zone + " " + delta.ToString("+0;-0") + " (now " + player.CountInZone(zone) + ")");
        CheckWinCondition();
        BroadcastState();
    }

    public void SetFacing(CardInstance card, CardFacing facing)
    {
        if (gameOver || card.facing == facing) return;
        card.facing = facing;
        MatchLogger.CardFlipped(card);
        OnCardFacingChanged?.Invoke(card);
        BroadcastState();
    }

    /* Six damage or an empty deck loses. The deck is the HUD's count, as the camera can't see it,
     and only once it has had cards (the starting count, or online the opponent's first update).
     A vanguard hidden under a hand isn't really gone, so no-vanguard isn't auto-checked. */
    public void CheckWinCondition()
    {
        if (gameOver || players[0] == null || players[1] == null) return;

        bool[] loses = new bool[2];
        string reason = null;
        for (int i = 0; i < 2; i++)
        {
            int deck = players[i].CountInZone("Deck");
            if (deck > 0) mHadDeck[i] = true;

            if (players[i].CountInZone("Damage") >= 6) reason = players[i].playerName + " reached 6 damage";
            else if (mHadDeck[i] && deck <= 0) reason = players[i].playerName + "'s deck ran out";
            else continue;
            loses[i] = true;
        }
        if (!loses[0] && !loses[1]) return;

        EndGame(loses[0] && loses[1] ? null : players[loses[0] ? 1 : 0], loses[0] && loses[1] ? "both players lost at once" : reason);
    }

    // Ends the match; a null winner is a draw (MatchTimer's time running out)
    public void EndGame(PlayerState winner, string reason)
    {
        if (gameOver) return;
        gameOver = true;
        winnerIndex = winner != null ? winner.playerIndex : -1;
        endReason = reason;
        OnRuleNotice?.Invoke("Game Over", (winner != null ? winner.playerName + " wins" : "Draw") + " — " + reason + ".");
        OnGameOver?.Invoke();
    }

    // Broadcasts the current phase and game state to all listeners
    public void BroadcastState()
    {
        if (players[0] == null || players[1] == null) return;
        CheckWinCondition(); // every count change passes through here, e.g. the deck's last draw
        OnPhaseChanged?.Invoke(currentPhase, CurrentPlayer.playerName);
        OnGameStateUpdated?.Invoke(this);
    }

    // Swaps the current player index to pass the turn
    public void SwapCurrentPlayer()
    {
        currentPlayerIndex = 1 - currentPlayerIndex;
    }

    // Clears all power and critical modifiers that lasted only one turn
    public void ClearTurnModifiers()
    {
        foreach (var player in players)
        {
            foreach (var card in player.AllFieldCards())
            {
                card.powerModifier = 0;
                card.criticalModifier = 0;
                card.cannotDriveCheckThisTurn = false;
            }
        }
    }

    // Clears power and critical modifiers that lasted only one battle
    public void ClearBattleModifiers(CardInstance attacker)
    {
        if (attacker != null)
            attacker.powerModifier = 0;
    }
}

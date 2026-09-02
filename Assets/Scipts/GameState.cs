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

    // Force I → Power +10000 your turn
    // Force II → Critical becomes 2
    // Accel I → new circle Power +10000
    // Accel II → new circle Power +5000 draw
    // Protect I → Protect pseudo-card hand
    // Protect II → intercept Shield +10000

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
    public CardData data;
    public CardOrientation orientation = CardOrientation.Stand;
    public CardFacing facing = CardFacing.FaceUp;
    public int powerModifier;
    public int criticalModifier;
    public bool isLocked;
    public bool isMusicOrder;
    public bool cannotDriveCheckThisTurn;
    public bool hasDriveByEffect;

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
    public Stack<CardInstance> mainDeck = new Stack<CardInstance>(); // exactly 50 cards
    public List<CardInstance> rideDeck = new List<CardInstance>(); // grade 0 to 3
    public List<CardInstance> dropZone = new List<CardInstance>();
    public List<CardInstance> soul = new List<CardInstance>(); // cards under vanguard
    public List<CardInstance> damageZone = new List<CardInstance>(); // lose at 6
    public List<CardInstance> orderZone = new List<CardInstance>(); // active set orders
    public List<CardInstance> gZone = new List<CardInstance>(); // shares space with order zone on playmat
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

    // Draws the top card from the main deck into hand
    public void DrawCard(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (mainDeck.Count == 0) return;
            hand.Add(mainDeck.Pop());
        }
    }

    // Returns true if the main deck is empty
    public bool IsDeckEmpty() => mainDeck.Count == 0;

    // Returns true if both vanguard circle and soul are empty
    public bool HasNoVanguard() => vanguardCircle == null && soul.Count == 0;
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
    public int orderLimit = 1;

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

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Returns the player whose turn it currently is
    public PlayerState CurrentPlayer => players[currentPlayerIndex];

    // Returns the opponent of the current player
    public PlayerState Opponent => players[1 - currentPlayerIndex];

    // Checks all loss conditions for both players
    public void CheckWinCondition()
    {
        bool p0Loses = players[0].damageZone.Count >= 6 // six damage
                    || players[0].IsDeckEmpty() // empty deck
                    || players[0].HasNoVanguard(); // no vanguard

        bool p1Loses = players[1].damageZone.Count >= 6
                    || players[1].IsDeckEmpty()
                    || players[1].HasNoVanguard();

        if (p0Loses && p1Loses)
        {
            Debug.Log("DRAW");
            gameOver = true;
        }
        else if (p0Loses)
        {
            Debug.Log(players[0].playerName + " LOSES");
            gameOver = true;
        }
        else if (p1Loses)
        {
            Debug.Log(players[1].playerName + " LOSES");
            gameOver = true;
        }
    }

    // Broadcasts the current phase and game state to all listeners
    public void BroadcastState()
    {
        if (players[0] == null || players[1] == null) return;
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

using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Always-on-screen HUD showing phase, damage counts, deck and hand sizes
public class PhaseHUDOverlay : MonoBehaviour
{
    [Header("Phase Display")]
    public TextMeshProUGUI phaseText;
    public TextMeshProUGUI turnPlayerText;
    public Image phaseIcon;

    [Header("Player 1 Stats")]
    public TextMeshProUGUI damageCounterP1;
    public TextMeshProUGUI deckCountP1;
    public TextMeshProUGUI handCountP1;

    [Header("Player 2 Stats")]
    public TextMeshProUGUI damageCounterP2;
    public TextMeshProUGUI deckCountP2;
    public TextMeshProUGUI handCountP2;

    [Header("Phase Icons")]
    public Sprite standIcon;
    public Sprite drawIcon;
    public Sprite rideIcon;
    public Sprite mainIcon;
    public Sprite battleIcon;
    public Sprite endIcon;

    void OnEnable()
    {
        GameState.OnPhaseChanged += OnPhaseChanged;
        GameState.OnGameStateUpdated += OnGameStateUpdated;
    }

    void OnDisable()
    {
        GameState.OnPhaseChanged -= OnPhaseChanged;
        GameState.OnGameStateUpdated -= OnGameStateUpdated;
    }

    // Updates phase name, icon and turn player label
    public void OnPhaseChanged(TurnPhase newPhase, string turnPlayerName)
    {
        if (phaseText != null) phaseText.text = FormatPhaseName(newPhase);
        if (turnPlayerText != null) turnPlayerText.text = turnPlayerName + "'s Turn";
        if (phaseIcon != null) phaseIcon.sprite = GetPhaseSprite(newPhase);
    }

    // Updates all player stat counters
    public void OnGameStateUpdated(GameState state)
    {
        if (state.players[0] == null || state.players[1] == null) return;

        PlayerState p1 = state.players[0];
        PlayerState p2 = state.players[1];

        if (damageCounterP1 != null)
        {
            damageCounterP1.text = "DMG: " + p1.damageZone.Count + "/6";
            damageCounterP1.color = p1.damageZone.Count >= 5 ? Color.red : Color.white; // red at 5 damage
        }

        if (damageCounterP2 != null)
        {
            damageCounterP2.text = "DMG: " + p2.damageZone.Count + "/6";
            damageCounterP2.color = p2.damageZone.Count >= 5 ? Color.red : Color.white;
        }

        if (deckCountP1 != null) deckCountP1.text = "Deck: " + p1.mainDeck.Count;
        if (deckCountP2 != null) deckCountP2.text = "Deck: " + p2.mainDeck.Count;
        if (handCountP1 != null) handCountP1.text = "Hand: " + p1.hand.Count;
        if (handCountP2 != null) handCountP2.text = "Hand: " + p2.hand.Count;
    }

    // Returns a human readable phase label
    string FormatPhaseName(TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.StandPhase: return "Stand Phase";
            case TurnPhase.DrawPhase: return "Draw Phase";
            case TurnPhase.RidePhase: return "Ride Phase";
            case TurnPhase.MainPhase: return "Main Phase";
            case TurnPhase.BattlePhase: return "Battle Phase";
            case TurnPhase.EndPhase: return "End Phase";
            default: return "Unknown";
        }
    }

    // Returns the sprite matching the current phase
    Sprite GetPhaseSprite(TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.StandPhase: return standIcon;
            case TurnPhase.DrawPhase: return drawIcon;
            case TurnPhase.RidePhase: return rideIcon;
            case TurnPhase.MainPhase: return mainIcon;
            case TurnPhase.BattlePhase: return battleIcon;
            case TurnPhase.EndPhase: return endIcon;
            default: return null;
        }
    }
}

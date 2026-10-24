using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Always-on-screen HUD showing the phase and each player's zone counts
public class PhaseHUDOverlay : MonoBehaviour
{
    [Header("Phase Display")]
    public TextMeshProUGUI phaseText;
    public TextMeshProUGUI turnPlayerText;
    public Image phaseIcon;

    // One count line on the HUD, e.g. P1's "Drop: 3"
    [System.Serializable]
    public class ZoneCounter
    {
        public int playerIndex;
        public string zone; // PlayerState zone name, or "CB" for face-up damage
        public string label;
        public TextMeshProUGUI text;
    }

    [Header("Zone Counts")]
    public List<ZoneCounter> zoneCounters = new List<ZoneCounter>();

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

    // Updates every zone count line
    public void OnGameStateUpdated(GameState state)
    {
        foreach (ZoneCounter counter in zoneCounters)
        {
            PlayerState player = state.players[counter.playerIndex];
            if (counter.text == null || player == null) continue;

            bool isDamage = counter.zone == "Damage";
            int count = counter.zone == "CB" ? player.FaceUpDamage : player.CountInZone(counter.zone);
            counter.text.text = counter.label + ": " + count + (isDamage ? "/6" : "");
            counter.text.color = isDamage && count >= 5 ? Color.red : Color.white; // red at 5 damage
        }
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

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
    public TextMeshProUGUI waitingText; // RulesEngine.Waiting: what the phase still needs
    public TextMeshProUGUI timerText;   // MatchTimer's time left, when the match has a limit
    public Image phaseIcon;
    public string[] sideLabels = { "Left", "Right" }; // by player index, matching BoardLayout's over-the-board sides

    // One count line on the HUD, e.g. P1's "Drop: 3"
    [System.Serializable]
    public class ZoneCounter
    {
        public int playerIndex;
        public string zone; // PlayerState zone name, or "CB" for face-up damage
        public string label;
        public TextMeshProUGUI text; // or, for Damage and Energy, a bar
        public StatBar bar;
        public List<GameObject> parts = new List<GameObject>(); // the line's text and -/+ buttons, hidden when Energy is off
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
        GameSettings.Changed += OnSettingsChanged;
    }

    void OnDisable()
    {
        GameState.OnPhaseChanged -= OnPhaseChanged;
        GameState.OnGameStateUpdated -= OnGameStateUpdated;
        GameSettings.Changed -= OnSettingsChanged;
    }

    void OnSettingsChanged()
    {
        if (GameState.Instance != null) OnGameStateUpdated(GameState.Instance);
    }

    RulesEngine mRules;

    // Every frame: it changes on voice keywords and counts as well as phase changes
    void Update()
    {
        if (timerText != null)
        {
            timerText.text = MatchTimer.Label;
            timerText.color = MatchTimer.Remaining < 300 ? new Color(1f, 0.4f, 0.35f) : Color.white; // red for the last 5 minutes
        }

        if (waitingText == null) return;
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();
        string waiting = mRules != null ? mRules.Waiting : null;
        waitingText.text = waiting != null ? "Waiting: " + waiting : "";
    }

    // Updates phase name, icon and turn player label
    public void OnPhaseChanged(TurnPhase newPhase, string turnPlayerName)
    {
        if (phaseText != null) phaseText.text = FormatPhaseName(newPhase);
        if (turnPlayerText != null) turnPlayerText.text = FormatTurnLabel(turnPlayerName);
        if (phaseIcon != null) phaseIcon.sprite = GetPhaseSprite(newPhase);
    }

    // e.g. "Player 1 (Left) (Turn 3)"; each player's turn counts as its own turn
    string FormatTurnLabel(string turnPlayerName)
    {
        GameState gs = GameState.Instance;
        if (gs == null) return turnPlayerName + "'s Turn";

        int index = gs.currentPlayerIndex;
        string side = index < sideLabels.Length ? " (" + sideLabels[index] + ")" : "";
        return turnPlayerName + side + " (Turn " + gs.turnNumber + ")";
    }

    // Updates every zone count line
    public void OnGameStateUpdated(GameState state)
    {
        foreach (ZoneCounter counter in zoneCounters)
        {
            bool shown = counter.zone != "Energy" || GameSettings.EnergyEnabled;
            foreach (GameObject part in counter.parts)
                if (part != null && part.activeSelf != shown) part.SetActive(shown);

            PlayerState player = state.players[counter.playerIndex];
            if (counter.bar != null && player != null) counter.bar.Refresh(state); // Damage and Energy (StatBar)
            if (counter.text == null || player == null) continue;

            bool isDamage = counter.zone == "Damage";
            int count = counter.zone == "CB" ? player.FaceUpDamage : player.CountInZone(counter.zone);
            counter.text.text = counter.label + ": " + count + (isDamage ? "/6" : counter.zone == "Energy" ? "/" + RulesEngine.MaxEnergy : "");
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

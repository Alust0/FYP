using UnityEngine;

// Wires GameState events to the overlay components. Attach alongside them on one GameObject.
public class AROverlayEventBridge : MonoBehaviour
{
    [Header("Overlay Components")]
    public PhaseHUDOverlay      phaseHUD;
    public BattleOverlay        battleOverlay;
    public NotificationOverlay  notificationOverlay;
    public VuforiaCardOverlayManager cardOverlayManager;

    void OnEnable()
    {
        GameState.OnCardPowerChanged += HandleCardPowerChanged;   // power update
        GameState.OnRulePrompt       += HandleRulePrompt;          // contextual prompt
        GameState.OnPersonaRide      += HandlePersonaRide;         // persona ride popup
    }

    void OnDisable()
    {
        GameState.OnCardPowerChanged -= HandleCardPowerChanged;
        GameState.OnRulePrompt       -= HandleRulePrompt;
        GameState.OnPersonaRide      -= HandlePersonaRide;
    }

    // Passes a power update to the matching card overlay
    void HandleCardPowerChanged(string cardName, int basePower, int modifier)
    {
        if (cardOverlayManager != null)
            cardOverlayManager.UpdateCardPower(cardName, basePower, modifier);
    }

    // Passes a rule prompt to the matching card overlay
    void HandleRulePrompt(string cardName, string promptText)
    {
        if (cardOverlayManager != null)
            cardOverlayManager.ShowRulePrompt(cardName, promptText);
    }

    // Passes a persona ride event to the notification overlay
    void HandlePersonaRide(string cardName)
    {
        if (notificationOverlay != null)
            notificationOverlay.ShowPersonaRide(cardName);
    }
}

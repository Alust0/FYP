using UnityEngine;

// Forwards GameState card events to the card overlays
public class AROverlayEventBridge : MonoBehaviour
{
    [Header("Overlay Components")]
    public VuforiaCardOverlayManager cardOverlayManager;

    void OnEnable()
    {
        GameState.OnCardPowerChanged += HandleCardPowerChanged;   // power update
        GameState.OnRulePrompt       += HandleRulePrompt;          // contextual prompt
    }

    void OnDisable()
    {
        GameState.OnCardPowerChanged -= HandleCardPowerChanged;
        GameState.OnRulePrompt       -= HandleRulePrompt;
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
}

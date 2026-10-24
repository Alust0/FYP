using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Displays popup notifications for Persona Ride, Stride, Triggers and Gifts
public class NotificationOverlay : MonoBehaviour
{
    [Header("Notification Panel")]
    public GameObject notificationPanel;
    public TextMeshProUGUI notificationTitle;
    public TextMeshProUGUI notificationBody;
    public Image notificationIcon;

    [Header("Auto Hide Duration")]
    public float displayDuration = 3.5f;

    [Header("Trigger Sprites")]
    public Sprite criticalSprite;
    public Sprite drawSprite;
    public Sprite standSprite;
    public Sprite healSprite;
    public Sprite frontSprite;
    public Sprite overSprite;

    [Header("Gift Sprites")]
    public Sprite forceSprite;
    public Sprite accelSprite;
    public Sprite protectSprite;
    [Header("Special Sprites")]
    public Sprite personaRideSprite;
    public Sprite strideSprite;
    private Coroutine hideCoroutine;

    void OnEnable()
    {
        GameState.OnPersonaRide += ShowPersonaRide;
        GameState.OnTriggerActivated += ShowTrigger;
        GameState.OnGiftReceived += ShowGift;
        GameState.OnRuleNotice += ShowRuleNotice;
    }

    void OnDisable()
    {
        GameState.OnPersonaRide -= ShowPersonaRide;
        GameState.OnTriggerActivated -= ShowTrigger;
        GameState.OnGiftReceived -= ShowGift;
        GameState.OnRuleNotice -= ShowRuleNotice;
    }

    // Shows a referee message from the RulesEngine, e.g. a phase reminder or a rule warning
    public void ShowRuleNotice(string title, string body)
    {
        ShowNotification(title, body, null);
    }

    // Shows the Persona Ride notification
    public void ShowPersonaRide(string cardName)
    {
        ShowNotification(
            "Persona Ride!",
            cardName + "\nDraw 1 card. Vanguard gets Power +10000 until end of turn.",
            personaRideSprite
        );
    }

    // Shows a Stride notification
    public void ShowStride(string cardName)
    {
        ShowNotification(
            "Stride!",
            cardName + " strode onto the vanguard circle!",
            strideSprite
        );
    }

    // Shows a trigger notification for drive or damage check
    public void ShowTrigger(TriggerIcon icon, string context)
    {
        string contextLabel = context == "drive" ? "[Drive Check]" : "[Damage Check]";
        string title = contextLabel + " " + GetTriggerName(icon) + " Trigger!";
        string body = GetTriggerEffectText(icon);
        Sprite sprite = GetTriggerSprite(icon);

        ShowNotification(title, body, sprite);
    }

    // Shows a gift notification — not in D format but kept for future format support
    public void ShowGift(ImaginaryGift gift, GiftType giftType)
    {
        string typeLabel = giftType == GiftType.TypeI ? "Type I" : "Type II";
        string title = "Imaginary Gift: " + gift.ToString() + " " + typeLabel;
        string body = GetGiftEffectText(gift, giftType);
        Sprite sprite = GetGiftSprite(gift);

        ShowNotification(title, body, sprite);
    }

    // Shows the notification panel with the given content
    void ShowNotification(string title, string body, Sprite icon)
    {
        if (notificationPanel == null) return;

        if (notificationTitle != null) notificationTitle.text = title;
        if (notificationBody != null) notificationBody.text = body;
        if (notificationIcon != null && icon != null) notificationIcon.sprite = icon;

        notificationPanel.SetActive(true);

        // Reset auto-hide timer if a new notification comes in
        if (hideCoroutine != null) StopCoroutine(hideCoroutine);
        hideCoroutine = StartCoroutine(AutoHide(displayDuration));
    }

    // Auto hides the notification after delay
    IEnumerator AutoHide(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (notificationPanel != null)
            notificationPanel.SetActive(false); // dismiss notification
    }

    // Returns a human readable trigger name
    string GetTriggerName(TriggerIcon icon)
    {
        switch (icon)
        {
            case TriggerIcon.Critical: return "Critical";
            case TriggerIcon.Draw: return "Draw";
            case TriggerIcon.Stand: return "Stand";
            case TriggerIcon.Heal: return "Heal";
            case TriggerIcon.Front: return "Front";
            case TriggerIcon.Over: return "Over";
            default: return "Unknown";
        }
    }

    // Returns the effect description for a trigger type
    string GetTriggerEffectText(TriggerIcon icon)
    {
        switch (icon)
        {
            case TriggerIcon.Critical: return "Choose 1 unit: +1 Critical.\nChoose 1 unit: +Power.";
            case TriggerIcon.Draw: return "Draw 1 card.\nChoose 1 unit: +Power.";
            case TriggerIcon.Stand: return "Stand 1 rear-guard.\nChoose 1 unit: +Power.";
            case TriggerIcon.Heal: return "If your damage ≥ opponent's: heal 1 damage.\nChoose 1 unit: +Power.";
            case TriggerIcon.Front: return "All front row units get +Power.";
            case TriggerIcon.Over: return "Draw 1. Choose 1 unit: Power +100000000.\nThis card is removed from the game.";
            default: return "";
        }
    }

    // Returns the effect description for a gift type
    string GetGiftEffectText(ImaginaryGift gift, GiftType giftType)
    {
        switch (gift)
        {
            case ImaginaryGift.Force:
                return giftType == GiftType.TypeI
                    ? "Unit on chosen circle gets Power +10000 during your turn."
                    : "Original Critical of unit on chosen circle becomes 2.";
            case ImaginaryGift.Accel:
                return giftType == GiftType.TypeI
                    ? "Gain a new rear-guard circle. Unit there gets Power +10000."
                    : "Gain a new rear-guard circle. Draw 1. Unit there gets Power +5000.";
            case ImaginaryGift.Protect:
                return giftType == GiftType.TypeI
                    ? "Add a Protect pseudo-card to your hand."
                    : "Place Protect II marker on a rear-guard circle. Intercept +Shield 10000.";
            default:
                return "";
        }
    }

    // Returns the sprite for a trigger icon
    Sprite GetTriggerSprite(TriggerIcon icon)
    {
        switch (icon)
        {
            case TriggerIcon.Critical: return criticalSprite;
            case TriggerIcon.Draw: return drawSprite;
            case TriggerIcon.Stand: return standSprite;
            case TriggerIcon.Heal: return healSprite;
            case TriggerIcon.Front: return frontSprite;
            case TriggerIcon.Over: return overSprite;
            default: return null;
        }
    }

    // Returns the sprite for a gift type
    Sprite GetGiftSprite(ImaginaryGift gift)
    {
        switch (gift)
        {
            case ImaginaryGift.Force: return forceSprite;
            case ImaginaryGift.Accel: return accelSprite;
            case ImaginaryGift.Protect: return protectSprite;
            default: return null;
        }
    }
}

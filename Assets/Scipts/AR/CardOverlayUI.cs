using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Controls the UI panel anchored to a single detected card
public class CardOverlayUI : MonoBehaviour
{
    [Header("Card Info Text")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI powerText;
    public TextMeshProUGUI criticalText;
    public TextMeshProUGUI gradeText;

    [Header("Icons")]
    public Image orientationIcon; // stand or rest sprite
    public Image triggerIconImage;
    public Image giftIconImage;

    [Header("Orientation Sprites")]
    public Sprite standSprite;
    public Sprite restSprite;

    [Header("Trigger Sprites")]
    public Sprite criticalSprite;
    public Sprite drawSprite;
    public Sprite standTriggerSprite;
    public Sprite healSprite;
    public Sprite frontSprite;
    public Sprite overSprite;

    [Header("Gift Sprites")]
    public Sprite forceSprite;
    public Sprite accelSprite;
    public Sprite protectSprite;

    [Header("Rule Prompt")]
    public GameObject rulePromptPanel;
    public TextMeshProUGUI rulePromptText;

    // Populates all UI fields from a CardData asset
    public void Populate(CardData data)
    {
        if (cardNameText != null) cardNameText.text = data.cardName;
        if (gradeText != null) gradeText.text = "G" + data.grade;
        if (powerText != null) powerText.text = data.power.ToString();
        if (criticalText != null) criticalText.text = "C:" + data.critical;

        SetTriggerIcon(data.triggerIcon);
        SetGiftIcon(data.imaginaryGift);

        if (orientationIcon != null && standSprite != null)
            orientationIcon.sprite = standSprite; // default stand

        if (rulePromptPanel != null)
            rulePromptPanel.SetActive(false); // hidden by default
    }

    // Updates the power display with color coding for modifiers
    public void UpdatePower(int basePower, int modifier)
    {
        if (powerText == null) return;

        int total = basePower + modifier;
        powerText.text = total.ToString();

        if (modifier > 0) powerText.color = Color.green; // boosted green
        else if (modifier < 0) powerText.color = Color.red;
        else powerText.color = Color.white;
    }

    // Updates the critical display
    public void UpdateCritical(int baseCrit, int modifier)
    {
        if (criticalText != null)
            criticalText.text = "C:" + (baseCrit + modifier);
    }

    // Swaps the orientation icon between stand and rest
    public void SetRestIndicator(bool isRested)
    {
        if (orientationIcon == null) return;

        orientationIcon.sprite = isRested ? restSprite : standSprite;
        orientationIcon.color = isRested ? Color.yellow : Color.white; // yellow when rested
    }

    // Shows a contextual rule prompt above the card
    public void ShowRulePrompt(string promptText)
    {
        if (rulePromptPanel == null || rulePromptText == null) return;

        rulePromptText.text = promptText;
        rulePromptPanel.SetActive(true);
        StartCoroutine(AutoHidePrompt(5f)); // auto dismiss after 5s
    }

    // Assigns the correct trigger icon sprite
    void SetTriggerIcon(TriggerIcon icon)
    {
        if (triggerIconImage == null) return;

        triggerIconImage.gameObject.SetActive(icon != TriggerIcon.None);

        switch (icon)
        {
            case TriggerIcon.Critical: triggerIconImage.sprite = criticalSprite; break;
            case TriggerIcon.Draw: triggerIconImage.sprite = drawSprite; break;
            case TriggerIcon.Stand: triggerIconImage.sprite = standTriggerSprite; break;
            case TriggerIcon.Heal: triggerIconImage.sprite = healSprite; break;
            case TriggerIcon.Front: triggerIconImage.sprite = frontSprite; break;
            case TriggerIcon.Over: triggerIconImage.sprite = overSprite; break;
        }
    }

    // Assigns the correct imaginary gift icon sprite
    void SetGiftIcon(ImaginaryGift gift)
    {
        if (giftIconImage == null) return;

        giftIconImage.gameObject.SetActive(gift != ImaginaryGift.None);

        switch (gift)
        {
            case ImaginaryGift.Force: giftIconImage.sprite = forceSprite; break;
            case ImaginaryGift.Accel: giftIconImage.sprite = accelSprite; break;
            case ImaginaryGift.Protect: giftIconImage.sprite = protectSprite; break;
        }
    }

    // Auto hides the rule prompt after a delay
    IEnumerator AutoHidePrompt(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (rulePromptPanel != null)
            rulePromptPanel.SetActive(false); // dismiss prompt
    }
}

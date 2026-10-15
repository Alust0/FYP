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

    [Header("Detail Panel")]
    [Tooltip("Toggled by detailToggleButton; holds the descriptive fields that don't fit the compact overlay.")]
    public GameObject detailPanel;
    public TextMeshProUGUI clanRaceText;
    public TextMeshProUGUI skillBadgeText;
    public TextMeshProUGUI flavorBodyText;
    public TextMeshProUGUI rarityText;
    public Button detailToggleButton;

    bool mDetailOpen;

    void Awake()
    {
        if (detailToggleButton != null)
            detailToggleButton.onClick.AddListener(ToggleDetailPanel);
        if (detailPanel != null)
            detailPanel.SetActive(false);
    }

    /* The detail panel lies flat with the card by default; once opened, billboard it toward
     the camera so long skill/flavor text stays legible. */
    void LateUpdate()
    {
        if (!mDetailOpen || detailPanel == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 toCam = detailPanel.transform.position - cam.transform.position;
        if (toCam.sqrMagnitude < 0.0001f) return;

        detailPanel.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
    }

    public void ToggleDetailPanel()
    {
        mDetailOpen = !mDetailOpen;
        detailPanel?.SetActive(mDetailOpen);
    }

    // Populates all UI fields from a CardData asset
    public void Populate(CardData data)
    {
        /* Prefer the English name: the default TMP font (LiberationSans SDF) has no CJK
         glyphs, so Japanese-only names render as missing-glyph boxes. */
        if (cardNameText != null)
            cardNameText.text = !string.IsNullOrEmpty(data.cardNameEn) ? data.cardNameEn : data.cardName;
        if (gradeText != null) 
            gradeText.text = "G" + data.grade;
        if (powerText != null) 
            powerText.text = data.power.ToString();
        if (criticalText != null) 
            criticalText.text = "C:" + data.critical;

        SetTriggerIcon(data.triggerIcon);
        SetGiftIcon(data.imaginaryGift);

        if (orientationIcon != null && standSprite != null)
            orientationIcon.sprite = standSprite; // default stand

        if (clanRaceText != null)
            clanRaceText.text = string.IsNullOrEmpty(data.clan) ? data.race : data.clan + " / " + data.race;
        if (skillBadgeText != null) 
            skillBadgeText.text = data.skillText;
        if (flavorBodyText != null) 
            flavorBodyText.text = data.flavorText;
        if (rarityText != null) 
            rarityText.text = data.rarity;

        if (rulePromptPanel != null)
            rulePromptPanel.SetActive(false); // hidden by default
    }

    // Updates the power display with color coding for modifiers
    public void UpdatePower(int basePower, int modifier)
    {
        if (powerText == null) return;

        int total = basePower + modifier;
        powerText.text = total.ToString();

        if (modifier > 0) 
            powerText.color = Color.green; // boosted green
        else if (modifier < 0) 
            powerText.color = Color.red;
        else 
            powerText.color = Color.white;
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
        if (orientationIcon == null) 
            return;

        orientationIcon.sprite = isRested ? restSprite : standSprite;
        orientationIcon.color = isRested ? Color.yellow : Color.white; // yellow when rested
    }

    // Shows a contextual rule prompt above the card
    public void ShowRulePrompt(string promptText)
    {
        if (rulePromptPanel == null || rulePromptText == null) 
        {
            return;
        }

        rulePromptText.text = promptText;
        rulePromptPanel.SetActive(true);
        StartCoroutine(AutoHidePrompt(5f)); // auto dismiss after 5s
    }

    // Assigns the correct trigger icon sprite
    void SetTriggerIcon(TriggerIcon icon)
    {
        if (triggerIconImage == null) 
        {
            return;
        }

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
        if (giftIconImage == null) 
        {
            return;
        }

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

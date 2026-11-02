using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public TextMeshProUGUI shieldText;
    public TextMeshProUGUI gradeText;
    public GameObject statBand, shieldBand; // the bands behind power/critical and shield, hidden when the card has none

    // Bright on the overlay's dark bands: raised green, lowered red
    public static readonly Color PowerColor = Color.white;
    public static readonly Color CriticalColor = new Color(1f, 0.85f, 0.2f);
    public static readonly Color ShieldColor = new Color(0.45f, 0.85f, 1f);
    static readonly Color Raised = new Color(0.35f, 1f, 0.35f);
    static readonly Color Lowered = new Color(1f, 0.4f, 0.4f);

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
    [Tooltip("Card name, grade/power line and full skill text, for the skill panel.")]
    public TextMeshProUGUI skillTitleText;
    public TextMeshProUGUI skillInfoText;
    public TextMeshProUGUI skillBodyText;
    public Button detailToggleButton;

    bool mDetailOpen;
    CardData mData;
    CardInstance mInstance; // the card on the board, for its bonuses
    int mShownPower = int.MinValue, mShownCritical = int.MinValue;

    void Awake()
    {
        if (detailToggleButton != null)
            detailToggleButton.onClick.AddListener(ToggleDetailPanel);
        if (detailPanel != null)
            detailPanel.SetActive(false);
    }

    /* Once opened, the detail panel turns to match the camera, so it sits upright on screen
     whichever way the card lies. Looking at the camera with world up instead made it spin: an
     overhead camera looks along world up, which leaves that rotation undefined. With the view
     turned a quarter anticlockwise (RotatedView), it's laid a quarter clockwise to read upright. */
    void LateUpdate()
    {
        ShowStats();
        if (!mDetailOpen || detailPanel == null) return;

        Camera cam = Camera.main;
        if (cam != null)
            detailPanel.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, RotatedView.Turned ? -90f : 0f);
    }

    void OnDestroy() => sOpen.Remove(this);

    public void ToggleDetailPanel()
    {
        CardInfoPanel.Instance?.Show(mData); // the sidebar shows it too
        mDetailOpen = !mDetailOpen;
        detailPanel?.SetActive(mDetailOpen);
        if (mDetailOpen) sOpen.Add(this);
        else sOpen.Remove(this);
    }

    public void CloseDetailPanel()
    {
        mDetailOpen = false;
        detailPanel?.SetActive(false);
        sOpen.Remove(this);
    }

    static readonly List<CardOverlayUI> sOpen = new List<CardOverlayUI>(); // detail panels open now

    /* The detail panels are this player's own, so SplitScreenView hides them while it draws the
     picture the opponent gets, and shows them again straight after */
    public static void HideOpenPanels(bool hide)
    {
        sOpen.RemoveAll(ui => ui == null);
        foreach (CardOverlayUI ui in sOpen)
            if (ui.detailPanel != null) ui.detailPanel.SetActive(!hide);
    }

    // Populates all UI fields from a CardData asset
    public void Populate(CardData data)
    {
        mData = data;
        /* Prefer the English name: the default TMP font (LiberationSans SDF) has no CJK
         glyphs, so Japanese-only names render as missing-glyph boxes. */
        if (cardNameText != null)
            cardNameText.text = !string.IsNullOrEmpty(data.cardNameEn) ? data.cardNameEn : data.cardName;
        if (gradeText != null) 
            gradeText.text = "G" + data.grade;
        // Orders have no power, critical or shield; G3s usually have no shield
        bool unit = !data.isOrderCard;
        if (statBand != null) statBand.SetActive(unit);
        if (shieldBand != null) shieldBand.SetActive(unit && data.shield > 0);
        if (powerText != null)
            SetPower(data.power, 0);
        if (criticalText != null)
            SetCritical(data.critical, 0);
        if (shieldText != null)
            shieldText.text = "<size=50%>SHIELD</size> " + data.shield;

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

        string name = !string.IsNullOrEmpty(data.cardNameEn) ? data.cardNameEn : data.cardName;
        if (skillTitleText != null)
            skillTitleText.text = "<b>" + name + "</b>";
        if (skillInfoText != null)
            skillInfoText.text = TypeLabel(data) + "   Grade " + data.grade + "   Power " + data.power + "   Shield " + data.shield +
                (string.IsNullOrEmpty(data.skillText) ? "" : "   " + data.skillText);
        if (skillBodyText != null)
        {
            string trigger = TriggerEffect(data.triggerIcon);
            string skill = string.IsNullOrWhiteSpace(data.abilityText) ? "<i>No skill.</i>" : FormatSkill(data.abilityText);
            skillBodyText.text = trigger == null ? skill
                : "<color=#b3261e><b>" + data.triggerIcon + " Trigger:</b> " + trigger + "</color>\n\n" + skill;
        }

        if (rulePromptPanel != null)
            rulePromptPanel.SetActive(false); // hidden by default
    }

    /* The database's own type name when it's in English, else worked out from the card (Japanese-only
     entries would show as missing-glyph boxes in the default font) */
    public static string TypeLabel(CardData data)
    {
        string type = data.cardType?.Trim();
        if (!string.IsNullOrEmpty(type) && type.All(c => c < 128)) return type;

        if (data.isOrderCard) return data.isBlitzOrder ? "Blitz Order" : "Normal Order";
        return data.isTriggerUnit ? "Trigger Unit" : "Normal Unit";
    }

    // What the trigger does when drive or damage checked, as RulesEngine.ResolveTrigger applies it
    public static string TriggerEffect(TriggerIcon icon)
    {
        switch (icon)
        {
            case TriggerIcon.Critical: return "One of your units gets Critical +1, and one gets Power +10000 until end of turn.";
            case TriggerIcon.Draw: return "Draw a card, and one of your units gets Power +10000 until end of turn.";
            case TriggerIcon.Front: return "All of your front row units get Power +10000 until end of turn.";
            case TriggerIcon.Heal: return "One of your units gets Power +10000 until end of turn. If your damage is equal to or more than your opponent's, heal 1 damage.";
            case TriggerIcon.Stand: return "Stand one of your rear-guards, and one of your units gets Power +10000 until end of turn.";
            case TriggerIcon.Over: return "Remove this card from the game. Draw a card, one of your units gets Power +100,000,000 until end of turn, and resolve its extra effect.";
            default: return null;
        }
    }

    // Skill markers like [AUTO] or [ACT] in bold blue, so each skill's start stands out
    public static string FormatSkill(string text, string markerColor = "#1f4fa8")
    {
        string safe = ReadableJa(text).Replace("<", "<noparse><</noparse>"); // card text isn't markup
        return Regex.Replace(safe, @"\[[^\]]{1,20}\]", m => "<b><color=" + markerColor + ">" + m.Value + "</color></b>");
    }

    /* The Noto Sans JP font asset has no Japanese brackets or commas (they show as boxes), so they're
     swapped for plain ones; 【自】 becomes [自], which FormatSkill then marks like [AUTO] */
    public static string ReadableJa(string text) => string.IsNullOrEmpty(text) ? text : text
        .Replace('【', '[').Replace('】', ']').Replace('「', '"').Replace('」', '"')
        .Replace('『', '"').Replace('』', '"').Replace('、', ',').Replace('。', '.').Replace('　', ' ');

    // The card this overlay is on: its power and critical then follow its bonuses every frame
    public void Bind(CardInstance instance) => mInstance = instance;

    /* Power as the battle has it (a boosted attacker's includes its booster's). A change rolls the
     number up or down to the new value over RollSeconds, the number swelling as it goes. */
    void ShowStats()
    {
        if (mInstance == null) return;
        if (sRules == null) sRules = FindFirstObjectByType<RulesEngine>();

        int power = sRules != null ? sRules.ShownPower(mInstance) : mInstance.TotalPower;
        if (power != mPowerTo)
        {
            mPowerFrom = mPowerTo == int.MinValue ? power : mShownPower; // from what's showing, even mid-roll
            mPowerTo = power;
            mRollStart = Time.time;
        }
        float t = Mathf.Clamp01((Time.time - mRollStart) / RollSeconds);
        if (t < 1f || mShownPower != power)
        {
            float eased = 1f - (1f - t) * (1f - t) * (1f - t); // fast, then settling
            SetPower(mInstance.data.power, Mathf.RoundToInt(Mathf.Lerp(mPowerFrom, mPowerTo, eased)) - mInstance.data.power);
            if (powerText != null) powerText.rectTransform.localScale = Vector3.one * (1f + 0.3f * Mathf.Sin(t * Mathf.PI));
        }

        if (mInstance.TotalCritical != mShownCritical) UpdateCritical(mInstance.data.critical, mInstance.criticalModifier);
    }

    static RulesEngine sRules;
    const float RollSeconds = 0.9f;
    int mPowerFrom, mPowerTo = int.MinValue;
    float mRollStart;

    // Updates the power display with color coding for modifiers (once bound, ShowStats does)
    public void UpdatePower(int basePower, int modifier)
    {
        if (mInstance == null) SetPower(basePower, modifier);
    }

    // Updates the critical display
    public void UpdateCritical(int baseCrit, int modifier) => SetCritical(baseCrit, modifier);

    void SetPower(int basePower, int modifier)
    {
        if (powerText == null) return;
        mShownPower = basePower + modifier;
        powerText.text = "<size=45%>POWER</size>\n" + mShownPower;
        powerText.color = modifier > 0 ? Raised : modifier < 0 ? Lowered : PowerColor;
    }

    void SetCritical(int baseCrit, int modifier)
    {
        if (criticalText == null) return;
        mShownCritical = baseCrit + modifier;
        criticalText.text = "<size=45%>CRIT</size>\n" + mShownCritical;
        criticalText.color = modifier > 0 ? Raised : modifier < 0 ? Lowered : CriticalColor;
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

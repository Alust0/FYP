using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vuforia;

/* The overlay shown on each tracked card: name, power, critical, shield and the skill panel.
 The rest of VuforiaCardOverlayManager is in VuforiaCardOverlayManager.cs. */
public partial class VuforiaCardOverlayManager
{
    // Builds a minimal overlay canvas when no custom prefab is assigned
    GameObject BuildOverlay(Transform parent)
    {
        if (overlayPrefab != null)
        {
            GameObject instance = Instantiate(overlayPrefab, parent);
            instance.transform.localPosition = Vector3.zero;
            return instance;
        }

        GameObject canvasObj = new GameObject("CardOverlayCanvas");
        canvasObj.transform.SetParent(parent, false);
        canvasObj.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        canvasObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(630f, 880f);
        canvasObj.transform.localScale = Vector3.one * OverlayScale(parent, canvasRT.sizeDelta.x);

        /* Bold, bright text with a black outline on dark bands, so it reads over any card art: the name
         across the top, power and critical over the card's bottom strip, shield sideways on its left edge
         (where the card prints it) */
        RectTransform nameBand = CreateBand(canvasObj.transform, "NameBand", new Vector2(0f, 0.85f), new Vector2(1f, 1f));
        TextMeshProUGUI nameText = Bright(CreateText(nameBand, "CardName", new Vector2(0.03f, 0f), new Vector2(0.77f, 1f), 84, Color.white), 44);

        // Each a small label over a big number (CardOverlayUI.SetPower/SetCritical)
        RectTransform statBand = CreateBand(canvasObj.transform, "StatBand", new Vector2(0f, 0f), new Vector2(1f, 0.23f));
        TextMeshProUGUI powerText = Bright(CreateText(statBand, "Power", new Vector2(0.03f, 0f), new Vector2(0.72f, 1f), 130, Color.white), 70);
        powerText.alignment = TextAlignmentOptions.Left;
        powerText.lineSpacing = -25f;
        TextMeshProUGUI critText = Bright(CreateText(statBand, "Critical", new Vector2(0.72f, 0f), new Vector2(0.97f, 1f), 130, CardOverlayUI.CriticalColor), 70);
        critText.alignment = TextAlignmentOptions.Right;
        critText.lineSpacing = -25f;

        RectTransform shieldBand = CreateBand(canvasObj.transform, "ShieldBand", new Vector2(0.115f, 0.53f), new Vector2(0.115f, 0.53f));
        shieldBand.sizeDelta = new Vector2(520f, 140f); // between the name and stat bands
        shieldBand.localRotation = Quaternion.Euler(0f, 0f, 90f); // reads bottom to top, as printed
        TextMeshProUGUI shieldText = Bright(CreateText(shieldBand, "Shield", Vector2.zero, Vector2.one, 100, CardOverlayUI.ShieldColor), 56);

        // Small tap target in the corner opens the detail panel built below
        GameObject toggleBtnObj = new GameObject("DetailToggleButton");
        toggleBtnObj.transform.SetParent(canvasObj.transform, false);
        RectTransform toggleRT = toggleBtnObj.AddComponent<RectTransform>();
        toggleRT.anchorMin = new Vector2(0.78f, 0.88f);
        toggleRT.anchorMax = new Vector2(1f, 1f);
        toggleRT.offsetMin = Vector2.zero;
        toggleRT.offsetMax = Vector2.zero;
        UnityEngine.UI.Image toggleBg = toggleBtnObj.AddComponent<UnityEngine.UI.Image>();
        toggleBg.color = new Color(1f, 1f, 1f, 0.85f);
        Button toggleBtn = toggleBtnObj.AddComponent<Button>();
        CreateText(toggleBtnObj.transform, "Label", Vector2.zero, Vector2.one, 50, Color.black).text = "i";

        /* Skill panel: name, grade/power line and the card's full skill text. Hidden until toggled,
         then held upright toward the camera (see CardOverlayUI.LateUpdate) so the text is readable.
         Card-shaped and half again as big as the card, since skill text runs long. */
        GameObject detailObj = new GameObject("DetailPanel");
        detailObj.transform.SetParent(canvasObj.transform, false);
        detailObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
        detailObj.transform.localScale = Vector3.one * 1.5f;
        Canvas detailCanvas = detailObj.AddComponent<Canvas>();
        detailCanvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler detailScaler = detailObj.AddComponent<CanvasScaler>();
        detailScaler.dynamicPixelsPerUnit = 100;
        detailObj.AddComponent<GraphicRaycaster>();
        RectTransform detailRT = detailObj.GetComponent<RectTransform>();
        detailRT.sizeDelta = new Vector2(630f, 880f);
        UnityEngine.UI.Image detailBg = detailObj.AddComponent<UnityEngine.UI.Image>();
        detailBg.color = new Color(1f, 1f, 1f, 0.92f); // light so the black text reads

        // It covers the "i" button, so a tap anywhere on it closes it; the X says so
        Button closeBtn = detailObj.AddComponent<Button>();
        closeBtn.targetGraphic = detailBg;
        CreateText(detailObj.transform, "Close", new Vector2(0.88f, 0.9f), new Vector2(0.98f, 0.99f), 48, new Color(0.6f, 0.1f, 0.1f)).text = "X";

        TextMeshProUGUI skillTitle = CreateText(detailObj.transform, "SkillTitle", new Vector2(0.04f, 0.89f), new Vector2(0.87f, 0.98f), 69, Color.black);
        skillTitle.enableAutoSizing = true;
        skillTitle.fontSizeMin = 49;
        skillTitle.fontSizeMax = 69;
        TextMeshProUGUI skillInfo = CreateText(detailObj.transform, "SkillInfo", new Vector2(0.04f, 0.83f), new Vector2(0.96f, 0.89f), 51, new Color(0.25f, 0.25f, 0.25f));
        skillInfo.enableAutoSizing = true;
        skillInfo.fontSizeMin = 41;
        skillInfo.fontSizeMax = 51;
        TextMeshProUGUI skillBody = CreateText(detailObj.transform, "SkillBody", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.82f), 57, Color.black);
        skillBody.alignment = TextAlignmentOptions.TopLeft;
        skillBody.enableAutoSizing = true; // long skills shrink to fit
        skillBody.fontSizeMin = 24;
        skillBody.fontSizeMax = 57;

        CardOverlayUI ui = canvasObj.AddComponent<CardOverlayUI>();
        ui.cardNameText = nameText;
        ui.powerText = powerText;
        ui.criticalText = critText;
        ui.shieldText = shieldText;
        ui.statBand = statBand.gameObject;
        ui.shieldBand = shieldBand.gameObject;
        ui.detailPanel = detailObj;
        ui.skillTitleText = skillTitle;
        ui.skillInfoText = skillInfo;
        ui.skillBodyText = skillBody;
        ui.detailToggleButton = toggleBtn;

        /* ui.Awake() already ran (AddComponent calls it synchronously) before these fields
         were assigned, so wire the listener and initial state here. */
        toggleBtn.onClick.AddListener(ui.ToggleDetailPanel);
        closeBtn.onClick.AddListener(ui.CloseDetailPanel);
        detailObj.SetActive(false);

        return canvasObj;
    }

    /* Scale that makes the overlay canvas as wide as the card. Deck targets are real size (63 mm)
     while Cloud Reco targets use their uploaded width, so a fixed scale only fits one of them. */
    static float OverlayScale(Transform target, float canvasWidth)
    {
        if (!(target.GetComponent<ObserverBehaviour>() is ImageTargetBehaviour image)) return 0.001f;

        float cardWidth = image.GetSize().x;
        float targetScale = target.lossyScale.x;
        if (cardWidth <= 0f || targetScale <= 0f) return 0.001f;
        return cardWidth / (canvasWidth * targetScale);
    }

    // A dark see-through strip behind overlay text; clicks go through it
    static RectTransform CreateBand(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        UnityEngine.UI.Image image = obj.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0f, 0f, 0f, 0.62f);
        image.raycastTarget = false;
        return rt;
    }

    // Bold with a black outline, shrinking (down to minSize) to fit
    static TextMeshProUGUI Bright(TextMeshProUGUI text, float minSize)
    {
        text.fontStyle = FontStyles.Bold;
        text.enableAutoSizing = true;
        text.fontSizeMin = minSize;
        text.fontSizeMax = text.fontSize;
        text.outlineWidth = 0.22f;
        text.outlineColor = Color.black;
        text.raycastTarget = false;
        return text;
    }

    TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int fontSize, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return tmp;
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Automatically creates and attaches a card overlay UI to a Vuforia Image Target
// Attach this script to the ImageTarget GameObject
public class CardOverlaySetup : MonoBehaviour
{
    [Header("Card Data")]
    public CardData cardData;   // assign the matching CardData ScriptableObject in Inspector

    private CardOverlayUI overlayUI;

    void Start()
    {
        CreateOverlay();

        // Populate the overlay if card data is assigned
        if (cardData != null && overlayUI != null)
            overlayUI.Populate(cardData);
    }

    // Builds the overlay Canvas as a child of this Image Target
    void CreateOverlay()
    {
        // Create the World Space Canvas
        GameObject canvasObj        = new GameObject("CardOverlayCanvas");
        canvasObj.transform.SetParent(transform);
        canvasObj.transform.localPosition = new Vector3(0f, 0.01f, 0f);   // float just above the card
        canvasObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // face upward
        canvasObj.transform.localScale    = new Vector3(0.001f, 0.001f, 0.001f);

        Canvas canvas               = canvasObj.AddComponent<Canvas>();
        canvas.renderMode           = RenderMode.WorldSpace;

        CanvasScaler scaler         = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;

        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT  = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta      = new Vector2(630f, 880f);   // standard card ratio

        // Semi-transparent dark background panel
        GameObject bgPanel          = new GameObject("Background");
        bgPanel.transform.SetParent(canvasObj.transform, false);
        Image bgImage               = bgPanel.AddComponent<Image>();
        bgImage.color               = new Color(0f, 0f, 0f, 0.75f);
        RectTransform bgRT          = bgPanel.GetComponent<RectTransform>();
        bgRT.anchorMin              = Vector2.zero;
        bgRT.anchorMax              = Vector2.one;
        bgRT.offsetMin              = Vector2.zero;
        bgRT.offsetMax              = Vector2.zero;

        // Card name — top center
        TextMeshProUGUI cardNameText = CreateText(canvasObj.transform, "CardName",
            new Vector2(0f, 0.88f), new Vector2(1f, 1f), 60, Color.white, TextAlignmentOptions.Center);

        // Grade — top left
        TextMeshProUGUI gradeText    = CreateText(canvasObj.transform, "Grade",
            new Vector2(0f, 0.75f), new Vector2(0.5f, 0.88f), 55, Color.cyan, TextAlignmentOptions.Left);

        // Power — center large
        TextMeshProUGUI powerText    = CreateText(canvasObj.transform, "Power",
            new Vector2(0f, 0.55f), new Vector2(1f, 0.75f), 80, Color.white, TextAlignmentOptions.Center);

        // Critical — bottom left
        TextMeshProUGUI critText     = CreateText(canvasObj.transform, "Critical",
            new Vector2(0f, 0.4f), new Vector2(0.5f, 0.55f), 55, Color.red, TextAlignmentOptions.Left);

        // Stand / Rest indicator — bottom right
        TextMeshProUGUI orientText   = CreateText(canvasObj.transform, "Orientation",
            new Vector2(0.5f, 0.4f), new Vector2(1f, 0.55f), 50, Color.yellow, TextAlignmentOptions.Right);
        orientText.text              = "STAND";

        // Rule prompt panel — hidden by default
        GameObject rulePanel         = new GameObject("RulePromptPanel");
        rulePanel.transform.SetParent(canvasObj.transform, false);
        Image ruleBg                 = rulePanel.AddComponent<Image>();
        ruleBg.color                 = new Color(0.1f, 0.1f, 0.4f, 0.9f);
        RectTransform rulePanelRT    = rulePanel.GetComponent<RectTransform>();
        rulePanelRT.anchorMin        = new Vector2(0f, 0f);
        rulePanelRT.anchorMax        = new Vector2(1f, 0.4f);
        rulePanelRT.offsetMin        = Vector2.zero;
        rulePanelRT.offsetMax        = Vector2.zero;

        TextMeshProUGUI ruleText     = CreateText(rulePanel.transform, "RuleText",
            new Vector2(0f, 0f), new Vector2(1f, 1f), 45, Color.white, TextAlignmentOptions.Center);
        ruleText.text                = "";
        rulePanel.SetActive(false);

        // Attach CardOverlayUI and wire all fields
        overlayUI                    = canvasObj.AddComponent<CardOverlayUI>();
        overlayUI.cardNameText       = cardNameText;
        overlayUI.gradeText          = gradeText;
        overlayUI.powerText          = powerText;
        overlayUI.criticalText       = critText;
        overlayUI.rulePromptPanel    = rulePanel;
        overlayUI.rulePromptText     = ruleText;

        Debug.Log("[CardOverlaySetup] Overlay created for: " + (cardData != null ? cardData.cardName : "unknown card"));
    }

    // Helper that creates a TextMeshPro text object anchored within the canvas
    TextMeshProUGUI CreateText(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, int fontSize,
        Color color, TextAlignmentOptions alignment)
    {
        GameObject obj          = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp     = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize            = fontSize;
        tmp.color               = color;
        tmp.alignment           = alignment;
        tmp.text                = name;

        RectTransform rt        = obj.GetComponent<RectTransform>();
        rt.anchorMin            = anchorMin;
        rt.anchorMax            = anchorMax;
        rt.offsetMin            = new Vector2(10f, 5f);
        rt.offsetMax            = new Vector2(-10f, -5f);

        return tmp;
    }
}

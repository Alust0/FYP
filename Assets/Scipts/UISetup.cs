using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Automatically creates and positions all HUD UI elements at runtime
// Attach this to any GameObject in the scene and press Play
public class UISetup : MonoBehaviour
{
    // References set automatically by this script
    private Canvas canvas;
    private PhaseHUDOverlay phaseHUD;
    private BattleOverlay battleOverlay;
    private NotificationOverlay notificationOverlay;
    private AROverlayEventBridge eventBridge;

    void Awake()
    {
        // Skip setup entirely if the canvas was already built (e.g. by a previous play session
        // or because UISetup exists alongside the pre-built scene objects)
        if (GameObject.Find("AROverlayCanvas") != null)
        {
            Debug.Log("[UISetup] AROverlayCanvas already present — skipping UI creation.");
            return;
        }

        CreateCanvas();
        CreateHUDPanel();
        CreateBattlePanel();
        CreateNotificationPanel();
        WireEventBridge();

        Debug.Log("[UISetup] All UI elements created successfully.");
    }

    // Creates the main Canvas with correct settings
    void CreateCanvas()
    {
        GameObject canvasObj = new GameObject("AROverlayCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();
    }

    // Creates the always-on HUD panel at the top of the screen
    void CreateHUDPanel()
    {
        GameObject hudPanel = CreatePanel("HUDPanel", canvas.transform,
            new Vector2(0f, 1f), // anchor top-left
            new Vector2(0f, 1f),
            new Vector2(10f, -10f), // position top-left with padding
            new Vector2(600f, 220f)); // width height

        // Make panel semi-transparent dark background
        Image img = hudPanel.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);

        // Phase and turn player labels
        TextMeshProUGUI phaseText = CreateText("PhaseText", hudPanel.transform, new Vector2(10f, -10f), new Vector2(580f, 40f), "Stand Phase", 28, TextAlignmentOptions.Left);
        TextMeshProUGUI turnPlayerText = CreateText("TurnPlayerText", hudPanel.transform, new Vector2(10f, -55f), new Vector2(580f, 35f), "Player 1's Turn", 22, TextAlignmentOptions.Left);
        turnPlayerText.color = new Color(0.8f, 0.8f, 0f); // yellow turn indicator

        // Divider label
        CreateText("P1Label", hudPanel.transform, new Vector2(10f, -100f), new Vector2(280f, 25f), "── Player 1 ──", 18, TextAlignmentOptions.Left);
        CreateText("P2Label", hudPanel.transform, new Vector2(300f, -100f), new Vector2(280f, 25f), "── Player 2 ──", 18, TextAlignmentOptions.Left);

        // Player 1 stats
        TextMeshProUGUI damageP1 = CreateText("DamageP1", hudPanel.transform, new Vector2(10f, -130f), new Vector2(280f, 28f), "DMG: 0/6", 20, TextAlignmentOptions.Left);
        TextMeshProUGUI deckP1 = CreateText("DeckP1", hudPanel.transform, new Vector2(10f, -160f), new Vector2(140f, 25f), "Deck: 50", 18, TextAlignmentOptions.Left);
        TextMeshProUGUI handP1 = CreateText("HandP1", hudPanel.transform, new Vector2(150f, -160f), new Vector2(140f, 25f), "Hand: 5", 18, TextAlignmentOptions.Left);

        // Player 2 stats
        TextMeshProUGUI damageP2 = CreateText("DamageP2", hudPanel.transform, new Vector2(300f, -130f), new Vector2(280f, 28f), "DMG: 0/6", 20, TextAlignmentOptions.Left);
        TextMeshProUGUI deckP2 = CreateText("DeckP2", hudPanel.transform, new Vector2(300f, -160f), new Vector2(140f, 25f), "Deck: 50", 18, TextAlignmentOptions.Left);
        TextMeshProUGUI handP2 = CreateText("HandP2", hudPanel.transform, new Vector2(440f, -160f), new Vector2(140f, 25f), "Hand: 5", 18, TextAlignmentOptions.Left);

        // Attach PhaseHUDOverlay and assign all fields
        phaseHUD = hudPanel.AddComponent<PhaseHUDOverlay>();
        phaseHUD.phaseText = phaseText;
        phaseHUD.turnPlayerText = turnPlayerText;
        phaseHUD.damageCounterP1 = damageP1;
        phaseHUD.damageCounterP2 = damageP2;
        phaseHUD.deckCountP1 = deckP1;
        phaseHUD.deckCountP2 = deckP2;
        phaseHUD.handCountP1 = handP1;
        phaseHUD.handCountP2 = handP2;
    }

    // Creates the battle overlay panel in the center of the screen
    void CreateBattlePanel()
    {
        GameObject battlePanel = CreatePanel("BattlePanel", canvas.transform,
            new Vector2(0.5f, 0.5f), // anchor center
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), // center position
            new Vector2(400f, 200f));

        Image img = battlePanel.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.85f);

        TextMeshProUGUI atkText = CreateText("AttackerPower", battlePanel.transform, new Vector2(0f, 60f), new Vector2(380f, 45f), "ATK: 0", 30, TextAlignmentOptions.Center);
        TextMeshProUGUI defText = CreateText("DefenderPower", battlePanel.transform, new Vector2(0f, 10f), new Vector2(380f, 45f), "DEF: 0", 30, TextAlignmentOptions.Center);
        TextMeshProUGUI resultText = CreateText("ResultText", battlePanel.transform, new Vector2(0f, -55f), new Vector2(380f, 55f), "HIT!", 42, TextAlignmentOptions.Center);
        resultText.color = Color.red;

        battlePanel.SetActive(false); // hidden until battle starts

        battleOverlay = battlePanel.AddComponent<BattleOverlay>();
        battleOverlay.battlePanel = battlePanel;
        battleOverlay.attackerPowerText = atkText;
        battleOverlay.defenderPowerText = defText;
        battleOverlay.resultText = resultText;
    }

    // Creates the notification popup panel at the top-center of the screen
    void CreateNotificationPanel()
    {
        GameObject notifPanel = CreatePanel("NotificationPanel", canvas.transform,
            new Vector2(0.5f, 1f), // anchor top-center
            new Vector2(0.5f, 1f),
            new Vector2(0f, -10f), // just below top edge
            new Vector2(700f, 160f));

        Image img = notifPanel.GetComponent<Image>();
        img.color = new Color(0.1f, 0.1f, 0.3f, 0.92f);

        // Icon image on the left
        GameObject iconObj = new GameObject("NotificationIcon");
        iconObj.transform.SetParent(notifPanel.transform, false);
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.color = Color.white;
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.5f);
        iconRT.anchorMax = new Vector2(0f, 0.5f);
        iconRT.pivot = new Vector2(0f, 0.5f);
        iconRT.anchoredPosition = new Vector2(10f, 0f);
        iconRT.sizeDelta = new Vector2(80f, 80f);

        // Title and body text
        TextMeshProUGUI titleText = CreateText("NotifTitle", notifPanel.transform, new Vector2(100f, -15f), new Vector2(580f, 45f), "Persona Ride!", 28, TextAlignmentOptions.Left);
        titleText.color = new Color(1f, 0.85f, 0f); // gold title

        TextMeshProUGUI bodyText = CreateText("NotifBody", notifPanel.transform, new Vector2(100f, -70f), new Vector2(580f, 70f), "Effect text here.", 20, TextAlignmentOptions.Left);
        bodyText.color = Color.white;

        notifPanel.SetActive(false); // hidden until notification fires

        notificationOverlay = notifPanel.AddComponent<NotificationOverlay>();
        notificationOverlay.notificationPanel = notifPanel;
        notificationOverlay.notificationTitle = titleText;
        notificationOverlay.notificationBody = bodyText;
        notificationOverlay.notificationIcon = iconImg;
    }

    // Wires all overlay components to the AROverlayEventBridge
    void WireEventBridge()
    {
        // Reuse existing bridge if already in the scene
        eventBridge = FindFirstObjectByType<AROverlayEventBridge>();
        if (eventBridge == null)
        {
            GameObject bridgeObj = new GameObject("AROverlayEventBridge");
            eventBridge = bridgeObj.AddComponent<AROverlayEventBridge>();
        }

        eventBridge.phaseHUD = phaseHUD;
        eventBridge.battleOverlay = battleOverlay;
        eventBridge.notificationOverlay = notificationOverlay;

        Debug.Log("[UISetup] Event bridge wired.");
    }

    // Helper that creates a UI Panel with the given anchors position and size
    GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.5f);

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        return panel;
    }

    // Helper that creates a TextMeshPro text object with the given settings
    TextMeshProUGUI CreateText(string name, Transform parent, Vector2 position, Vector2 size, string defaultText, int fontSize, TextAlignmentOptions alignment)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = defaultText;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;

        RectTransform rt = textObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        return tmp;
    }
}

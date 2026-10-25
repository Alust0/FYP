using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Creates and positions all HUD UI elements at runtime. Attach to any GameObject in the scene.
public class UISetup : MonoBehaviour
{
    // References set automatically by this script
    private Canvas canvas;
    private PhaseHUDOverlay phaseHUD;
    private BattleOverlay battleOverlay;
    private NotificationOverlay notificationOverlay;
    private AROverlayEventBridge eventBridge;

    // HUD count rows per player: PlayerState zone name ("CB" = face-up damage) and its label
    static readonly (string zone, string label)[] HudZones =
    {
        ("Damage", "DMG"), ("CB", "CB"), ("Soul", "Soul"), ("Drop", "Drop"), ("Deck", "Deck"),
    };

    void Awake()
    {
        // Skip setup if the canvas already exists (earlier play session, or pre-built scene objects)
        if (GameObject.Find("AROverlayCanvas") != null)
        {
            Debug.Log("[UISetup] AROverlayCanvas already present — skipping UI creation.");
            return;
        }

        CreateCanvas();
        CreateHUDPanel();
        CreateBattlePanel();
        CreateNotificationPanel();
        if (MatchSession.IsOnline) CreateOpponentFieldPanel();
        CreateSettingsMenu();
        WireEventBridge();

        gameObject.AddComponent<HoverPads>(); // hand gestures

        // F2 test panel for placing cards without a camera, e.g. in a second editor
        if (Application.isEditor) gameObject.AddComponent<DebugCardPlacer>();

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
            new Vector2(600f, 280f)); // width height

        // Make panel semi-transparent dark background
        Image img = hudPanel.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);

        // Phase and turn player labels
        TextMeshProUGUI phaseText = CreateText("PhaseText", hudPanel.transform, new Vector2(10f, -10f), new Vector2(580f, 40f), "Stand Phase", 28, TextAlignmentOptions.Left);
        TextMeshProUGUI turnPlayerText = CreateText("TurnPlayerText", hudPanel.transform, new Vector2(10f, -55f), new Vector2(580f, 35f), "Player 1 (Left) (Turn 1)", 22, TextAlignmentOptions.Left);
        turnPlayerText.color = new Color(0.8f, 0.8f, 0f); // yellow turn indicator

        // Divider labels: player names, and which one is this PC online
        for (int player = 0; player < 2; player++)
        {
            string name = MatchSession.PlayerNames[player] + (MatchSession.IsOnline && MatchSession.IsLocalSeat(player) ? " (You)" : "");
            CreateText("P" + (player + 1) + "Label", hudPanel.transform, new Vector2(10f + player * 290f, -100f), new Vector2(280f, 25f), "── " + name + " ──", 18, TextAlignmentOptions.Left);
        }

        phaseHUD = hudPanel.AddComponent<PhaseHUDOverlay>();
        if (MatchSession.IsOnline) phaseHUD.sideLabels = new string[0]; // players aren't on a left/right side of one board

        /* A count line per player and zone, with -/+ buttons to fix counts the camera gets wrong.
         Online only for this PC's player: the opponent's counts come from their PC. */
        for (int player = 0; player < 2; player++)
        {
            for (int row = 0; row < HudZones.Length; row++)
            {
                (string zone, string label) = HudZones[row];
                Vector2 pos = new Vector2(10f + player * 290f, -130f - row * 28f);

                TextMeshProUGUI countText = CreateText(label + "P" + (player + 1), hudPanel.transform, pos, new Vector2(160f, 26f), label + ": 0", 18, TextAlignmentOptions.Left);
                if (MatchSession.IsLocalSeat(player))
                {
                    CreateCountButton(hudPanel.transform, pos + new Vector2(170f, 0f), "-", player, zone, -1);
                    CreateCountButton(hudPanel.transform, pos + new Vector2(210f, 0f), "+", player, zone, 1);
                }

                phaseHUD.zoneCounters.Add(new PhaseHUDOverlay.ZoneCounter { playerIndex = player, zone = zone, label = label, text = countText });
            }
        }

        // Ends the current phase; Space and the Next Phase hover pad do the same
        GameObject nextBtnObj = CreatePanel("NextPhaseButton", hudPanel.transform,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(150f, 40f));
        nextBtnObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);
        Button nextBtn = nextBtnObj.AddComponent<Button>();
        nextBtn.onClick.AddListener(() => FindFirstObjectByType<RulesEngine>()?.AdvancePhase());
        CreateText("Label", nextBtnObj.transform, new Vector2(0f, -5f), new Vector2(150f, 30f), "Next Phase >", 20, TextAlignmentOptions.Center);

        phaseHUD.phaseText = phaseText;
        phaseHUD.turnPlayerText = turnPlayerText;
    }

    // A small HUD button that corrects one player's zone count by hand
    void CreateCountButton(Transform parent, Vector2 position, string label, int playerIndex, string zone, int delta)
    {
        GameObject btnObj = CreatePanel("CountButton", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(34f, 24f));
        btnObj.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);
        btnObj.AddComponent<Button>().onClick.AddListener(() => AdjustZoneCount(playerIndex, zone, delta));
        CreateText("Label", btnObj.transform, Vector2.zero, new Vector2(34f, 24f), label, 18, TextAlignmentOptions.Center);
    }

    // Shared by the HUD buttons and HoverPads
    public static void AdjustZoneCount(int playerIndex, string zone, int delta)
    {
        GameState gs = GameState.Instance;
        PlayerState player = gs != null ? gs.players[playerIndex] : null;
        if (player == null) return;

        if (zone != "CB")
        {
            gs.AdjustCount(player, zone, delta);
            return;
        }

        // CB counts face-up damage, so "-" pays Counter Blast by turning one more damage face down
        if (delta < 0 && player.FaceUpDamage == 0) return;
        gs.AdjustCount(player, "FaceDownDamage", -delta);
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

        /* On the canvas, not the hidden panel: a component added to an inactive object doesn't get
         OnEnable, so it never subscribed to the notices and nothing showed */
        notificationOverlay = canvas.gameObject.AddComponent<NotificationOverlay>();
        notificationOverlay.notificationPanel = notifPanel;
        notificationOverlay.notificationTitle = titleText;
        notificationOverlay.notificationBody = bodyText;
        notificationOverlay.notificationIcon = iconImg;
    }

    // Online: the opponent's field in text, top-right, since this PC's camera can't see their cards
    void CreateOpponentFieldPanel()
    {
        GameObject panel = CreatePanel("OpponentFieldPanel", canvas.transform,
            new Vector2(1f, 1f), // anchor top-right
            new Vector2(1f, 1f),
            new Vector2(-10f, -10f),
            new Vector2(440f, 330f));
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        TextMeshProUGUI text = CreateText("OpponentFieldText", panel.transform, new Vector2(12f, -10f), new Vector2(416f, 310f), "", 18, TextAlignmentOptions.TopLeft);
        panel.AddComponent<OpponentFieldOverlay>().fieldText = text;
    }

    // Settings button bottom-left, opening a centered menu (Esc too); built last so it draws over the rest
    void CreateSettingsMenu()
    {
        GameObject buttonObj = CreatePanel("SettingsButton", canvas.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(10f, 10f), new Vector2(170f, 50f));
        buttonObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        Button openButton = buttonObj.AddComponent<Button>();
        CreateText("Label", buttonObj.transform, new Vector2(0f, -9f), new Vector2(170f, 34f), "Settings", 24, TextAlignmentOptions.Center);

        // Dims the game and blocks clicks behind the menu
        GameObject overlay = CreatePanel("SettingsOverlay", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        GameObject box = CreatePanel("SettingsBox", overlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 440f));
        box.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.16f, 0.97f);

        CreateText("Title", box.transform, new Vector2(0f, -22f), new Vector2(520f, 50f), "Settings", 38, TextAlignmentOptions.Center);

        // On the always-visible button, so Esc still works while the menu is hidden
        SettingsMenu menu = buttonObj.AddComponent<SettingsMenu>();
        menu.overlay = overlay;
        openButton.onClick.AddListener(menu.Open);

        CreateMenuButton(box.transform, "Resume", -100f, new Color(0.25f, 0.28f, 0.36f, 0.95f), menu.Close);
        CreateMenuButton(box.transform, "Calibrate Board", -184f, new Color(0.25f, 0.28f, 0.36f, 0.95f), menu.Calibrate);
        Button leave = CreateMenuButton(box.transform, "Leave Match", -268f, Color.red, menu.Leave);
        menu.leaveBackground = leave.GetComponent<Image>();
        menu.leaveLabel = leave.GetComponentInChildren<TextMeshProUGUI>();

        string info = (MatchSession.IsOnline ? "Room " + MatchSession.RoomCode + "   •   " : "") + "Esc: open/close   •   C: calibrate";
        TextMeshProUGUI infoText = CreateText("Info", box.transform, new Vector2(0f, -370f), new Vector2(520f, 40f), info, 18, TextAlignmentOptions.Center);
        infoText.color = new Color(0.7f, 0.72f, 0.78f);
    }

    // A wide button centered across the top of parent, y px down from its top edge
    Button CreateMenuButton(Transform parent, string label, float y, Color color, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = CreatePanel(label + "Button", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(400f, 64f));
        btnObj.GetComponent<Image>().color = color;
        Button button = btnObj.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        CreateText("Label", btnObj.transform, new Vector2(0f, -14f), new Vector2(400f, 40f), label, 26, TextAlignmentOptions.Center);
        return button;
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

using System.IO;
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
    private MatchLogPanel logPanel;
    private AROverlayEventBridge eventBridge;

    /* HUD count rows per player: PlayerState zone name ("CB" = face-up damage) and its label.
     Energy isn't a zone, just a count, and its row hides when GameSettings.EnergyEnabled is off. */
    static readonly (string zone, string label)[] HudZones =
    {
        ("Damage", "DMG"), ("CB", "CB"), ("Soul", "Soul"), ("Drop", "Drop"), ("Deck", "Deck"), ("RideDeck", "Ride"), ("Energy", "Energy"),
    };

    void Awake()
    {
        // Skip setup if the canvas already exists (earlier play session, or pre-built scene objects)
        if (GameObject.Find("AROverlayCanvas") != null)
        {
            Debug.Log("[UISetup] AROverlayCanvas already present — skipping UI creation.");
            return;
        }

        gameObject.AddComponent<MatchLogger>(); // first, so it catches the match from its first phase
        gameObject.AddComponent<MatchTimer>(); // the match's time, and the lobby's time limit

        CreateCanvas();
        CreateBattlePanel();
        CreateNotificationPanel(); // before the sidebar, whose Settings menu has to draw over it
        CreateLogPanel(); // before the sidebar too, whose Settings menu opens it
        CreateSidebar();
        CreateExitButton(); // last, over everything
        WireEventBridge();

        gameObject.AddComponent<VoiceCommands>(); // keywords: next phase, guard, drive check...
        gameObject.AddComponent<HandZones>(); // hand on the deck draws; its on-screen buttons are off (useButtons)
        gameObject.AddComponent<CardBackTracker>(); // face-down cards: the deck, flipped damage
        gameObject.AddComponent<AttackTargetPicker>(); // pops up over the defender's front row when attacking
        gameObject.AddComponent<CounterBlastPicker>(); // which damage cards a Counter Blast flips
        gameObject.AddComponent<OpponentUnitPicker>(); // over the opponent's units a skill has its owner choose
        gameObject.AddComponent<ZoneHighlighter>(); // lights up the zones a skill, check or cost needs cards moved on

        // F2 test panel for placing cards without a camera, e.g. in a second editor
        if (Application.isEditor) gameObject.AddComponent<DebugCardPlacer>();

        Debug.Log("[UISetup] All UI elements created successfully.");
    }

    GameObject mExitButton;

    void OnDestroy() => GameState.OnGameOver -= ShowExitButton;

    // "Exit to Menu" in the bottom-right corner, shown once the game is over
    void CreateExitButton()
    {
        mExitButton = new GameObject("ExitButton", typeof(RectTransform));
        mExitButton.transform.SetParent(canvas.transform, false);
        RectTransform rt = (RectTransform)mExitButton.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-16f, 16f);
        rt.sizeDelta = new Vector2(240f, 70f);
        mExitButton.AddComponent<Image>().color = new Color(0.55f, 0.15f, 0.15f, 0.95f);
        mExitButton.AddComponent<Button>().onClick.AddListener(() => UnityEngine.SceneManagement.SceneManager.LoadScene("Menu"));
        CreateText("Label", mExitButton.transform, Vector2.zero, rt.sizeDelta, "Exit to Menu", 26, TextAlignmentOptions.Center).raycastTarget = false;
        mExitButton.SetActive(false);
        GameState.OnGameOver += ShowExitButton;
    }

    void ShowExitButton()
    {
        if (mExitButton != null) mExitButton.SetActive(true);
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

    static readonly Color SectionColor = new Color(0.11f, 0.12f, 0.17f, 0.95f);
    static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.15f);

    /* Everything but the battle and notice popups sits in a sidebar down the right of the screen,
     beside the camera views (SplitScreenView keeps them out of its way). Top to bottom: buttons,
     phase and counts, the last card played, and online the opponent's field. */
    void CreateSidebar()
    {
        GameObject sidebar = CreatePanel("Sidebar", canvas.transform, new Vector2(1f - SplitScreenView.SidebarWidth, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        Stretch((RectTransform)sidebar.transform, Vector2.zero, Vector2.zero);
        sidebar.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.97f);

        CreateButtonRow(sidebar.transform, 8f, 56f);
        CreatePhaseSection(sidebar.transform, 72f, 302f);
        // Built up from the bottom edge: online the opponent's field, the action buttons, then the card panel fills the rest
        float above = 8f;

        if (MatchSession.IsOnline)
        {
            const float opponentHeight = 152f;
            CreateOpponentFieldSection(sidebar.transform, above, opponentHeight);
            above += opponentHeight + 8f;
        }

        const float actionsHeight = 64f;
        CreateActionSection(sidebar.transform, above, actionsHeight);
        above += actionsHeight + 8f;

        CreateCardSection(sidebar.transform, 382f, above);
    }

    // Guard / no guard, drive and damage check, attack targets: buttons that appear when the battle calls for them
    void CreateActionSection(Transform sidebar, float bottom, float height)
    {
        GameObject sectionObj = CreatePanel("ActionPanel", sidebar, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
        RectTransform section = (RectTransform)sectionObj.transform;
        section.pivot = new Vector2(0.5f, 0f);
        Stretch(section, new Vector2(8f, bottom), new Vector2(-8f, bottom + height));
        sectionObj.GetComponent<Image>().color = SectionColor;

        TextMeshProUGUI hint = CreateFillText("Hint", section, "Battle buttons (guard, drive check...) show here when needed.", 15, TextAlignmentOptions.Center);
        hint.color = new Color(0.55f, 0.58f, 0.65f);

        sectionObj.AddComponent<ActionButtons>().Build(section, hint);
    }

    // Next Phase across most of the row, then Undo, Draw, Settings and Match Log at its right end
    void CreateButtonRow(Transform sidebar, float top, float height)
    {
        RectTransform row = CreateSection("ButtonRow", sidebar, top, height, Color.clear);

        // Ends the current phase; Space and saying "next phase" do the same
        GameObject nextBtnObj = CreatePanel("NextPhaseButton", row, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch((RectTransform)nextBtnObj.transform, Vector2.zero, new Vector2(-364f, 0f));
        nextBtnObj.GetComponent<Image>().color = new Color(0.85f, 0.87f, 0.92f, 0.95f);
        Button nextBtn = nextBtnObj.AddComponent<Button>();
        nextBtn.onClick.AddListener(() => FindFirstObjectByType<RulesEngine>()?.AdvancePhase());
        TextMeshProUGUI nextLabel = CreateFillText("Label", nextBtnObj.transform, "Next Phase  >", 26, TextAlignmentOptions.Center);
        nextLabel.color = new Color(0.08f, 0.09f, 0.13f);

        // Steps the referee back before its last change (RulesEngine.Undo); Ctrl+Z does the same
        GameObject undoObj = CreatePanel("UndoButton", row, new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        RectTransform undoRT = (RectTransform)undoObj.transform;
        undoRT.pivot = new Vector2(1f, 0.5f);
        undoRT.offsetMin = new Vector2(-356f, 0f);
        undoRT.offsetMax = new Vector2(-268f, 0f);
        undoObj.GetComponent<Image>().color = ButtonColor;
        undoObj.AddComponent<Button>().onClick.AddListener(() => FindFirstObjectByType<RulesEngine>()?.Undo());
        CreateFillText("Label", undoObj.transform, "Undo", 22, TextAlignmentOptions.Center);

        // Draws outside the Draw Phase, e.g. from a skill; saying "draw card" does the same
        GameObject drawObj = CreatePanel("DrawButton", row, new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        RectTransform drawRT = (RectTransform)drawObj.transform;
        drawRT.pivot = new Vector2(1f, 0.5f);
        drawRT.offsetMin = new Vector2(-260f, 0f);
        drawRT.offsetMax = new Vector2(-148f, 0f);
        drawObj.GetComponent<Image>().color = ButtonColor;
        drawObj.AddComponent<Button>().onClick.AddListener(() => Draw(LocalPlayer()));
        CreateFillText("Label", drawObj.transform, "Draw", 22, TextAlignmentOptions.Center);

        CreateSettingsMenu(CreateIconButton(row, "SettingsButton", "Settings", 74f, 66f));
        CreateIconButton(row, "LogButton", "Logs", 0f, 66f).onClick.AddListener(logPanel.Toggle);
    }

    /* A button width px wide, right px in from the row's right end, showing StreamingAssets/Icons/<icon>.png
     (swap the file to change it), or the icon's name if there's no such file */
    Button CreateIconButton(Transform row, string name, string icon, float right, float width)
    {
        GameObject obj = CreatePanel(name, row, new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        RectTransform rt = (RectTransform)obj.transform;
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-right - width, 0f);
        rt.offsetMax = new Vector2(-right, 0f);
        obj.GetComponent<Image>().color = ButtonColor;

        Sprite sprite = LoadIcon(icon);
        if (sprite == null)
        {
            CreateFillText("Label", obj.transform, icon, 18, TextAlignmentOptions.Center);
            return obj.AddComponent<Button>();
        }

        GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
        iconObj.transform.SetParent(obj.transform, false);
        RectTransform iconRT = (RectTransform)iconObj.transform;
        iconRT.anchorMin = Vector2.zero;
        iconRT.anchorMax = Vector2.one;
        Stretch(iconRT, new Vector2(12f, 10f), new Vector2(-12f, -10f));
        Image image = iconObj.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return obj.AddComponent<Button>();
    }

    // A picture from StreamingAssets/Icons, e.g. "Settings" for Settings.png; null if it's missing or unreadable
    static Sprite LoadIcon(string name)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Icons", name + ".png");
        if (!File.Exists(path))
        {
            Debug.LogWarning("[UISetup] No icon at " + path + "; showing its name instead.");
            return null;
        }

        Texture2D texture = new Texture2D(2, 2);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Destroy(texture);
            return null;
        }
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    // Phase, turn player and a column of counts per player
    void CreatePhaseSection(Transform sidebar, float top, float height)
    {
        RectTransform section = CreateSection("PhasePanel", sidebar, top, height, SectionColor);

        TextMeshProUGUI phaseText = CreateStretchText("PhaseText", section, 8f, 38f, 12f, 12f, "Stand Phase", 28, TextAlignmentOptions.Left);
        TextMeshProUGUI turnPlayerText = CreateStretchText("TurnPlayerText", section, 46f, 32f, 12f, 12f, "Player 1 (Turn 1)", 20, TextAlignmentOptions.Left);
        turnPlayerText.color = new Color(0.95f, 0.85f, 0.2f); // yellow turn indicator

        // With a time limit, the time left takes the top of the waiting text's corner
        float waitingTop = MatchTimer.HasLimit ? 34f : 8f;
        TextMeshProUGUI waitingText = CreateStretchText("WaitingText", section, waitingTop, 80f - waitingTop, 230f, 12f, "", 17, TextAlignmentOptions.TopRight);
        waitingText.color = new Color(1f, 0.6f, 0.35f);
        waitingText.enableAutoSizing = true;
        waitingText.fontSizeMin = 11;
        waitingText.fontSizeMax = 17;

        phaseHUD = section.gameObject.AddComponent<PhaseHUDOverlay>();
        phaseHUD.waitingText = waitingText;
        if (MatchTimer.HasLimit)
            phaseHUD.timerText = CreateStretchText("TimerText", section, 8f, 24f, 230f, 12f, "", 20, TextAlignmentOptions.TopRight);
        if (MatchSession.IsOneSide) phaseHUD.sideLabels = new string[0]; // players aren't on a left/right side of one board

        for (int player = 0; player < 2; player++)
        {
            // Each player gets half the panel's width
            GameObject column = new GameObject("Player" + (player + 1), typeof(RectTransform));
            column.transform.SetParent(section, false);
            RectTransform columnRT = (RectTransform)column.transform;
            columnRT.anchorMin = new Vector2(player * 0.5f, 0f);
            columnRT.anchorMax = new Vector2(player * 0.5f + 0.5f, 1f);
            columnRT.offsetMin = Vector2.zero;
            columnRT.offsetMax = Vector2.zero;

            string name = MatchSession.PlayerNames[player] + (MatchSession.IsOnline && MatchSession.IsLocalSeat(player) ? " (You)" : "");
            TextMeshProUGUI nameText = CreateStretchText("Name", columnRT, 84f, 24f, 12f, 4f, "— " + name + " —", 17, TextAlignmentOptions.Left);
            nameText.color = new Color(0.75f, 0.78f, 0.85f);

            /* A count line per zone, with -/+ buttons to fix counts the camera gets wrong.
             Online only for this PC's player: the opponent's counts come from their PC. */
            for (int row = 0; row < HudZones.Length; row++)
            {
                (string zone, string label) = HudZones[row];
                Vector2 pos = new Vector2(12f, -112f - row * 26f);

                var counter = new PhaseHUDOverlay.ZoneCounter { playerIndex = player, zone = zone, label = label };
                // Damage and Energy as bars (damage cards red, Counter Blasted gray), the rest as text
                if (zone == "Damage" || zone == "Energy")
                {
                    counter.bar = StatBar.Create(columnRT, pos + new Vector2(0f, -1f), new Vector2(110f, 22f), player, zone == "Damage");
                    counter.parts.Add(counter.bar.gameObject);
                }
                else
                {
                    counter.text = CreateText(label + "P" + (player + 1), columnRT, pos, new Vector2(110f, 24f), label + ": 0", 18, TextAlignmentOptions.Left);
                    counter.parts.Add(counter.text.gameObject);
                }
                if (MatchSession.IsLocalSeat(player))
                {
                    counter.parts.Add(CreateCountButton(columnRT, pos + new Vector2(118f, 0f), "-", player, zone, -1));
                    counter.parts.Add(CreateCountButton(columnRT, pos + new Vector2(156f, 0f), "+", player, zone, 1));
                }

                phaseHUD.zoneCounters.Add(counter);
            }
        }

        phaseHUD.phaseText = phaseText;
        phaseHUD.turnPlayerText = turnPlayerText;
    }

    // A small HUD button that corrects one player's zone count by hand
    GameObject CreateCountButton(Transform parent, Vector2 position, string label, int playerIndex, string zone, int delta)
    {
        GameObject btnObj = CreatePanel("CountButton", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(34f, 24f));
        btnObj.GetComponent<Image>().color = ButtonColor;
        btnObj.AddComponent<Button>().onClick.AddListener(() => AdjustZoneCount(playerIndex, zone, delta));
        CreateText("Label", btnObj.transform, Vector2.zero, new Vector2(34f, 24f), label, 18, TextAlignmentOptions.Center);
        return btnObj;
    }

    // Shared by the HUD buttons and VoiceCommands
    public static void AdjustZoneCount(int playerIndex, string zone, int delta)
    {
        GameState gs = GameState.Instance;
        PlayerState player = gs != null ? gs.players[playerIndex] : null;
        if (player == null) return;
        if (zone == "Energy" && delta > 0 && player.CountInZone("Energy") >= RulesEngine.MaxEnergy) return;

        if (zone != "CB")
        {
            gs.AdjustCount(player, zone, delta);
            return;
        }

        // CB counts face-up damage, so "-" pays Counter Blast by turning one more damage face down
        if (delta < 0 && player.FaceUpDamage == 0) return;
        gs.AdjustCount(player, "FaceDownDamage", -delta);
    }

    // Online it's always this PC's player; on one PC, the turn player's
    public static PlayerState LocalPlayer()
    {
        GameState gs = GameState.Instance;
        if (gs == null || gs.players[0] == null) return null;
        return MatchSession.IsOnline ? gs.players[MatchSession.LocalSeat] : gs.CurrentPlayer;
    }

    // Counts one card off the deck; shared by the Draw button, VoiceCommands and HandZones
    public static void Draw(PlayerState player)
    {
        if (player == null) return;
        AdjustZoneCount(player.playerIndex, "Deck", -1);
        GameState.OnRuleNotice?.Invoke("Draw", player.playerName + " drew 1 card.");
    }

    // Creates the battle overlay panel in the middle of the camera area
    void CreateBattlePanel()
    {
        float middle = (1f - SplitScreenView.SidebarWidth) * 0.5f; // of the camera area, left of the sidebar
        GameObject battlePanel = CreatePanel("BattlePanel", canvas.transform,
            new Vector2(middle, 0.5f),
            new Vector2(middle, 0.5f),
            new Vector2(0f, 0f), // center position
            new Vector2(400f, 200f));
        ((RectTransform)battlePanel.transform).pivot = new Vector2(0.5f, 0.5f);

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

    /* Notices, rulings and triggers: a popup that slides up from the bottom, in the middle of the
     camera area, and back down once nothing more is waiting */
    void CreateNotificationPanel()
    {
        const float height = 150f;
        float middle = (1f - SplitScreenView.SidebarWidth) * 0.5f; // of the camera area, left of the sidebar
        GameObject sectionObj = CreatePanel("NotificationPanel", canvas.transform, new Vector2(middle, 0f), new Vector2(middle, 0f), Vector2.zero, new Vector2(660f, height));
        RectTransform section = (RectTransform)sectionObj.transform;
        section.pivot = new Vector2(0.5f, 0f);
        sectionObj.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.3f, 0.95f);

        // Icon at the top-right, for triggers and Persona Ride
        GameObject iconObj = new GameObject("NotificationIcon");
        iconObj.transform.SetParent(section, false);
        Image iconImg = iconObj.AddComponent<Image>();
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = iconRT.anchorMax = iconRT.pivot = new Vector2(1f, 1f);
        iconRT.anchoredPosition = new Vector2(-10f, -10f);
        iconRT.sizeDelta = new Vector2(56f, 56f);
        iconImg.enabled = false;

        TextMeshProUGUI titleText = CreateStretchText("NotifTitle", section, 10f, 36f, 12f, 74f, "Notices", 24, TextAlignmentOptions.Left);
        titleText.color = new Color(1f, 0.85f, 0f); // gold title
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 16;
        titleText.fontSizeMax = 24;

        TextMeshProUGUI bodyText = CreateStretchText("NotifBody", section, 50f, height - 58f, 12f, 12f, "", 19, TextAlignmentOptions.TopLeft);
        bodyText.enableAutoSizing = true;
        bodyText.fontSizeMin = 13;
        bodyText.fontSizeMax = 19;

        /* On the always-active panel: a component added to an inactive object doesn't get
         OnEnable, so it would never subscribe to the notices */
        notificationOverlay = section.gameObject.AddComponent<NotificationOverlay>();
        notificationOverlay.notificationPanel = section.gameObject;
        notificationOverlay.notificationTitle = titleText;
        notificationOverlay.notificationBody = bodyText;
        notificationOverlay.notificationIcon = iconImg;
        notificationOverlay.slideUp = true;

        // Clicks go through to what's under it, e.g. a card's "i"
        foreach (Graphic graphic in sectionObj.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    // The notifications so far, scrollable, at the top of the camera area; the sidebar's log button or L
    void CreateLogPanel()
    {
        float middle = (1f - SplitScreenView.SidebarWidth) * 0.5f; // of the camera area, left of the sidebar
        GameObject box = CreatePanel("LogPanel", canvas.transform, new Vector2(middle, 1f), new Vector2(middle, 1f), new Vector2(0f, -24f), new Vector2(760f, 620f));
        RectTransform boxRT = (RectTransform)box.transform;
        boxRT.pivot = new Vector2(0.5f, 1f);
        box.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.16f, 0.97f);

        TextMeshProUGUI title = CreateStretchText("Title", boxRT, 10f, 40f, 16f, 70f, "Match Log", 28, TextAlignmentOptions.Left);
        title.color = new Color(1f, 0.85f, 0f);

        GameObject closeObj = CreatePanel("CloseButton", boxRT, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(48f, 40f));
        ((RectTransform)closeObj.transform).pivot = new Vector2(1f, 1f);
        closeObj.GetComponent<Image>().color = ButtonColor;
        Button close = closeObj.AddComponent<Button>();
        CreateFillText("Label", closeObj.transform, "X", 24, TextAlignmentOptions.Center);

        // Scrolls a text that grows with the list, clipped to the box under the title
        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(boxRT, false);
        RectTransform viewportRT = (RectTransform)viewport.transform;
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        Stretch(viewportRT, new Vector2(16f, 12f), new Vector2(-16f, -60f));

        TextMeshProUGUI list = CreateText("List", viewportRT, Vector2.zero, Vector2.zero, "", 23, TextAlignmentOptions.TopLeft);
        list.richText = false; // notices are plain text
        RectTransform listRT = list.rectTransform;
        listRT.anchorMin = new Vector2(0f, 1f);
        listRT.anchorMax = new Vector2(1f, 1f);
        listRT.pivot = new Vector2(0.5f, 1f);
        listRT.sizeDelta = Vector2.zero;
        list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = box.AddComponent<ScrollRect>();
        scroll.viewport = viewportRT;
        scroll.content = listRT;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // On this always-active object, so L still works while the box is hidden
        logPanel = gameObject.AddComponent<MatchLogPanel>();
        logPanel.panel = box;
        logPanel.listText = list;
        logPanel.scroll = scroll;
        close.onClick.AddListener(logPanel.Close);
    }

    // The last card put on the board (by either player) or tapped: picture, stats and skill
    void CreateCardSection(Transform sidebar, float top, float bottom)
    {
        GameObject sectionObj = CreatePanel("CardPanel", sidebar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch((RectTransform)sectionObj.transform, new Vector2(8f, bottom), new Vector2(-8f, -top));
        sectionObj.GetComponent<Image>().color = SectionColor;
        Transform section = sectionObj.transform;

        GameObject artObj = new GameObject("CardArt", typeof(RectTransform));
        artObj.transform.SetParent(section, false);
        RectTransform artRT = (RectTransform)artObj.transform;
        artRT.anchorMin = artRT.anchorMax = artRT.pivot = new Vector2(0f, 1f);
        artRT.anchoredPosition = new Vector2(10f, -10f);
        artRT.sizeDelta = new Vector2(120f, 168f);
        RawImage art = artObj.AddComponent<RawImage>();
        art.color = new Color(1f, 1f, 1f, 0.08f);

        TextMeshProUGUI nameText = CreateStretchText("Name", section, 10f, 40f, 142f, 10f, "No card yet", 24, TextAlignmentOptions.TopLeft);
        nameText.fontStyle = FontStyles.Bold;
        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 15;
        nameText.fontSizeMax = 24;
        TextMeshProUGUI statsText = CreateStretchText("Stats", section, 54f, 28f, 142f, 10f, "", 20, TextAlignmentOptions.TopLeft);
        statsText.color = new Color(1f, 0.92f, 0.6f);
        TextMeshProUGUI typeText = CreateStretchText("Type", section, 84f, 28f, 142f, 10f, "", 18, TextAlignmentOptions.TopLeft);
        TextMeshProUGUI clanText = CreateStretchText("Clan", section, 114f, 60f, 142f, 10f, "", 16, TextAlignmentOptions.TopLeft);
        clanText.color = new Color(0.7f, 0.73f, 0.8f);

        TextMeshProUGUI skillText = CreateFillText("Skill", section, "Put a card on a zone, or tap its i, to see it here.", 19, TextAlignmentOptions.TopLeft);
        Stretch(skillText.rectTransform, new Vector2(12f, 10f), new Vector2(-12f, -188f));
        skillText.enableAutoSizing = true; // long skills shrink to fit
        skillText.fontSizeMin = 12;
        skillText.fontSizeMax = 19;

        CardInfoPanel panel = sectionObj.AddComponent<CardInfoPanel>();
        panel.art = art;
        panel.nameText = nameText;
        panel.statsText = statsText;
        panel.typeText = typeText;
        panel.clanText = clanText;
        panel.skillText = skillText;
    }

    // Online: the opponent's field in text, bottom px up from the sidebar's bottom
    void CreateOpponentFieldSection(Transform sidebar, float bottom, float height)
    {
        GameObject panel = CreatePanel("OpponentFieldPanel", sidebar, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
        Stretch((RectTransform)panel.transform, new Vector2(8f, bottom), new Vector2(-8f, bottom + height));
        panel.GetComponent<Image>().color = SectionColor;

        TextMeshProUGUI text = CreateFillText("OpponentFieldText", panel.transform, "", 15, TextAlignmentOptions.TopLeft);
        Stretch(text.rectTransform, new Vector2(12f, 6f), new Vector2(-12f, -6f));
        text.enableAutoSizing = true;
        text.fontSizeMin = 10;
        text.fontSizeMax = 15;
        panel.AddComponent<OpponentFieldOverlay>().fieldText = text;
    }

    const float MenuWidth = 560f;
    static readonly Color MenuButtonColor = new Color(0.25f, 0.28f, 0.36f, 0.95f);

    // The menu the sidebar's Settings button opens, centered (Esc too); built last so it draws over the rest
    void CreateSettingsMenu(Button openButton)
    {
        // Dims the game and blocks clicks behind the menu
        GameObject overlay = CreatePanel("SettingsOverlay", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        GameObject box = CreatePanel("SettingsBox", overlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MenuWidth, 540f));
        ((RectTransform)box.transform).pivot = new Vector2(0.5f, 0.5f);
        box.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.16f, 0.97f);

        CreateText("Title", box.transform, new Vector2(0f, -22f), new Vector2(MenuWidth, 50f), "Settings", 38, TextAlignmentOptions.Center);

        // On the always-visible button, so Esc still works while the menu is hidden
        SettingsMenu menu = openButton.gameObject.AddComponent<SettingsMenu>();
        menu.overlay = overlay;
        menu.scaledRoot = canvas.transform;
        openButton.onClick.AddListener(menu.Open);

        CreateCornerIcon(box.transform, "Close", true).onClick.AddListener(menu.Close);
        CreateCornerIcon(box.transform, "Rotate", false, new Color(0.85f, 0.87f, 0.92f, 0.95f))
            .onClick.AddListener(() => GameSettings.RotateView = !GameSettings.RotateView);
        new GameObject("RotatedView").AddComponent<RotatedView>();

        CreateMenuButton(box.transform, "Resume", -84f, MenuButtonColor, menu.Close);
        CreateMenuButton(box.transform, "Calibrate Board", -160f, MenuButtonColor, menu.Calibrate);
        menu.fontSlider = CreateMenuSlider(box.transform, -240f, out menu.fontLabel);
        menu.brightnessSlider = CreateMenuSlider(box.transform, -316f, out menu.brightnessLabel);
        Button leave = CreateMenuButton(box.transform, "Leave Match", -396f, Color.red, menu.Leave);
        menu.leaveBackground = leave.GetComponent<Image>();
        menu.leaveLabel = leave.GetComponentInChildren<TextMeshProUGUI>();

        menu.infoText = CreateText("Info", box.transform, new Vector2(0f, -480f), new Vector2(MenuWidth, 40f), "", 18, TextAlignmentOptions.Center);
        menu.infoText.color = new Color(0.7f, 0.72f, 0.78f);
    }

    /* A 48 px StreamingAssets/Icons/<icon>.png button in the top right (or left) corner of parent,
     on a tile of background if given (e.g. for a dark icon), or the icon's name on a plain button
     if there's no such file */
    Button CreateCornerIcon(Transform parent, string icon, bool right, Color? background = null)
    {
        Vector2 corner = new Vector2(right ? 1f : 0f, 1f);
        GameObject obj = CreatePanel(icon + "Button", parent, corner, corner, new Vector2(right ? -14f : 14f, -14f), new Vector2(48f, 48f));
        Image image = obj.GetComponent<Image>();
        Button button = obj.AddComponent<Button>();

        Sprite sprite = LoadIcon(icon);
        if (sprite == null)
        {
            image.color = MenuButtonColor;
            CreateFillText("Label", obj.transform, icon, 14, TextAlignmentOptions.Center);
            return button;
        }

        if (background is Color tile)
        {
            image.color = tile;
            GameObject iconObj = CreatePanel("Icon", obj.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Stretch((RectTransform)iconObj.transform, new Vector2(8f, 8f), new Vector2(-8f, -8f));
            image = iconObj.GetComponent<Image>();
            image.raycastTarget = false;
        }
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        return button;
    }

    // A label with a 0-1 slider under it, 400 px wide and centered, y px down from parent's top edge
    Slider CreateMenuSlider(Transform parent, float y, out TextMeshProUGUI label)
    {
        label = CreateText("SliderLabel", parent, new Vector2((MenuWidth - 400f) / 2f, y), new Vector2(400f, 30f), "", 22, TextAlignmentOptions.Left);

        GameObject obj = DefaultControls.CreateSlider(new DefaultControls.Resources());
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y - 38f);
        rt.sizeDelta = new Vector2(400f, 20f);

        // Without the editor's default sprites these are plain rectangles; colored to match the menu
        obj.transform.Find("Background").GetComponent<Image>().color = MenuButtonColor;
        obj.transform.Find("Fill Area/Fill").GetComponent<Image>().color = new Color(0.96f, 0.76f, 0.26f);
        obj.transform.Find("Handle Slide Area/Handle").GetComponent<Image>().color = Color.white;
        return obj.GetComponent<Slider>();
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

    // Makes sure an AROverlayEventBridge exists in the scene
    void WireEventBridge()
    {
        // Reuse existing bridge if already in the scene
        eventBridge = FindFirstObjectByType<AROverlayEventBridge>();
        if (eventBridge == null)
        {
            GameObject bridgeObj = new GameObject("AROverlayEventBridge");
            eventBridge = bridgeObj.AddComponent<AROverlayEventBridge>();
        }

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

    // A sidebar block across its width (8 px margins), top px down from its top edge and height px tall
    RectTransform CreateSection(string name, Transform sidebar, float top, float height, Color color)
    {
        GameObject obj = CreatePanel(name, sidebar, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        Image img = obj.GetComponent<Image>();
        img.color = color;
        img.enabled = color.a > 0f;
        RectTransform rt = (RectTransform)obj.transform;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(8f, -top - height);
        rt.offsetMax = new Vector2(-8f, -top);
        return rt;
    }

    // Sets edges relative to the anchors: offsetMin = left/bottom, offsetMax = right/top
    static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // Text across its parent's width (left/right margins), top px down and height px tall
    TextMeshProUGUI CreateStretchText(string name, Transform parent, float top, float height, float left, float right, string defaultText, int fontSize, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI tmp = CreateText(name, parent, Vector2.zero, Vector2.zero, defaultText, fontSize, alignment);
        RectTransform rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, -top - height);
        rt.offsetMax = new Vector2(-right, -top);
        return tmp;
    }

    // Text filling its parent
    TextMeshProUGUI CreateFillText(string name, Transform parent, string defaultText, int fontSize, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI tmp = CreateText(name, parent, Vector2.zero, Vector2.zero, defaultText, fontSize, alignment);
        tmp.rectTransform.anchorMin = Vector2.zero;
        tmp.rectTransform.anchorMax = Vector2.one;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        Stretch(tmp.rectTransform, Vector2.zero, Vector2.zero);
        return tmp;
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

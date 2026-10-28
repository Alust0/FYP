using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
using UnityEngine.Windows.Speech;
#endif

/* Voice keywords, always listening through Windows' offline phrase recognition. Only the fixed
 keywords below are recognised; nothing else is recorded or sent. Each keyword heard shows who
 said it over the camera view, and online the keyword (just the word) goes to the other PC too,
 so both see it and both rule engines follow the battle. M mutes. Added by UISetup. */
public class VoiceCommands : MonoBehaviour
{
    public enum Command
    {
        NextPhase, TurnEnd, Draw, SoulCharge, CounterBlast,
        AttackLeft, AttackVanguard, AttackRight,
        Guard, NoGuard, DriveCheck, DamageCheck,
    }

    static readonly Dictionary<string, Command> Phrases = new Dictionary<string, Command>
    {
        ["next phase"] = Command.NextPhase,
        ["turn end"] = Command.TurnEnd,
        ["end turn"] = Command.TurnEnd,
        ["draw card"] = Command.Draw,
        ["soul charge"] = Command.SoulCharge,
        ["counter blast"] = Command.CounterBlast,
        ["attack left"] = Command.AttackLeft,
        ["attack vanguard"] = Command.AttackVanguard,
        ["attack right"] = Command.AttackRight,
        ["guard"] = Command.Guard,
        ["no guard"] = Command.NoGuard,
        ["drive check"] = Command.DriveCheck,
        ["damage check"] = Command.DamageCheck,
    };

    public static VoiceCommands Instance { get; private set; }

    // This PC's latest keyword and how many it has heard, uploaded by OnlineMatch
    public static string LastSaid { get; private set; }
    public static int SaidCount { get; private set; }

    public Key muteKey = Key.M;
    [Tooltip("Accept keywords Windows is only fairly sure of. Off: fewer mistakes, but it may need saying twice.")]
    public bool acceptMediumConfidence = true;
    public float toastSeconds = 3f;

    RulesEngine mRules;
    bool mMuted;
    string mStatus = "Voice: starting...";
    TextMeshProUGUI mToast;
    CanvasGroup mToastGroup;
    float mToastUntil;
    TextMeshProUGUI mStatusText;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
    KeywordRecognizer mRecognizer;
#endif

    void Awake() => Instance = this;

    void Start()
    {
        BuildUI();
        StartListening();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
        PhraseRecognitionSystem.OnError -= OnSpeechError;
        if (mRecognizer != null)
        {
            if (mRecognizer.IsRunning) mRecognizer.Stop();
            mRecognizer.Dispose();
        }
#endif
    }

    void StartListening()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
        if (!PhraseRecognitionSystem.isSupported)
        {
            mStatus = "Voice: not available on this PC";
            return;
        }

        PhraseRecognitionSystem.OnError += OnSpeechError;

        mRecognizer = new KeywordRecognizer(Phrases.Keys.ToArray(), acceptMediumConfidence ? ConfidenceLevel.Medium : ConfidenceLevel.High);
        mRecognizer.OnPhraseRecognized += args =>
        {
            // Two editor windows on one PC hear the same microphone; only the one in front acts
            if (!mMuted && Application.isFocused) Heard(args.text);
        };
        mRecognizer.Start();
        mStatus = "Voice: listening";
#else
        mStatus = "Voice: not available on this platform";
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
    void OnSpeechError(SpeechError error)
    {
        mStatus = "Voice: stopped (" + error + ")";
        Debug.LogWarning("[VoiceCommands] Speech recognition error: " + error);
    }
#endif

    void Update()
    {
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();

        if (Keyboard.current != null && Keyboard.current[muteKey].wasPressedThisFrame) mMuted = !mMuted;

        Camera cam = Camera.main;
        if (cam != null) PlaceUI(cam.pixelRect);
        mStatusText.text = (mMuted ? "Voice: muted" : mStatus) + "  (M)";

        float left = mToastUntil - Time.time;
        mToastGroup.alpha = Mathf.Clamp01(left / 0.5f); // fades over its last half second
    }

    /* A keyword chosen with ActionButtons when voice isn't working: handled exactly like saying it,
     so it also shows and reaches the other PC */
    public void Press(string phrase) => Heard(phrase, true);

    // A keyword from this PC's microphone (or ActionButtons, pressed)
    void Heard(string phrase, bool pressed = false)
    {
        GameState gs = GameState.Instance;
        if (gs == null || gs.players[0] == null || !Phrases.TryGetValue(phrase, out Command command)) return;

        PlayerState who = Speaker(gs, command);
        LastSaid = phrase;
        SaidCount++;
        ShowToast(who, phrase);
        MatchLog.Action(who.playerName + (pressed ? " pressed \"" : " said \"") + phrase + "\"");
        Run(command, who, gs);
    }

    /* Online: the opponent's keyword. Their counts and phases already come with their board, so
     only the battle declarations are acted on here, for this PC's rule engine to follow the battle. */
    public void HeardRemote(string phrase, PlayerState who)
    {
        if (phrase == null || who == null || !Phrases.TryGetValue(phrase, out Command command)) return;

        ShowToast(who, phrase);
        MatchLog.Action(who.playerName + " said \"" + phrase + "\" (on their PC)");
        if (TryDeclaration(command, out RulesEngine.Declaration declaration)) mRules?.Declare(declaration, who);
    }

    /* Online it's always this PC's player. On one PC, the defender's keywords are the player not
     taking their turn, and everything else is the turn player's. */
    static PlayerState Speaker(GameState gs, Command command)
    {
        if (MatchSession.IsOnline) return gs.players[MatchSession.LocalSeat];
        bool defending = command == Command.Guard || command == Command.NoGuard || command == Command.DamageCheck;
        return defending ? gs.Opponent : gs.CurrentPlayer;
    }

    void Run(Command command, PlayerState who, GameState gs)
    {
        if (mRules == null) return;

        if (TryDeclaration(command, out RulesEngine.Declaration declaration))
        {
            mRules.Declare(declaration, who);
            return;
        }

        switch (command)
        {
            case Command.NextPhase: mRules.AdvancePhase(); break;
            case Command.TurnEnd: mRules.RequestTurnEnd(); break;
            case Command.Draw: UISetup.Draw(who); break;

            case Command.SoulCharge:
                UISetup.AdjustZoneCount(who.playerIndex, "Deck", -1);
                UISetup.AdjustZoneCount(who.playerIndex, "Soul", 1);
                Notice("Soul Charge", who.playerName + " put the top card of their deck into the soul.");
                break;

            case Command.CounterBlast:
                if (who.FaceUpDamage == 0)
                {
                    Notice("Counter Blast", who.playerName + " has no face-up damage to pay with.");
                    break;
                }
                UISetup.AdjustZoneCount(who.playerIndex, "CB", -1);
                Notice("Counter Blast", who.playerName + " paid 1. " + who.FaceUpDamage + " face-up damage left.");
                break;

            case Command.AttackLeft:
            case Command.AttackVanguard:
            case Command.AttackRight:
                if (!mRules.IsChoosingTarget || who != gs.CurrentPlayer)
                    Notice("Attack", "Rest a front-row unit to attack first, then say which unit it attacks.");
                else
                    mRules.ChooseTarget(RulesEngine.FrontRow[command - Command.AttackLeft]);
                break;
        }
    }

    static bool TryDeclaration(Command command, out RulesEngine.Declaration declaration)
    {
        switch (command)
        {
            case Command.Guard: declaration = RulesEngine.Declaration.Guard; return true;
            case Command.NoGuard: declaration = RulesEngine.Declaration.NoGuard; return true;
            case Command.DriveCheck: declaration = RulesEngine.Declaration.DriveCheck; return true;
            case Command.DamageCheck: declaration = RulesEngine.Declaration.DamageCheck; return true;
            default: declaration = default; return false;
        }
    }

    // Logged by MatchLog
    static void Notice(string title, string body) => GameState.OnRuleNotice?.Invoke(title, body);

    // UI: the keyword heard, top-centre of the camera view, and the microphone's state bottom-left

    void ShowToast(PlayerState who, string phrase)
    {
        mToast.text = "<b>" + who.playerName + "</b>:  \"" + phrase.ToUpperInvariant() + "\"";
        mToastUntil = Time.time + toastSeconds;
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("VoiceCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // no scaler: one unit is one screen pixel
        canvasObj.transform.SetParent(transform, false);

        GameObject toastObj = new GameObject("VoiceToast", typeof(RectTransform));
        toastObj.transform.SetParent(canvasObj.transform, false);
        Image background = toastObj.AddComponent<Image>();
        background.color = new Color(0.05f, 0.06f, 0.1f, 0.85f);
        background.raycastTarget = false;
        mToastGroup = toastObj.AddComponent<CanvasGroup>();
        mToastGroup.alpha = 0f;
        mToast = AddText(toastObj.transform, 28, TextAlignmentOptions.Center);
        mToast.color = new Color(1f, 0.85f, 0.2f);

        GameObject statusObj = new GameObject("VoiceStatus", typeof(RectTransform));
        statusObj.transform.SetParent(canvasObj.transform, false);
        mStatusText = AddText(statusObj.transform, 18, TextAlignmentOptions.BottomLeft);
    }

    void PlaceUI(Rect camera)
    {
        RectTransform toast = (RectTransform)mToastGroup.transform;
        toast.anchorMin = toast.anchorMax = toast.pivot = new Vector2(0f, 0f);
        toast.sizeDelta = new Vector2(Mathf.Min(560f, camera.width * 0.8f), 54f);
        toast.anchoredPosition = new Vector2(camera.center.x - toast.sizeDelta.x * 0.5f, camera.yMax - 54f - 12f);

        RectTransform status = (RectTransform)mStatusText.transform.parent;
        status.anchorMin = status.anchorMax = status.pivot = new Vector2(0f, 0f);
        status.sizeDelta = new Vector2(360f, 28f);
        status.anchoredPosition = new Vector2(camera.x + 10f, camera.y + 8f);
    }

    static TextMeshProUGUI AddText(Transform parent, int size, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 4f);
        rt.offsetMax = new Vector2(-10f, -4f);

        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.outlineWidth = 0.2f;
        text.outlineColor = Color.black;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12;
        text.fontSizeMax = size;
        text.raycastTarget = false;
        return text;
    }
}

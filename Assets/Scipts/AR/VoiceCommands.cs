using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA
using UnityEngine.Windows.Speech;
#endif

/* Voice keywords, always listening through Windows' offline phrase recognition. Only the fixed
 keywords below are recognised; nothing else is recorded or sent. Each keyword heard shows who
 said it over the camera view, and online the keyword (just the word) goes to the other PC too,
 so both see it and both rule engines follow the battle. M mutes. Added by UISetup. Its UI is built in VoiceCommands.UI.cs. */
public partial class VoiceCommands : MonoBehaviour
{
    public enum Command
    {
        NextPhase, TurnEnd, Draw, SoulCharge, CounterBlast,
        AttackLeft, AttackVanguard, AttackRight,
        Guard, NoGuard, DriveCheck, DamageCheck, PerfectGuard,
        ActivateSkill, SkipSkill,
        ActivateVanguard, ActivateLeftFront, ActivateRightFront, ActivateLeftBack, ActivateCenterBack, ActivateRightBack,
        SkillDone, SkipStep, PayFirst, PaySecond, // a skill's steps (SkillRules)
    }

    // The circles "activate ..." names, in Command order from ActivateVanguard
    static readonly string[] ActivateCircles = { "Vanguard", "LeftFront", "RightFront", "LeftBack", "CenterBack", "RightBack" };

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
        ["perfect guard"] = Command.PerfectGuard, // a Sentinel: the attacked unit can't be hit
        ["activate skill"] = Command.ActivateSkill,
        ["skip skill"] = Command.SkipSkill,
        ["activate vanguard"] = Command.ActivateVanguard,
        ["activate left front"] = Command.ActivateLeftFront,
        ["activate right front"] = Command.ActivateRightFront,
        ["activate left back"] = Command.ActivateLeftBack,
        ["activate center back"] = Command.ActivateCenterBack,
        ["activate right back"] = Command.ActivateRightBack,
        ["skill done"] = Command.SkillDone,
        ["skip step"] = Command.SkipStep,
        ["pay first"] = Command.PayFirst,
        ["pay second"] = Command.PaySecond,
    };

    // e.g. "activate left front" for "LeftFront", for an [ACT] skill's button
    public static string ActivatePhrase(string zone)
    {
        int i = System.Array.IndexOf(ActivateCircles, zone);
        return i < 0 ? "activate skill" : Phrases.First(p => p.Value == Command.ActivateVanguard + i).Key;
    }

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

    // e.g. "Voice: listening  (M)", shown in the Settings menu
    public string Status => (mMuted ? "Voice: muted" : mStatus) + "  (M)";
    TextMeshProUGUI mToast;
    CanvasGroup mToastGroup;
    float mToastUntil;

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
        MatchLogger.Action(who.playerName + (pressed ? " pressed \"" : " said \"") + phrase + "\"");
        Run(command, who, gs);
    }

    /* Online: the opponent's keyword. Their counts and phases already come with their board, so
     only the battle declarations are acted on here, for this PC's rule engine to follow the battle. */
    public void HeardRemote(string phrase, PlayerState who)
    {
        if (phrase == null || who == null || !Phrases.TryGetValue(phrase, out Command command)) return;

        ShowToast(who, phrase);
        MatchLogger.Action(who.playerName + " said \"" + phrase + "\" (on their PC)");
        if (TryDeclaration(command, out RulesEngine.Declaration declaration)) mRules?.Declare(declaration, who);
        else if (command == Command.SkillDone) mRules?.Skills?.OpponentDone(); // their part of this PC's skill (OpponentStep)
    }

    /* Online it's always this PC's player. On one PC, the defender's keywords are the player not
     taking their turn, an [AUTO]'s answer is its owner's, and everything else is the turn player's. */
    PlayerState Speaker(GameState gs, Command command)
    {
        if (MatchSession.IsOnline) return gs.players[MatchSession.LocalSeat];
        // Answering an [AUTO] or a skill's step: its owner, who may be the defender
        bool skillAnswer = command == Command.ActivateSkill || command == Command.SkipSkill || command >= Command.SkillDone;
        if (skillAnswer && mRules?.Skills?.PromptOwner != null)
            return mRules.Skills.PromptOwner;
        bool defending = command == Command.Guard || command == Command.NoGuard || command == Command.DamageCheck || command == Command.PerfectGuard;
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

            case Command.ActivateSkill: mRules.Skills?.ActivateSkill(); break;
            case Command.SkipSkill: mRules.Skills?.SkipSkill(); break;
            case Command.ActivateVanguard:
            case Command.ActivateLeftFront:
            case Command.ActivateRightFront:
            case Command.ActivateLeftBack:
            case Command.ActivateCenterBack:
            case Command.ActivateRightBack:
                mRules.Skills?.ActivateOn(ActivateCircles[command - Command.ActivateVanguard]);
                break;
            case Command.SkillDone: mRules.Skills?.Done(); break;
            case Command.SkipStep: mRules.Skills?.SkipStep(); break;
            case Command.PayFirst: mRules.Skills?.Choose(0); break;
            case Command.PaySecond: mRules.Skills?.Choose(1); break;
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
            case Command.PerfectGuard: declaration = RulesEngine.Declaration.PerfectGuard; return true;
            default: declaration = default; return false;
        }
    }

    // Logged by MatchLogger
    static void Notice(string title, string body) => GameState.OnRuleNotice?.Invoke(title, body);
}

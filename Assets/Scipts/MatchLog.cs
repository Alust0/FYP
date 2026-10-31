using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static MenuUI;

/* Logs scene: the match logs MatchLogger saved (persistentDataPath/MatchLogs) on the left, newest
 first, and the picked one on the right: its result (Win, Lose or Draw, the damage each player
 dealt and the time) over the log, coloured by kind of line. A filter and a search narrow the log
 down; Copy puts the whole file on the clipboard. Builds its own UI (MatchLog.UI.cs). */
public partial class MatchLog : MonoBehaviour
{
    [SerializeField] private string menuSceneName = "Menu";
    [Tooltip("Characters per text block. One TextMeshPro text can't hold a whole long log.")]
    [SerializeField] private int charsPerBlock = 4000;

    enum Kind { Info, Action, Notice, Phase, Board }

    // MatchLogger's result line, from this PC's player's side
    class Result
    {
        public string outcome; // "Win", "Lose" or "Draw"
        public string score;   // "Ann 6 - 4 Bo": damage dealt, this PC's player first
        public string time;    // "23:15"
    }

    // One line of the log, or a whole board snapshot
    class Entry
    {
        public Kind kind;
        public string time;  // "14:30:12", or "" for info and board lines
        public string stamp; // "T3 Ride"
        public string text;
    }

    static readonly string[] FilterNames = { "Everything", "Actions", "Notifications", "Phases & boards" };
    static readonly Color SelectedRowColor = new Color(0.28f, 0.32f, 0.42f);
    // "14:30:12 [T3 Ride] Action text", as MatchLogger.Write makes them
    static readonly Regex LinePattern = new Regex(@"^(\d\d:\d\d:\d\d) \[([^\]]*)\] (\w+)\s+(.*)$");
    // "=== Result: Win | Ann 6 - 4 Bo | 23:15 | Bo reached 6 damage ===", as MatchLogger.WriteResult makes it
    static readonly Regex ResultPattern = new Regex(@"^=== Result: (Win|Lose|Draw) \| (.*) \| ([\d:]+) \| .* ===$", RegexOptions.Multiline);
    static readonly Color WinColor = new Color(0.45f, 0.86f, 0.5f);
    static readonly Color LoseColor = new Color(1f, 0.45f, 0.42f);

    string mFolder;
    List<string> mFiles = new List<string>();
    string mOpenFile;
    string mRawText;
    List<Entry> mEntries = new List<Entry>();
    string mDeleteWarned; // the file already warned about, so a second Delete deletes it

    RectTransform mFileListContent;
    TMP_Text mFileCount;
    TMP_Text mMatchTitle;
    TMP_Text mOutcomeText;
    TMP_Text mScoreText;
    TMP_Dropdown mFilterDropdown;
    TMP_InputField mSearchInput;
    RectTransform mLogContent;
    ScrollRect mLogScroll;
    Button mCopyButton;
    Button mDeleteButton;
    TMP_Text mStatusText;

    void Start()
    {
        mFolder = Path.Combine(Application.persistentDataPath, "MatchLogs");
        BuildUI();
        LoadFileList();
        if (mFiles.Count > 0) Open(mFiles[0]);
        else ShowLog();
    }

    // The file list

    void LoadFileList()
    {
        try
        {
            mFiles = Directory.Exists(mFolder)
                ? Directory.GetFiles(mFolder, "*.txt").OrderByDescending(File.GetLastWriteTime).ToList()
                : new List<string>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MatchLog] Can't read the logs folder: " + e.Message);
            mFiles = new List<string>();
        }
        ShowFileList();
    }

    void ShowFileList()
    {
        foreach (Transform row in mFileListContent) Destroy(row.gameObject);
        mFileCount.text = mFiles.Count == 1 ? "1 match" : mFiles.Count + " matches";

        if (mFiles.Count == 0)
        {
            TMP_Text empty = AddText(mFileListContent, "No match logs yet. Every match saves one here.", 24, Muted, TextAlignmentOptions.Center);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
            return;
        }

        foreach (string file in mFiles)
        {
            bool open = file == mOpenFile;
            Button row = AddButton(mFileListContent, Title(file) + "\n<size=20><color=#A8B0C2>" + Escape(Summary(file)) + "</color></size>",
                () => Open(file), open ? SelectedRowColor : RowColor, Color.white, 26);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
            TMP_Text label = row.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.margin = new Vector4(18f, 0f, 18f, 0f);
        }
    }

    // "Thu 29 Oct, 14:30 (P1)" from "Match 2026-10-29 143012 P1.txt"
    static string Title(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        if (name.Length < 23 || !DateTime.TryParseExact(name.Substring(6, 17), "yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
            return Escape(name);
        string seat = name.Substring(23).Trim();
        return when.ToString("ddd d MMM, HH:mm") + (seat.Length > 0 ? " (" + seat + ")" : "");
    }

    // "Win 6 - 4, online room ABCD: Ann vs Bo", from the result and the file's first line
    static string Summary(string file)
    {
        string text;
        try
        {
            using (StreamReader reader = OpenReader(file)) text = reader.ReadToEnd();
        }
        catch (Exception)
        {
            return "Couldn't read this log";
        }

        string first = text.Split('\n')[0].TrimEnd('\r');
        int comma = first.IndexOf(", ", StringComparison.Ordinal);
        string players = comma >= 0 ? first.Substring(comma + 2) : first;

        Result result = FindResult(text);
        if (result == null) return players;
        Match score = Regex.Match(result.score, @"\d+ - \d+");
        return result.outcome + (score.Success ? " " + score.Value : "") + ", " + players;
    }

    // The log's last result line, or null for a log from before results were saved
    static Result FindResult(string text)
    {
        MatchCollection matches = ResultPattern.Matches(text.Replace("\r", ""));
        if (matches.Count == 0) return null;
        Match match = matches[matches.Count - 1];
        return new Result { outcome = match.Groups[1].Value, score = match.Groups[2].Value, time = match.Groups[3].Value };
    }

    // Shares the file with MatchLogger, which still has it open during a match in another window
    static StreamReader OpenReader(string file) =>
        new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8);

    // One log

    void Open(string file)
    {
        mOpenFile = file;
        mDeleteWarned = null;
        try
        {
            using (StreamReader reader = OpenReader(file)) mRawText = reader.ReadToEnd();
            mEntries = Parse(mRawText);
            SetStatus("");
        }
        catch (Exception e)
        {
            mRawText = null;
            mEntries = new List<Entry>();
            SetStatus("Couldn't read this log: " + e.Message);
        }
        mMatchTitle.text = Title(file);
        ShowResult();
        ShowFileList();
        ShowLog();
    }

    // Over the log: Win, Lose or Draw, then the damage score and the time
    void ShowResult()
    {
        Result result = mRawText != null ? FindResult(mRawText) : null;
        if (result == null)
        {
            mOutcomeText.text = mOpenFile != null ? "No result" : "";
            mOutcomeText.color = Muted;
            mScoreText.text = "";
            return;
        }

        mOutcomeText.text = result.outcome;
        mOutcomeText.color = result.outcome == "Win" ? WinColor : result.outcome == "Lose" ? LoseColor : Muted;
        mScoreText.text = Escape(result.score) + "<color=#A8B0C2>   •   " + result.time + "</color>";
    }

    // Board snapshots are one "=== Board" line then indented lines; they're kept together
    static List<Entry> Parse(string text)
    {
        var entries = new List<Entry>();
        foreach (string line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.Length == 0) continue;

            Entry last = entries.Count > 0 ? entries[entries.Count - 1] : null;
            if (last != null && last.kind == Kind.Board && line.StartsWith(" "))
            {
                last.text += "\n" + line;
                continue;
            }

            if (line.StartsWith("=== Board"))
            {
                entries.Add(new Entry { kind = Kind.Board, time = "", stamp = "", text = line });
                continue;
            }

            Match match = LinePattern.Match(line);
            if (!match.Success)
            {
                entries.Add(new Entry { kind = Kind.Info, time = "", stamp = "", text = line });
                continue;
            }

            Kind kind = match.Groups[3].Value == "Action" ? Kind.Action
                : match.Groups[3].Value == "Notice" ? Kind.Notice
                : match.Groups[3].Value == "Phase" ? Kind.Phase
                : Kind.Info;
            entries.Add(new Entry { kind = kind, time = match.Groups[1].Value, stamp = match.Groups[2].Value, text = match.Groups[4].Value });
        }
        return entries;
    }

    bool Shown(Entry entry, string search)
    {
        bool kindShown = mFilterDropdown.value switch
        {
            1 => entry.kind == Kind.Action,
            2 => entry.kind == Kind.Notice,
            3 => entry.kind == Kind.Phase || entry.kind == Kind.Board,
            _ => true,
        };
        return kindShown && (search.Length == 0 || entry.text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // Splits the shown entries into blocks of about charsPerBlock, top of the log first
    void ShowLog()
    {
        foreach (Transform block in mLogContent) Destroy(block.gameObject);
        bool open = mRawText != null;
        mCopyButton.interactable = open;
        mDeleteButton.interactable = mOpenFile != null;

        if (!open)
        {
            if (mOpenFile == null) mMatchTitle.text = "No match picked";
            AddLogHint(mFiles.Count == 0 ? "Play a match and its log shows up here." : "Pick a match on the left.");
            return;
        }

        string search = mSearchInput.text.Trim();
        List<Entry> shown = mEntries.Where(e => Shown(e, search)).ToList();
        if (shown.Count == 0)
        {
            AddLogHint(search.Length > 0 ? "Nothing in this log matches \"" + search + "\"." : "Nothing of that kind in this log.");
            return;
        }

        var sb = new StringBuilder();
        foreach (Entry entry in shown)
        {
            if (sb.Length > 0 && sb.Length + entry.text.Length > charsPerBlock)
            {
                AddLogBlock(sb.ToString());
                sb.Clear();
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Format(entry));
        }
        AddLogBlock(sb.ToString());

        Canvas.ForceUpdateCanvases();
        mLogScroll.verticalNormalizedPosition = 1f;
    }

    // Time and turn greyed, then the text in its kind's colour; boards in fixed-width so their columns line up
    static string Format(Entry entry)
    {
        string text = Escape(entry.text);
        string prefix = entry.time.Length > 0 ? "<color=#7C8496><mspace=0.55em>" + entry.time + "  " + Escape(entry.stamp).PadRight(11) + "</mspace></color> " : "";
        switch (entry.kind)
        {
            case Kind.Phase: return "\n" + prefix + "<b><color=#F5C242>" + text.Trim('-', ' ') + "</color></b>";
            case Kind.Notice: return prefix + "<color=#8CCBFF>" + text + "</color>";
            case Kind.Board: return "<color=#A8B0C2><mspace=0.55em>" + text + "</mspace></color>\n";
            case Kind.Info: return "<i><color=#A8B0C2>" + text + "</color></i>";
            default: return prefix + text;
        }
    }

    // Card names are shown as written, never read as TextMeshPro tags
    static string Escape(string text) => text.Replace("<", "<\u200B");

    // Buttons

    void OnCopy()
    {
        if (mRawText == null) return;
        GUIUtility.systemCopyBuffer = mRawText;
        SetStatus("Copied the whole log to the clipboard.");
    }

    void OnOpenFolder()
    {
        Directory.CreateDirectory(mFolder);
        Application.OpenURL("file:///" + mFolder.Replace('\\', '/'));
    }

    // Warns the first time and deletes the second
    void OnDelete()
    {
        if (mOpenFile == null) return;
        if (mDeleteWarned != mOpenFile)
        {
            mDeleteWarned = mOpenFile;
            SetStatus("Press Delete again to delete this log for good.");
            return;
        }

        try
        {
            File.Delete(mOpenFile);
        }
        catch (Exception e)
        {
            SetStatus("Couldn't delete it: " + e.Message);
            return;
        }

        mOpenFile = null;
        mRawText = null;
        mEntries.Clear();
        ShowResult();
        LoadFileList();
        if (mFiles.Count > 0) Open(mFiles[0]);
        else ShowLog();
        SetStatus("Log deleted.");
    }

    void OnBack() => SceneManager.LoadScene(menuSceneName);

    void SetStatus(string text) => mStatusText.text = text;
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MenuUI;

/* MatchLog's screen, built in code so the Logs scene needs no Canvas: saved matches on the
 left, the open log on the right. The rest of MatchLog is in MatchLog.cs. */
public partial class MatchLog
{
    void BuildUI()
    {
        RectTransform root = BuildCanvas("MatchLogCanvas");

        TMP_Text title = AddText(root, "Match Logs", 64, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(128f, -110f), new Vector2(-300f, -20f));
        Place((RectTransform)SceneSettings.AddButton(root).transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -96f), new Vector2(110f, -34f));
        Button back = AddButton(root, "Back", OnBack, PanelColor, Color.white, 30);
        Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-240f, -96f), new Vector2(-48f, -34f));

        BuildFileList(root);
        BuildLogPanel(root);
    }

    void BuildFileList(RectTransform root)
    {
        RectTransform panel = AddBox(root, "MatchesPanel", PanelColor);
        Place(panel, new Vector2(0f, 0f), new Vector2(0.28f, 1f), new Vector2(48f, 48f), new Vector2(-12f, -130f));

        TMP_Text heading = AddText(panel, "Matches", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 24f, 24f);
        mFileCount = AddText(panel, "", 24, Muted, TextAlignmentOptions.MidlineRight);
        PlaceTop(mFileCount.rectTransform, 0f, 1f, 18f, 56f, 200f, 24f);

        RectTransform list = AddBox(panel, "List", new Color(0f, 0f, 0f, 0.2f));
        Place(list, Vector2.zero, Vector2.one, new Vector2(24f, 96f), new Vector2(-24f, -90f));
        mFileListContent = AddScrollContent(list);
        AddListLayout(mFileListContent, 8f);

        Button openFolder = AddButton(panel, "Open Folder", OnOpenFolder, RowColor, Color.white, 24);
        Place((RectTransform)openFolder.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 24f), new Vector2(-24f, 80f));
    }

    void BuildLogPanel(RectTransform root)
    {
        RectTransform panel = AddBox(root, "LogPanel", PanelColor);
        Place(panel, new Vector2(0.28f, 0f), new Vector2(1f, 1f), new Vector2(12f, 48f), new Vector2(-48f, -130f));

        mMatchTitle = AddText(panel, "", 30, Muted, TextAlignmentOptions.Left);
        PlaceTop(mMatchTitle.rectTransform, 0f, 1f, 18f, 44f, 28f, 28f);

        // The result, centred: Win, Lose or Draw, then the damage dealt and the time
        mOutcomeText = AddText(panel, "", 72, Color.white, TextAlignmentOptions.Center);
        mOutcomeText.fontStyle = FontStyles.Bold;
        PlaceTop(mOutcomeText.rectTransform, 0f, 1f, 30f, 84f, 28f, 28f);
        mScoreText = AddText(panel, "", 34, Color.white, TextAlignmentOptions.Center);
        PlaceTop(mScoreText.rectTransform, 0f, 1f, 114f, 48f, 28f, 28f);

        mFilterDropdown = AddDropdown(panel);
        PlaceTop((RectTransform)mFilterDropdown.transform, 0f, 0.3f, 178f, 60f, 28f, 8f);
        mFilterDropdown.ClearOptions();
        mFilterDropdown.AddOptions(new List<string>(FilterNames));
        mFilterDropdown.onValueChanged.AddListener(_ => ShowLog());

        mSearchInput = AddInput(panel, "Search, e.g. a card name or \"trigger\"...");
        PlaceTop((RectTransform)mSearchInput.transform, 0.3f, 1f, 178f, 60f, 8f, 28f);
        mSearchInput.onValueChanged.AddListener(_ => ShowLog());

        RectTransform log = AddBox(panel, "Log", new Color(0f, 0f, 0f, 0.2f));
        Place(log, Vector2.zero, Vector2.one, new Vector2(28f, 96f), new Vector2(-28f, -258f));
        mLogContent = AddScrollContent(log);
        AddListLayout(mLogContent, 0f);
        mLogScroll = log.GetComponent<ScrollRect>();

        mCopyButton = AddButton(panel, "Copy", OnCopy, Accent, DarkText, 28);
        Place((RectTransform)mCopyButton.transform, Vector2.zero, Vector2.zero, new Vector2(28f, 24f), new Vector2(228f, 80f));
        mDeleteButton = AddButton(panel, "Delete", OnDelete, RowColor, Color.white, 28);
        Place((RectTransform)mDeleteButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-228f, 24f), new Vector2(-28f, 80f));

        mStatusText = AddText(panel, "", 22, Color.white, TextAlignmentOptions.Center);
        Place(mStatusText.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(248f, 24f), new Vector2(-248f, 80f));
    }

    // A block of log text, as tall as its lines
    void AddLogBlock(string richText)
    {
        TMP_Text block = AddText(mLogContent, richText, 22, Color.white, TextAlignmentOptions.TopLeft);
        block.overflowMode = TextOverflowModes.Overflow;
        block.raycastTarget = false;
    }

    void AddLogHint(string text)
    {
        TMP_Text hint = AddText(mLogContent, text, 24, Muted, TextAlignmentOptions.Center);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
    }
}

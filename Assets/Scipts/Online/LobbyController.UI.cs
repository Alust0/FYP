using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MenuUI;

/* LobbyController's screen, built in code so the Lobby scene needs no Canvas: the room list on the
 left, your deck and camera top-right and the host panel below it.
 The rest of LobbyController is in LobbyController.cs. */
public partial class LobbyController
{
    void BuildUI()
    {
        RectTransform root = BuildCanvas("LobbyCanvas");

        // Header
        TMP_Text title = AddText(root, "Online Lobby", 64, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(128f, -110f), new Vector2(-300f, -20f));
        Place((RectTransform)SceneSettings.AddButton(root).transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -96f), new Vector2(110f, -34f));
        Button back = AddButton(root, "Back", OnBack, PanelColor, Color.white, 30);
        Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-240f, -96f), new Vector2(-48f, -34f));

        BuildRoomList(root);
        BuildDeckPanel(root);
        BuildHostPanel(root);

        LoadDecks();
        LoadCameras();
        ShowHosting(false);
    }

    void BuildRoomList(RectTransform root)
    {
        RectTransform panel = AddBox(root, "RoomsPanel", PanelColor);
        Place(panel, new Vector2(0f, 0f), new Vector2(0.62f, 1f), new Vector2(48f, 48f), new Vector2(-16f, -130f));

        TMP_Text heading = AddText(panel, "Open Rooms", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 28f, 420f);
        mRoomCount = AddText(panel, "", 26, Muted, TextAlignmentOptions.Left);
        PlaceTop(mRoomCount.rectTransform, 0f, 1f, 30f, 40f, 290f, 220f);
        Button refresh = AddButton(panel, "Refresh", RefreshSoon, RowColor, Color.white, 26);
        PlaceTop((RectTransform)refresh.transform, 1f, 1f, 18f, 56f, -200f, 28f);

        mHostFilter = AddInput(panel, "Search by host name...");
        PlaceTop((RectTransform)mHostFilter.transform, 0f, 0.6f, 92f, 60f, 28f, 10f);
        mHostFilter.onValueChanged.AddListener(_ => ShowRooms());

        // Checked when joining a private room
        mPasswordInput = AddInput(panel, "Room password...");
        PlaceTop((RectTransform)mPasswordInput.transform, 0.6f, 1f, 92f, 60f, 10f, 28f);
        mPasswordInput.characterLimit = PasswordMaxLength;
        mPasswordInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        mPasswordInput.onValueChanged.AddListener(_ => mPasswordInput.image.color = InputColor); // clears a wrong-password tint

        // Scrolling list
        RectTransform scrollArea = AddBox(panel, "List", new Color(0f, 0f, 0f, 0.2f));
        Place(scrollArea, Vector2.zero, Vector2.one, new Vector2(28f, 28f), new Vector2(-28f, -172f));
        mListContent = AddScrollContent(scrollArea);
        AddListLayout(mListContent, 10f);
    }

    void BuildDeckPanel(RectTransform root)
    {
        RectTransform panel = AddBox(root, "DeckPanel", PanelColor);
        Place(panel, new Vector2(0.62f, 1f), new Vector2(1f, 1f), new Vector2(16f, -540f), new Vector2(-48f, -130f));

        TMP_Text heading = AddText(panel, "Your Deck", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 28f, 260f);

        // Plays alone, to try the deck and camera without an opponent
        mTestButton = AddButton(panel, "Test Solo", OnTest, RowColor, Color.white, 26);
        PlaceTop((RectTransform)mTestButton.transform, 1f, 1f, 18f, 56f, -228f, 28f);

        mDeckDropdown = AddDropdown(panel);
        PlaceTop((RectTransform)mDeckDropdown.transform, 0f, 1f, 86f, 60f, 28f, 28f);
        mDeckDropdown.onValueChanged.AddListener(OnDeckChosen);

        RectTransform frame = AddBox(panel, "CoverFrame", Accent);
        Place(frame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -322f), new Vector2(140f, -166f)); // card-shaped, 63:88
        mDeckCover = AddRawImage(frame, 4f);

        mDeckInfo = AddText(panel, "", 24, Muted, TextAlignmentOptions.TopLeft);
        Place(mDeckInfo.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(164f, -322f), new Vector2(-28f, -170f));

        TMP_Text cameraLabel = AddText(panel, "Camera", 28, Color.white, TextAlignmentOptions.MidlineLeft);
        PlaceTop(cameraLabel.rectTransform, 0f, 1f, 340f, 56f, 28f, 28f);
        mCameraDropdown = AddDropdown(panel);
        PlaceTop((RectTransform)mCameraDropdown.transform, 0f, 1f, 340f, 56f, 164f, 28f);
    }

    void BuildHostPanel(RectTransform root)
    {
        RectTransform panel = AddBox(root, "HostPanel", PanelColor);
        Place(panel, new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Vector2(16f, 48f), new Vector2(-48f, -564f));

        TMP_Text heading = AddText(panel, "Host a Room", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 28f, 28f);

        TMP_Text info = AddText(panel, "Game v" + GameVersion + "   •   " + CardImageLibrary.Count + " cards\nPlaying as:  " + PlayerSession.Name,
            24, Muted, TextAlignmentOptions.TopLeft);
        PlaceTop(info.rectTransform, 0f, 1f, 76f, 64f, 28f, 28f);

        mPrivateToggle = AddToggle(panel, "Private room (join by password only)");
        PlaceTop((RectTransform)mPrivateToggle.transform, 0f, 1f, 150f, 44f, 28f, 28f);

        // The match ends in a draw when it runs out; the guest gets the host's
        TMP_Text timerLabel = AddText(panel, "Timer", 28, Color.white, TextAlignmentOptions.MidlineLeft);
        PlaceTop(timerLabel.rectTransform, 0f, 1f, 206f, 56f, 28f, 28f);
        mTimerDropdown = AddDropdown(panel);
        PlaceTop((RectTransform)mTimerDropdown.transform, 0f, 1f, 206f, 56f, 164f, 28f);
        mTimerDropdown.ClearOptions();
        mTimerDropdown.AddOptions(TimerOptions.Select(TimerName).ToList());

        mHostButton = AddButton(panel, "Host Room", OnHost, Accent, DarkText, 32);
        PlaceTop((RectTransform)mHostButton.transform, 0f, 0.62f, 278f, 70f, 28f, 8f);
        mCancelButton = AddButton(panel, "Cancel", OnCancelHost, RowColor, Color.white, 32);
        PlaceTop((RectTransform)mCancelButton.transform, 0f, 0.62f, 278f, 70f, 28f, 8f);

        // Off: the basic rules only, no card skills; the guest gets the host's (and Test Solo uses it too)
        mSkillsToggle = AddToggle(panel, "Card skills");
        PlaceTop((RectTransform)mSkillsToggle.transform, 0.62f, 1f, 291f, 44f, 12f, 28f);
        mSkillsToggle.isOn = true;

        mStatusText = AddText(panel, "", 24, Color.white, TextAlignmentOptions.Top);
        Place(mStatusText.rectTransform, Vector2.zero, new Vector2(1f, 1f), new Vector2(28f, 12f), new Vector2(-28f, -368f));
    }
}

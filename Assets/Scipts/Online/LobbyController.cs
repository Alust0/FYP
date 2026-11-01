using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static MenuUI;

/* Lobby before an online match: a list of open rooms to join, and a panel to host your own with
 the deck you'll play. Private rooms are listed too, but joining one needs its password typed
 into the Room password box. Each room is a Realtime Database entry, rooms/{code}:
   status     "waiting" until a guest joins, then "playing"; "closed" once someone leaves
   isPublic   false for private rooms
   passwordHash  private rooms only: PasswordHash(code, password), so the password itself isn't stored
   createdMs, host {id, name, beatMs}, guest {id, name, beatMs}
   deck, cover, gameVersion, cardsVersion — shown in the list
   timerMinutes  the host's Timer, 0 for none (MatchSession.TimerMinutes)
   noSkills   true when the host turned Card skills off (MatchSession.SkillsOn); missing means on
 and, once the match starts, turn/seat0/seat1 (see OnlineMatch). The host refreshes beatMs while
 waiting, so rooms left behind by a closed game drop off the list. Builds its own UI (LobbyController.UI.cs), like UISetup. */
public partial class LobbyController : MonoBehaviour
{
    [SerializeField] private string matchSceneName = "Ingame";
    [SerializeField] private string menuSceneName = "Menu";
    [Tooltip("Seconds between checks for a guest while hosting. Each check is one database read.")]
    [SerializeField] private float pollSeconds = 1f;
    [Tooltip("Seconds between refreshes of the room list. Each refresh reads every listed room.")]
    [SerializeField] private float listRefreshSeconds = 4f;
    [SerializeField] private float heartbeatSeconds = 4f;
    [Tooltip("A waiting room whose host hasn't refreshed it for this long counts as abandoned.")]
    [SerializeField] private float staleRoomSeconds = 15f;

    const string Rooms = "rooms";
    const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O or 1/I mix-ups
    const int CodeLength = 5; // room codes and made-up private room passwords
    const int PasswordMaxLength = 12;
    static readonly Color WrongInputColor = new Color(1f, 0.72f, 0.72f);
    const string DeckPref = "Lobby.DeckSet";
    static readonly int[] TimerOptions = { 0, 30, 40, 50, 60 }; // minutes, in the Timer dropdown's order

    // Room list
    TMP_InputField mHostFilter;
    TMP_InputField mPasswordInput;
    RectTransform mListContent;
    TMP_Text mRoomCount;
    List<RtdbRest.Result> mRooms = new List<RtdbRest.Result>();
    float mNextRefresh;
    bool mRefreshing;
    bool mJoining;

    // Deck
    TMP_Dropdown mDeckDropdown;
    RawImage mDeckCover;
    TMP_Text mDeckInfo;
    readonly List<Deck> mDecks = new List<Deck>(); // dropdown order; null = Inspector test deck
    Deck mChosenDeck;
    TMP_Dropdown mCameraDropdown;
    string[] mCameras = new string[0];
    Button mTestButton;

    // Hosting
    Toggle mPrivateToggle;
    Toggle mSkillsToggle; // Card skills on/off, next to Host Room
    TMP_Dropdown mTimerDropdown;
    Button mHostButton;
    Button mCancelButton;
    TMP_Text mStatusText;
    Coroutine mHosting;
    string mHostedCode; // room this PC is waiting in, removed if it leaves before anyone joins

    readonly Dictionary<string, Texture2D> mCovers = new Dictionary<string, Texture2D>();

    static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static string RoomPath(string code) => Rooms + "/" + code;
    static string GameVersion => Application.version;
    bool IsHosting => mHosting != null;

    void Start()
    {
        MatchSession.End(); // back from an earlier match
        BuildUI();
        if (!RtdbRest.Init()) SetStatus("Couldn't read the database settings (google-services.json).");
    }

    void Update()
    {
        if (!IsHosting && !mRefreshing && Time.time >= mNextRefresh)
            StartCoroutine(RefreshRooms());
    }

    void OnDestroy()
    {
        LeaveHostedRoom();
        foreach (Texture2D cover in mCovers.Values) Destroy(cover);
    }

    // Room list

    IEnumerator RefreshRooms()
    {
        mRefreshing = true;

        List<RtdbRest.Result> rooms = null;
        yield return RtdbRest.Query(Rooms, new Dictionary<string, object> { ["status"] = "waiting" }, 30, r => rooms = r);
        mRooms = rooms;

        ShowRooms();
        mNextRefresh = Time.time + listRefreshSeconds;
        mRefreshing = false;
    }

    void RefreshSoon() => mNextRefresh = 0f;

    void ShowRooms()
    {
        foreach (Transform row in mListContent) Destroy(row.gameObject);

        string hostFilter = mHostFilter.text.Trim();

        List<RtdbRest.Result> shown = mRooms
            .Where(IsJoinable)
            .Where(r => hostFilter.Length == 0 || HostName(r).IndexOf(hostFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(r => r.fields.GetLong("createdMs"))
            .ToList();

        foreach (RtdbRest.Result room in shown) AddRoomRow(room);

        mRoomCount.text = shown.Count == 1 ? "1 room" : shown.Count + " rooms";
        if (shown.Count == 0)
        {
            TMP_Text empty = AddText(mListContent, mRooms.Count == 0 ? "No open rooms yet. Host one!" : "No rooms match your filters.", 30, Muted, TextAlignmentOptions.Center);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        }
    }

    void AddRoomRow(RtdbRest.Result room)
    {
        RectTransform row = AddBox(mListContent, "Room " + room.id, RowColor);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 136f;

        // Deck cover, card-shaped, with a gold edge
        RectTransform frame = AddBox(row, "CoverFrame", Accent);
        Place(frame, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, -58f), new Vector2(98f, 58f));
        RawImage cover = AddRawImage(frame, 3f);
        cover.texture = Cover(room.fields.GetString("cover"));
        cover.color = cover.texture != null ? Color.white : RowColor;

        bool isPrivate = !room.fields.GetBool("isPublic");
        TMP_Text title = AddText(row, HostName(room) + "'s Room", 34, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -54f), new Vector2(-230f, -10f));

        TMP_Text deck = AddText(row, room.fields.GetString("deck") ?? "", 24, Accent, TextAlignmentOptions.Left);
        Place(deck.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -88f), new Vector2(-230f, -56f));

        TMP_Text details = AddText(row, "Game v" + room.fields.GetString("gameVersion") + "   •   " +
            room.fields.GetLong("cardsVersion") + " cards" + (isPrivate ? "   •   Private" : "") +
            (RoomTimer(room) > 0 ? "   •   " + TimerName(RoomTimer(room)) + " timer" : "") +
            (RoomSkills(room) ? "" : "   •   No skills"), 22, Muted, TextAlignmentOptions.Left);
        Place(details.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -122f), new Vector2(-230f, -90f));

        // Different builds may not sync the same way, so joining needs the same game version
        bool sameVersion = room.fields.GetString("gameVersion") == GameVersion;
        Button join = AddButton(row, sameVersion ? "Join" : "Update needed", () => OnJoin(room), Accent, DarkText, 28);
        Place((RectTransform)join.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-210f, 18f), new Vector2(-20f, 76f));
        join.interactable = sameVersion && !IsHosting && !mJoining;
    }

    bool IsJoinable(RtdbRest.Result room)
    {
        Dictionary<string, object> host = room.fields.GetMap("host");
        return room.fields.GetString("status") == "waiting"
            && host != null && host.GetString("id") != MatchSession.ClientId
            && room.fields.GetMap("guest") == null
            && NowMs - host.GetLong("beatMs") < staleRoomSeconds * 1000f;
    }

    static string HostName(RtdbRest.Result room) => room.fields.GetMap("host").GetString("name") ?? "Someone";

    static int RoomTimer(RtdbRest.Result room) => (int)room.fields.GetLong("timerMinutes");

    static bool RoomSkills(RtdbRest.Result room) => !room.fields.GetBool("noSkills");

    static string TimerName(int minutes) => minutes == 0 ? "None" : minutes == 60 ? "1 hour" : minutes + " min";

    int ChosenTimer => TimerOptions[mTimerDropdown.value];

    // Joining

    void OnJoin(RtdbRest.Result room)
    {
        if (mJoining || IsHosting) return;

        if (!room.fields.GetBool("isPublic"))
        {
            string password = TypedPassword;
            if (password.Length == 0)
            {
                ShowPasswordProblem(HostName(room) + "'s room is private. Type its password into the Room password box, then press Join.");
                return;
            }
            if (PasswordHash(room.id, password) != room.fields.GetString("passwordHash"))
            {
                ShowPasswordProblem("Wrong password for " + HostName(room) + "'s room.");
                return;
            }
        }
        StartCoroutine(Join(room));
    }

    // Takes the guest seat, only if the room hasn't changed since it was read, so two guests can't both get in
    IEnumerator Join(RtdbRest.Result room)
    {
        mJoining = true;
        ShowRooms(); // greys out the Join buttons
        SetStatus("Joining " + HostName(room) + "'s room...");
        string problem = "Couldn't join that room. Try again.";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var fields = new Dictionary<string, object> { ["status"] = "playing", ["guest"] = SeatInfo() };

            RtdbRest.Result result = null;
            yield return RtdbRest.Patch(RoomPath(room.id), fields, new[] { "status", "guest" }, r => result = r, room.version);

            if (result.succeeded)
            {
                StartMatch(room.id, 1, HostName(room), PlayerSession.Name, RoomTimer(room), RoomSkills(room));
                yield break;
            }
            Debug.LogWarning("[LobbyController] Join attempt " + (attempt + 1) + " failed: " + result.error);
            if (result.status == 403 || result.status == 0)
            {
                problem = Problem(result);
                break;
            }

            // Usually the host's heartbeat changed the room just before: read it again and retry if it's still open
            RtdbRest.Result fresh = null;
            yield return RtdbRest.Get(RoomPath(room.id), r => fresh = r);
            if (!fresh.succeeded || !IsJoinable(fresh))
            {
                problem = "That room just filled up or closed.";
                break;
            }
            room = fresh;
        }

        SetStatus(problem);
        mJoining = false;
        RefreshSoon();
    }

    // Hosting

    void OnHost()
    {
        if (IsHosting || mJoining) return;
        mHosting = StartCoroutine(HostRoom(!mPrivateToggle.isOn));
        ShowHosting(true);
    }

    void OnCancelHost()
    {
        if (mHosting != null) StopCoroutine(mHosting);
        StopHosting("");
    }

    void StopHosting(string status)
    {
        mHosting = null;
        LeaveHostedRoom();
        ShowHosting(false);
        SetStatus(status);
        RefreshSoon();
    }

    IEnumerator HostRoom(bool isPublic)
    {
        SetStatus("Creating your room...");

        Deck deck = mChosenDeck;
        int timer = ChosenTimer;
        var fields = new Dictionary<string, object>
        {
            ["status"] = "waiting",
            ["isPublic"] = isPublic,
            ["createdMs"] = NowMs,
            ["host"] = SeatInfo(),
            ["deck"] = DeckLabel(deck),
            ["cover"] = deck?.CoverCard ?? "",
            ["gameVersion"] = GameVersion,
            ["cardsVersion"] = CardImageLibrary.Count,
            ["timerMinutes"] = (long)timer,
            ["noSkills"] = !mSkillsToggle.isOn,
        };

        // The one typed into the Room password box, or a made-up one if it's empty
        string password = isPublic ? null : TypedPassword.Length > 0 ? TypedPassword : RandomCode();
        string code = null;
        RtdbRest.Result result = null;
        for (int attempt = 0; attempt < 5 && code == null; attempt++)
        {
            string candidate = RandomCode();
            if (!isPublic) fields["passwordHash"] = PasswordHash(candidate, password);
            yield return RtdbRest.Create(Rooms, candidate, fields, r => result = r);

            if (result.succeeded) code = candidate;
            else if (result.status != 409) break; // 409: that code is taken, so try another
        }

        if (code == null)
        {
            Debug.LogWarning("[LobbyController] Couldn't create a room: " + result?.error);
            StopHosting("Couldn't create a room. " + Problem(result));
            yield break;
        }

        mHostedCode = code;
        if (!isPublic) mPasswordInput.text = password; // shows a made-up one to the host
        SetStatus(isPublic
            ? "Your room is in the list. Waiting for an opponent..."
            : "Private room: give your opponent the password in your Room password box.");

        float lastBeat = Time.time;
        while (true)
        {
            yield return new WaitForSeconds(pollSeconds);

            if (Time.time - lastBeat >= heartbeatSeconds)
            {
                lastBeat = Time.time;
                var beat = new Dictionary<string, object> { ["host"] = new Dictionary<string, object> { ["beatMs"] = NowMs } };
                yield return RtdbRest.Patch(RoomPath(code), beat, new[] { "host.beatMs" }, null);
            }

            RtdbRest.Result room = null;
            yield return RtdbRest.Get(RoomPath(code), r => room = r);

            if (room.status == 404)
            {
                mHostedCode = null;
                StopHosting("Your room was closed.");
                yield break;
            }
            if (!room.succeeded) continue; // a dropped read; the next poll tries again

            Dictionary<string, object> guest = room.fields.GetMap("guest");
            if (guest != null)
            {
                mHostedCode = null;
                StartMatch(code, 0, PlayerSession.Name, guest.GetString("name"), timer, mSkillsToggle.isOn);
                yield break;
            }
        }
    }

    void LeaveHostedRoom()
    {
        if (mHostedCode == null) return;
        RtdbRest.DeleteWithoutWaiting(RoomPath(mHostedCode));
        mHostedCode = null;
    }

    static Dictionary<string, object> SeatInfo() => new Dictionary<string, object>
    {
        ["id"] = MatchSession.ClientId,
        ["name"] = PlayerSession.Name,
        ["beatMs"] = NowMs,
    };

    static string RandomCode()
    {
        char[] code = new char[CodeLength];
        for (int i = 0; i < code.Length; i++) code[i] = CodeLetters[UnityEngine.Random.Range(0, CodeLetters.Length)];
        return new string(code);
    }

    // Salted with the room code, so the same password in two rooms doesn't look the same
    static string PasswordHash(string code, string password)
    {
        using (SHA256 sha = SHA256.Create())
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(code + ":" + password)).Select(b => b.ToString("x2")));
    }

    void StartMatch(string code, int localSeat, string hostName, string guestName, int timerMinutes, bool skills)
    {
        MatchSession.Begin(code, localSeat, hostName ?? "Player 1", guestName ?? "Player 2", timerMinutes, skills);
        CameraChoice.Apply();
        SceneManager.LoadScene(matchSceneName);
    }

    // The Test button: the match with this deck and camera alone, no room or opponent
    void OnTest()
    {
        if (IsHosting || mJoining) return;
        MatchSession.BeginSolo(PlayerSession.Name, ChosenTimer, mSkillsToggle.isOn);
        CameraChoice.Apply();
        SceneManager.LoadScene(matchSceneName);
    }

    // Says why a request failed, instead of always blaming the connection
    static string Problem(RtdbRest.Result result)
    {
        if (result == null || result.status == 0) return "Couldn't reach the server. Check your internet connection.";
        if (result.status == 403) return "The database refused it: its Realtime Database rules don't allow rooms yet.";
        return "The server answered " + result.status + ". Try again.";
    }

    // Decks: the account's saved decks (Cloud Save), the starter decks, and the Inspector test deck

    /* Per editor window: PlayerPrefs are shared by a project's editor windows, so with one key a
     deck picked in one window became the other's too. Holds the deck's id */
    static string DeckPrefKey => DeckPref + "." + CameraChoice.WindowId;

    // Starter decks straight away; the saved ones join the list once Cloud Save answers
    async void LoadDecks()
    {
        ShowDecks(new List<Deck>());
        try
        {
            List<Deck> saved = await DeckRepository.LoadSavedAsync();
            if (this != null) ShowDecks(saved);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LobbyController] Couldn't load saved decks: " + e.Message);
            if (this != null) SetStatus("Couldn't load your saved decks. The starter decks still work.");
        }
    }

    void ShowDecks(List<Deck> saved)
    {
        mDecks.Clear();
        mDecks.Add(null);
        mDecks.AddRange(saved);
        mDecks.AddRange(DeckRepository.Starters());

        mDeckDropdown.ClearOptions();
        mDeckDropdown.AddOptions(mDecks.Select(DeckLabel).ToList());

        // A hosted room already shows its deck, so that one stays picked
        string id = IsHosting ? mChosenDeck?.id ?? "" : PlayerPrefs.GetString(DeckPrefKey, PlayerPrefs.GetString(DeckPref, ""));
        int index = Mathf.Max(0, mDecks.FindIndex(deck => (deck?.id ?? "") == id));
        mDeckDropdown.SetValueWithoutNotify(index);
        ShowChosenDeck(index);
    }

    // Only a pick from the dropdown is remembered, so a saved deck not loaded yet isn't forgotten
    void OnDeckChosen(int index)
    {
        ShowChosenDeck(index);
        PlayerPrefs.SetString(DeckPrefKey, mChosenDeck?.id ?? "");
    }

    void ShowChosenDeck(int index)
    {
        mChosenDeck = mDecks[index];
        MatchSession.DeckId = mChosenDeck?.id;

        mDeckCover.texture = Cover(mChosenDeck?.CoverCard);
        mDeckCover.color = mDeckCover.texture != null ? Color.white : RowColor;
        mDeckInfo.text = mChosenDeck == null
            ? "Uses Player 1's deck from the InspectorDeckSource in the Ingame scene."
            : DeckSummary(mChosenDeck) + "\nThe camera learns these before the match.";
    }

    static string DeckSummary(Deck deck)
    {
        int different = deck.UniqueCardNumbers.Count();
        int total = deck.TotalCards;
        return total == different ? different + " different cards." : total + " cards, " + different + " different.";
    }

    // Each window (e.g. Player 2 in Multiplayer Play Mode) or build picks its own webcam
    void LoadCameras()
    {
        mCameras = CameraChoice.Devices;
        mCameraDropdown.ClearOptions();

        if (mCameras.Length == 0)
        {
            mCameraDropdown.AddOptions(new List<string> { "No camera found" });
            mCameraDropdown.interactable = false;
            return;
        }

        mCameraDropdown.AddOptions(mCameras.ToList());
        mCameraDropdown.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(mCameras, CameraChoice.Current)));
        mCameraDropdown.onValueChanged.AddListener(index => CameraChoice.Choose(mCameras[index]));
    }

    static string DeckLabel(Deck deck) => deck == null ? "Test deck (Inspector)" : deck.deckName;

    // A card picture from StreamingAssets/cards, loaded once; null if there isn't one
    Texture2D Cover(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber)) return null;
        if (mCovers.TryGetValue(cardNumber, out Texture2D cached)) return cached;

        string path = CardImageLibrary.GetPath(cardNumber);
        Texture2D texture = null;
        if (path != null)
        {
            texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(texture);
                texture = null;
            }
        }
        mCovers[cardNumber] = texture;
        return texture;
    }

    void SetStatus(string text)
    {
        if (mStatusText != null) mStatusText.text = text;
    }

    string TypedPassword => mPasswordInput.text.Trim().ToUpperInvariant();

    // Tints the password box too, as the status line is across the screen from it
    void ShowPasswordProblem(string text)
    {
        SetStatus(text);
        mPasswordInput.image.color = WrongInputColor;
    }

    void ShowHosting(bool hosting)
    {
        mHostButton.gameObject.SetActive(!hosting);
        mCancelButton.gameObject.SetActive(hosting);
        mPrivateToggle.interactable = !hosting;
        mTimerDropdown.interactable = !hosting; // the guest gets the timer the room was made with
        mSkillsToggle.interactable = !hosting; // and its Card skills setting
        mDeckDropdown.interactable = !hosting;
        mCameraDropdown.interactable = !hosting && mCameras.Length > 0;
        mTestButton.interactable = !hosting;
        mPasswordInput.readOnly = hosting; // the room's password can't change once it's hosted
        ShowRooms(); // Join buttons are off while hosting
    }

    void OnBack()
    {
        OnCancelHost();
        SceneManager.LoadScene(menuSceneName);
    }
}

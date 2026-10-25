using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/* Lobby before an online match: a list of open rooms to join, and a panel to host your own with
 the deck you'll play. Hidden rooms stay out of the list; typing their code into the code filter
 finds them. Each room is a Firestore document, rooms/{code}:
   status     "waiting" until a guest joins, then "playing"; "closed" once someone leaves
   isPublic   false for hidden rooms
   createdMs, host {id, name, beatMs}, guest {id, name, beatMs}
   deck, cover, gameVersion, cardsVersion — shown in the list
 and, once the match starts, turn/seat0/seat1 (see OnlineMatch). The host refreshes beatMs while
 waiting, so rooms left behind by a closed game drop off the list. Builds its own UI, like UISetup. */
public class LobbyController : MonoBehaviour
{
    [SerializeField] private string matchSceneName = "Ingame";
    [SerializeField] private string menuSceneName = "Menu";
    [Tooltip("Seconds between checks for a guest while hosting. Each check is one Firestore read.")]
    [SerializeField] private float pollSeconds = 1f;
    [Tooltip("Seconds between refreshes of the room list. Each refresh reads every listed room.")]
    [SerializeField] private float listRefreshSeconds = 4f;
    [SerializeField] private float heartbeatSeconds = 4f;
    [Tooltip("A waiting room whose host hasn't refreshed it for this long counts as abandoned.")]
    [SerializeField] private float staleRoomSeconds = 15f;

    const string Rooms = "rooms";
    const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O or 1/I mix-ups
    const int CodeLength = 5;
    const string DeckPref = "Lobby.DeckSet";

    static readonly Color Background = new Color(0.07f, 0.08f, 0.11f);
    static readonly Color PanelColor = new Color(0.12f, 0.14f, 0.19f);
    static readonly Color RowColor = new Color(0.18f, 0.21f, 0.28f);
    static readonly Color Accent = new Color(0.96f, 0.76f, 0.26f);
    static readonly Color Muted = new Color(0.66f, 0.69f, 0.76f);
    static readonly Color DarkText = new Color(0.1f, 0.1f, 0.12f);

    // Room list
    TMP_InputField mHostFilter;
    TMP_InputField mCodeFilter;
    RectTransform mListContent;
    TMP_Text mRoomCount;
    List<FirestoreRest.Result> mRooms = new List<FirestoreRest.Result>();
    FirestoreRest.Result mCodeMatch; // a hidden room found by its code
    float mNextRefresh;
    bool mRefreshing;
    bool mJoining;

    // Deck
    TMP_Dropdown mDeckDropdown;
    RawImage mDeckCover;
    TMP_Text mDeckInfo;
    readonly List<string> mDeckSets = new List<string>(); // dropdown order; null = Inspector test deck
    TMP_Dropdown mCameraDropdown;
    string[] mCameras = new string[0];

    // Hosting
    Toggle mHiddenToggle;
    Button mHostButton;
    Button mCancelButton;
    TMP_Text mCodeText;
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
        if (!FirestoreRest.Init()) SetStatus("Couldn't read the database settings (google-services.json).");
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

        List<FirestoreRest.Result> rooms = null;
        yield return FirestoreRest.Query(Rooms, new Dictionary<string, object> { ["status"] = "waiting", ["isPublic"] = true }, 30, r => rooms = r);
        mRooms = rooms;

        string code = mCodeFilter.text.Trim().ToUpperInvariant();
        mCodeMatch = null;
        if (code.Length == CodeLength && !mRooms.Any(r => r.id == code))
        {
            FirestoreRest.Result hidden = null;
            yield return FirestoreRest.Get(RoomPath(code), r => hidden = r);
            if (hidden.ok) mCodeMatch = hidden;
        }

        ShowRooms();
        mNextRefresh = Time.time + listRefreshSeconds;
        mRefreshing = false;
    }

    void RefreshSoon() => mNextRefresh = 0f;

    void ShowRooms()
    {
        foreach (Transform row in mListContent) Destroy(row.gameObject);

        string hostFilter = mHostFilter.text.Trim();
        string codeFilter = mCodeFilter.text.Trim().ToUpperInvariant();

        IEnumerable<FirestoreRest.Result> rooms = mRooms;
        if (mCodeMatch != null) rooms = rooms.Append(mCodeMatch);

        List<FirestoreRest.Result> shown = rooms
            .Where(IsJoinable)
            .Where(r => hostFilter.Length == 0 || HostName(r).IndexOf(hostFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(r => codeFilter.Length == 0 || r.id.StartsWith(codeFilter))
            .OrderByDescending(r => r.fields.GetLong("createdMs"))
            .ToList();

        foreach (FirestoreRest.Result room in shown) AddRoomRow(room);

        mRoomCount.text = shown.Count == 1 ? "1 room" : shown.Count + " rooms";
        if (shown.Count == 0)
        {
            TMP_Text empty = AddText(mListContent, mRooms.Count == 0 ? "No open rooms yet. Host one!" : "No rooms match your filters.", 30, Muted, TextAlignmentOptions.Center);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        }
    }

    void AddRoomRow(FirestoreRest.Result room)
    {
        RectTransform row = AddBox(mListContent, "Room " + room.id, RowColor);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 136f;

        // Deck cover, card-shaped, with a gold edge
        RectTransform frame = AddBox(row, "CoverFrame", Accent);
        Place(frame, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, -58f), new Vector2(98f, 58f));
        RawImage cover = AddRawImage(frame, 3f);
        cover.texture = Cover(room.fields.GetString("cover"));
        cover.color = cover.texture != null ? Color.white : RowColor;

        bool hidden = !room.fields.GetBool("isPublic");
        TMP_Text title = AddText(row, HostName(room) + "'s Room", 34, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -54f), new Vector2(-230f, -10f));

        TMP_Text deck = AddText(row, DeckLabel(room.fields.GetString("deck")), 24, Accent, TextAlignmentOptions.Left);
        Place(deck.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -88f), new Vector2(-230f, -56f));

        TMP_Text details = AddText(row, "Game v" + room.fields.GetString("gameVersion") + "   •   " +
            room.fields.GetLong("cardsVersion") + " cards" + (hidden ? "   •   Hidden" : ""), 22, Muted, TextAlignmentOptions.Left);
        Place(details.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -122f), new Vector2(-230f, -90f));

        TMP_Text code = AddText(row, "#" + room.id, 22, Muted, TextAlignmentOptions.Right);
        Place(code.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-210f, -44f), new Vector2(-20f, -12f));

        // Different builds may not sync the same way, so joining needs the same game version
        bool sameVersion = room.fields.GetString("gameVersion") == GameVersion;
        Button join = AddButton(row, sameVersion ? "Join" : "Update needed", () => OnJoin(room), Accent, DarkText, 28);
        Place((RectTransform)join.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-210f, 18f), new Vector2(-20f, 76f));
        join.interactable = sameVersion && !IsHosting && !mJoining;
    }

    bool IsJoinable(FirestoreRest.Result room)
    {
        Dictionary<string, object> host = room.fields.GetMap("host");
        return room.fields.GetString("status") == "waiting"
            && host != null && host.GetString("id") != MatchSession.ClientId
            && room.fields.GetMap("guest") == null
            && NowMs - host.GetLong("beatMs") < staleRoomSeconds * 1000f;
    }

    static string HostName(FirestoreRest.Result room) => room.fields.GetMap("host").GetString("name") ?? "Someone";

    // Joining

    void OnJoin(FirestoreRest.Result room)
    {
        if (mJoining || IsHosting) return;
        StartCoroutine(Join(room));
    }

    // Takes the guest seat, only if the room hasn't changed since it was read, so two guests can't both get in
    IEnumerator Join(FirestoreRest.Result room)
    {
        mJoining = true;
        ShowRooms(); // greys out the Join buttons
        SetStatus("Joining " + HostName(room) + "'s room...");
        string problem = "Couldn't join that room. Try again.";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var fields = new Dictionary<string, object> { ["status"] = "playing", ["guest"] = SeatInfo() };

            FirestoreRest.Result result = null;
            yield return FirestoreRest.Patch(RoomPath(room.id), fields, new[] { "status", "guest" }, r => result = r, room.updateTime);

            if (result.ok)
            {
                StartMatch(room.id, 1, HostName(room), PlayerSession.Name);
                yield break;
            }
            Debug.LogWarning("[LobbyController] Join attempt " + (attempt + 1) + " failed: " + result.error);
            if (result.status == 403 || result.status == 0)
            {
                problem = Problem(result);
                break;
            }

            // Usually the host's heartbeat changed the room just before: read it again and retry if it's still open
            FirestoreRest.Result fresh = null;
            yield return FirestoreRest.Get(RoomPath(room.id), r => fresh = r);
            if (!fresh.ok || !IsJoinable(fresh))
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
        mHosting = StartCoroutine(HostRoom(!mHiddenToggle.isOn));
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

        string deck = MatchSession.DeckSet;
        var fields = new Dictionary<string, object>
        {
            ["status"] = "waiting",
            ["isPublic"] = isPublic,
            ["createdMs"] = NowMs,
            ["host"] = SeatInfo(),
            ["deck"] = deck ?? "",
            ["cover"] = CoverCard(deck) ?? "",
            ["gameVersion"] = GameVersion,
            ["cardsVersion"] = CardImageLibrary.Count,
        };

        string code = null;
        FirestoreRest.Result result = null;
        for (int attempt = 0; attempt < 5 && code == null; attempt++)
        {
            string candidate = RandomCode();
            yield return FirestoreRest.Create(Rooms, candidate, fields, r => result = r);

            if (result.ok) code = candidate;
            else if (result.status != 409) break; // 409: that code is taken, so try another
        }

        if (code == null)
        {
            Debug.LogWarning("[LobbyController] Couldn't create a room: " + result?.error);
            StopHosting("Couldn't create a room. " + Problem(result));
            yield break;
        }

        mHostedCode = code;
        mCodeText.text = code;
        SetStatus(isPublic
            ? "Your room is in the list. Waiting for an opponent..."
            : "Hidden room: give your opponent this code to type into the code filter.");

        float lastBeat = Time.time;
        while (true)
        {
            yield return new WaitForSeconds(pollSeconds);

            if (Time.time - lastBeat >= heartbeatSeconds)
            {
                lastBeat = Time.time;
                var beat = new Dictionary<string, object> { ["host"] = new Dictionary<string, object> { ["beatMs"] = NowMs } };
                yield return FirestoreRest.Patch(RoomPath(code), beat, new[] { "host.beatMs" }, null);
            }

            FirestoreRest.Result room = null;
            yield return FirestoreRest.Get(RoomPath(code), r => room = r);

            if (room.status == 404)
            {
                mHostedCode = null;
                StopHosting("Your room was closed.");
                yield break;
            }
            if (!room.ok) continue; // a dropped read; the next poll tries again

            Dictionary<string, object> guest = room.fields.GetMap("guest");
            if (guest != null)
            {
                mHostedCode = null;
                StartMatch(code, 0, PlayerSession.Name, guest.GetString("name"));
                yield break;
            }
        }
    }

    void LeaveHostedRoom()
    {
        if (mHostedCode == null) return;
        FirestoreRest.DeleteWithoutWaiting(RoomPath(mHostedCode));
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

    void StartMatch(string code, int localSeat, string hostName, string guestName)
    {
        MatchSession.Begin(code, localSeat, hostName ?? "Player 1", guestName ?? "Player 2");
        CameraChoice.Apply();
        SceneManager.LoadScene(matchSceneName);
    }

    // Says why a request failed, instead of always blaming the connection
    static string Problem(FirestoreRest.Result result)
    {
        if (result == null || result.status == 0) return "Couldn't reach the server. Check your internet connection.";
        if (result.status == 403) return "The database refused it: its Firestore rules don't allow rooms yet.";
        return "The server answered " + result.status + ". Try again.";
    }

    // Decks: the Start/Trial Deck sets in the card pictures, plus the Inspector test deck

    /* Per editor window: PlayerPrefs are shared by a project's editor windows, so with one key a
     deck picked in one window became the other's too */
    static string DeckPrefKey => DeckPref + "." + CameraChoice.WindowId;

    void LoadDecks()
    {
        mDeckSets.Clear();
        mDeckSets.Add(null);
        mDeckSets.AddRange(CardImageLibrary.SetCodes().Where(code => Regex.IsMatch(code, @"-(SD|TD|TTD|LTD)\d+$")));

        mDeckDropdown.ClearOptions();
        mDeckDropdown.AddOptions(mDeckSets.Select(DeckLabel).ToList());

        string saved = PlayerPrefs.GetString(DeckPrefKey, PlayerPrefs.GetString(DeckPref, ""));
        int index = Mathf.Max(0, mDeckSets.IndexOf(saved.Length > 0 ? saved : null));
        mDeckDropdown.SetValueWithoutNotify(index);
        OnDeckChosen(index);
    }

    void OnDeckChosen(int index)
    {
        string set = mDeckSets[index];
        MatchSession.DeckSet = set;
        PlayerPrefs.SetString(DeckPrefKey, set ?? "");

        mDeckCover.texture = Cover(CoverCard(set));
        mDeckCover.color = mDeckCover.texture != null ? Color.white : RowColor;
        mDeckInfo.text = set == null
            ? "Uses Player 1's deck from the InspectorDeckSource in the Ingame scene."
            : CardImageLibrary.CardNumbersInSet(set).Count + " different cards.\nThe camera learns these before the match.";
    }

    // Editor only: each window (e.g. Player 2 in Multiplayer Play Mode) picks its own webcam
    void LoadCameras()
    {
        mCameras = CameraChoice.Devices;
        mCameraDropdown.ClearOptions();

        if (!Application.isEditor || mCameras.Length == 0)
        {
            mCameraDropdown.AddOptions(new List<string> { Application.isEditor ? "No camera found" : "Device camera" });
            mCameraDropdown.interactable = false;
            return;
        }

        mCameraDropdown.AddOptions(mCameras.ToList());
        mCameraDropdown.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(mCameras, CameraChoice.Current)));
        mCameraDropdown.onValueChanged.AddListener(index => CameraChoice.Choose(mCameras[index]));
    }

    static string DeckLabel(string set)
    {
        if (string.IsNullOrEmpty(set)) return "Test deck (Inspector)";
        return (Regex.IsMatch(set, @"-SD\d+$") ? "Start Deck " : "Trial Deck ") + set;
    }

    static string CoverCard(string set) => string.IsNullOrEmpty(set) ? null : CardImageLibrary.CardNumbersInSet(set).FirstOrDefault();

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

    void ShowHosting(bool hosting)
    {
        mHostButton.gameObject.SetActive(!hosting);
        mCancelButton.gameObject.SetActive(hosting);
        mHiddenToggle.interactable = !hosting;
        mDeckDropdown.interactable = !hosting;
        mCameraDropdown.interactable = !hosting && Application.isEditor && mCameras.Length > 0;
        if (!hosting) mCodeText.text = "";
        ShowRooms(); // Join buttons are off while hosting
    }

    // UI

    void BuildUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        GameObject canvasObj = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = AddBox(canvasObj.transform, "Background", Background);
        Place(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Header
        TMP_Text title = AddText(root, "Online Lobby", 64, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -110f), new Vector2(-300f, -20f));
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

        mCodeFilter = AddInput(panel, "Room code...");
        PlaceTop((RectTransform)mCodeFilter.transform, 0.6f, 1f, 92f, 60f, 10f, 28f);
        mCodeFilter.characterLimit = CodeLength;
        mCodeFilter.contentType = TMP_InputField.ContentType.Alphanumeric;
        mCodeFilter.onValueChanged.AddListener(text =>
        {
            ShowRooms();
            if (text.Trim().Length == CodeLength) RefreshSoon(); // may be a hidden room's code
        });

        // Scrolling list
        RectTransform scrollArea = AddBox(panel, "List", new Color(0f, 0f, 0f, 0.2f));
        Place(scrollArea, Vector2.zero, Vector2.one, new Vector2(28f, 28f), new Vector2(-28f, -172f));
        ScrollRect scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollArea, false);
        Place((RectTransform)viewport.transform, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        mListContent = (RectTransform)content.transform;
        mListContent.anchorMin = new Vector2(0f, 1f);
        mListContent.anchorMax = new Vector2(1f, 1f);
        mListContent.pivot = new Vector2(0.5f, 1f);
        mListContent.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = mListContent;
    }

    void BuildDeckPanel(RectTransform root)
    {
        RectTransform panel = AddBox(root, "DeckPanel", PanelColor);
        Place(panel, new Vector2(0.62f, 1f), new Vector2(1f, 1f), new Vector2(16f, -540f), new Vector2(-48f, -130f));

        TMP_Text heading = AddText(panel, "Your Deck", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 28f, 28f);

        GameObject dropdownObj = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownObj.transform.SetParent(panel, false);
        PlaceTop((RectTransform)dropdownObj.transform, 0f, 1f, 86f, 60f, 28f, 28f);
        mDeckDropdown = dropdownObj.GetComponent<TMP_Dropdown>();
        StyleDropdown(mDeckDropdown);
        mDeckDropdown.onValueChanged.AddListener(OnDeckChosen);

        RectTransform frame = AddBox(panel, "CoverFrame", Accent);
        Place(frame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -322f), new Vector2(140f, -166f)); // card-shaped, 63:88
        mDeckCover = AddRawImage(frame, 4f);

        mDeckInfo = AddText(panel, "", 24, Muted, TextAlignmentOptions.TopLeft);
        Place(mDeckInfo.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(164f, -322f), new Vector2(-28f, -170f));

        TMP_Text cameraLabel = AddText(panel, "Camera", 28, Color.white, TextAlignmentOptions.MidlineLeft);
        PlaceTop(cameraLabel.rectTransform, 0f, 1f, 340f, 56f, 28f, 28f);
        GameObject cameraObj = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        cameraObj.transform.SetParent(panel, false);
        PlaceTop((RectTransform)cameraObj.transform, 0f, 1f, 340f, 56f, 164f, 28f);
        mCameraDropdown = cameraObj.GetComponent<TMP_Dropdown>();
        StyleDropdown(mCameraDropdown);
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

        mHiddenToggle = AddToggle(panel, "Hidden room (join by code only)");
        PlaceTop((RectTransform)mHiddenToggle.transform, 0f, 1f, 150f, 44f, 28f, 28f);

        mHostButton = AddButton(panel, "Host Room", OnHost, Accent, DarkText, 32);
        PlaceTop((RectTransform)mHostButton.transform, 0f, 1f, 206f, 70f, 28f, 28f);
        mCancelButton = AddButton(panel, "Cancel", OnCancelHost, RowColor, Color.white, 32);
        PlaceTop((RectTransform)mCancelButton.transform, 0f, 1f, 206f, 70f, 28f, 28f);

        mCodeText = AddText(panel, "", 60, Accent, TextAlignmentOptions.Center);
        PlaceTop(mCodeText.rectTransform, 0f, 1f, 284f, 66f, 28f, 28f);

        mStatusText = AddText(panel, "", 24, Color.white, TextAlignmentOptions.Top);
        Place(mStatusText.rectTransform, Vector2.zero, new Vector2(1f, 1f), new Vector2(28f, 12f), new Vector2(-28f, -356f));
    }

    void OnBack()
    {
        OnCancelHost();
        SceneManager.LoadScene(menuSceneName);
    }

    // UI helpers

    // Stretches between anchors (fractions of the parent), then insets by the offsets
    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // A band `top` px down from the parent's top edge, between x fractions, inset left/right by px
    static void PlaceTop(RectTransform rt, float xMin, float xMax, float top, float height, float left, float right) =>
        Place(rt, new Vector2(xMin, 1f), new Vector2(xMax, 1f), new Vector2(left, -top - height), new Vector2(-right, -top));

    static RectTransform AddBox(Transform parent, string name, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = color;
        return (RectTransform)obj.transform;
    }

    // For card pictures, filling the parent minus a border
    static RawImage AddRawImage(Transform parent, float border)
    {
        GameObject obj = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
        obj.transform.SetParent(parent, false);
        Place((RectTransform)obj.transform, Vector2.zero, Vector2.one, new Vector2(border, border), new Vector2(-border, -border));
        return obj.GetComponent<RawImage>();
    }

    static TMP_Text AddText(Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    static Button AddButton(Transform parent, string label, UnityAction onClick, Color background, Color textColor, float fontSize)
    {
        GameObject obj = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
        obj.name = label;
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = background;

        TMP_Text text = obj.GetComponentInChildren<TMP_Text>();
        text.text = label;
        text.fontSize = fontSize;
        text.color = textColor;

        Button button = obj.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        return button;
    }

    static TMP_InputField AddInput(Transform parent, string placeholder)
    {
        GameObject obj = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        obj.name = placeholder;
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = new Color(0.92f, 0.93f, 0.96f);

        TMP_InputField input = obj.GetComponent<TMP_InputField>();
        input.textComponent.fontSize = 28;
        input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        if (input.placeholder is TMP_Text hint)
        {
            hint.text = placeholder;
            hint.fontSize = 28;
            hint.alignment = TextAlignmentOptions.MidlineLeft;
        }
        return input;
    }

    static Toggle AddToggle(Transform parent, string label)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        RectTransform box = AddBox(obj.transform, "Box", new Color(0.92f, 0.93f, 0.96f));
        Place(box, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -18f), new Vector2(36f, 18f));
        RectTransform check = AddBox(box, "Check", Accent);
        Place(check, Vector2.zero, Vector2.one, new Vector2(7f, 7f), new Vector2(-7f, -7f));

        TMP_Text text = AddText(obj.transform, label, 26, Color.white, TextAlignmentOptions.MidlineLeft);
        Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(52f, 0f), Vector2.zero);

        Toggle toggle = obj.AddComponent<Toggle>();
        toggle.targetGraphic = box.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = false;
        return toggle;
    }

    static void StyleDropdown(TMP_Dropdown dropdown)
    {
        dropdown.GetComponent<Image>().color = new Color(0.92f, 0.93f, 0.96f);
        dropdown.captionText.fontSize = 28;
        dropdown.captionText.color = DarkText;
        dropdown.itemText.fontSize = 26;
        dropdown.itemText.color = DarkText;

        Transform arrow = dropdown.transform.Find("Arrow");
        if (arrow != null) arrow.gameObject.SetActive(false); // no sprite for it without the default UI resources

        RectTransform template = dropdown.template;
        template.sizeDelta = new Vector2(template.sizeDelta.x, 420f);
        Transform checkmark = template.Find("Viewport/Content/Item/Item Checkmark");
        if (checkmark != null) checkmark.GetComponent<Image>().color = Accent;
        RectTransform item = template.Find("Viewport/Content/Item") as RectTransform;
        if (item != null) item.sizeDelta = new Vector2(item.sizeDelta.x, 44f);
    }
}

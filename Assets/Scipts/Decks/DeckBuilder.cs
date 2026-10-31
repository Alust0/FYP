using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using static MenuUI;

/* Deck scene: the account's saved decks on the left, a card browser in the middle (one set or all
 of them, narrowed by a card-number search) and the open deck on the right. Clicking a card adds a copy. Decks save to
 Cloud Save through DeckRepository. Builds its own UI (DeckBuilder.UI.cs), like the lobby.
 Deck rules: up to maxCopies of a card number, and one nation. Cray Elementals (no nation) go in any
 deck, and dual-nation cards go in a deck of either of their nations.
 Scan Cards shows the camera instead of the card grid, and CardScanner adds each card it finds
 like a click. */
public partial class DeckBuilder : MonoBehaviour
{
    [SerializeField] private string menuSceneName = "Menu";
    [Tooltip("Scans cards with the camera. Left empty, one is added to this object.")]
    [SerializeField] private CardScanner scanner;
    [Tooltip("Shows the Scan Cards button. Off while scanning is still unreliable.")]
    [SerializeField] private bool showScanButton = false;
    [Tooltip("Copies allowed of one card number.")]
    [SerializeField] private int maxCopies = 4;
    [Tooltip("Cards in a full deck. The count turns gold at exactly this many; saving isn't blocked.")]
    [SerializeField] private int deckSize = 50;
    [Tooltip("Cards added to the browser at a time; scrolling to the bottom adds the next lot.")]
    [FormerlySerializedAs("maxSearchResults")] [SerializeField] private int pageSize = 60;

    static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.7f);
    static readonly char[] NationSeparators = { '/', '・', ',', '、' };

    // Saved decks, in the dropdown under the deck's name
    TMP_Dropdown mDeckDropdown;
    Button mDeckArrow;
    readonly List<Deck> mDeckChoices = new List<Deck>(); // the dropdown's decks; null is the open deck before it's saved, or "choose a deck"
    List<Deck> mSaved = new List<Deck>();
    bool mLoading = true;

    // Card details panel
    RawImage mDetailPicture;
    AspectRatioFitter mDetailFitter;
    Texture2D mDetailTexture; // its own copy, as the browser frees its pictures
    TMP_Text mDetailText;
    string mDetailNumber;

    // Card browser; the set dropdown's first option is every set
    const string AllSets = "All sets";
    TMP_Text mCardsHeading;
    TMP_Dropdown mSetDropdown;
    TMP_InputField mSearchInput;
    Button mWholeSetButton;
    RectTransform mGridContent;
    ScrollRect mGridScroll;
    readonly HashSet<string> mGridNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // cards in the grid now
    List<string> mSets = new List<string>();
    List<string> mAllNumbers;
    string mShownKey; // what the grid shows, so the same view isn't rebuilt
    IEnumerator<string> mMoreCards; // cards not added to the grid yet; null once all are
    readonly List<(string number, RawImage image)> mPictureQueue = new List<(string, RawImage)>();
    readonly Dictionary<string, TMP_Text> mBadges = new Dictionary<string, TMP_Text>();
    readonly Dictionary<string, Texture2D> mPictures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    Coroutine mLoadingPictures;

    /* Filters: they need each shown card's details from Firestore, so the cards load first.
     Cards the database doesn't have are hidden while a filter is on, as they can't be checked */
    const string AnyNation = "Any nation";
    const string NoNation = "No nation";
    const string AnyRarity = "Any rarity";
    static readonly string[] GradeOptions = { "Any grade", "Grade 0", "Grade 1", "Grade 2", "Grade 3", "Grade 4" };
    static readonly string[] TypeOptions = { "Any type", "Normal Unit", "Trigger Unit", "G Unit", "Order" };
    // The Kind row's options per type; trigger names match TriggerIcon
    static readonly string[] TriggerKinds = { "Any trigger", "Critical", "Draw", "Front", "Heal", "Over", "Stand" };
    static readonly string[] UnitKinds = { "Any unit", "Sentinel", "Persona Ride", "Overdress", "Token", "Ride Deck Crest", "Special Move" };
    static readonly string[] OrderKinds = { "Any order", "Normal Order", "Blitz Order", "Set Order", "Trigger Order" };
    GameObject mFilterPopup;
    RectTransform mFilterBox;
    TMP_Dropdown mKindDropdown;
    TMP_Text mKindLabel;
    Image mFilterIcon;
    Button mFilterButton;
    TMP_Dropdown mNationDropdown;
    TMP_Dropdown mGradeDropdown;
    TMP_Dropdown mRarityDropdown;
    TMP_Dropdown mTypeDropdown;
    readonly List<string> mNationOptions = new List<string> { AnyNation };
    readonly List<string> mRarityOptions = new List<string> { AnyRarity };
    int mDetailsRequest; // the newest LoadDetails, so an older one finishing late does nothing

    // Scan mode
    Button mScanButton;
    GameObject mScanBar;
    TMP_Text mScanText;
    RectTransform mGrid;
    Image mBackground;
    Image mCardsPanel;

    // Card details from Firestore, for the nation rule; cards it doesn't have can't be checked
    readonly Dictionary<string, CardData> mCardData = new Dictionary<string, CardData>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> mNotInDatabase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Deck sections, picked in the dropdown at the top of the deck panel; clicked cards go into the picked one
    enum DeckSection { Main, Ride, Extras, DeckSleeve, RideSleeve /*, GZone, Gifts */ }
    static readonly DeckSection[] Sections = { DeckSection.Main, DeckSection.Ride, DeckSection.Extras, DeckSection.DeckSleeve, DeckSection.RideSleeve /*, DeckSection.GZone, DeckSection.Gifts */ };
    // The sleeve sections show sleeves in the browser instead of cards
    bool SleeveSection => mSection == DeckSection.DeckSleeve || mSection == DeckSection.RideSleeve;
    const int RideDeckSize = 4;
    TMP_Dropdown mSectionDropdown;
    DeckSection mSection;

    // Open deck: a copy, so the saved one only changes on Save
    Deck mDeck;
    bool mDirty;
    string mDiscardWarned; // the action already warned about, so doing it again discards
    bool mBusy;
    TMP_InputField mNameInput;
    TMP_Text mCountText;
    TMP_Text mNationText;
    RectTransform mEntriesContent;
    TMP_Text mDeckHint;
    Button mSaveButton;
    Button mUndoButton;
    Button mCancelButton;
    Button mDeleteButton;
    readonly List<Deck> mUndo = new List<Deck>(); // the open deck before each card change, newest last
    const int MaxUndo = 50;
    TMP_Text mStatusText;
    GameObject mStatusBar;

    void Start()
    {
        // Card names and grades; the Deck scene has no database of its own
        if (CardFirestoreDatabase.Instance == null)
            new GameObject("CardFirestoreDatabase").AddComponent<CardFirestoreDatabase>();

        BuildUI();
        SetUpScanner();
        ShowDeck();
        ShowDeckChoices();
        LoadSavedDecks();
    }

    void OnDestroy()
    {
        foreach (Texture2D picture in mPictures.Values)
            if (picture != null) Destroy(picture);
        if (mDetailTexture != null) Destroy(mDetailTexture);
    }

    // Scan mode

    // Ticking the box in Play Mode shows or hides the button straight away
    void OnValidate()
    {
        if (mScanButton == null) return;
        if (!showScanButton && scanner != null && scanner.IsScanning) StopScanning("");
        mScanButton.gameObject.SetActive(showScanButton);
    }

    void SetUpScanner()
    {
        if (scanner == null) scanner = GetComponent<CardScanner>();
        if (scanner == null) scanner = gameObject.AddComponent<CardScanner>();
        scanner.CardScanned += OnCardClicked;
        scanner.StatusChanged += text => mScanText.text = text;
        scanner.Failed += StopScanning;
    }

    void OnScan()
    {
        if (scanner.IsScanning)
        {
            StopScanning("");
            return;
        }
        if (!scanner.CanScan)
        {
            SetStatus("Scanning needs Vuforia's AR Camera and the Cloud Reco objects in the Deck scene.");
            return;
        }

        ShowScanMode(true);
        scanner.StartScanning();
    }

    void StopScanning(string status)
    {
        scanner.StopScanning();
        ShowScanMode(false);
        if (status.Length > 0) SetStatus(status);
    }

    // The camera's picture shows through where the card grid was
    void ShowScanMode(bool on)
    {
        mGrid.gameObject.SetActive(!on);
        mScanBar.SetActive(on);
        mBackground.color = on ? Color.clear : Background;
        mCardsPanel.color = on ? Color.clear : PanelColor;
        mScanButton.GetComponentInChildren<TMP_Text>().text = on ? "Stop Scanning" : "Scan Cards";
    }

    // Saved decks

    async void LoadSavedDecks()
    {
        try
        {
            mSaved = await DeckRepository.LoadSavedAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DeckBuilder] Couldn't load saved decks: " + e.Message);
            if (this != null) SetStatus("Couldn't load your saved decks. Check your internet connection.");
        }
        if (this == null) return;
        mLoading = false;
        ShowDeckChoices();
    }

    /* Fills the deck dropdown with the saved decks and selects the open one. An unsaved deck, or
     no deck, takes a first option of its own, so picking any saved deck counts as a change. */
    void ShowDeckChoices()
    {
        mDeckChoices.Clear();
        var options = new List<string>();
        bool saved = mDeck != null && mSaved.Any(d => d.id == mDeck.id);
        if (!saved)
        {
            mDeckChoices.Add(null);
            options.Add(mDeck != null ? mDeck.deckName + "  <color=#7A8294>(unsaved)</color>" :
                mLoading ? "Loading your decks..." : mSaved.Count == 0 ? "No saved decks yet" : "Choose a deck...");
        }
        foreach (Deck deck in mSaved)
        {
            mDeckChoices.Add(deck);
            options.Add(deck.deckName + "  <size=20><color=#7A8294>" + deck.TotalCards + " cards</color></size>");
        }

        mDeckDropdown.ClearOptions();
        mDeckDropdown.AddOptions(options);
        mDeckDropdown.SetValueWithoutNotify(saved ? mDeckChoices.FindIndex(d => d != null && d.id == mDeck.id) : 0);
        mDeckArrow.interactable = mSaved.Count > 0;
    }

    void OnDeckChosen(int index)
    {
        Deck chosen = index >= 0 && index < mDeckChoices.Count ? mDeckChoices[index] : null;
        if (chosen != null) OnOpenDeck(chosen);
        ShowDeckChoices(); // back to the open deck if unsaved changes stopped the switch
    }

    void OnOpenDeck(Deck saved)
    {
        if (mDeck != null && mDeck.id == saved.id) return;
        if (!CanDiscard("open " + saved.id)) return;
        Open(Copy(saved));
    }

    void OnNewDeck()
    {
        if (!CanDiscard("new")) return;
        Open(new Deck { deckName = "New Deck" });
    }

    void Open(Deck deck)
    {
        if (deck != null)
        {
            deck.rideDeck ??= new List<DeckEntry>(); // decks saved before these sections existed
            deck.extras ??= new List<DeckEntry>();
        }
        mDeck = deck;
        mDirty = false;
        mDiscardWarned = null;
        mUndo.Clear();
        SetStatus("");
        ShowDeck();
        ShowDeckChoices();
    }

    async void OnSave()
    {
        if (mDeck == null || mBusy) return;
        if (string.IsNullOrWhiteSpace(mDeck.deckName)) mDeck.deckName = "Untitled Deck";
        mDeck.deckName = mDeck.deckName.Trim();

        // Decks saved before the nation rule can still mix nations
        HashSet<string> shared = SharedNations();
        if (shared != null && shared.Count == 0)
        {
            SetStatus("This deck mixes nations. Remove cards until only one nation is left, then save.");
            return;
        }

        SetBusy(true, "Saving...");
        try
        {
            Deck saved = Copy(mDeck);
            await DeckRepository.SaveAsync(saved);
            if (this == null) return;
            mDeck.id = saved.id;
            mSaved = await DeckRepository.LoadSavedAsync();
            if (this == null) return;
            mDirty = false;
            SetBusy(false, "Saved \"" + mDeck.deckName + "\".");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DeckBuilder] Couldn't save the deck: " + e.Message);
            if (this != null) SetBusy(false, "Couldn't save the deck. Check your internet connection.");
        }
        if (this == null) return;
        ShowDeck();
        ShowDeckChoices();
    }

    async void OnDelete()
    {
        if (mDeck == null || mBusy) return;

        // An unsaved new deck just closes
        if (string.IsNullOrEmpty(mDeck.id))
        {
            if (CanDiscard("delete")) Open(null);
            return;
        }
        if (mDiscardWarned != "delete")
        {
            mDiscardWarned = "delete";
            SetStatus("Press Delete again to delete \"" + mDeck.deckName + "\" for good.");
            return;
        }

        SetBusy(true, "Deleting...");
        try
        {
            string name = mDeck.deckName;
            await DeckRepository.DeleteAsync(mDeck.id);
            if (this == null) return;
            mSaved = await DeckRepository.LoadSavedAsync();
            if (this == null) return;
            Open(null);
            SetBusy(false, "Deleted \"" + name + "\".");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DeckBuilder] Couldn't delete the deck: " + e.Message);
            if (this != null) SetBusy(false, "Couldn't delete the deck. Check your internet connection.");
        }
    }

    // Before a change, so Undo can go back to this
    void RememberDeck()
    {
        if (mDeck == null) return;
        mUndo.Add(Copy(mDeck));
        if (mUndo.Count > MaxUndo) mUndo.RemoveAt(0);
    }

    void OnUndo()
    {
        if (mUndo.Count == 0 || mBusy) return;
        Deck last = mUndo[mUndo.Count - 1];
        mUndo.RemoveAt(mUndo.Count - 1);
        Restore(last);
        SetStatus("");
    }

    // Back to the saved deck (an unsaved deck empties); Undo brings the changes back
    void OnCancel()
    {
        if (mDeck == null || !mDirty || mBusy) return;
        RememberDeck();
        Deck saved = SavedVersion();
        Restore(saved != null ? Copy(saved) : new Deck { deckName = mDeck.deckName });
        SetStatus("Changes thrown away. Undo brings them back.");
    }

    void Restore(Deck deck)
    {
        deck.id = mDeck.id;
        deck.rideDeck ??= new List<DeckEntry>();
        deck.extras ??= new List<DeckEntry>();
        mDeck = deck;

        // Unsaved unless it's back to what was saved
        Deck saved = SavedVersion();
        mDirty = saved != null ? JsonUtility.ToJson(Copy(saved)) != JsonUtility.ToJson(Copy(mDeck)) : mDeck.AllEntries.Any();
        mDiscardWarned = null;
        ShowDeck();
        ShowDeckChoices();
    }

    Deck SavedVersion() => string.IsNullOrEmpty(mDeck?.id) ? null : mSaved.FirstOrDefault(d => d.id == mDeck.id);

    void OnBack()
    {
        if (!CanDiscard("back")) return;
        if (scanner.IsScanning) StopScanning("");
        SceneManager.LoadScene(menuSceneName);
    }

    // With unsaved changes, warns the first time and goes ahead the second
    bool CanDiscard(string action)
    {
        if (!mDirty || mDiscardWarned == action) return true;
        mDiscardWarned = action;
        SetStatus("This deck has unsaved changes. Save it, or do that again to throw them away.");
        return false;
    }

    static Deck Copy(Deck deck) => new Deck
    {
        id = deck.id,
        deckName = deck.deckName,
        cards = CopyEntries(deck.cards),
        rideDeck = CopyEntries(deck.rideDeck),
        extras = CopyEntries(deck.extras),
        deckSleeve = deck.deckSleeve,
        rideDeckSleeve = deck.rideDeckSleeve,
    };

    static List<DeckEntry> CopyEntries(List<DeckEntry> entries) =>
        (entries ?? new List<DeckEntry>()).Select(entry => new DeckEntry { cardNumber = entry.cardNumber, count = entry.count }).ToList();

    // The open deck's list for a section
    List<DeckEntry> SectionCards(DeckSection section)
    {
        switch (section)
        {
            case DeckSection.Ride: return mDeck.rideDeck;
            case DeckSection.Extras: return mDeck.extras;
            //case DeckSection.GZone: return mDeck.gZone;
            //case DeckSection.Gifts: return mDeck.gifts;
            default: return mDeck.cards;
        }
    }

    static string SectionName(DeckSection section)
    {
        switch (section)
        {
            case DeckSection.Ride: return "Ride Deck";
            case DeckSection.Extras: return "Tokens / Crests";
            case DeckSection.DeckSleeve: return "Deck Sleeve";
            case DeckSection.RideSleeve: return "Ride Deck Sleeve";
            //case DeckSection.GZone: return "G Zone";
            //case DeckSection.Gifts: return "Gifts";
            default: return "Main Deck";
        }
    }

    // The open deck's sleeve for a sleeve section
    string SleeveOf(DeckSection section) => mDeck == null ? null : section == DeckSection.RideSleeve ? mDeck.rideDeckSleeve : mDeck.deckSleeve;

    void OnSectionChanged(int index)
    {
        bool wasSleeves = SleeveSection;
        mSection = Sections[Mathf.Clamp(index, 0, Sections.Length - 1)];
        SetStatus("");
        if (SleeveSection != wasSleeves) ShowBrowserMode();
        ShowDeck();
    }

    /* Sleeve sections swap the card browser for the sleeve pictures: no filters or whole sets, and
     the search is cleared, as card numbers don't match sleeve ids */
    void ShowBrowserMode()
    {
        bool sleeves = SleeveSection;
        if (scanner.IsScanning) StopScanning("");
        mFilterPopup.SetActive(false);
        mFilterButton.gameObject.SetActive(!sleeves);
        mWholeSetButton.gameObject.SetActive(!sleeves);
        if (mSearchInput.placeholder is TMP_Text hint)
            hint.text = sleeves ? "Search sleeve number, e.g. s120..." : "Search card number, e.g. BT01-005...";
        mSearchInput.SetTextWithoutNotify("");
        ShowBrowser();
    }

    // Each section's name and card count; the ride deck shows its limit and the sleeves their picture
    void ShowSectionOptions()
    {
        var options = new List<string>();
        foreach (DeckSection section in Sections)
        {
            string size;
            if (section == DeckSection.DeckSleeve || section == DeckSection.RideSleeve)
                size = string.IsNullOrEmpty(SleeveOf(section)) ? "none" : SleeveOf(section);
            else
            {
                int count = mDeck != null ? SectionCards(section).Sum(e => e.count) : 0;
                size = section == DeckSection.Ride ? count + "/" + RideDeckSize : count.ToString();
            }
            options.Add(SectionName(section) + "  <color=#7A8294>" + size + "</color>");
        }
        mSectionDropdown.ClearOptions();
        mSectionDropdown.AddOptions(options);
        mSectionDropdown.SetValueWithoutNotify(Array.IndexOf(Sections, mSection));
    }

    // Editing the open deck

    // Waits for the card's details, to check its nation
    void OnCardClicked(string number)
    {
        if (SleeveLibrary.IsSleeve(number))
        {
            OnSleeveClicked(number);
            return;
        }
        ShowCardInfo(number, true);
        WithCard(number, card =>
        {
            if (mDeck == null) Open(new Deck { deckName = "New Deck" });
            if (CanJoin(card, out string problem)) AddCopy(number);
            else SetStatus(problem);
        });
    }

    // Puts the sleeve on the picked sleeve section, or takes it off with null
    void OnSleeveClicked(string id)
    {
        ShowCardInfo(id, true);
        if (mDeck == null) Open(new Deck { deckName = "New Deck" });
        if (string.Equals(SleeveOf(mSection), id, StringComparison.OrdinalIgnoreCase)) return;

        RememberDeck();
        if (mSection == DeckSection.RideSleeve) mDeck.rideDeckSleeve = id;
        else mDeck.deckSleeve = id;
        MarkDirty();
        ShowDeck();
    }

    void AddCopy(string number)
    {
        string problem = ChangeCount(number, +1);
        if (problem != null) SetStatus(problem);
    }

    /* A card's nations: two for dual-nation cards, none for Cray Elementals and the few other
     nationless cards (stored as "" or "-"), which go in any deck */
    static List<string> Nations(CardData card) => SplitNations(card.nation);

    static List<string> SplitNations(string nation) => (nation ?? "").Split(NationSeparators)
        .Select(nation => nation.Trim())
        .Where(nation => nation.Length > 0 && nation != "-" && nation != "‐")
        .ToList();

    // The nations every card in the deck (plus `extra`) shares; null while none of them has a nation
    HashSet<string> SharedNations(CardData extra = null)
    {
        HashSet<string> shared = null;
        IEnumerable<CardData> cards = mDeck.AllEntries.Select(entry => mCardData.TryGetValue(entry.cardNumber, out CardData card) ? card : null);
        if (extra != null) cards = cards.Append(extra);

        foreach (CardData card in cards)
        {
            List<string> nations = card != null ? Nations(card) : null;
            if (nations == null || nations.Count == 0) continue;
            if (shared == null) shared = new HashSet<string>(nations, StringComparer.OrdinalIgnoreCase);
            else shared.IntersectWith(nations);
        }
        return shared;
    }

    // False for a card of another nation. A card missing from the database can't be checked, so it's let in
    bool CanJoin(CardData card, out string problem)
    {
        problem = null;
        if (card == null || Nations(card).Count == 0) return true;

        HashSet<string> shared = SharedNations(card);
        if (shared == null || shared.Count > 0) return true;

        problem = CardName(card) + " is " + NationLabel(card) + ", but this deck is " + DeckNationLabel() +
            ". Only Cray Elementals and dual-nation cards can join another nation's deck.";
        return false;
    }

    string DeckNationLabel()
    {
        HashSet<string> shared = SharedNations();
        if (shared == null) return "no nation yet";
        return shared.Count == 0 ? "mixed" : string.Join(" / ", shared);
    }

    static string NationLabel(CardData card)
    {
        List<string> nations = Nations(card);
        if (nations.Count > 0) return string.Join(" / ", nations);
        string race = card.race ?? "";
        bool elemental = race.IndexOf("elemental", StringComparison.OrdinalIgnoreCase) >= 0 || race.Contains("エレメンタル");
        return elemental ? "Cray Elemental" : "No nation";
    }

    /* Changes the copies in the picked section; null if done, else why not. The copy limit counts the
     main and ride decks together (the 50); tokens and crests have none. */
    string ChangeCount(string number, int delta)
    {
        List<DeckEntry> cards = SectionCards(mSection);
        DeckEntry entry = cards.FirstOrDefault(e => SameCard(e.cardNumber, number));
        if (delta > 0)
        {
            if (mSection == DeckSection.Ride && cards.Sum(e => e.count) + delta > RideDeckSize)
                return "The ride deck holds " + RideDeckSize + " cards.";
            int inFifty = mDeck.cards.Concat(mDeck.rideDeck).Where(e => SameCard(e.cardNumber, number)).Sum(e => e.count);
            if (mSection != DeckSection.Extras && inFifty + delta > maxCopies)
                return "A deck can have at most " + maxCopies + " copies of " + number + ".";
        }

        RememberDeck();
        int count = (entry?.count ?? 0) + delta;
        if (entry == null) cards.Add(new DeckEntry { cardNumber = number, count = count });
        else if (count <= 0) cards.Remove(entry);
        else entry.count = count;

        MarkDirty();
        ShowDeck();
        return null;
    }

    static bool SameCard(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /* Each card in the chosen Start/Trial Deck once, on top of what's there, once every card's
     nation is known. Only for those sets, as booster sets mix nations */
    void OnAddWholeSet()
    {
        string set = ChosenSet;
        if (set == null || !DeckRepository.IsStarter(set)) return;

        List<string> numbers = CardImageLibrary.CardNumbersInSet(set);
        int waiting = numbers.Count;
        SetStatus("Checking " + set + "...");
        foreach (string number in numbers)
            WithCard(number, _ =>
            {
                if (--waiting == 0) AddWholeSet(set, numbers);
            });
    }

    void AddWholeSet(string set, List<string> numbers)
    {
        if (mDeck == null) Open(new Deck { deckName = set });
        RememberDeck();

        int leftOut = 0;
        foreach (string number in numbers)
        {
            if (mDeck.cards.Any(e => string.Equals(e.cardNumber, number, StringComparison.OrdinalIgnoreCase))) continue;
            mCardData.TryGetValue(number, out CardData card);
            if (!CanJoin(card, out _))
            {
                leftOut++;
                continue;
            }
            mDeck.cards.Add(new DeckEntry { cardNumber = number, count = 1 });
        }

        MarkDirty();
        ShowDeck();
        if (leftOut > 0) SetStatus(leftOut + " cards of another nation were left out.");
    }

    void MarkDirty()
    {
        mDirty = true;
        mDiscardWarned = null;
        SetStatus("");
    }

    void ShowDeck()
    {
        bool open = mDeck != null;
        // No deck: only the dropdown's "Choose a deck..." shows, not the name box's hint over it
        mNameInput.gameObject.SetActive(open);
        mDeckDropdown.captionText.enabled = !open;
        mNameInput.SetTextWithoutNotify(open ? mDeck.deckName : "");
        mSaveButton.interactable = open && !mBusy && DeckRepository.IsLoggedIn;
        mDeleteButton.interactable = open && !mBusy;
        ShowSummary();

        foreach (Transform cell in mEntriesContent) Destroy(cell.gameObject);
        string sleeve = SleeveSection ? SleeveOf(mSection) : null;
        mDeckHint.text = !open ? "Pick a deck from the list above, press New Deck, or click a card to start one." :
            SleeveSection ? "No " + SectionName(mSection).ToLowerInvariant() + " yet. Click a sleeve to use it." :
            "No cards in the " + SectionName(mSection) + " yet. Click a card to add it here.";
        mDeckHint.gameObject.SetActive(!open || (SleeveSection ? string.IsNullOrEmpty(sleeve) : SectionCards(mSection).Count == 0));
        if (open)
        {
            if (SleeveSection)
            {
                if (!string.IsNullOrEmpty(sleeve)) AddSleeveCell(sleeve);
            }
            else
                foreach (DeckEntry entry in SectionCards(mSection).OrderBy(e => e.cardNumber, StringComparer.OrdinalIgnoreCase))
                    AddDeckCell(entry);
            foreach (DeckEntry entry in mDeck.AllEntries)
                WithCard(entry.cardNumber, _ => ShowSummary()); // the deck's nation may only be known now
        }

        ShowBadges();
        FreeUnusedPictures();
    }

    // Card count and nation above the cards, section counts, and whether the Save button has anything to save
    void ShowSummary()
    {
        ShowSectionOptions();
        ShowSaveLabel();
        if (mDeck == null)
        {
            mCountText.text = "";
            mNationText.text = "";
            return;
        }

        int total = mDeck.TotalCards;
        mCountText.text = total + " / " + deckSize + " cards";
        mCountText.color = total == deckSize ? Accent : Muted;

        string nation = DeckNationLabel();
        int notChecked = mDeck.AllEntries.Count(entry => mNotInDatabase.Contains(entry.cardNumber));
        mNationText.text = (nation == "mixed" ? "<color=#FF7A7A>Mixed nations: remove cards until one is left</color>" : "Nation: " + nation) +
            (notChecked > 0 ? "   •   " + notChecked + " not in the database, so unchecked" : "");
    }

    // A deck card's picture with its count in the corner and - / + along the bottom; pointing shows its details
    void AddDeckCell(DeckEntry entry)
    {
        string number = entry.cardNumber;
        RectTransform cell = AddBox(mEntriesContent, number, RowColor);
        cell.gameObject.AddComponent<PointerEnterRelay>().onEnter = () => ShowCardInfo(number, false);

        RawImage picture = AddRawImage(cell, 2f);
        picture.raycastTarget = false;
        picture.color = RowColor; // until its picture loads
        QueuePicture(number, picture);

        RectTransform badge = AddBox(cell, "Count", Accent);
        Place(badge, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -40f), new Vector2(-4f, -4f));
        TMP_Text count = AddText(badge, "x" + entry.count, 24, DarkText, TextAlignmentOptions.Center);
        Place(count.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Button minus = AddButton(cell, "-", () => ChangeCount(number, -1), OverlayColor, Color.white, 34);
        Place((RectTransform)minus.transform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(2f, 2f), new Vector2(-1f, 46f));
        Button plus = AddButton(cell, "+", () => AddCopy(number), OverlayColor, Color.white, 34);
        Place((RectTransform)plus.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 2f), new Vector2(-2f, 46f));
    }

    // The deck's sleeve for the picked section, with Remove along the bottom
    void AddSleeveCell(string id)
    {
        RectTransform cell = AddBox(mEntriesContent, id, RowColor);
        cell.gameObject.AddComponent<PointerEnterRelay>().onEnter = () => ShowCardInfo(id, false);

        RawImage picture = AddRawImage(cell, 2f);
        picture.raycastTarget = false;
        picture.color = RowColor; // until its picture loads
        QueuePicture(id, picture);

        Button remove = AddButton(cell, "Remove", () =>
        {
            RememberDeck();
            if (mSection == DeckSection.RideSleeve) mDeck.rideDeckSleeve = null;
            else mDeck.deckSleeve = null;
            MarkDirty();
            ShowDeck();
        }, OverlayColor, Color.white, 24);
        Place((RectTransform)remove.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(2f, 2f), new Vector2(-2f, 46f));
    }

    // Card browser

    void LoadSets()
    {
        mSets = CardImageLibrary.SetCodes();
        mSetDropdown.ClearOptions();
        mSetDropdown.AddOptions(new List<string> { AllSets }.Concat(mSets).ToList());

        mSetDropdown.SetValueWithoutNotify(0); // All sets
        ShowNationOptions();
        ShowBrowser();
    }

    // Null for All sets
    string ChosenSet => mSetDropdown.value > 0 && mSetDropdown.value <= mSets.Count ? mSets[mSetDropdown.value - 1] : null;

    void OnSetChanged()
    {
        if (scanner.IsScanning) StopScanning("");
        ShowNationOptions();
        OnFiltersChanged(); // the picked nation may not be in the new list
    }

    // The chosen set's cards, or a search over every set, then the filters
    void ShowBrowser()
    {
        string search = mSearchInput.text.Trim();
        if (SleeveSection)
        {
            List<string> sleeves = SleeveLibrary.AllIds();
            mCardsHeading.text = "Sleeves  <size=26><color=#A8B0C2>" + sleeves.Count + "</color></size>";
            ShowCards(sleeves.Where(id => id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList(),
                sleeves.Count == 0 ? "No sleeve pictures in StreamingAssets/Sleeves." : "No sleeves match.");
            return;
        }

        string set = ChosenSet;
        mCardsHeading.text = "Cards  <size=26><color=#A8B0C2>" + (set ?? AllSets) + "</color></size>";
        mWholeSetButton.interactable = search.Length == 0 && set != null && DeckRepository.IsStarter(set);

        if (set == null && FiltersOn)
        {
            ShowCards(FilterEverySet(search), "No D or DZ cards match the filters.");
            return;
        }
        // Every set at once is too many pictures to show unfiltered
        if (set == null && search.Length == 0)
        {
            ShowCards(new List<string>(), "Pick a filter or type a card number to look through every set.");
            return;
        }

        List<string> numbers = BrowserNumbers();
        if (!FiltersOn)
        {
            ShowCards(numbers, "No cards match.");
            return;
        }
        if (numbers.All(HasDetails))
        {
            ShowCards(numbers.Where(PassesFilters).ToList(), "No cards here match the filters.");
            return;
        }
        ShowCards(new List<string>(), "Loading card details for the filters...");
        LoadDetails(numbers, ShowBrowser);
    }

    List<string> BrowserNumbers()
    {
        string search = mSearchInput.text.Trim();
        string set = ChosenSet;
        bool Matches(string n) => n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        if (set != null) return CardImageLibrary.CardNumbersInSet(set).Where(Matches).ToList();
        if (search.Length == 0) return new List<string>();

        if (mAllNumbers == null) mAllNumbers = CardImageLibrary.AllCardNumbers();
        return mAllNumbers.Where(Matches).ToList();
    }

    /* All sets with a filter on: checks the scraper data's cards (D and DZ) on this PC, so Firestore
     isn't asked about thousands of cards. Older series aren't searched. Lazy, so only as many sets
     are read as the pages scrolled to need. */
    IEnumerable<string> FilterEverySet(string search)
    {
        if (mAllNumbers == null) mAllNumbers = CardImageLibrary.AllCardNumbers();
        foreach (string number in mAllNumbers)
        {
            if (search.Length > 0 && number.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!mCardData.ContainsKey(number))
            {
                if (!LocalCardData.TryGetCard(number, out CardData card)) continue;
                mCardData[number] = card;
            }
            if (PassesFilters(number)) yield return number;
        }
    }

    // Filters

    bool FiltersOn => mNationDropdown.value > 0 || mGradeDropdown.value > 0 || mRarityDropdown.value > 0 || mTypeDropdown.value > 0;

    /* Opening them loads the set's card details, so the Nation list has their nations. All sets
     already lists every nation, and its search can match far too many cards to look up. */
    void ToggleFilters()
    {
        bool open = !mFilterPopup.activeSelf;
        mFilterPopup.SetActive(open);
        if (open && !FiltersOn && ChosenSet != null) LoadDetails(BrowserNumbers(), () => { });
    }

    void ClearFilters()
    {
        mNationDropdown.SetValueWithoutNotify(0);
        mGradeDropdown.SetValueWithoutNotify(0);
        mRarityDropdown.SetValueWithoutNotify(0);
        mTypeDropdown.SetValueWithoutNotify(0);
        ShowKindOptions();
        OnFiltersChanged();
    }

    // A new type brings its own Kind options
    void OnTypeChanged()
    {
        ShowKindOptions();
        OnFiltersChanged();
    }

    // The Kind row's options for the picked type, or null if it has none (any type, G units)
    string[] KindOptions
    {
        get
        {
            switch (TypeOptions[mTypeDropdown.value])
            {
                case "Trigger Unit": return TriggerKinds;
                case "Normal Unit": return UnitKinds;
                case "Order": return OrderKinds;
                default: return null;
            }
        }
    }

    // Read from the card's type and Japanese text, which name these more reliably than the translations
    static bool IsKind(CardData card, string kind)
    {
        string type = card.cardType ?? "", ja = card.abilityTextJa ?? "", en = card.abilityText ?? "", skill = card.skillText ?? "";
        bool Has(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        switch (kind)
        {
            case "Sentinel": return ja.Contains("【永】：守護者") || Has(en, "]: Sentinel") || Has(en, "]:Sentinel");
            case "Persona Ride": return Has(skill, "Persona Ride") || skill.Contains("ペルソナライド");
            case "Overdress": return ja.Contains("【オーバードレス】") || Has(en, "overDress");
            case "Token": return type == "その他" || Has(type, "Token") || type.Contains("トークン");
            case "Ride Deck Crest": return Has(type, "crest") || type.Contains("クレスト");
            case "Special Move": return Has(type, "Special move") || type.Contains("必殺技");
            case "Blitz Order": return Has(type, "Blitz") || type.Contains("ブリッツ");
            case "Set Order": return Has(type, "Set Order") || type.Contains("セット");
            case "Trigger Order": return Has(type, "Trigger") || type.Contains("トリガー");
            case "Normal Order": return !IsKind(card, "Blitz Order") && !IsKind(card, "Set Order") && !IsKind(card, "Trigger Order");
            default: return Enum.TryParse(kind, out TriggerIcon icon) && card.triggerIcon == icon; // the trigger kinds
        }
    }

    // The button's icon turns gold while a filter is on
    void OnFiltersChanged()
    {
        if (mFilterIcon != null) mFilterIcon.color = FiltersOn ? Accent : Color.white;
        ShowBrowser();
    }

    bool HasDetails(string number) => mCardData.ContainsKey(number) || mNotInDatabase.Contains(number);

    bool PassesFilters(string number)
    {
        if (!mCardData.TryGetValue(number, out CardData card)) return false;

        string nation = mNationOptions[mNationDropdown.value];
        List<string> nations = Nations(card);
        if (nation == NoNation ? nations.Count > 0 : nation != AnyNation && !nations.Contains(nation, StringComparer.OrdinalIgnoreCase)) return false;
        if (mGradeDropdown.value > 0 && card.grade != mGradeDropdown.value - 1) return false;
        if (mRarityDropdown.value > 0 && !string.Equals((card.rarity ?? "").Trim(), mRarityOptions[mRarityDropdown.value], StringComparison.OrdinalIgnoreCase)) return false;
        if (mTypeDropdown.value > 0 && TypeOptions[mTypeDropdown.value] != TypeName(card)) return false;
        string[] kinds = KindOptions;
        return kinds == null || mKindDropdown.value == 0 || IsKind(card, kinds[mKindDropdown.value]);
    }

    static string TypeName(CardData card)
    {
        if (card.isOrderCard) return "Order";
        if (card.isTriggerUnit) return "Trigger Unit";
        string type = card.cardType ?? "";
        return type.Contains("G Unit") || type.Contains("Gユニット") ? "G Unit" : "Normal Unit";
    }

    // Fetches the details of the cards that don't have them yet, then runs `done` unless a newer call came since
    void LoadDetails(List<string> numbers, Action done)
    {
        int request = ++mDetailsRequest;
        List<string> missing = numbers.Where(n => !HasDetails(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0)
        {
            ShowNationOptions();
            done();
            return;
        }

        int waiting = missing.Count;
        foreach (string number in missing)
            WithCard(number, _ =>
            {
                if (--waiting > 0) return;
                ShowNationOptions();
                if (request == mDetailsRequest) done();
            });
    }

    // Every nation of the cards loaded so far (and every bundled card's for All sets), keeping the one picked
    void ShowNationOptions()
    {
        string picked = mNationOptions[mNationDropdown.value];
        IEnumerable<string> loaded = mCardData.Values.SelectMany(Nations);
        if (ChosenSet == null) loaded = loaded.Concat(LocalCardData.Nations());
        List<string> nations = loaded.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        mNationOptions.Clear();
        mNationOptions.Add(AnyNation);
        mNationOptions.AddRange(nations);
        mNationOptions.Add(NoNation);

        mNationDropdown.ClearOptions();
        mNationDropdown.AddOptions(mNationOptions);
        mNationDropdown.SetValueWithoutNotify(Mathf.Max(0, mNationOptions.FindIndex(n => string.Equals(n, picked, StringComparison.OrdinalIgnoreCase))));
        ShowRarityOptions();
    }

    // Same as the nations: the loaded cards' rarities (and every bundled card's for All sets)
    void ShowRarityOptions()
    {
        string picked = mRarityOptions[mRarityDropdown.value];
        IEnumerable<string> loaded = mCardData.Values.Select(card => (card.rarity ?? "").Trim());
        if (ChosenSet == null) loaded = loaded.Concat(LocalCardData.Rarities());
        List<string> rarities = loaded.Where(r => r.Length > 0 && r != "-").Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();

        mRarityOptions.Clear();
        mRarityOptions.Add(AnyRarity);
        mRarityOptions.AddRange(rarities);

        mRarityDropdown.ClearOptions();
        mRarityDropdown.AddOptions(mRarityOptions);
        mRarityDropdown.SetValueWithoutNotify(Mathf.Max(0, mRarityOptions.FindIndex(r => string.Equals(r, picked, StringComparison.OrdinalIgnoreCase))));
    }

    /* Shows the first page of `numbers`; OnGridScrolled adds the rest a page at a time. Skipped when
     the browser and filters haven't changed, so details arriving late don't rebuild the grid. */
    void ShowCards(IEnumerable<string> numbers, string emptyText)
    {
        string key = SleeveSection + "|" + ChosenSet + "|" + mSearchInput.text.Trim() + "|" + mNationOptions[mNationDropdown.value] + "|" +
            mGradeDropdown.value + "|" + mRarityOptions[mRarityDropdown.value] + "|" + mTypeDropdown.value + "|" + mKindDropdown.value + "|" + emptyText;
        if (key == mShownKey) return;
        mShownKey = key;

        // Queued pictures of these cells are skipped once the cells are gone
        foreach (Transform cell in mGridContent) Destroy(cell.gameObject);
        mBadges.Clear();
        mGridNumbers.Clear();
        FreeUnusedPictures();

        mMoreCards?.Dispose();
        mMoreCards = numbers.GetEnumerator();
        mGridScroll.verticalNormalizedPosition = 1f;

        if (!AddMoreCards())
        {
            TMP_Text empty = AddText(mGridContent, emptyText, 26, Muted, TextAlignmentOptions.Center);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
        }
    }

    // Near the bottom of the grid: the next page
    void OnGridScrolled(Vector2 position)
    {
        if (mMoreCards != null && position.y < 0.1f) AddMoreCards();
    }

    // Adds up to pageSize more cards to the grid; false if there were none left
    bool AddMoreCards()
    {
        int added = 0;
        while (added < pageSize && mMoreCards != null && mMoreCards.MoveNext())
        {
            AddCardCell(mMoreCards.Current);
            added++;
        }
        if (added < pageSize)
        {
            mMoreCards?.Dispose();
            mMoreCards = null;
        }

        ShowBadges();
        return added > 0;
    }

    void AddCardCell(string number)
    {
        RectTransform cell = AddBox(mGridContent, number, RowColor);
        Button button = cell.gameObject.AddComponent<Button>();
        button.targetGraphic = cell.GetComponent<Image>();
        button.onClick.AddListener(() => OnCardClicked(number));
        cell.gameObject.AddComponent<PointerEnterRelay>().onEnter = () => ShowCardInfo(number, false);
        mGridNumbers.Add(number);

        RawImage picture = AddRawImage(cell, 3f);
        picture.color = RowColor; // until its picture loads
        QueuePicture(number, picture);

        // How many the open deck has; for a sleeve, which part of the deck uses it
        RectTransform badge = AddBox(cell, "Count", Accent);
        float badgeWidth = SleeveLibrary.IsSleeve(number) ? 80f : 46f;
        Place(badge, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-6f - badgeWidth, 6f), new Vector2(-6f, 46f));
        TMP_Text count = AddText(badge, "", 26, DarkText, TextAlignmentOptions.Center);
        Place(count.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        mBadges[number] = count;
    }

    void ShowBadges()
    {
        foreach (KeyValuePair<string, TMP_Text> badge in mBadges)
        {
            if (SleeveLibrary.IsSleeve(badge.Key))
            {
                bool deck = SameCard(mDeck?.deckSleeve, badge.Key), ride = SameCard(mDeck?.rideDeckSleeve, badge.Key);
                badge.Value.text = deck && ride ? "Both" : deck ? "Deck" : "Ride";
                badge.Value.transform.parent.gameObject.SetActive(deck || ride);
                continue;
            }
            int count = mDeck?.AllEntries.Where(e => SameCard(e.cardNumber, badge.Key)).Sum(e => e.count) ?? 0;
            badge.Value.text = "x" + count;
            badge.Value.transform.parent.gameObject.SetActive(count > 0);
        }
    }

    // Shows the card's picture in `image`: at once if it's loaded, else once LoadPictures gets to it
    void QueuePicture(string number, RawImage image)
    {
        if (mPictures.TryGetValue(number, out Texture2D picture) && picture != null)
        {
            image.texture = picture;
            image.color = Color.white;
            return;
        }
        mPictureQueue.Add((number, image));
        if (mLoadingPictures == null) mLoadingPictures = StartCoroutine(LoadPictures());
    }

    // A few per frame, so a big set doesn't freeze the screen; pictures queued meanwhile join in
    IEnumerator LoadPictures()
    {
        int loaded = 0;
        for (int i = 0; i < mPictureQueue.Count; i++)
        {
            (string number, RawImage image) = mPictureQueue[i];
            if (image == null) continue; // its cell was replaced
            if (!mPictures.TryGetValue(number, out Texture2D picture))
            {
                picture = LoadPicture(number);
                mPictures[number] = picture;
                if (++loaded % 4 == 0) yield return null;
            }
            if (image == null || picture == null) continue;
            image.texture = picture;
            image.color = Color.white;
        }
        mPictureQueue.Clear();
        mLoadingPictures = null;
    }

    // ~300 KB each, so pictures neither the grid nor the deck shows are freed
    void FreeUnusedPictures()
    {
        var keep = new HashSet<string>(mGridNumbers, StringComparer.OrdinalIgnoreCase);
        if (mDeck != null)
        {
            keep.UnionWith(mDeck.AllEntries.Select(e => e.cardNumber));
            if (!string.IsNullOrEmpty(mDeck.deckSleeve)) keep.Add(mDeck.deckSleeve);
            if (!string.IsNullOrEmpty(mDeck.rideDeckSleeve)) keep.Add(mDeck.rideDeckSleeve);
        }
        foreach (string number in mPictures.Keys.Where(n => !keep.Contains(n)).ToList())
        {
            if (mPictures[number] != null) Destroy(mPictures[number]);
            mPictures.Remove(number);
        }
    }

    static Texture2D LoadPicture(string number)
    {
        string path = SleeveLibrary.IsSleeve(number) ? SleeveLibrary.GetPath(number) : CardImageLibrary.GetPath(number);
        if (path == null) return null;

        var texture = new Texture2D(2, 2);
        if (texture.LoadImage(File.ReadAllBytes(path), true)) return texture;
        Destroy(texture);
        return null;
    }

    /* Shows a card in the details panel. Pointing at a card only uses details already on this PC
     (`fetch` false), so sweeping over older cards doesn't spend Firestore reads; a click fetches. */
    void ShowCardInfo(string number, bool fetch)
    {
        if (number != mDetailNumber)
        {
            mDetailNumber = number;
            if (mDetailTexture != null) Destroy(mDetailTexture);
            mDetailTexture = LoadPicture(number);
            mDetailPicture.texture = mDetailTexture;
            mDetailPicture.color = mDetailTexture != null ? Color.white : RowColor;
            if (mDetailTexture != null) mDetailFitter.aspectRatio = mDetailTexture.width / (float)mDetailTexture.height;
        }

        if (SleeveLibrary.IsSleeve(number))
        {
            mDetailText.text = "<b>Sleeve " + number + "</b>\n<color=#A8B0C2>Click it to use it as the " +
                (mSection == DeckSection.RideSleeve ? "ride deck's" : "deck's") + " sleeve.</color>";
            return;
        }

        if (!fetch && !mCardData.ContainsKey(number) && !LocalCardData.TryGet(number, out _))
        {
            mDetailText.text = "<b>" + number + "</b>\n<color=#A8B0C2>Click the card to load its details.</color>";
            return;
        }
        WithCard(number, card =>
        {
            if (mDetailText != null && mDetailNumber == number) mDetailText.text = DetailText(number, card); // unless another card is shown since
        });
    }

    static string DetailText(string number, CardData card)
    {
        if (card == null) return "<b>" + number + "</b>\n<color=#A8B0C2>Not in the card database.</color>";

        var text = new System.Text.StringBuilder();
        text.Append("<size=18><b>").Append(CardName(card)).Append("</b></size>\n");
        if (!string.IsNullOrEmpty(card.cardNameEn) && card.cardNameEn != card.cardName)
            text.Append("<color=#A8B0C2>").Append(card.cardName).Append("</color>\n");
        text.Append("<color=#A8B0C2>").Append(number);
        if (!string.IsNullOrEmpty(card.rarity)) text.Append("  •  ").Append(card.rarity);
        text.Append("</color>\n\n");

        text.Append(card.cardType);
        if (card.triggerIcon != TriggerIcon.None) text.Append("  •  ").Append(card.triggerIcon).Append(" trigger");
        text.Append('\n');
        text.Append("Grade ").Append(card.grade).Append("  •  ").Append(card.power).Append(" power");
        if (card.shield > 0) text.Append("  •  ").Append(card.shield).Append(" shield");
        text.Append('\n').Append(NationLabel(card));
        if (!string.IsNullOrEmpty(card.race)) text.Append("  •  ").Append(card.race);
        text.Append('\n');
        if (!string.IsNullOrEmpty(card.skillText)) text.Append("<color=#F5C242>").Append(card.skillText).Append("</color>\n");

        if (!string.IsNullOrEmpty(card.abilityText)) text.Append('\n').Append(CardOverlayUI.ReadableJa(card.abilityText)).Append('\n');
        if (!string.IsNullOrEmpty(card.flavorText)) text.Append("\n<i><color=#A8B0C2>").Append(card.flavorText).Append("</color></i>");
        return text.ToString();
    }

    // Card details from the scraper data (D and DZ), else Firestore, or null if neither has the card
    void WithCard(string number, Action<CardData> onCard)
    {
        if (LocalCardData.TryGetCard(number, out CardData local))
        {
            OnCardDetails(number, local, onCard);
            return;
        }
        if (CardFirestoreDatabase.Instance == null)
        {
            OnCardDetails(number, null, onCard); // counts as not in the database, so the filters don't wait on it
            return;
        }
        CardFirestoreDatabase.Instance.GetCardByNumber(number, card => OnCardDetails(number, card, onCard), _ => OnCardDetails(number, null, onCard));
    }

    void OnCardDetails(string number, CardData card, Action<CardData> onCard)
    {
        if (this == null) return;
        if (card != null) mCardData[number] = card;
        else mNotInDatabase.Add(number);
        onCard(card);
    }

    static string CardName(CardData card) => string.IsNullOrEmpty(card.cardNameEn) ? card.cardName : card.cardNameEn;

    // With nothing else to say, says why Save is greyed out when not logged in
    void SetStatus(string text)
    {
        if (mStatusText == null) return;
        mStatusText.text = text.Length == 0 && !DeckRepository.IsLoggedIn ? "Not logged in, so decks can't be saved." : text;
        mStatusBar.SetActive(mStatusText.text.Length > 0);
    }

    // "Saved" while nothing has changed since saving, else "Save", noting unsaved changes; Undo and Cancel when they'd do something
    void ShowSaveLabel()
    {
        TMP_Text label = mSaveButton.GetComponentInChildren<TMP_Text>();
        bool saved = mDeck != null && !mDirty && !string.IsNullOrEmpty(mDeck.id);
        label.text = saved ? "Saved" : mDirty ? "Save\n<size=18>unsaved changes</size>" : "Save";
        mUndoButton.interactable = mDeck != null && mUndo.Count > 0 && !mBusy;
        mCancelButton.interactable = mDeck != null && mDirty && !mBusy;
    }

    void SetBusy(bool busy, string status)
    {
        mBusy = busy;
        SetStatus(status);
        if (mDeck != null)
        {
            mSaveButton.interactable = !busy && DeckRepository.IsLoggedIn;
            mDeleteButton.interactable = !busy;
        }
        ShowSaveLabel();
    }
}

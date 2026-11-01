using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MenuUI;

/* DeckBuilder's screen, built in code so the Deck scene needs no Canvas: the pointed-at card's
 details on the left, the card browser in the middle and the open deck on the right.
 The rest of DeckBuilder is in DeckBuilder.cs. */
public partial class DeckBuilder
{
    void BuildUI()
    {
        RectTransform root = BuildCanvas("DeckBuilderCanvas");
        mBackground = root.GetComponent<Image>();

        TMP_Text title = AddText(root, "Deck Builder", 54, Color.white, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -96f), new Vector2(-300f, -14f));
        Button back = AddButton(root, "Back", OnBack, PanelColor, Color.white, 20);
        Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-200f, -82f), new Vector2(-48f, -32f));

        // Export / Import, top middle over the deck panel (0.64 to 1 across, inset 6 and 48, so centred at 0.82 less 21 px)
        mExportButton = AddButton(root, "Export", OnExport, RowColor, Color.white, 22);
        Place((RectTransform)mExportButton.transform, new Vector2(0.82f, 1f), new Vector2(0.82f, 1f), new Vector2(-141f, -82f), new Vector2(-27f, -32f));
        Button import = AddButton(root, "Import", OnImport, RowColor, Color.white, 22);
        Place((RectTransform)import.transform, new Vector2(0.82f, 1f), new Vector2(0.82f, 1f), new Vector2(-15f, -82f), new Vector2(99f, -32f));

        BuildCardDetails(root);
        BuildBrowser(root);
        BuildDeckPanel(root);
        LoadSets();
    }

    // The card last pointed at or clicked: its picture, then its text in a scroll area
    void BuildCardDetails(RectTransform root)
    {
        RectTransform panel = AddBox(root, "DetailsPanel", PanelColor);
        Place(panel, new Vector2(0f, 0f), new Vector2(0.2f, 1f), new Vector2(48f, 32f), new Vector2(-6f, -104f));

        TMP_Text heading = AddText(panel, "Card Details", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 24f, 24f);

        // Fits the picture in the box at its own shape, as some cards are sideways
        RectTransform frame = AddBox(panel, "PictureFrame", Color.clear);
        PlaceTop(frame, 0f, 1f, 86f, 380f, 24f, 24f);
        mDetailPicture = AddRawImage(frame, 0f);
        mDetailFitter = mDetailPicture.gameObject.AddComponent<AspectRatioFitter>();
        mDetailFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        mDetailFitter.aspectRatio = 63f / 88f;
        mDetailPicture.color = RowColor; // until a card is shown

        RectTransform text = AddBox(panel, "Text", new Color(0f, 0f, 0f, 0.2f));
        Place(text, Vector2.zero, Vector2.one, new Vector2(24f, 24f), new Vector2(-24f, -482f));
        RectTransform content = AddScrollContent(text);
        AddListLayout(content, 0f);
        mDetailText = AddText(content, "Point at a card to see its details.", 12, Color.white, TextAlignmentOptions.TopLeft);
        mDetailText.overflowMode = TextOverflowModes.Overflow;
        mDetailText.margin = new Vector4(6f, 4f, 6f, 4f);
    }

    void BuildBrowser(RectTransform root)
    {
        RectTransform panel = AddBox(root, "CardsPanel", PanelColor);
        Place(panel, new Vector2(0.2f, 0f), new Vector2(0.64f, 1f), new Vector2(6f, 32f), new Vector2(-6f, -104f));
        mCardsPanel = panel.GetComponent<Image>();

        // The chosen set shows after the heading, as its dropdown is in the filters
        mCardsHeading = AddText(panel, "Cards", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(mCardsHeading.rectTransform, 0f, 1f, 18f, 56f, 28f, 500f);
        mScanButton = AddButton(panel, "Scan Cards", OnScan, Accent, DarkText, 24);
        PlaceTop((RectTransform)mScanButton.transform, 1f, 1f, 18f, 56f, -464f, 252f);
        mScanButton.gameObject.SetActive(showScanButton);
        mWholeSetButton = AddButton(panel, "Add Whole Set", OnAddWholeSet, RowColor, Color.white, 24);
        PlaceTop((RectTransform)mWholeSetButton.transform, 1f, 1f, 18f, 56f, -240f, 28f);

        // Filter button, then the search over the rest of the row
        Button filter = AddIconButton(panel, "Filter", ToggleFilters, RowColor);
        PlaceTop((RectTransform)filter.transform, 0f, 0f, 86f, 60f, 28f, -88f);
        mFilterButton = filter;
        Transform filterIcon = filter.transform.Find("Icon");
        mFilterIcon = filterIcon != null ? filterIcon.GetComponent<Image>() : null;

        // Browsing again ends scanning
        mSearchInput = AddInput(panel, "Search card number, e.g. BT01-005...");
        PlaceTop((RectTransform)mSearchInput.transform, 0f, 1f, 86f, 60f, 100f, 28f);
        mSearchInput.onValueChanged.AddListener(_ =>
        {
            if (scanner.IsScanning) StopScanning("");
            ShowBrowser();
        });

        mGrid = AddBox(panel, "Grid", new Color(0f, 0f, 0f, 0.2f));
        Place(mGrid, Vector2.zero, Vector2.one, new Vector2(28f, 28f), new Vector2(-28f, -166f));
        mGridContent = AddScrollContent(mGrid);
        mGridScroll = mGrid.GetComponent<ScrollRect>();
        mGridScroll.onValueChanged.AddListener(OnGridScrolled);
        GridLayoutGroup layout = mGridContent.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(140f, 196f); // card-shaped, 63:88
        layout.spacing = new Vector2(12f, 12f);
        layout.childAlignment = TextAnchor.UpperCenter;

        // What scanning is doing, over the bottom of the camera's picture
        RectTransform scanBar = AddBox(panel, "ScanBar", new Color(0f, 0f, 0f, 0.65f));
        Place(scanBar, Vector2.zero, new Vector2(1f, 0f), new Vector2(28f, 28f), new Vector2(-28f, 88f));
        mScanText = AddText(scanBar, "", 24, Color.white, TextAlignmentOptions.Center);
        Place(mScanText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        mScanBar = scanBar.gameObject;
        mScanBar.SetActive(false);

        BuildFilters(panel); // last, so it opens over the grid
    }

    /* Set, nation, grade, rarity and card type, in a box under the filter button. A Kind row (trigger, unit
     ability or order type) shows under Type once a type that has kinds is picked. */
    void BuildFilters(RectTransform panel)
    {
        mFilterBox = AddBox(panel, "Filters", RowColor);
        Place(mFilterBox, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -654f), new Vector2(508f, -154f));
        mFilterBox.gameObject.AddComponent<Outline>().effectColor = Accent;
        mFilterPopup = mFilterBox.gameObject;

        TMP_Text heading = AddText(mFilterBox, "Filter cards", 32, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 14f, 48f, 24f, 24f);

        mSetDropdown = AddFilterRow(mFilterBox, "Set", 74f, new List<string> { AllSets }, OnSetChanged, out _); // options come in LoadSets
        mNationDropdown = AddFilterRow(mFilterBox, "Nation", 144f, new List<string> { AnyNation }, OnFiltersChanged, out _);
        mGradeDropdown = AddFilterRow(mFilterBox, "Grade", 214f, GradeOptions.ToList(), OnFiltersChanged, out _);
        mRarityDropdown = AddFilterRow(mFilterBox, "Rarity", 284f, new List<string> { AnyRarity }, OnFiltersChanged, out _); // options come with the nations'
        mTypeDropdown = AddFilterRow(mFilterBox, "Type", 354f, TypeOptions.ToList(), OnTypeChanged, out _);
        mKindDropdown = AddFilterRow(mFilterBox, "Kind", 424f, new List<string> { "" }, OnFiltersChanged, out mKindLabel);

        Button clear = AddButton(mFilterBox, "Clear", ClearFilters, PanelColor, Color.white, 26);
        Place((RectTransform)clear.transform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(24f, 20f), new Vector2(-8f, 76f));
        Button done = AddButton(mFilterBox, "Done", ToggleFilters, Accent, DarkText, 26);
        Place((RectTransform)done.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(8f, 20f), new Vector2(-24f, 76f));

        ShowKindOptions();
        mFilterPopup.SetActive(false);
    }

    TMP_Dropdown AddFilterRow(RectTransform box, string label, float top, List<string> options, System.Action onChanged, out TMP_Text labelText)
    {
        labelText = AddText(box, label, 26, Color.white, TextAlignmentOptions.MidlineLeft);
        PlaceTop(labelText.rectTransform, 0f, 1f, top, 56f, 24f, 24f);

        TMP_Dropdown dropdown = AddDropdown(box);
        PlaceTop((RectTransform)dropdown.transform, 0f, 1f, top, 56f, 130f, 24f);
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.onValueChanged.AddListener(_ => onChanged());
        return dropdown;
    }

    // The Kind row's options for the picked type; the row and the box's extra height go when there are none
    void ShowKindOptions()
    {
        string[] kinds = KindOptions;
        mKindDropdown.ClearOptions();
        if (kinds != null) mKindDropdown.AddOptions(kinds.ToList());
        mKindDropdown.SetValueWithoutNotify(0);

        bool show = kinds != null;
        mKindDropdown.gameObject.SetActive(show);
        mKindLabel.gameObject.SetActive(show);
        mFilterBox.offsetMin = new Vector2(mFilterBox.offsetMin.x, show ? -724f : -654f);
    }

    void BuildDeckPanel(RectTransform root)
    {
        RectTransform panel = AddBox(root, "DeckPanel", PanelColor);
        Place(panel, new Vector2(0.64f, 0f), new Vector2(1f, 1f), new Vector2(6f, 32f), new Vector2(-48f, -104f));

        TMP_Text heading = AddText(panel, "Deck", 40, Color.white, TextAlignmentOptions.Left);
        PlaceTop(heading.rectTransform, 0f, 1f, 18f, 56f, 28f, 28f);

        // Which part of the deck is shown and gets clicked cards: main deck, ride deck, tokens / crests
        mSectionDropdown = AddDropdown(panel);
        PlaceTop((RectTransform)mSectionDropdown.transform, 1f, 1f, 18f, 56f, -300f, 28f);
        mSectionDropdown.captionText.fontSize = 24;
        mSectionDropdown.itemText.fontSize = 24;
        mSectionDropdown.onValueChanged.AddListener(OnSectionChanged);

        BuildDeckChooser(panel);

        mNationText = AddText(panel, "", 22, Muted, TextAlignmentOptions.MidlineLeft);
        PlaceTop(mNationText.rectTransform, 0f, 1f, 152f, 36f, 30f, 28f);

        // The deck's cards as pictures, each with its count and - / + over it
        RectTransform list = AddBox(panel, "Entries", new Color(0f, 0f, 0f, 0.2f));
        Place(list, Vector2.zero, Vector2.one, new Vector2(28f, 100f), new Vector2(-28f, -196f));
        mEntriesContent = AddScrollContent(list);
        GridLayoutGroup layout = mEntriesContent.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(126f, 176f); // four across; card-shaped, 63:88
        layout.spacing = new Vector2(10f, 10f);
        layout.childAlignment = TextAnchor.UpperCenter;

        mDeckHint = AddText(list, "Pick a deck from the list above, press New Deck, or click a card to start one.", 24, Muted, TextAlignmentOptions.Center);
        Place(mDeckHint.rectTransform, Vector2.zero, Vector2.one, new Vector2(24f, 24f), new Vector2(-24f, -24f));
        mDeckHint.raycastTarget = false;

        mSaveButton = AddButton(panel, "Save", OnSave, Accent, DarkText, 30);
        Place((RectTransform)mSaveButton.transform, Vector2.zero, new Vector2(0.3f, 0f), new Vector2(28f, 24f), new Vector2(-6f, 88f));
        mUndoButton = AddButton(panel, "Undo", OnUndo, RowColor, Color.white, 26);
        Place((RectTransform)mUndoButton.transform, new Vector2(0.3f, 0f), new Vector2(0.5f, 0f), new Vector2(6f, 24f), new Vector2(-6f, 88f));
        mCancelButton = AddButton(panel, "Cancel", OnCancel, RowColor, Color.white, 26);
        Place((RectTransform)mCancelButton.transform, new Vector2(0.5f, 0f), new Vector2(0.7f, 0f), new Vector2(6f, 24f), new Vector2(-6f, 88f));
        mDeleteButton = AddButton(panel, "Delete", OnDelete, RowColor, Color.white, 30);
        Place((RectTransform)mDeleteButton.transform, new Vector2(0.7f, 0f), new Vector2(1f, 0f), new Vector2(6f, 24f), new Vector2(-28f, 88f));

        // Messages show in a bar over the bottom of the deck's cards, only while there is one; clicks pass through
        RectTransform statusBar = AddBox(list, "StatusBar", new Color(0f, 0f, 0f, 0.8f));
        Place(statusBar, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 64f));
        statusBar.GetComponent<Image>().raycastTarget = false;
        mStatusText = AddText(statusBar, "", 22, Color.white, TextAlignmentOptions.Center);
        Place(mStatusText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        mStatusText.raycastTarget = false;
        mStatusBar = statusBar.gameObject;
        mStatusBar.SetActive(false);
    }

    /* The open deck's name, typed in place, over a dropdown of the saved decks that the arrow
     opens; New Deck sits beside it */
    void BuildDeckChooser(RectTransform panel)
    {
        mDeckDropdown = AddDropdown(panel);
        PlaceTop((RectTransform)mDeckDropdown.transform, 0f, 1f, 86f, 60f, 28f, 232f);
        mDeckDropdown.captionText.text = "";
        mDeckDropdown.onValueChanged.AddListener(OnDeckChosen);

        mNameInput = AddInput(panel, "Deck name...");
        PlaceTop((RectTransform)mNameInput.transform, 0f, 1f, 86f, 60f, 28f, 292f);
        mNameInput.characterLimit = 40;
        mNameInput.onValueChanged.AddListener(text =>
        {
            if (mDeck == null) return;
            mDeck.deckName = text;
            MarkDirty();
            ShowSummary();
            ShowDeckChoices();
        });

        mDeckArrow = AddIconButton(panel, "Down", () => mDeckDropdown.Show(), InputColor);
        PlaceTop((RectTransform)mDeckArrow.transform, 1f, 1f, 86f, 60f, -292f, 232f);
        Transform icon = mDeckArrow.transform.Find("Icon");
        if (icon != null) icon.GetComponent<Image>().color = DarkText;

        Button newDeck = AddButton(panel, "New Deck", OnNewDeck, Accent, DarkText, 26);
        PlaceTop((RectTransform)newDeck.transform, 1f, 1f, 86f, 60f, -220f, 28f);
    }
}

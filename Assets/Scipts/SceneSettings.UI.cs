using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static MenuUI;

// The gear for scenes that didn't add one, and the menu, each on its own canvas over the scene's UI
public partial class SceneSettings
{
    const float MenuWidth = 560f;

    GameObject mOverlay;
    TMP_Text mWindowLabel;
    TMP_Text mFontLabel;
    Slider mFontSlider;

    void BuildCornerButton()
    {
        Transform canvas = AddCanvas("SettingsButtonCanvas", 90);
        Button gear = AddButton(canvas);
        Place((RectTransform)gear.transform, Vector2.one, Vector2.one, new Vector2(-96f, -96f), new Vector2(-24f, -24f));
    }

    void BuildMenu()
    {
        // Dark cover over the whole screen, so nothing behind the menu can be clicked
        RectTransform cover = AddBox(AddCanvas("SettingsCanvas", 100), "SettingsOverlay", new Color(0f, 0f, 0f, 0.6f));
        Place(cover, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        mOverlay = cover.gameObject;

        Vector2 middle = new Vector2(0.5f, 0.5f);
        RectTransform box = AddBox(cover, "SettingsBox", new Color(0.1f, 0.11f, 0.16f, 0.97f));
        Place(box, middle, middle, new Vector2(-MenuWidth / 2f, -230f), new Vector2(MenuWidth / 2f, 230f));

        TMP_Text title = AddText(box, "Settings", 38, Color.white, TextAlignmentOptions.Center);
        PlaceTop(title.rectTransform, 0f, 1f, 22f, 50f, 80f, 80f);
        Button close = AddIconButton(box, "Close", Close, Color.clear);
        Place((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-62f, -62f), new Vector2(-14f, -14f));

        AddMenuButton(box, "Resume", 92f, RowColor, Color.white, Close);
        mWindowLabel = AddMenuButton(box, WindowMode.Label, 168f, RowColor, Color.white, WindowMode.Toggle)
            .GetComponentInChildren<TMP_Text>();

        mFontLabel = AddText(box, "", 22, Color.white, TextAlignmentOptions.Left);
        PlaceTop(mFontLabel.rectTransform, 0.5f, 0.5f, 248f, 30f, -200f, -200f);
        mFontSlider = AddSlider(box, 286f);
        mFontSlider.minValue = GameSettings.MinFontScale;
        mFontSlider.maxValue = GameSettings.MaxFontScale;
        mFontSlider.onValueChanged.AddListener(OnFontScaleChanged);

        AddMenuButton(box, "Quit Game", 334f, new Color(0.55f, 0.15f, 0.15f, 0.95f), Color.white, Application.Quit);

        TMP_Text info = AddText(box, "Esc: settings   •   F11: display", 18, Muted, TextAlignmentOptions.Center);
        PlaceTop(info.rectTransform, 0f, 1f, 412f, 30f, 20f, 20f);
    }

    // A 400 x 60 button centred across box, top px down from its top edge
    static Button AddMenuButton(RectTransform box, string label, float top, Color background, Color text, UnityEngine.Events.UnityAction onClick)
    {
        Button button = MenuUI.AddButton(box, label, onClick, background, text, 26);
        PlaceTop((RectTransform)button.transform, 0.5f, 0.5f, top, 60f, -200f, -200f);
        return button;
    }

    // A 400 px 0-1 slider centred across box, top px down from its top edge
    static Slider AddSlider(RectTransform box, float top)
    {
        GameObject obj = DefaultControls.CreateSlider(new DefaultControls.Resources());
        obj.transform.SetParent(box, false);
        PlaceTop((RectTransform)obj.transform, 0.5f, 0.5f, top, 20f, -200f, -200f);

        // Without the editor's default sprites these are plain rectangles; coloured to match the menu
        obj.transform.Find("Background").GetComponent<Image>().color = RowColor;
        obj.transform.Find("Fill Area/Fill").GetComponent<Image>().color = Accent;
        obj.transform.Find("Handle Slide Area/Handle").GetComponent<Image>().color = Color.white;
        return obj.GetComponent<Slider>();
    }

    // A 1920x1080-scaled overlay canvas drawn over sortingOrder's lower canvases, in this scene
    static Transform AddCanvas(string name, int sortingOrder)
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        GameObject obj = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = obj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return obj.transform;
    }
}

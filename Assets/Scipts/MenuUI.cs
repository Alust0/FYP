using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/* Colours and builders for the menu screens that make their UI in code (lobby, deck builder),
 so they look the same. Laid out for a 1920x1080 reference screen. */
public static class MenuUI
{
    public static readonly Color Background = new Color(0.07f, 0.08f, 0.11f);
    public static readonly Color PanelColor = new Color(0.12f, 0.14f, 0.19f);
    public static readonly Color RowColor = new Color(0.18f, 0.21f, 0.28f);
    public static readonly Color Accent = new Color(0.96f, 0.76f, 0.26f);
    public static readonly Color Muted = new Color(0.66f, 0.69f, 0.76f);
    public static readonly Color DarkText = new Color(0.1f, 0.1f, 0.12f);
    public static readonly Color InputColor = new Color(0.92f, 0.93f, 0.96f);

    // An EventSystem if the scene has none, and a full-screen canvas; returns its background
    public static RectTransform BuildCanvas(string name)
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        GameObject canvasObj = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = AddBox(canvasObj.transform, "Background", Background);
        Place(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return root;
    }

    // Stretches between anchors (fractions of the parent), then insets by the offsets
    public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // A band `top` px down from the parent's top edge, between x fractions, inset left/right by px
    public static void PlaceTop(RectTransform rt, float xMin, float xMax, float top, float height, float left, float right) =>
        Place(rt, new Vector2(xMin, 1f), new Vector2(xMax, 1f), new Vector2(left, -top - height), new Vector2(-right, -top));

    public static RectTransform AddBox(Transform parent, string name, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = color;
        return (RectTransform)obj.transform;
    }

    // For card pictures, filling the parent minus a border
    public static RawImage AddRawImage(Transform parent, float border)
    {
        GameObject obj = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
        obj.transform.SetParent(parent, false);
        Place((RectTransform)obj.transform, Vector2.zero, Vector2.one, new Vector2(border, border), new Vector2(-border, -border));
        return obj.GetComponent<RawImage>();
    }

    public static TMP_Text AddText(Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment)
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

    public static Button AddButton(Transform parent, string label, UnityAction onClick, Color background, Color textColor, float fontSize)
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

    /* A square button showing StreamingAssets/Icons/{icon}.png (white, so `Image.color` tints it),
     or its name if the file is missing. Returns the button; the icon is its child "Icon" */
    public static Button AddIconButton(Transform parent, string icon, UnityAction onClick, Color background)
    {
        RectTransform box = AddBox(parent, icon, background);
        Button button = box.gameObject.AddComponent<Button>();
        button.targetGraphic = box.GetComponent<Image>();
        button.onClick.AddListener(onClick);

        Sprite sprite = LoadIcon(icon);
        if (sprite == null)
        {
            TMP_Text label = AddText(box, icon, 20, Color.white, TextAlignmentOptions.Center);
            Place(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        RectTransform image = AddBox(box, "Icon", Color.white);
        Place(image, Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -12f));
        Image picture = image.GetComponent<Image>();
        picture.sprite = sprite;
        picture.preserveAspect = true;
        picture.raycastTarget = false;
        return button;
    }

    static Sprite LoadIcon(string name)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Icons", name + ".png");
        if (!File.Exists(path)) return null;

        Texture2D texture = new Texture2D(2, 2);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Object.Destroy(texture);
            return null;
        }
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    public static TMP_InputField AddInput(Transform parent, string placeholder)
    {
        GameObject obj = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        obj.name = placeholder;
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = InputColor;

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

    public static Toggle AddToggle(Transform parent, string label)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform));
        obj.transform.SetParent(parent, false);

        RectTransform box = AddBox(obj.transform, "Box", InputColor);
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

    public static TMP_Dropdown AddDropdown(Transform parent)
    {
        GameObject obj = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        obj.transform.SetParent(parent, false);
        TMP_Dropdown dropdown = obj.GetComponent<TMP_Dropdown>();
        StyleDropdown(dropdown);
        return dropdown;
    }

    public static void StyleDropdown(TMP_Dropdown dropdown)
    {
        dropdown.GetComponent<Image>().color = InputColor;
        dropdown.captionText.fontSize = 28;
        dropdown.captionText.color = DarkText;
        dropdown.itemText.fontSize = 26;
        dropdown.itemText.color = DarkText;

        Transform arrow = dropdown.transform.Find("Arrow");
        if (arrow != null) arrow.gameObject.SetActive(false); // no sprite for it without the default UI resources

        RectTransform template = dropdown.template;
        template.sizeDelta = new Vector2(template.sizeDelta.x, 420f);
        if (template.GetComponent<DropdownScrollToChosen>() == null) template.gameObject.AddComponent<DropdownScrollToChosen>();
        ScrollRect scroll = template.GetComponent<ScrollRect>();
        if (scroll != null) scroll.scrollSensitivity = 15f; // a wheel notch moves about two options
        Transform checkmark = template.Find("Viewport/Content/Item/Item Checkmark");
        if (checkmark != null) checkmark.GetComponent<Image>().color = Accent;
        RectTransform item = template.Find("Viewport/Content/Item") as RectTransform;
        if (item != null) item.sizeDelta = new Vector2(item.sizeDelta.x, 44f);
    }

    /* Makes the box scroll vertically and returns the content to fill. The content grows with its
     children; give it a layout group (vertical list or grid) */
    public static RectTransform AddScrollContent(RectTransform scrollArea)
    {
        ScrollRect scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollArea, false);
        Place((RectTransform)viewport.transform, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));

        GameObject contentObj = new GameObject("Content", typeof(RectTransform));
        contentObj.transform.SetParent(viewport.transform, false);
        RectTransform content = (RectTransform)contentObj.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        contentObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = content;
        return content;
    }

    // A vertical list for AddScrollContent's content
    public static void AddListLayout(RectTransform content, float spacing)
    {
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }
}

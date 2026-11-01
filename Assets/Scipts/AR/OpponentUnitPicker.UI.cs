using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* OpponentUnitPicker's canvas and its boxes, one per circle of the opponent's field.
 The rest of OpponentUnitPicker is in OpponentUnitPicker.cs. */
public partial class OpponentUnitPicker
{
    void BuildCanvas()
    {
        mCanvasObj = new GameObject("OpponentUnitCanvas", typeof(RectTransform));
        Canvas canvas = mCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // over the camera views and the hand buttons
        mCanvasObj.AddComponent<GraphicRaycaster>(); // no scaler: one unit is one screen pixel
        RotatedView.Follow(canvas);

        for (int i = 0; i < mMarkers.Length; i++) mMarkers[i] = BuildMarker(mCanvasObj.transform, Circles[i]);
        mCanvasObj.SetActive(false);
    }

    Marker BuildMarker(Transform parent, string zone)
    {
        GameObject obj = new GameObject("Choose_" + zone, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;

        Image image = obj.AddComponent<Image>();
        image.color = new Color(0.85f, 0.1f, 0.1f, 0.45f);
        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => mRules?.Skills?.ChooseOpponent(zone));
        Ripple.AddEdges(rect, 3f, Color.white);

        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        labelObj.transform.SetParent(obj.transform, false);
        RectTransform labelRect = (RectTransform)labelObj.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 4f);
        labelRect.offsetMax = new Vector2(-4f, -4f);
        RotatedView.KeepUpright(labelRect);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = 24;
        label.outlineWidth = 0.2f;
        label.outlineColor = Color.black;
        label.raycastTarget = false;

        Ripple.Add(rect, new Color(1f, 0.3f, 0.3f));
        obj.SetActive(false);
        return new Marker { rect = rect, label = label };
    }
}

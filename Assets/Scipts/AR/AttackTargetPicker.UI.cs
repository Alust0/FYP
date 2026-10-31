using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* AttackTargetPicker's canvas and its three target boxes.
 The rest of AttackTargetPicker is in AttackTargetPicker.cs. */
public partial class AttackTargetPicker
{
    void BuildCanvas()
    {
        mCanvasObj = new GameObject("AttackTargetCanvas", typeof(RectTransform));
        Canvas canvas = mCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // over the camera views and the hand buttons
        mCanvasObj.AddComponent<GraphicRaycaster>(); // no scaler: one unit is one screen pixel
        RotatedView.Follow(canvas);

        for (int i = 0; i < mMarkers.Length; i++) mMarkers[i] = BuildMarker(mCanvasObj.transform, i);
        mCanvasObj.SetActive(false);
    }

    Marker BuildMarker(Transform parent, int index)
    {
        GameObject obj = new GameObject("Target_" + RulesEngine.FrontRow[index], typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;

        Image image = obj.AddComponent<Image>();
        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        string zone = RulesEngine.FrontRow[index];
        button.onClick.AddListener(() => mRules?.ChooseTarget(zone));

        // A white border: bottom, top, left, right
        const float t = 3f;
        Vector2[,] edges =
        {
            { new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, t) },
            { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -t), Vector2.zero },
            { new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(t, 0f) },
            { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-t, 0f), Vector2.zero },
        };
        for (int e = 0; e < 4; e++)
        {
            GameObject edgeObj = new GameObject("Edge", typeof(RectTransform));
            edgeObj.transform.SetParent(obj.transform, false);
            RectTransform edge = (RectTransform)edgeObj.transform;
            edge.anchorMin = edges[e, 0];
            edge.anchorMax = edges[e, 1];
            edge.offsetMin = edges[e, 2];
            edge.offsetMax = edges[e, 3];
            Image edgeImage = edgeObj.AddComponent<Image>();
            edgeImage.color = Color.white;
            edgeImage.raycastTarget = false;
        }

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
        label.fontSizeMax = 26;
        label.outlineWidth = 0.2f;
        label.outlineColor = Color.black;
        label.raycastTarget = false;

        GameObject fillObj = new GameObject("Hold", typeof(RectTransform));
        fillObj.transform.SetParent(obj.transform, false);
        fillObj.transform.SetAsFirstSibling(); // under the label
        RectTransform fill = (RectTransform)fillObj.transform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.pivot = new Vector2(0f, 0.5f); // fills left to right
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fill.localScale = new Vector3(0f, 1f, 1f);
        Image fillImage = fillObj.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 1f, 0.4f, 0.5f);
        fillImage.raycastTarget = false;

        return new Marker { rect = rect, image = image, button = button, label = label, fill = fill };
    }
}

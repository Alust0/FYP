using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* VoiceCommands' pop-up: the keyword heard, top-centre of the camera view.
 The rest of VoiceCommands is in VoiceCommands.cs. */
public partial class VoiceCommands
{
    void ShowToast(PlayerState who, string phrase)
    {
        mToast.text = "<b>" + who.playerName + "</b>:  \"" + phrase.ToUpperInvariant() + "\"";
        mToastUntil = Time.time + toastSeconds;
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("VoiceCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // no scaler: one unit is one screen pixel
        canvasObj.transform.SetParent(transform, false);

        GameObject toastObj = new GameObject("VoiceToast", typeof(RectTransform));
        toastObj.transform.SetParent(canvasObj.transform, false);
        Image background = toastObj.AddComponent<Image>();
        background.color = new Color(0.05f, 0.06f, 0.1f, 0.85f);
        background.raycastTarget = false;
        mToastGroup = toastObj.AddComponent<CanvasGroup>();
        mToastGroup.alpha = 0f;
        mToast = AddText(toastObj.transform, 28, TextAlignmentOptions.Center);
        mToast.color = new Color(1f, 0.85f, 0.2f);
    }

    void PlaceUI(Rect camera)
    {
        RectTransform toast = (RectTransform)mToastGroup.transform;
        toast.anchorMin = toast.anchorMax = toast.pivot = new Vector2(0f, 0f);
        toast.sizeDelta = new Vector2(Mathf.Min(560f, camera.width * 0.8f), 54f);
        toast.anchoredPosition = new Vector2(camera.center.x - toast.sizeDelta.x * 0.5f, camera.yMax - 54f - 12f);
    }

    static TextMeshProUGUI AddText(Transform parent, int size, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 4f);
        rt.offsetMax = new Vector2(-10f, -4f);

        TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.outlineWidth = 0.2f;
        text.outlineColor = Color.black;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12;
        text.fontSizeMax = size;
        text.raycastTarget = false;
        return text;
    }
}

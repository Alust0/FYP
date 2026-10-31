using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* HandZones' buttons, trigger pick circles, deck frame and H preview. On their own overlay canvas,
 so they aren't in the camera picture being checked.
 The rest of HandZones is in HandZones.cs. */
public partial class HandZones
{
    void BuildCanvas()
    {
        GameObject canvasObj = new GameObject("HandZonesCanvas", typeof(RectTransform));
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -5; // under the HUD, which sits beside the camera view anyway
        canvasObj.AddComponent<GraphicRaycaster>(); // the trigger pick circles can be clicked
        mCanvas = (RectTransform)canvasObj.transform; // no scaler: one unit is one screen pixel
        RotatedView.Follow(canvas);
    }

    void BuildButton(Area area, string text)
    {
        bool target = area.targetZone != null;
        area.ui = AddRect(mCanvas, "HandButton_" + (target ? area.targetZone : "NextPhase"),
            target ? new Color(0.5f, 0.05f, 0.05f, 0.6f) : new Color(0.05f, 0.06f, 0.1f, 0.55f));
        AddFrame(area.ui, new Color(1f, 1f, 1f, 0.8f));

        area.fill = AddRect(area.ui, "Fill", new Color(0.2f, 1f, 0.4f, 0.45f));
        area.fill.anchorMin = Vector2.zero;
        area.fill.anchorMax = Vector2.one;
        area.fill.pivot = new Vector2(0f, 0.5f); // fills left to right
        area.fill.offsetMin = area.fill.offsetMax = Vector2.zero;

        area.label = AddLabel(area.ui, text);
    }

    // A lit circle for a trigger pick; also clickable, for when a finger isn't picked up
    void BuildPickFrame(Area area)
    {
        area.ui = AddRect(mCanvas, "Pick_" + area.pickKey, new Color(0.2f, 0.8f, 1f, 0.18f));
        Image background = area.ui.GetComponent<Image>();
        background.raycastTarget = true;
        Button button = area.ui.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        string zone = area.pickZone;
        button.onClick.AddListener(() => mRules?.Pick(zone));
        AddFrame(area.ui, new Color(0.2f, 0.8f, 1f, 0.95f));

        area.fill = AddRect(area.ui, "Fill", new Color(0.2f, 1f, 0.4f, 0.45f));
        area.fill.anchorMin = Vector2.zero;
        area.fill.anchorMax = Vector2.one;
        area.fill.pivot = new Vector2(0f, 0.5f);
        area.fill.offsetMin = area.fill.offsetMax = Vector2.zero;

        area.label = AddLabel(area.ui, "");
    }

    void BuildDeckFrame(Area area)
    {
        area.ui = AddRect(mCanvas, "HandDeck", new Color(1f, 0.85f, 0.2f, 0.12f));
        AddFrame(area.ui, new Color(1f, 0.85f, 0.2f, 0.95f));
        area.label = AddLabel(area.ui, "Draw\nfrom here");
    }

    /* H: the picture being checked, bottom-right of the camera view, with every live area boxed
     (green while covered) and how much of it has changed. If the picture doesn't match the camera
     view, the hand check is looking in the wrong place. */
    void BuildPreview()
    {
        mPreview = new GameObject("HandZonesPreview", typeof(RectTransform));
        mPreview.transform.SetParent(mCanvas, false);
        mPreviewImage = mPreview.AddComponent<RawImage>();
        mPreviewImage.raycastTarget = false;
        RectTransform rt = (RectTransform)mPreview.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;

        foreach (Area area in mAreas)
        {
            area.previewFrame = AddRect(rt, "Area", Color.clear);
            AddFrame(area.previewFrame, Color.white);
            area.previewText = AddLabel(area.previewFrame, "");
        }
        mPreview.SetActive(false);
    }

    void UpdatePreview()
    {
        if (mPreview == null || !mPreview.activeSelf) return;

        Rect px = mCamera.pixelRect;
        RectTransform rt = (RectTransform)mPreview.transform;
        Vector2 size = new Vector2(px.width, px.height) * 0.35f;
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(px.xMax - size.x - 8f, px.y + 8f);

        bool webcam = mWebcam != null && mWebcam.isPlaying;
        mPreviewImage.texture = webcam ? mWebcam : (Texture)mCapture;
        Rect vis = webcam ? VisiblePart(mWebcam.width, mWebcam.height) : new Rect(0f, 0f, 1f, 1f);
        bool flip = webcam ? mWebcam.videoVerticallyMirrored != flipPicture : flipPicture;
        mPreviewImage.uvRect = flip ? new Rect(vis.x, vis.yMax, vis.width, -vis.height) : vis;

        foreach (Area area in mAreas)
        {
            bool live = area.view.width > 0f && area.baseline != null;
            area.previewFrame.gameObject.SetActive(live);
            if (!live) continue;

            area.previewFrame.anchoredPosition = new Vector2(area.view.x * size.x, area.view.y * size.y);
            area.previewFrame.sizeDelta = new Vector2(area.view.width * size.x, area.view.height * size.y);
            foreach (Image edge in area.previewFrame.GetComponentsInChildren<Image>())
                if (edge.transform != area.previewFrame) edge.color = area.covered ? Color.green : Color.white;
            area.previewText.text = Mathf.RoundToInt(area.changed * 100f) + "%";
        }
    }

    void PlaceUI(Area area)
    {
        bool visible = area.shown && area.view.width > 0f && !(area.pickZone != null && mRules != null && mRules.IsChoosingTarget); // AttackTargetPicker draws those
        if (area.ui.gameObject.activeSelf != visible) area.ui.gameObject.SetActive(visible);
        if (!visible) return;

        Rect px = mCamera.pixelRect;
        area.ui.anchoredPosition = new Vector2(px.x + area.view.x * px.width, px.y + area.view.y * px.height);
        area.ui.sizeDelta = new Vector2(area.view.width * px.width, area.view.height * px.height);

        if (area.fill != null)
        {
            float hold = area.pickZone != null ? pickHoldSeconds : holdSeconds;
            float progress = area.covered && !area.fired ? (Time.time - area.coveredSince) / hold : 0f;
            area.fill.localScale = new Vector3(Mathf.Clamp01(progress), 1f, 1f);
        }

        // What this circle would get, and whether it has it now
        if (area.pickZone != null && mRules != null)
        {
            string what = mRules.PickNow == RulesEngine.TriggerPick.Critical ? "Critical +1"
                : mRules.PickNow == RulesEngine.TriggerPick.Stand ? "Stand" : "Power";
            area.label.text = what + (area.pickZone == mRules.PickCurrentZone ? "\n(on it now)" : "");
        }
    }

    static RectTransform AddRect(Transform parent, string name, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        Image image = obj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    // A thin border just inside the rect
    static void AddFrame(RectTransform parent, Color color)
    {
        const float t = 3f;
        (Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)[] sides =
        {
            (new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, t)),
            (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -t), Vector2.zero),
            (new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(t, 0f)),
            (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-t, 0f), Vector2.zero),
        };
        foreach (var side in sides)
        {
            RectTransform rt = AddRect(parent, "Edge", color);
            rt.anchorMin = side.min;
            rt.anchorMax = side.max;
            rt.offsetMin = side.offMin;
            rt.offsetMax = side.offMax;
        }
    }

    static TextMeshProUGUI AddLabel(RectTransform parent, string text)
    {
        GameObject obj = new GameObject("Label", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);

        TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8;
        label.fontSizeMax = 22;
        label.outlineWidth = 0.2f;
        label.outlineColor = Color.black;
        label.raycastTarget = false;
        RotatedView.KeepUpright(rt);
        return label;
    }
}

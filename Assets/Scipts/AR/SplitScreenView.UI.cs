using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* The opponent's camera picture and name strip, for SplitScreenView's top half.
 The rest of SplitScreenView is in SplitScreenView.cs. */
public partial class SplitScreenView
{
    // Top half of the screen, under the HUD's canvas
    void BuildFeedPanel()
    {
        GameObject canvasObj = new GameObject("OpponentCameraCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10;
        RotatedView.Follow(canvas);
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        mFeedPanel = new GameObject("OpponentCamera", typeof(RectTransform));
        mFeedPanel.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)mFeedPanel.transform, new Vector2(0f, 0.5f), new Vector2(1f, 1f)); // placed by ApplyLayout

        GameObject feedObj = new GameObject("Feed", typeof(RectTransform));
        feedObj.transform.SetParent(mFeedPanel.transform, false);
        Stretch((RectTransform)feedObj.transform, Vector2.zero, Vector2.one);
        mFeed = feedObj.AddComponent<RawImage>();
        mFeed.color = new Color(0.08f, 0.08f, 0.1f);
        mFeed.uvRect = new Rect(1f, 1f, -1f, -1f); // turned round: their side faces yours
        mFeed.raycastTarget = false;
        mFitter = feedObj.AddComponent<AspectRatioFitter>();
        mFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        mFitter.aspectRatio = ViewAspect;

        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        labelObj.transform.SetParent(mFeedPanel.transform, false);
        mLabel = labelObj.AddComponent<TextMeshProUGUI>(); // placed by PlaceLabel
        mLabel.fontSize = 20;
        mLabel.alignment = TextAlignmentOptions.Center;
        mLabel.color = Color.white;
        mLabel.outlineWidth = 0.2f;
        mLabel.outlineColor = Color.black;
        mLabel.raycastTarget = false;
    }

    static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

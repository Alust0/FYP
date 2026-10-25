using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

/* Online: both players' fields on screen. This PC's camera view shrinks into the bottom half, and
 the opponent's camera view (sent over CameraFeedLink) fills the top half, turned round as if seen
 from across the table. V switches back to this PC's view alone. Added by GameSetup when online. */
public class SplitScreenView : MonoBehaviour
{
    public Key toggleKey = Key.V;
    public int captureWidth = 640;
    [Range(1, 100)] public int directQuality = 70;
    [Range(1, 100)] public int fallbackQuality = 40;

    // Each view at half size keeps the screen's shape, so the AR overlays line up as before
    static readonly Rect OwnView = new Rect(0.25f, 0f, 0.5f, 0.5f);
    static readonly Rect FullView = new Rect(0f, 0f, 1f, 1f);

    CameraFeedLink mLink;
    Camera mCamera;
    Camera mBackdrop; // clears the screen around the two views
    GameObject mFeedPanel;
    RawImage mFeed;
    AspectRatioFitter mFitter;
    TextMeshProUGUI mLabel;
    Texture2D mIncoming;
    RenderTexture mCapture;
    bool mSplit = true;
    bool mReadingBack;

    void Start()
    {
        if (!MatchSession.IsOnline)
        {
            enabled = false;
            return;
        }

        mLink = gameObject.AddComponent<CameraFeedLink>();
        mIncoming = new Texture2D(2, 2);
        BuildFeedPanel();
    }

    void OnDestroy()
    {
        if (mCamera != null) mCamera.rect = FullView;
        if (mCapture != null) mCapture.Release();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) mSplit = !mSplit;

        if (mCamera == null) mCamera = Camera.main;
        if (mCamera == null) return;
        ApplyLayout();

        byte[] frame = mLink.TakeLatest();
        if (frame != null && mIncoming.LoadImage(frame))
        {
            mFeed.texture = mIncoming;
            mFeed.color = Color.white;
            mFitter.aspectRatio = (float)mIncoming.width / mIncoming.height;
        }

        string opponent = MatchSession.PlayerNames[MatchSession.RemoteSeat];
        mLabel.text = mFeed.texture == null ? "Waiting for " + opponent + "'s camera..."
            : opponent + "'s side" + (mLink.UsingFallback ? "  <color=#fc6>(slow link: no direct connection)</color>" : "");

        if (!mReadingBack && mLink.ReadyToSend) Capture();
    }

    // Again after Vuforia's own updates, in case it resets the camera's viewport
    void LateUpdate()
    {
        if (mCamera != null) ApplyLayout();
    }

    void ApplyLayout()
    {
        Rect wanted = mSplit ? OwnView : FullView;
        if (mCamera.rect != wanted) mCamera.rect = wanted;

        if (mBackdrop == null) mBackdrop = CreateBackdrop();
        mBackdrop.depth = mCamera.depth - 1;
        mBackdrop.enabled = mSplit;
        mFeedPanel.SetActive(mSplit);
    }

    Camera CreateBackdrop()
    {
        Camera backdrop = new GameObject("SplitScreenBackdrop").AddComponent<Camera>();
        backdrop.clearFlags = CameraClearFlags.SolidColor;
        backdrop.backgroundColor = Color.black;
        backdrop.cullingMask = 0;
        return backdrop;
    }

    /* Renders the AR camera (camera picture, zones and card overlays, without the HUD) into a small
     texture, reads it back without stalling, and sends it as a JPEG. */
    void Capture()
    {
        int height = Mathf.Max(1, Mathf.RoundToInt(captureWidth * (float)Screen.height / Screen.width));
        if (mCapture == null || mCapture.height != height || mCapture.width != captureWidth)
        {
            if (mCapture != null) mCapture.Release();
            mCapture = new RenderTexture(captureWidth, height, 24);
        }

        Rect rect = mCamera.rect;
        mCamera.rect = FullView;
        var request = new RenderPipeline.StandardRequest { destination = mCapture };
        if (RenderPipeline.SupportsRenderRequest(mCamera, request))
            RenderPipeline.SubmitRenderRequest(mCamera, request);
        else
        {
            mCamera.targetTexture = mCapture;
            mCamera.Render();
            mCamera.targetTexture = null;
        }
        mCamera.rect = rect;

        mReadingBack = true;
        int quality = mLink.Direct ? directQuality : fallbackQuality;
        int width = mCapture.width;
        AsyncGPUReadback.Request(mCapture, 0, TextureFormat.RGB24, done =>
        {
            mReadingBack = false;
            if (this == null || done.hasError) return;
            byte[] jpeg = ImageConversion.EncodeArrayToJPG(done.GetData<byte>().ToArray(), GraphicsFormat.R8G8B8_SRGB,
                (uint)width, (uint)height, 0, quality);
            mLink.Send(jpeg);
        });
    }

    // Top half of the screen, under the HUD's canvas
    void BuildFeedPanel()
    {
        GameObject canvasObj = new GameObject("OpponentCameraCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        mFeedPanel = new GameObject("OpponentCamera", typeof(RectTransform));
        mFeedPanel.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)mFeedPanel.transform, new Vector2(OwnView.x, 0.5f), new Vector2(OwnView.xMax, 1f));

        GameObject feedObj = new GameObject("Feed", typeof(RectTransform));
        feedObj.transform.SetParent(mFeedPanel.transform, false);
        Stretch((RectTransform)feedObj.transform, Vector2.zero, Vector2.one);
        mFeed = feedObj.AddComponent<RawImage>();
        mFeed.color = new Color(0.08f, 0.08f, 0.1f);
        mFeed.uvRect = new Rect(1f, 1f, -1f, -1f); // turned round: their side faces yours
        mFeed.raycastTarget = false;
        mFitter = feedObj.AddComponent<AspectRatioFitter>();
        mFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        mFitter.aspectRatio = 16f / 9f;

        GameObject labelObj = new GameObject("Label", typeof(RectTransform));
        labelObj.transform.SetParent(mFeedPanel.transform, false);
        RectTransform label = (RectTransform)labelObj.transform;
        Stretch(label, new Vector2(0f, 1f), new Vector2(1f, 1f));
        label.pivot = new Vector2(0.5f, 1f);
        label.sizeDelta = new Vector2(0f, 34f);
        mLabel = labelObj.AddComponent<TextMeshProUGUI>();
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

using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

/* Lays the camera views out left of UISetup's sidebar. Online: both players' fields, this PC's
 camera view in the bottom half and the opponent's (sent over CameraFeedLink) in the top half,
 turned round as if seen from across the table; V switches to this PC's view alone. Offline: this
 PC's view alone. Added by GameSetup. Its UI is built in SplitScreenView.UI.cs. */
public partial class SplitScreenView : MonoBehaviour
{
    public Key toggleKey = Key.V;
    public int captureWidth = 640;
    public int fallbackWidth = 480; // smaller through the database, so each picture downloads sooner
    [Range(1, 100)] public int directQuality = 70;
    [Range(1, 100)] public int fallbackQuality = 40;

    public const float SidebarWidth = 0.26f; // of the screen's width, on the right
    const float Area = 1f - SidebarWidth;

    /* Every view, on screen and sent, is this shape whatever the window's shape, so both players'
     views match. Vuforia fits its camera picture to the camera's on-screen box (pixelWidth/Height),
     so a box of another shape crops the picture rather than stretching it. */
    const float ViewAspect = 16f / 9f;
    const float LabelHeight = 34f; // the opponent's name strip
    static readonly Rect FullView = new Rect(0f, 0f, 1f, 1f);

    /* Online with both views up: where the opponent's camera picture is, as fractions of the
     screen (origin bottom-left). It's turned round, so a point at (x, y) of their view shows at
     (1 - x, 1 - y) of this. Null when it isn't on screen. */
    public static Rect? OpponentView { get; private set; }

    // Both views together (or this PC's alone), as fractions of the screen, for RotatedView to turn
    public static Rect ViewsArea { get; private set; } = new Rect(0f, 0f, Area, 1f);

    CameraFeedLink mLink;
    BoardLayout mBoard;
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
        mSplit = MatchSession.IsOnline;
        if (!MatchSession.IsOnline) return;

        mLink = gameObject.AddComponent<CameraFeedLink>();
        mIncoming = new Texture2D(2, 2);
        BuildFeedPanel();
    }

    void OnDestroy()
    {
        OpponentView = null;
        if (mCamera != null) mCamera.rect = FullView;
        if (mCapture != null) mCapture.Release();
    }

    void Update()
    {
        if (mCamera == null) mCamera = Camera.main;
        if (mCamera == null) return;
        ApplyLayout();
        if (mLink == null) return; // offline: just the layout

        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) mSplit = !mSplit;

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
        Rect wanted = ViewRect(mSplit);
        if (mCamera.rect != wanted) mCamera.rect = wanted;

        if (mBackdrop == null) mBackdrop = CreateBackdrop();
        mBackdrop.depth = mCamera.depth - 1;

        OpponentView = null;
        bool split = mFeedPanel != null && mSplit;
        ViewsArea = split ? new Rect(wanted.x, wanted.y, wanted.width, 1f - 2f * wanted.y) : wanted; // the two boxes, mirrored about the middle
        if (mFeedPanel == null) return;
        mFeedPanel.SetActive(mSplit);
        if (mSplit) OpponentView = new Rect(wanted.x, 1f - wanted.yMax, wanted.width, wanted.height);

        // The opponent's view: the same box, mirrored into the top half
        RectTransform panel = (RectTransform)mFeedPanel.transform;
        panel.anchorMin = new Vector2(wanted.x, 1f - wanted.yMax);
        panel.anchorMax = new Vector2(wanted.xMax, 1f - wanted.y);
        PlaceLabel(panel);
    }

    /* Across the panel's top; with RotatedView's view turned, down its right edge a quarter turn
     clockwise instead, which the turn puts across the top again, upright */
    void PlaceLabel(RectTransform panel)
    {
        RectTransform label = mLabel.rectTransform;
        bool turned = GameSettings.RotateView;
        label.anchorMin = turned ? new Vector2(1f, 0.5f) : new Vector2(0f, 1f);
        label.anchorMax = turned ? new Vector2(1f, 0.5f) : new Vector2(1f, 1f);
        label.pivot = turned ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1f);
        label.localEulerAngles = new Vector3(0f, 0f, turned ? -90f : 0f);
        label.anchoredPosition = turned ? new Vector2(-LabelHeight * 0.5f, 0f) : Vector2.zero;
        label.sizeDelta = turned ? new Vector2(panel.rect.height, LabelHeight) : new Vector2(0f, LabelHeight);
    }

    /* The biggest ViewAspect box left of the sidebar, as a fraction of the screen: split, in the
     middle of the bottom half; alone, in the middle of the area */
    static Rect ViewRect(bool split)
    {
        float width = Screen.width, height = Screen.height;
        float areaWidth = Area * width;
        float areaHeight = split ? height * 0.5f : height;

        float boxHeight = Mathf.Min(areaHeight, areaWidth / ViewAspect);
        float boxWidth = boxHeight * ViewAspect;
        float x = (areaWidth - boxWidth) * 0.5f;
        float y = (areaHeight - boxHeight) * 0.5f;
        return new Rect(x / width, y / height, boxWidth / width, boxHeight / height);
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
        int wide = mLink.Direct ? captureWidth : fallbackWidth;
        int height = Mathf.Max(1, Mathf.RoundToInt(wide / ViewAspect)); // same shape as the view on screen
        if (mCapture == null || mCapture.height != height || mCapture.width != wide)
        {
            if (mCapture != null) mCapture.Release();
            mCapture = new RenderTexture(wide, height, 24);
        }

        /* The zone labels as the opponent reads them: their PC shows this picture turned round,
         and a quarter turn anticlockwise more if their view is turned (RotatedView) */
        if (mBoard == null) mBoard = FindFirstObjectByType<BoardLayout>();
        int ownAngle = mBoard != null ? mBoard.LabelAngle : 0;
        if (mBoard != null) mBoard.SetLabelAngle(OnlineMatch.OpponentTurned ? 90 : 180);

        Rect rect = mCamera.rect;
        mCamera.rect = FullView;
        CardOverlayUI.HideOpenPanels(true); // a card's detail panel is only for this player
        var request = new RenderPipeline.StandardRequest { destination = mCapture };
        if (RenderPipeline.SupportsRenderRequest(mCamera, request))
            RenderPipeline.SubmitRenderRequest(mCamera, request);
        else
        {
            RenderTexture target = mCamera.targetTexture;
            mCamera.targetTexture = mCapture;
            mCamera.Render();
            mCamera.targetTexture = target;
        }
        CardOverlayUI.HideOpenPanels(false);
        mCamera.rect = rect;
        if (mBoard != null) mBoard.SetLabelAngle(ownAngle);

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
}

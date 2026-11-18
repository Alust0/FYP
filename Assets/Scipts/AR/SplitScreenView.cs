using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Lays out the camera views; online, the opponent's on top
public partial class SplitScreenView : MonoBehaviour
{
    public Key toggleKey = Key.V;
    public int captureWidth = 960; // the opponent's view is about this wide at 1080p
    public int fallbackWidth = 480; // smaller through the database, so each picture downloads sooner
    [Range(1, 100)] public int directQuality = 80;
    [Range(1, 100)] public int fallbackQuality = 40;

    public const float SidebarWidth = 0.26f; // of the screen's width, on the right
    const float Area = 1f - SidebarWidth;

    // Every view uses this aspect so both players' views match
    const float ViewAspect = 16f / 9f;
    const float LabelHeight = 34f; // the opponent's name strip
    static readonly Rect FullView = new Rect(0f, 0f, 1f, 1f);

    // Where the opponent's picture is on screen (turned round); null if hidden
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
    // Captures being read back or encoded; two at once so 30 a second keeps up
    const int MaxInFlight = 2;
    int mInFlight; // main thread only
    int mFinished; // finished on worker threads (Interlocked)
    int mCaptureSeq;
    readonly object mEncodedLock = new object();
    byte[] mEncoded;
    int mEncodedSeq;
    float mNextCapture;

    void Start()
    {
        mSplit = MatchSession.IsOnline;
        if (!MatchSession.IsOnline) { return; }

        mLink = gameObject.AddComponent<CameraFeedLink>();
        mIncoming = new Texture2D(2, 2);
        BuildFeedPanel();
    }

    void OnDestroy()
    {
        OpponentView = null;
        if (mCamera != null) { mCamera.rect = FullView; }
        if (mCapture != null) { mCapture.Release(); }
    }

    void Update()
    {
        if (mCamera == null) { mCamera = Camera.main; }
        if (mCamera == null) { return; }
        ApplyLayout();
        if (mLink == null) { return; } // offline: just the layout

        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) { mSplit = !mSplit; }

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

        mInFlight -= System.Threading.Interlocked.Exchange(ref mFinished, 0);
        byte[] encoded;
        lock (mEncodedLock)
        {
            encoded = mEncoded;
            mEncoded = null;
        }
        if (encoded != null) { mLink.Send(encoded); }

        // Direct: paced from each capture's start, so the rate holds; the database waits for each upload
        bool due = mLink.Direct ? Time.unscaledTime >= mNextCapture && !mLink.Busy : mLink.ReadyToSend;
        if (due && mInFlight < (mLink.Direct ? MaxInFlight : 1))
        {
            mNextCapture = Time.unscaledTime + mLink.directSeconds;
            Capture();
        }
    }

    // Again after Vuforia's own updates, in case it resets the camera's viewport
    void LateUpdate()
    {
        if (mCamera != null) { ApplyLayout(); }
    }

    void ApplyLayout()
    {
        Rect wanted = ViewRect(mSplit);
        if (mCamera.rect != wanted) { mCamera.rect = wanted; }

        if (mBackdrop == null) { mBackdrop = CreateBackdrop(); }
        mBackdrop.depth = mCamera.depth - 1;

        OpponentView = null;
        bool split = mFeedPanel != null && mSplit;
        ViewsArea = split ? new Rect(wanted.x, wanted.y, wanted.width, 1f - 2f * wanted.y) : wanted; // the two boxes, mirrored about the middle
        if (mFeedPanel == null) { return; }
        mFeedPanel.SetActive(mSplit);
        if (mSplit) { OpponentView = new Rect(wanted.x, 1f - wanted.yMax, wanted.width, wanted.height); }

        // The opponent's view: the same box, mirrored into the top half
        RectTransform panel = (RectTransform)mFeedPanel.transform;
        panel.anchorMin = new Vector2(wanted.x, 1f - wanted.yMax);
        panel.anchorMax = new Vector2(wanted.xMax, 1f - wanted.y);
        PlaceLabel(panel);
    }

    // Title across the panel's top
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

    // The biggest ViewAspect box left of the sidebar
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

    // Captures the AR camera and sends it as a JPEG
    void Capture()
    {
        int wide = mLink.Direct ? captureWidth : fallbackWidth;
        int height = Mathf.Max(1, Mathf.RoundToInt(wide / ViewAspect)); // same shape as the view on screen
        if (mCapture == null || mCapture.height != height || mCapture.width != wide)
        {
            if (mCapture != null) { mCapture.Release(); }
            mCapture = new RenderTexture(wide, height, 24);
        }

        // Zone labels turned as the opponent reads them
        if (mBoard == null) { mBoard = FindFirstObjectByType<BoardLayout>(); }
        int ownAngle = mBoard != null ? mBoard.LabelAngle : 0;
        if (mBoard != null) { mBoard.SetLabelAngle(OnlineMatch.OpponentTurned ? 90 : 180); }

        Rect rect = mCamera.rect;
        mCamera.rect = FullView;
        CardOverlayUI.HideOpenPanels(true); // a card's detail panel is only for this player
        var request = new RenderPipeline.StandardRequest { destination = mCapture };
        if (RenderPipeline.SupportsRenderRequest(mCamera, request))
        {
            RenderPipeline.SubmitRenderRequest(mCamera, request);
        }
        else
        {
            RenderTexture target = mCamera.targetTexture;
            mCamera.targetTexture = mCapture;
            mCamera.Render();
            mCamera.targetTexture = target;
        }
        CardOverlayUI.HideOpenPanels(false);
        mCamera.rect = rect;
        if (mBoard != null) { mBoard.SetLabelAngle(ownAngle); }

        mInFlight++;
        int seq = ++mCaptureSeq;
        int quality = mLink.Direct ? directQuality : fallbackQuality;
        int width = mCapture.width;
        AsyncGPUReadback.Request(mCapture, 0, TextureFormat.RGB24, done =>
        {
            if (this == null || done.hasError)
            {
                System.Threading.Interlocked.Increment(ref mFinished);
                return;
            }
            byte[] pixels = done.GetData<byte>().ToArray();
            // Encoding on a worker thread keeps the frame rate up; Update sends the result
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    byte[] jpeg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8_SRGB, (uint)width, (uint)height, 0, quality);
                    lock (mEncodedLock)
                        if (seq > mEncodedSeq) // never older over newer
                        {
                            mEncoded = jpeg;
                            mEncodedSeq = seq;
                        }
                }
                catch (System.Exception) { }
                System.Threading.Interlocked.Increment(ref mFinished);
            });
        });
    }
}

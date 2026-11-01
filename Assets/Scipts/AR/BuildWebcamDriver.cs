using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using Vuforia;
using Debug = UnityEngine.Debug;

/* Builds only (UWP). There Vuforia opens a camera of its own choosing and can't be told which one,
 so instead it's started with its Unity webcam driver (UnityDriver.dll, what Play Mode uses), and
 this feeds that driver frames from the webcam picked in the lobby. If anything fails, Vuforia
 starts the normal way and picks its own camera. */
public class BuildWebcamDriver : MonoBehaviour
{
    const string DriverLibrary = "UnityDriver.dll";
    const int MaxWidth = 1920; // same as Play Mode's webcam profile
    const int RequestedFps = 30;
    const int Rgba8888 = 5;    // Vuforia.DriverPixelFormat.RGBA8888

    // Mirrors of Vuforia's internal driver structs, which its UnityDriver.dll reads

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void DriverCreatedFn(IntPtr driver);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void GetConfigFn(IntPtr buffer);

    struct DriverData
    {
        public DriverCreatedFn DriverCreated;
        public GetConfigFn GetConfig;
    }

    struct DriverConfig
    {
        public uint Capabilities; // 0 = camera image only
        public int Width, Height, Fps, Format;
        public int IsAnchorSupported;
        public uint CameraRotationInDegrees;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct CameraImage
    {
        public long Timestamp;
        public long ExposureTime;
        public IntPtr Buffer;
        public int BufferSize;
        public int Index;
        public int Width;
        public int Height;
        public int Stride;
        public int Format;
        public float FocalLengthX, FocalLengthY, PrincipalPointX, PrincipalPointY; // 0 = unknown, as in Play Mode
        public float D0, D1, D2, D3, D4, D5, D6, D7;
    }

    [DllImport(DriverLibrary, CallingConvention = CallingConvention.StdCall)]
    static extern void unityDriverPostCameraFrame(IntPtr driver, IntPtr frame);

    static readonly DriverCreatedFn sOnDriverCreated = OnDriverCreated; // kept alive for native code
    static readonly GetConfigFn sOnGetConfig = OnGetConfig;
    static IntPtr sDriver;
    static DriverConfig sConfig;
    static IntPtr sUserData;
    static BuildWebcamDriver sInstance;
    static readonly int GainId = Shader.PropertyToID("_Gain");

    WebCamTexture mWebcam;
    RenderTexture mFlipped;
    Material mBrightness; // flips and brightens each frame; null if the shader is missing
    Texture2D mReadback;
    byte[] mPixels;
    GCHandle mPixelsHandle;
    IntPtr mImagePtr;
    bool mVuforiaRunning;

    /* Play Mode's webcam turns this on, so the editor never froze on losing focus; builds need it
     too, or alt-tabbing pauses the game, its camera and the online sync. */
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void KeepRunningInBackground() => Application.runInBackground = true;

    /* True while this feeds Vuforia and can brighten its frames (GameSettings.CameraBrightness).
     UWP can't reach the webcam's own brightness: there's no DirectShow, and the webcam is in use */
    public static bool AdjustsBrightness => sInstance != null && sInstance.mBrightness != null;

    [MonoPInvokeCallback(typeof(DriverCreatedFn))]
    static void OnDriverCreated(IntPtr driver) => sDriver = driver;

    [MonoPInvokeCallback(typeof(GetConfigFn))]
    static void OnGetConfig(IntPtr buffer) => Marshal.StructureToPtr(sConfig, buffer, false);

    // Replaces VuforiaApplication.Initialize() in builds; later calls switch to a newly chosen camera
    public static IEnumerator Initialize(string camera)
    {
        if (sInstance != null)
        {
            sInstance.Use(camera);
            yield break;
        }
        if (VuforiaApplication.Instance.IsInitialized) yield break;

        if (camera == null)
        {
            VuforiaApplication.Instance.Initialize();
            yield break;
        }

        var host = new GameObject("BuildWebcamDriver");
        DontDestroyOnLoad(host);
        sInstance = host.AddComponent<BuildWebcamDriver>();
        sInstance.Use(camera);

        // The size of the driver's frames is fixed at start, so wait for the webcam's real size
        float giveUp = Time.realtimeSinceStartup + 10f;
        while (sInstance.mWebcam.width <= 16 && Time.realtimeSinceStartup < giveUp)
            yield return null;

        if (sInstance.mWebcam.width <= 16)
        {
            Debug.LogWarning("[BuildWebcamDriver] '" + camera + "' gave no picture; Vuforia picks its own camera instead.");
            FallBack();
            yield break;
        }

        sInstance.SetUp();
        VuforiaApplication.Instance.OnVuforiaInitialized += OnInitialized;
        try
        {
            VuforiaApplication.Instance.Initialize(DriverLibrary, sUserData);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BuildWebcamDriver] Couldn't start Vuforia with the webcam driver: " + e.Message);
            VuforiaApplication.Instance.OnVuforiaInitialized -= OnInitialized;
            FallBack();
        }
    }

    static void OnInitialized(VuforiaInitError error)
    {
        VuforiaApplication.Instance.OnVuforiaInitialized -= OnInitialized;
        if (error == VuforiaInitError.NONE)
        {
            Debug.Log("[BuildWebcamDriver] Vuforia runs on '" + sInstance.mWebcam.deviceName + "' (" + sConfig.Width + "x" + sConfig.Height + ").");
            return;
        }

        Debug.LogWarning("[BuildWebcamDriver] Vuforia rejected the webcam driver (" + error + "); it picks its own camera instead.");
        VuforiaApplication.Instance.Deinit();
        FallBack();
    }

    static void FallBack()
    {
        if (sInstance != null) Destroy(sInstance.gameObject);
        sInstance = null;
        VuforiaApplication.Instance.Initialize();
    }

    void Use(string camera)
    {
        if (camera == null || (mWebcam != null && mWebcam.deviceName == camera)) return;

        bool playing = mWebcam == null || mWebcam.isPlaying;
        if (mWebcam != null)
        {
            mWebcam.Stop();
            Destroy(mWebcam);
        }
        mWebcam = new WebCamTexture(camera, MaxWidth, MaxWidth * 9 / 16, RequestedFps);
        if (playing) mWebcam.Play();
        Debug.Log("[BuildWebcamDriver] Using camera '" + camera + "'.");
    }

    // Frame size and buffers, from the webcam's size capped at MaxWidth
    void SetUp()
    {
        int width = Mathf.Min(mWebcam.width, MaxWidth);
        int height = Mathf.RoundToInt(width * (float)mWebcam.height / mWebcam.width) & ~1;

        sConfig = new DriverConfig { Width = width, Height = height, Fps = RequestedFps, Format = Rgba8888 };

        mFlipped = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        mReadback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        mPixels = new byte[width * height * 4];
        mPixelsHandle = GCHandle.Alloc(mPixels, GCHandleType.Pinned);
        mImagePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CameraImage)));

        Shader brightness = Resources.Load<Shader>("CameraBrightness");
        if (brightness != null && brightness.isSupported) mBrightness = new Material(brightness);
        else Debug.LogWarning("[BuildWebcamDriver] CameraBrightness shader unavailable; camera brightness can't be changed.");

        var data = new DriverData { DriverCreated = sOnDriverCreated, GetConfig = sOnGetConfig };
        sUserData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DriverData)));
        Marshal.StructureToPtr(data, sUserData, false); // must stay valid while Vuforia runs, so never freed

        // The webcam only needs to run while Vuforia does
        VuforiaApplication.Instance.OnVuforiaStarted += OnStarted;
        VuforiaApplication.Instance.OnVuforiaStopped += OnStopped;
        mWebcam.Stop();
    }

    void OnStarted()
    {
        mVuforiaRunning = true;
        if (!mWebcam.isPlaying) mWebcam.Play();
    }

    void OnStopped()
    {
        mVuforiaRunning = false;
        mWebcam.Stop();
    }

    void Update()
    {
        if (!mVuforiaRunning || sDriver == IntPtr.Zero || mReadback == null || !mWebcam.didUpdateThisFrame) return;

        // Unity textures are stored bottom row first; Vuforia wants the top row first
        if (mBrightness != null)
        {
            mBrightness.SetFloat(GainId, Mathf.Pow(2f, (GameSettings.CameraBrightness - 0.5f) * 2f)); // ×0.5 to ×2
            Graphics.Blit(mWebcam, mFlipped, mBrightness);
        }
        else Graphics.Blit(mWebcam, mFlipped, new Vector2(1f, -1f), new Vector2(0f, 1f));
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = mFlipped;
        mReadback.ReadPixels(new Rect(0, 0, sConfig.Width, sConfig.Height), 0, 0, false);
        RenderTexture.active = previous;
        mReadback.GetRawTextureData<byte>().CopyTo(mPixels);

        var image = new CameraImage
        {
            Timestamp = (long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency)),
            Buffer = mPixelsHandle.AddrOfPinnedObject(),
            BufferSize = mPixels.Length,
            Width = sConfig.Width,
            Height = sConfig.Height,
            Stride = sConfig.Width,
            Format = Rgba8888
        };
        Marshal.StructureToPtr(image, mImagePtr, false);
        unityDriverPostCameraFrame(sDriver, mImagePtr);
    }

    void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= OnStarted;
        VuforiaApplication.Instance.OnVuforiaStopped -= OnStopped;
        if (mWebcam != null) mWebcam.Stop();
        if (mPixelsHandle.IsAllocated) mPixelsHandle.Free();
        if (mImagePtr != IntPtr.Zero) Marshal.FreeHGlobal(mImagePtr);
        if (mBrightness != null) Destroy(mBrightness);
    }
}

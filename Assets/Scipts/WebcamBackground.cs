using UnityEngine;
using UnityEngine.UI;

// Displays the webcam feed as the background when running in the Unity Editor or standalone.
// On a real AR device, AR Foundation handles the camera feed automatically.
public class WebcamBackground : MonoBehaviour
{
    [Header("Settings")]
    [HideInInspector] public string selectedDeviceName = "";
    public int targetWidth = 1920;
    public int targetHeight = 1080;
    public int targetFPS = 30;

    private WebCamTexture webcamTexture;
    private RawImage backgroundImage;

    // void Start()
    // {
    //     // ARCameraBackground hooks into the camera command buffer and overwrites the final output
    //     // with black on Windows because XRCameraSubsystem has no frame data — disable it first.
    //     foreach (Camera cam in Camera.allCameras)
    //     {
    //         if (cam.GetComponent("ARCameraBackground") is Behaviour arCamBg)
    //         {
    //             arCamBg.enabled = false;
    //             Debug.Log("[Webcam] Disabled ARCameraBackground on " + cam.gameObject.name);
    //         }

    //         if (cam.clearFlags == CameraClearFlags.Nothing || cam.clearFlags == CameraClearFlags.Depth)
    //         {
    //             cam.clearFlags = CameraClearFlags.SolidColor;
    //             cam.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
    //         }
    //     }

    //     // Webcam feed only needed in Editor / standalone — AR Foundation handles it on device
    //     #if UNITY_EDITOR || UNITY_STANDALONE
    //     SetupWebcam();
    //     #endif
    // }

    // Starts the webcam and wires it to a full-screen RawImage.
    // Reuses the existing WebcamCanvas if one is already in the scene, otherwise creates one.
    void SetupWebcam()
    {
        WebCamDevice[] devices;
        try
        {
            devices = WebCamTexture.devices;
        }
        catch (System.Exception e)
        {
            // A connected camera (often virtual-camera software like OBS/DroidCam/iVCam)
            // can report a device name with invalid bytes, which crashes this call.
            Debug.LogError("[Webcam] Failed to enumerate webcam devices — a connected camera driver may be reporting an invalid name: " + e.Message);
            return;
        }

        if (devices.Length == 0)
        {
            Debug.LogWarning("[Webcam] No webcam found — scene will use solid background.");
            return;
        }

        for (int i = 0; i < devices.Length; i++)
            Debug.Log("[Webcam] Device " + i + ": " + devices[i].name);

        // Use the name chosen in the Inspector dropdown; fall back to device 0
        string deviceName = selectedDeviceName;
        if (string.IsNullOrEmpty(deviceName) || System.Array.FindIndex(devices, d => d.name == deviceName) < 0)
            deviceName = devices[0].name;

        Debug.Log("[Webcam] Using: " + deviceName);

        webcamTexture = new WebCamTexture(deviceName, targetWidth, targetHeight, targetFPS);

        // Find or build the canvas
        Canvas canvas;
        GameObject canvasObj = GameObject.Find("WebcamCanvas");
        if (canvasObj != null)
        {
            canvas = canvasObj.GetComponent<Canvas>();
            if (canvas == null)
            {
                // Canvas component missing — add it
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -100;
                if (canvasObj.GetComponent<CanvasScaler>() == null) canvasObj.AddComponent<CanvasScaler>();
                if (canvasObj.GetComponent<GraphicRaycaster>() == null) canvasObj.AddComponent<GraphicRaycaster>();
            }
        }
        else
        {
            canvasObj = new GameObject("WebcamCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // Ensure sorting order is correct even if canvas already existed
        canvas.sortingOrder = -100;

        // Find or create the RawImage that shows the feed
        backgroundImage = canvasObj.GetComponentInChildren<RawImage>();
        if (backgroundImage == null)
        {
            GameObject imgObj = new GameObject("WebcamImage");
            imgObj.transform.SetParent(canvasObj.transform, false);
            backgroundImage = imgObj.AddComponent<RawImage>();
            RectTransform rt = imgObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        backgroundImage.texture = webcamTexture;
        webcamTexture.Play();
        Debug.Log("[Webcam] Started: " + deviceName);
        // StartCoroutine(KeepAlive());
    }

    // // Returns true if the webcam feed is currently running
    // public bool IsRunning() => webcamTexture != null && webcamTexture.isPlaying;

    // Keeps the feed alive and corrects vertical flipping each half-second
    // System.Collections.IEnumerator KeepAlive()
    // {
    //     while (true)
    //     {
    //         yield return new WaitForSeconds(0.5f);

    //         if (webcamTexture == null) yield break;

    //         if (!webcamTexture.isPlaying)
    //         {
    //             webcamTexture.Play();
    //             Debug.Log("[Webcam] Restarted.");
    //         }

    //         if (backgroundImage != null)
    //         {
    //             if (backgroundImage.texture != webcamTexture)
    //                 backgroundImage.texture = webcamTexture;

    //             backgroundImage.uvRect = webcamTexture.videoVerticallyMirrored
    //                 ? new Rect(0, 1, 1, -1)
    //                 : new Rect(0, 0, 1, 1);
    //         }
    //     }
    // }

    // void OnDestroy()
    // {
    //     if (webcamTexture != null && webcamTexture.isPlaying)
    //     {
    //         webcamTexture.Stop();
    //         Debug.Log("[Webcam] Stopped.");
    //     }
    // }
}

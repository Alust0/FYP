using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Vuforia;

// Shows a pulsing scan frame + status text while Cloud Reco is actively
// searching, and flashes a "found" state briefly when a match comes in.
// Attach to the same GameObject as your CloudRecoBehaviour.
[RequireComponent(typeof(CloudRecoBehaviour))]
public class ScanningIndicatorUI : MonoBehaviour
{
    [Header("Vuforia")]
    [SerializeField] private CloudRecoBehaviour cloudReco;
    
    [Header("UI References (create these under a Canvas, see notes below)")]
    [SerializeField] private RectTransform scanFrame;   // a square/rect outline image
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private UnityEngine.UI.Image scanFrameImage;       // used to flash color on match

    [Header("Appearance")]
    [SerializeField] private Color scanningColor = Color.white;
    [SerializeField] private Color foundColor = Color.green;
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField] private float pulseMinAlpha = 0.3f;
    [SerializeField] private float pulseMaxAlpha = 1f;
    [SerializeField] private float foundFlashDuration = 1.2f;

    private bool isInitialized = false;
    private float foundFlashTimer = 0f;

    void Start()
    {
        if (cloudReco == null)
            cloudReco = GetComponent<CloudRecoBehaviour>();

        cloudReco.RegisterOnInitializedEventHandler(OnCloudRecoInitialized);
        cloudReco.RegisterOnNewSearchResultEventHandler(OnNewSearchResult);

        SetStatus("Initializing...");
    }

    void Update()
    {
        if (!isInitialized || scanFrameImage == null) return;

        if (foundFlashTimer > 0f)
        {
            // Holding the "found" color/state briefly after a match
            foundFlashTimer -= Time.deltaTime;
            if (foundFlashTimer <= 0f)
            {
                scanFrameImage.color = scanningColor;
                SetStatus("Scanning for card...");
            }
            return;
        }

        // Idle pulsing while actively scanning, gives visual confirmation
        // that recognition is still alive and searching each frame.
        float alpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha,
            (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f);

        Color c = scanFrameImage.color;
        c.a = alpha;
        scanFrameImage.color = c;
    }

    private void OnCloudRecoInitialized(CloudRecoBehaviour behaviour)
    {
        isInitialized = true;
        SetStatus("Scanning for card...");

        if (scanFrameImage != null)
            scanFrameImage.color = scanningColor;
    }

    private void OnNewSearchResult(CloudRecoBehaviour.CloudRecoSearchResult result)
    {
        SetStatus($"Card recognized: {result.TargetName}");

        if (scanFrameImage != null)
        {
            Color c = foundColor;
            c.a = 1f;
            scanFrameImage.color = c;
        }

        foundFlashTimer = foundFlashDuration;
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
            statusText.text = text;
    }
}
using UnityEngine;
using Vuforia;

// Forces the camera device to prioritize image quality/resolution over frame rate,
// keeps it in continuous autofocus, and bumps Unity's rendering quality to the
// highest level available — all of which improve the chances of a successful
// Cloud Reco match.
public class UpgradeCameraQuality : MonoBehaviour
{
    void Start()
    {
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);

        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;
    }

    private void OnVuforiaStarted()
    {
        var cameraDevice = VuforiaBehaviour.Instance.CameraDevice;
        cameraDevice.SetCameraMode(CameraMode.MODE_OPTIMIZE_QUALITY);

        // Cards are held close to the lens, so prefer macro (close-up) focus.
        // Fall back to continuous autofocus if the device doesn't support macro.
        if (!cameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_MACRO))
        {
            Debug.Log("Macro focus is not supported on this device, falling back to continuous autofocus.");
            if (!cameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_CONTINUOUSAUTO))
            {
                Debug.Log("Continuous autofocus is not supported on this device.");
            }
        }
    }
}

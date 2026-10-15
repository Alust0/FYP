using UnityEngine;
using Vuforia;

/* Prioritizes camera image quality over frame rate, keeps continuous autofocus on, and sets
 Unity's highest quality level, all to improve the chances of a Cloud Reco match. */
public class UpgradeCameraQuality : MonoBehaviour
{
    void Start()
    {
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);

        // Vuforia may already be running, in which case OnVuforiaStarted has fired without us
        if (VuforiaApplication.Instance.IsRunning)
            OnVuforiaStarted();

        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;
    }

    void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= OnVuforiaStarted;
    }

    private void OnVuforiaStarted()
    {
        var cameraDevice = VuforiaBehaviour.Instance.CameraDevice;

        bool modeSet = cameraDevice.SetCameraMode(CameraMode.MODE_OPTIMIZE_QUALITY);
        Debug.Log($"[CameraDiag] SetCameraMode(MODE_OPTIMIZE_QUALITY) returned: {modeSet}");

        /* Continuous autofocus keeps cards sharp at tabletop distance. Macro mode blurs a
         playmat viewed from above; trigger-auto is the fallback when continuous AF is missing. */
        bool focusSet = cameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_CONTINUOUSAUTO);
        Debug.Log($"[CameraDiag] SetFocusMode(CONTINUOUSAUTO) returned: {focusSet}");

        if (!focusSet)
        {
            bool triggerSet = cameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_TRIGGERAUTO);
            Debug.Log($"[CameraDiag] Fallback SetFocusMode(TRIGGERAUTO) returned: {triggerSet}");

            // Both fail on a PC webcam (Vuforia can't control its focus), so ask the driver directly
            if (!triggerSet)
                WebcamAutoFocus.Enable();
        }

        // Confirm what resolution Vuforia is actually using right now
        var videoMode = cameraDevice.GetVideoMode();
        Debug.Log($"[CameraDiag] Active video mode: {videoMode.width}x{videoMode.height}, framerate: {videoMode.frameRate}");
    }
}

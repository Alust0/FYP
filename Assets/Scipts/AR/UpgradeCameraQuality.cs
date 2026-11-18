using UnityEngine;
using Vuforia;

// Camera quality over frame rate, plus autofocus
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

        // Continuous autofocus, else trigger-auto
        bool focusSet = cameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_CONTINUOUSAUTO);
        Debug.Log($"[CameraDiag] SetFocusMode(CONTINUOUSAUTO) returned: {focusSet}");

        if (!focusSet)
        {
            bool triggerSet = cameraDevice.SetFocusMode(FocusMode.FOCUS_MODE_TRIGGERAUTO);
            Debug.Log($"[CameraDiag] Fallback SetFocusMode(TRIGGERAUTO) returned: {triggerSet}");

            // Ask the webcam driver directly (PC webcams)
            if (!triggerSet)
                WebcamAutoFocus.Enable(CameraChoice.Current);
        }

        // Confirm what resolution Vuforia is actually using right now
        var videoMode = cameraDevice.GetVideoMode();
        Debug.Log($"[CameraDiag] Active video mode: {videoMode.width}x{videoMode.height}, framerate: {videoMode.frameRate}");
    }
}

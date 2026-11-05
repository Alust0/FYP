using System;
using UnityEngine;

/* The in-game Settings menu's choices, saved on this PC (PlayerPrefs). Camera brightness is only
 here for UWP builds; elsewhere the webcam's driver keeps it (WebcamAutoFocus). */
public static class GameSettings
{
    const string FontScaleKey = "Settings.FontScale";
    const string RotateViewKey = "Settings.RotateView";
    const string CameraBrightnessKey = "Settings.CameraBrightness";
    const string CameraUpsideDownKey = "Settings.CameraUpsideDown";
    const string FullScreenKey = "Settings.FullScreen";

    public const float MinFontScale = 0.8f;
    public const float MaxFontScale = 1.3f;

    // After any setting changes
    public static event Action Changed;

    // Multiplies the HUD's text sizes
    public static float FontScale
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(FontScaleKey, 1f), MinFontScale, MaxFontScale);
        set
        {
            PlayerPrefs.SetFloat(FontScaleKey, Mathf.Clamp(value, MinFontScale, MaxFontScale));
            Changed?.Invoke();
        }
    }

    /* The camera area turned a quarter turn (RotatedView). Per editor window: Multiplayer Play Mode
     windows share PlayerPrefs, so one key turned both players' views */
    static string sRotateViewWindowKey; // read every frame, so worked out once
    static string RotateViewWindowKey => sRotateViewWindowKey ??=
        RotateViewKey + (CameraChoice.WindowId == "Main" ? "" : "." + CameraChoice.WindowId);
    public static bool RotateView
    {
        get => PlayerPrefs.GetInt(RotateViewWindowKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(RotateViewWindowKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    /* Builds' camera brightness, 0–1 with 0.5 unchanged, applied to the picture by BuildWebcamDriver.
     Per window like RotateView */
    static string sCameraBrightnessWindowKey; // read every frame, so worked out once
    static string CameraBrightnessWindowKey => sCameraBrightnessWindowKey ??=
        CameraBrightnessKey + (CameraChoice.WindowId == "Main" ? "" : "." + CameraChoice.WindowId);
    public static float CameraBrightness
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(CameraBrightnessWindowKey, 0.5f));
        set
        {
            PlayerPrefs.SetFloat(CameraBrightnessWindowKey, Mathf.Clamp01(value));
            Changed?.Invoke();
        }
    }

    /* Builds' webcam picture turned 180° before Vuforia sees it (BuildWebcamDriver), for a webcam
     mounted upside down. Per window like RotateView */
    static string sCameraUpsideDownWindowKey; // read every frame, so worked out once
    static string CameraUpsideDownWindowKey => sCameraUpsideDownWindowKey ??=
        CameraUpsideDownKey + (CameraChoice.WindowId == "Main" ? "" : "." + CameraChoice.WindowId);
    public static bool CameraUpsideDown
    {
        get => PlayerPrefs.GetInt(CameraUpsideDownWindowKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(CameraUpsideDownWindowKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    /* Builds fill the screen (borderless) or run in a window, applied by WindowMode. Not per window:
     the editor's Play Mode windows ignore it */
    public static bool HasFullScreen => PlayerPrefs.HasKey(FullScreenKey);
    public static bool FullScreen
    {
        get => PlayerPrefs.GetInt(FullScreenKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(FullScreenKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /* The Energy Generator: the Energy count, its charge each turn and the Energy Blast draw. Off hides all three.
     Not saved or in the menu: BoardLayout's Energy box in the Inspector sets it. */
    public static bool EnergyEnabled
    {
        get => sEnergyEnabled;
        set
        {
            if (sEnergyEnabled == value) return;
            sEnergyEnabled = value;
            Changed?.Invoke();
        }
    }
    static bool sEnergyEnabled = true;
}

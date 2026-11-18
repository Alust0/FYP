using System;
using UnityEngine;

// In-game Settings choices, saved in PlayerPrefs
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

    // Camera view turned a quarter (RotatedView), per editor window
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

    // Build camera brightness, 0-1 (0.5 = unchanged), per window
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

    // Build webcam turned 180° for an upside-down mount, per window
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

    // Full screen or windowed (builds only)
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

    // Energy count, charge and Energy Blast; set by BoardLayout's Energy box
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

using System;
using UnityEngine;

/* The in-game Settings menu's choices, saved on this PC (PlayerPrefs). Camera brightness isn't
 here: the webcam's driver keeps it (WebcamAutoFocus). */
public static class GameSettings
{
    const string FontScaleKey = "Settings.FontScale";
    const string RotateViewKey = "Settings.RotateView";

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

using System;
using UnityEngine;

/* The in-game Settings menu's choices, saved on this PC (PlayerPrefs). Camera brightness isn't
 here: the webcam's driver keeps it (WebcamAutoFocus). */
public static class GameSettings
{
    const string FontScaleKey = "Settings.FontScale";
    const string EnergyKey = "Settings.Energy";

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

    // The Energy Generator: the Energy count, its charge each turn and the Energy Blast draw. Off hides all three
    public static bool EnergyEnabled
    {
        get => PlayerPrefs.GetInt(EnergyKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(EnergyKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }
}

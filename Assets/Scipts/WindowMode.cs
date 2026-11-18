using UnityEngine;
using UnityEngine.InputSystem;

// Switches builds between full screen and windowed (F11)
 public class WindowMode : MonoBehaviour
{
    const float WindowedSize = 0.75f; // a window's share of the screen's width and height

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (GameSettings.HasFullScreen) Apply();

        GameObject obj = new GameObject("WindowMode");
        DontDestroyOnLoad(obj);
        obj.AddComponent<WindowMode>();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f11Key.wasPressedThisFrame) Toggle();
    }

    public static string Label => GameSettings.FullScreen ? "Display: Full screen" : "Display: Windowed";

    public static void Toggle()
    {
        GameSettings.FullScreen = !GameSettings.FullScreen;
        Apply();
    }

    // Does nothing in the editor; UWP ignores the window size and keeps the one the player set
    static void Apply()
    {
        bool full = GameSettings.FullScreen;
        if (Screen.fullScreen == full) return;

        int width = Display.main.systemWidth, height = Display.main.systemHeight;
        if (full) Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
        else Screen.SetResolution(Mathf.RoundToInt(width * WindowedSize), Mathf.RoundToInt(height * WindowedSize), FullScreenMode.Windowed);
    }
}

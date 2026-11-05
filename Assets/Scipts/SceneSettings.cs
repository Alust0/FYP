using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/* The Settings gear and menu for every scene outside a match (Ingame has its own SettingsMenu):
 display mode, the match's text size and Quit Game. Scenes built in code put the gear in their
 header with AddButton; any other scene (Login, SignUp, Menu) gets one in its top-right corner.
 Esc opens and closes it too. One per scene, made when the scene loads. */
public partial class SceneSettings : MonoBehaviour
{
    static SceneSettings sCurrent;

    bool mHasButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) => Ensure();
        Ensure(); // the first scene loaded before this ran
    }

    // This scene's, made if it has none yet
    static SceneSettings Ensure()
    {
        if (sCurrent == null || sCurrent.gameObject.scene != SceneManager.GetActiveScene())
            sCurrent = new GameObject("SceneSettings").AddComponent<SceneSettings>();
        return sCurrent;
    }

    // A gear button under parent that opens the menu; the caller places it
    public static Button AddButton(Transform parent)
    {
        SceneSettings settings = Ensure();
        settings.mHasButton = true;
        return MenuUI.AddIconButton(parent, "Settings", settings.Open, MenuUI.PanelColor);
    }

    IEnumerator Start()
    {
        yield return null; // after every Start, so scenes built in code have added their gear

        if (FindFirstObjectByType<SettingsMenu>() != null)
        {
            Destroy(gameObject); // a match: its own menu has the gear and Esc
            yield break;
        }
        if (!mHasButton) BuildCornerButton();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (mOverlay != null && mOverlay.activeSelf) Close();
            else Open();
        }

        // Every frame while open, so F11 shows straight away
        if (mOverlay != null && mOverlay.activeSelf) mWindowLabel.text = WindowMode.Label;
    }

    public void Open()
    {
        if (mOverlay == null) BuildMenu();
        mOverlay.SetActive(true);
        mFontSlider.SetValueWithoutNotify(GameSettings.FontScale);
        ShowFontScale();
    }

    public void Close()
    {
        if (mOverlay != null) mOverlay.SetActive(false);
    }

    void OnFontScaleChanged(float scale)
    {
        GameSettings.FontScale = scale;
        ShowFontScale();
    }

    void ShowFontScale() => mFontLabel.text = "Match text size: " + Mathf.RoundToInt(GameSettings.FontScale * 100f) + "%";
}

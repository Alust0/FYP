using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/* In-game Settings: opened by the Settings button or Esc. Resume, Calibrate Board (BoardCalibrator),
 text size, camera brightness, the Energy feature on or off (GameSettings), and Leave Match back to
 the menu, which needs a second tap so it isn't hit by accident. Online, leaving tells the opponent
 (OnlineMatch marks the room closed). Built by UISetup. */
public class SettingsMenu : MonoBehaviour
{
    public GameObject overlay;
    public TextMeshProUGUI leaveLabel;
    public Image leaveBackground;
    public string menuSceneName = "Menu";

    public Transform scaledRoot; // the HUD whose text the text size scales, all but this menu
    public Slider fontSlider;
    public TextMeshProUGUI fontLabel;
    public Slider brightnessSlider;
    public TextMeshProUGUI brightnessLabel;
    public Toggle energyToggle;

    const float ConfirmSeconds = 3f;
    static readonly Color LeaveColor = new Color(0.55f, 0.15f, 0.15f, 0.95f);
    static readonly Color ConfirmColor = new Color(0.85f, 0.2f, 0.2f, 1f);

    const float BrightnessDelay = 0.2f; // the driver is set once the slider rests this long, not every frame of a drag

    bool mConfirmingLeave;
    Coroutine mConfirmTimeout;
    float mBrightnessMovedAt = -1f; // a brightness still to send to the driver; -1 when none
    readonly Dictionary<TMP_Text, Vector3> mBaseSizes = new Dictionary<TMP_Text, Vector3>(); // (size, auto-size min, max) at scale 1

    string LeaveText => MatchSession.IsOnline ? "Leave Match" : "Exit to Menu";

    void Start()
    {
        fontSlider.minValue = GameSettings.MinFontScale;
        fontSlider.maxValue = GameSettings.MaxFontScale;
        fontSlider.SetValueWithoutNotify(GameSettings.FontScale);
        fontSlider.onValueChanged.AddListener(OnFontScaleChanged);
        ApplyFontScale();

        brightnessSlider.onValueChanged.AddListener(_ =>
        {
            mBrightnessMovedAt = Time.unscaledTime;
            ShowBrightness(true);
        });

        energyToggle.SetIsOnWithoutNotify(GameSettings.EnergyEnabled);
        energyToggle.onValueChanged.AddListener(on => GameSettings.EnergyEnabled = on);

        Close();
    }

    void Update()
    {
        if (mBrightnessMovedAt >= 0f && Time.unscaledTime - mBrightnessMovedAt >= BrightnessDelay)
        {
            mBrightnessMovedAt = -1f;
            if (!WebcamAutoFocus.TrySetBrightness(CameraChoice.Current, brightnessSlider.value))
                Debug.LogWarning("[SettingsMenu] Couldn't set the brightness of " + CameraChoice.Current);
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (overlay.activeSelf) Close();
            else Open();
        }
    }

    public void Open()
    {
        overlay.SetActive(true);
        ResetLeave();

        // Read each time: the camera, or its brightness in another program, may have changed
        bool adjustable = WebcamAutoFocus.TryGetBrightness(CameraChoice.Current, out float brightness);
        brightnessSlider.SetValueWithoutNotify(brightness);
        brightnessSlider.interactable = adjustable;
        ShowBrightness(adjustable);
    }

    void ShowBrightness(bool adjustable) => brightnessLabel.text = adjustable
        ? "Camera brightness: " + Mathf.RoundToInt(brightnessSlider.value * 100f) + "%"
        : "Camera brightness: not adjustable on this camera";

    void OnFontScaleChanged(float scale)
    {
        GameSettings.FontScale = scale;
        ApplyFontScale();
    }

    // Sizes every HUD text by the saved text size, from its size when first seen
    void ApplyFontScale()
    {
        float scale = GameSettings.FontScale;
        fontLabel.text = "Text size: " + Mathf.RoundToInt(scale * 100f) + "%";
        if (scaledRoot == null) return;

        foreach (TMP_Text text in scaledRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.transform.IsChildOf(overlay.transform)) continue; // the menu keeps its size, so it always fits its box
            if (!mBaseSizes.TryGetValue(text, out Vector3 size))
                mBaseSizes[text] = size = new Vector3(text.fontSize, text.fontSizeMin, text.fontSizeMax);

            text.fontSize = size.x * scale;
            text.fontSizeMin = size.y * scale;
            text.fontSizeMax = size.z * scale;
        }
    }

    public void Close()
    {
        overlay.SetActive(false);
        ResetLeave();
    }

    public void Calibrate()
    {
        Close();
        BoardCalibrator calibrator = FindFirstObjectByType<BoardCalibrator>();
        if (calibrator != null) calibrator.ToggleCalibration();
        else Debug.LogWarning("[SettingsMenu] No BoardCalibrator in the scene.");
    }

    public void Leave()
    {
        if (!mConfirmingLeave)
        {
            mConfirmingLeave = true;
            leaveLabel.text = MatchSession.IsOnline ? "Tap again to leave (opponent is told)" : "Tap again to exit";
            leaveBackground.color = ConfirmColor;
            mConfirmTimeout = StartCoroutine(CancelConfirmLater());
            return;
        }

        SceneManager.LoadScene(menuSceneName);
    }

    IEnumerator CancelConfirmLater()
    {
        yield return new WaitForSeconds(ConfirmSeconds);
        ResetLeave();
    }

    void ResetLeave()
    {
        if (mConfirmTimeout != null) StopCoroutine(mConfirmTimeout);
        mConfirmTimeout = null;
        mConfirmingLeave = false;
        leaveLabel.text = LeaveText;
        leaveBackground.color = LeaveColor;
    }
}

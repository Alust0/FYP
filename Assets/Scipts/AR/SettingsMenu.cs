using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/* In-game Settings: opened by the Settings button or Esc. Resume, Calibrate Board (BoardCalibrator),
 and Leave Match back to the menu, which needs a second tap so it isn't hit by accident. Online,
 leaving tells the opponent (OnlineMatch marks the room closed). Built by UISetup. */
public class SettingsMenu : MonoBehaviour
{
    public GameObject overlay;
    public TextMeshProUGUI leaveLabel;
    public Image leaveBackground;
    public string menuSceneName = "Menu";

    const float ConfirmSeconds = 3f;
    static readonly Color LeaveColor = new Color(0.55f, 0.15f, 0.15f, 0.95f);
    static readonly Color ConfirmColor = new Color(0.85f, 0.2f, 0.2f, 1f);

    bool mConfirmingLeave;
    Coroutine mConfirmTimeout;

    string LeaveText => MatchSession.IsOnline ? "Leave Match" : "Exit to Menu";

    void Start() => Close();

    void Update()
    {
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

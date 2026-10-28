using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/* In-match list of every notification so far (MatchLog), newest at the bottom, so one that slid
 away too soon can be read again. Opened from the Settings menu or with L; built by UISetup. */
public class MatchLogPanel : MonoBehaviour
{
    public GameObject panel;
    public TextMeshProUGUI listText;
    public ScrollRect scroll;
    public Key toggleKey = Key.L;
    [Tooltip("Older notifications are only in the log file.")]
    public int maxShown = 150;

    void OnEnable() => MatchLog.OnNotification += HandleNotification;
    void OnDisable() => MatchLog.OnNotification -= HandleNotification;

    void Start() => Close();

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) Toggle();
    }

    public void Toggle()
    {
        if (panel.activeSelf) Close();
        else Open();
    }

    public void Open()
    {
        panel.SetActive(true);
        Refresh();
    }

    public void Close() => panel.SetActive(false);

    void HandleNotification(string entry)
    {
        if (panel.activeSelf) Refresh();
    }

    // Shows the latest maxShown notifications, scrolled to the newest
    void Refresh()
    {
        var all = MatchLog.Notifications;
        listText.text = all.Count == 0 ? "No notifications yet." : string.Join("\n", all.Skip(Mathf.Max(0, all.Count - maxShown)));
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;
    }
}

using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if !UNITY_WSA
using Firebase.Auth;
#endif

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private Button logoutButton;
    [SerializeField] private string loginSceneName = "Login";

    void Start()
    {
        if (logoutButton != null) logoutButton.onClick.AddListener(Logout);
    }

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    // Signs out of Unity and Firebase and clears the cached session so auto-login won't run
    public void Logout()
    {
        AccountLock.Release(); // lets the account log in on another copy straight away
        // Skipped when the menu scene was opened directly in the editor without logging in
        if (UnityServices.State == ServicesInitializationState.Initialized)
            AuthenticationService.Instance.SignOut(true);
#if !UNITY_WSA
        FirebaseAuth.DefaultInstance.SignOut();
#endif
        SceneManager.LoadScene(loginSceneName);
    }
}

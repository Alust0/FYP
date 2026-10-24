using Firebase.Auth;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        // Skipped when the menu scene was opened directly in the editor without logging in
        if (UnityServices.State == ServicesInitializationState.Initialized)
        {
            AuthenticationService.Instance.SignOut(true);
            PlayerAccountService.Instance.SignOut();
        }
        FirebaseAuth.DefaultInstance.SignOut();
        SceneManager.LoadScene(loginSceneName);
    }
}

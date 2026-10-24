using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/* Login scene: Unity Player Accounts sign-in (browser), then a Firebase anonymous
 sign-in so Firestore rules allow access, then loads the menu. */
public class LoginController : MonoBehaviour
{
    [SerializeField] private Button loginButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private string menuSceneName = "Menu";

    bool subscribed;

    async void Start()
    {
        loginButton.onClick.AddListener(OnLoginClicked);
        exitButton.onClick.AddListener(Application.Quit);
        SetBusy(true, "Starting...");

        await Run(async () =>
        {
            await UnityServices.InitializeAsync();
            PlayerAccountService.Instance.SignedIn += OnPlayerAccountSignedIn;
            PlayerAccountService.Instance.SignInFailed += OnPlayerAccountSignInFailed;
            subscribed = true;

            if (AuthenticationService.Instance.IsSignedIn)
            {
                await FinishLoginAsync();
            }
            else if (AuthenticationService.Instance.SessionTokenExists)
            {
                // Restores the cached Unity session without the browser
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                await FinishLoginAsync();
            }
            else
            {
                SetBusy(false, "");
            }
        });
    }

    void OnDestroy()
    {
        if (!subscribed) return;
        PlayerAccountService.Instance.SignedIn -= OnPlayerAccountSignedIn;
        PlayerAccountService.Instance.SignInFailed -= OnPlayerAccountSignInFailed;
    }

    async void OnLoginClicked()
    {
        await Run(async () =>
        {
            if (PlayerAccountService.Instance.IsSignedIn)
            {
                await SignInWithUnityAsync();
                return;
            }

            SetBusy(true, "Opening browser...");
            await PlayerAccountService.Instance.StartSignInAsync();
            // Re-enabled so the player can retry if they close the browser
            SetBusy(false, "Finish signing in in your browser...");
        });
    }

    async void OnPlayerAccountSignedIn()
    {
        await Run(SignInWithUnityAsync);
    }

    void OnPlayerAccountSignInFailed(RequestFailedException e)
    {
        Fail(e, "Sign-in didn't finish. Please try again.");
    }

    async Task SignInWithUnityAsync()
    {
        SetBusy(true, "Signing in...");
        await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
        await FinishLoginAsync();
    }

    async Task FinishLoginAsync()
    {
        SetBusy(true, "Connecting...");
        DependencyStatus status = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (status != DependencyStatus.Available)
        {
            Debug.LogError("[LoginController] Firebase dependencies unavailable: " + status);
            Fail(null, "Couldn't connect to the game database. Please try again.");
            return;
        }

        FirebaseAuth auth = FirebaseAuth.DefaultInstance;
        if (auth.CurrentUser == null)
            await auth.SignInAnonymouslyAsync();

        SceneManager.LoadScene(menuSceneName);
    }

    // Runs a login step and turns any failure into a friendly status message
    async Task Run(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (PlayerAccountsException e)
        {
            Fail(e, "Unity account sign-in failed. Please try again.");
        }
        catch (AuthenticationException e)
        {
            Fail(e, "Couldn't log in. Please try again.");
        }
        catch (RequestFailedException e)
        {
            Fail(e, "Can't reach the login server. Check your internet connection.");
        }
        catch (FirebaseException e)
        {
            Fail(e, "Couldn't connect to the game database. Please try again.");
        }
    }

    void Fail(Exception e, string message)
    {
        if (e != null) Debug.LogException(e);
        SetBusy(false, message);
    }

    void SetBusy(bool busy, string status)
    {
        if (this == null) return; // scene already unloaded
        loginButton.interactable = !busy;
        statusText.text = status;
    }
}

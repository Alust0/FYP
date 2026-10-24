using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if !UNITY_WSA
using Firebase;
using Firebase.Auth;
#endif

/* Login scene: Unity username/password sign-in (works on UWP, no browser), then a Firebase
 anonymous sign-in so Firestore rules allow access, then loads the menu. */
public class LoginController : MonoBehaviour
{
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button signUpButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private string menuSceneName = "Menu";

    // Unity's rules for username/password accounts
    static readonly Regex UsernameRule = new Regex(@"^[A-Za-z0-9.\-@_]{3,20}$");
    static readonly Regex PasswordRule = new Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,30}$");

    async void Start()
    {
        if (usernameInput == null || passwordInput == null || loginButton == null ||
            signUpButton == null || exitButton == null || statusText == null)
        {
            Debug.LogError("[LoginController] Fill every slot in the Inspector (inputs, buttons and Status Text).", this);
            return;
        }

        passwordInput.contentType = TMP_InputField.ContentType.Password;
        passwordInput.ForceLabelUpdate();
        loginButton.onClick.AddListener(OnLoginClicked);
        signUpButton.onClick.AddListener(OnSignUpClicked);
        exitButton.onClick.AddListener(Application.Quit);
        SetBusy(true, "Starting...");

        await Run("Your saved login expired. Please log in again.", async () =>
        {
            await UnityServices.InitializeAsync();

            if (AuthenticationService.Instance.IsSignedIn)
            {
                await FinishLoginAsync();
            }
            else if (AuthenticationService.Instance.SessionTokenExists)
            {
                // Restores the cached session from the last login
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                await FinishLoginAsync();
            }
            else
            {
                SetBusy(false, "");
            }
        });
    }

    async void OnLoginClicked()
    {
        if (!ReadInputs(out string username, out string password)) return;

        await Run("Wrong username or password.", async () =>
        {
            SetBusy(true, "Logging in...");
            // Unity login already done if only the Firebase step failed last time
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
            await FinishLoginAsync();
        });
    }

    async void OnSignUpClicked()
    {
        if (!ReadInputs(out string username, out string password)) return;

        if (!UsernameRule.IsMatch(username))
        {
            SetBusy(false, "Username must be 3-20 characters: letters, numbers, . - @ _");
            return;
        }
        if (!PasswordRule.IsMatch(password))
        {
            SetBusy(false, "Password needs 8-30 characters with an uppercase letter, a lowercase letter, a number and a symbol.");
            return;
        }

        await Run("Couldn't create the account. That username may already be taken.", async () =>
        {
            SetBusy(true, "Creating account...");
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
            await FinishLoginAsync();
        });
    }

    bool ReadInputs(out string username, out string password)
    {
        username = usernameInput.text.Trim();
        password = passwordInput.text;
        if (username.Length > 0 && password.Length > 0) return true;

        SetBusy(false, "Enter your username and password.");
        return false;
    }

    async Task FinishLoginAsync()
    {
        SetBusy(true, "Connecting...");
        if (await SignInFirebaseAsync())
            SceneManager.LoadScene(menuSceneName);
    }

#if UNITY_WSA
    // Firebase's Unity SDK doesn't support UWP; CardFirestoreDatabase reads Firestore over REST there
    Task<bool> SignInFirebaseAsync() => Task.FromResult(true);
#else
    async Task<bool> SignInFirebaseAsync()
    {
        DependencyStatus status = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (status != DependencyStatus.Available)
        {
            Debug.LogError("[LoginController] Firebase dependencies unavailable: " + status);
            SetBusy(false, "Couldn't connect to the game database. Please try again.");
            return false;
        }

        FirebaseAuth auth = FirebaseAuth.DefaultInstance;
        if (auth.CurrentUser == null)
            await auth.SignInAnonymouslyAsync();
        return true;
    }
#endif

    // Runs a login step and turns any failure into a friendly status message
    async Task Run(string authFailMessage, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (AuthenticationException e)
        {
            Fail(e, authFailMessage);
        }
        catch (RequestFailedException e) when (e.ErrorCode == CommonErrorCodes.TransportError)
        {
            Fail(e, "Can't reach the login server. Check your internet connection.");
        }
        catch (RequestFailedException e)
        {
            Fail(e, authFailMessage);
        }
#if !UNITY_WSA
        catch (FirebaseException e)
        {
            Fail(e, "Couldn't connect to the game database. Please try again.");
        }
#endif
    }

    void Fail(Exception e, string message)
    {
        Debug.LogException(e);
        SetBusy(false, message);
    }

    void SetBusy(bool busy, string status)
    {
        if (this == null) return; // scene already unloaded
        loginButton.interactable = !busy;
        signUpButton.interactable = !busy;
        statusText.text = status;
    }
}

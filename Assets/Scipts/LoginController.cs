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
                SetBusy(false, AccountLock.KickMessage ?? "");
                AccountLock.KickMessage = null;
            }
        });
    }

    async void OnLoginClicked()
    {
        if (!ReadInputs(out string username, out string password)) return;

        await Run("Wrong username or password.", async () =>
        {
            SetBusy(true, "Logging in...");
            SignOutIfOtherAccount(username);
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
            SignOutIfOtherAccount(username);
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

    // A session left over from a failed attempt belongs to whoever typed last, not necessarily this username
    static void SignOutIfOtherAccount(string username)
    {
        var auth = AuthenticationService.Instance;
        if (auth.IsSignedIn && !string.Equals(auth.PlayerInfo?.Username, username, StringComparison.OrdinalIgnoreCase))
            auth.SignOut();
    }

    async Task FinishLoginAsync()
    {
        SetBusy(true, "Connecting...");
        await SyncPlayerNameAsync();
        if (!await SignInFirebaseAsync()) return;

        // One running copy per account: refused while another copy is logged in
        string problem = await AccountLock.ClaimAsync(PlayerSession.Id);
        if (problem != null)
        {
            AuthenticationService.Instance.SignOut(); // so Login uses the typed account, not this one
            SetBusy(false, problem);
            return;
        }
        SceneManager.LoadScene(menuSceneName);
    }

    /* Makes the dashboard's player name match the login username. A restored session may not carry
     the username, so it's fetched; failures only log, since the name is cosmetic. */
    static async Task SyncPlayerNameAsync()
    {
        var auth = AuthenticationService.Instance;
        try
        {
            if (string.IsNullOrEmpty(auth.PlayerInfo?.Username))
                await auth.GetPlayerInfoAsync();

            string username = auth.PlayerInfo?.Username;
            if (string.IsNullOrEmpty(username)) return;

            // Unity appends "#1234" to player names
            string current = await auth.GetPlayerNameAsync(false);
            if (!string.Equals(PlayerSession.StripSuffix(current), username, StringComparison.Ordinal))
                await auth.UpdatePlayerNameAsync(username);
        }
        catch (Exception e) when (e is AuthenticationException || e is RequestFailedException)
        {
            Debug.LogWarning("[LoginController] Couldn't set player name: " + e.Message);
        }
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
        // Wrong password, taken username and no internet are expected, so they only warn
        catch (AuthenticationException e)
        {
            Warn(e, authFailMessage);
        }
        catch (RequestFailedException e) when (e.ErrorCode == CommonErrorCodes.TransportError)
        {
            Warn(e, "Can't reach the login server. Check your internet connection.");
        }
        catch (RequestFailedException e)
        {
            Warn(e, authFailMessage);
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

    void Warn(Exception e, string message)
    {
        // Inner exception holds the server's reason, e.g. WRONG_USERNAME_PASSWORD
        Debug.LogWarning($"[LoginController] {message} ({e.InnerException?.Message ?? e.Message})");
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

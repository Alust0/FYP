using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if !UNITY_WSA
using Firebase;
using Firebase.Auth;
#endif

/* Login and SignUp scenes (same controller; the scene name picks the page): Unity username/password
 sign-in (works on UWP, no browser), then a Firebase anonymous sign-in so Firestore rules allow access,
 then loads the menu. On the login page Sign Up opens the sign-up page; on the sign-up page Login goes
 back. Sign-up also takes an email, one account per email, proved by a mailed code (EmailOtp):
 Realtime Database emails/{hash} holds {username, playerId} (so the login page takes the email in
 place of the username) and the player's Cloud Save key "email" keeps it with the account. */
public partial class LoginController : MonoBehaviour
{
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_InputField emailInput; // sign-up page; made from the username box when empty
    [SerializeField] private TMP_InputField confirmPasswordInput; // sign-up page only; optional
    [SerializeField] private Button loginButton;
    [SerializeField] private Button signUpButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private string menuSceneName = "Menu";
    [SerializeField] private string loginSceneName = "Login";
    [SerializeField] private string signUpSceneName = "SignUp";
    [SerializeField] private float welcomeSeconds = 1.5f; // "Connected! Welcome" shows this long before the menu
    [SerializeField] private bool requireEmailCode = true; // sign-up mails a code to prove the email

    // Unity's rules for username/password accounts; a username shaped like an email is refused too
    static readonly Regex UsernameRule = new Regex(@"^[A-Za-z0-9.\-@_]{3,20}$");
    static readonly Regex PasswordRule = new Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,30}$");
    static readonly Regex EmailRule = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    const string Emails = "emails";
    const string EmailKey = "email"; // Cloud Save

    static readonly Color SuccessColor = new Color(0.35f, 0.9f, 0.45f);
    static readonly Color ErrorColor = new Color(1f, 0.4f, 0.4f);

    bool mSignUpPage;
    Color mStatusColor; // the scene's status colour, for progress messages
    EmailOtp mOtp; // the code mailed for the sign-up's email
    string mVerifiedEmail; // proved on this visit, so a taken username afterwards doesn't need a new code

    async void Start()
    {
        if (usernameInput == null || passwordInput == null || loginButton == null ||
            signUpButton == null || exitButton == null || statusText == null)
        {
            Debug.LogError("[LoginController] Fill every slot in the Inspector (inputs, buttons and Status Text).", this);
            return;
        }

        mStatusColor = statusText.color;
        AddTitle();
        mSignUpPage = SceneManager.GetActiveScene().name == signUpSceneName;
        if (mSignUpPage && emailInput == null) emailInput = MakeEmailInput();
        if (!mSignUpPage) SetPlaceholder(usernameInput, "Username or email");
        // Typed text grows from the middle, like the placeholders
        foreach (TMP_InputField input in new[] { usernameInput, passwordInput, emailInput, confirmPasswordInput })
            if (input != null && input.textComponent != null) input.textComponent.alignment = TextAlignmentOptions.Center;

        passwordInput.contentType = TMP_InputField.ContentType.Password;
        passwordInput.ForceLabelUpdate();
        if (confirmPasswordInput != null)
        {
            confirmPasswordInput.contentType = TMP_InputField.ContentType.Password;
            confirmPasswordInput.ForceLabelUpdate();
        }
        loginButton.onClick.AddListener(mSignUpPage ? () => SceneManager.LoadScene(loginSceneName) : OnLoginClicked);
        signUpButton.onClick.AddListener(mSignUpPage ? OnSignUpClicked : () => SceneManager.LoadScene(signUpSceneName));
        exitButton.onClick.AddListener(Application.Quit);
        SetBusy(true, "Starting...");

        await Run("Your saved login expired. Please log in again.", async () =>
        {
            await UnityServices.InitializeAsync();

            if (mSignUpPage)
            {
                SetBusy(false, "");
            }
            else if (AuthenticationService.Instance.IsSignedIn)
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

        await Run("Wrong username/email or password.", async () =>
        {
            SetBusy(true, "Logging in...");

            // An email is swapped for its account's username; one not on file may be an old username with "@"
            if (EmailRule.IsMatch(username))
            {
                RtdbRest.Result entry = await Rtdb(done => RtdbRest.Get(EmailPath(username), done));
                if (entry.succeeded) username = entry.fields.GetString("username");
                else if (entry.status != 404) Debug.LogWarning("[LoginController] Email lookup failed: " + entry.error);
            }

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
        string email = emailInput.text.Trim().ToLowerInvariant();

        if (!EmailRule.IsMatch(email))
        {
            SetBusy(false, "Enter a valid email address.");
            return;
        }
        if (!UsernameRule.IsMatch(username))
        {
            SetBusy(false, "Username must be 3-20 characters long and can only use letters, numbers and . - _ @");
            return;
        }
        // The login page would read it as an email
        if (EmailRule.IsMatch(username))
        {
            SetBusy(false, "Username can't be an email address. Put your email in the Email box.");
            return;
        }
        if (!PasswordRule.IsMatch(password))
        {
            SetBusy(false, "Password needs 8-30 characters with an uppercase letter, a lowercase letter, a number and a symbol.");
            return;
        }
        if (confirmPasswordInput != null && confirmPasswordInput.text != password)
        {
            SetBusy(false, "The passwords don't match.");
            return;
        }

        await Run("Couldn't create the account. That username may already be taken.", async () =>
        {
            SetBusy(true, "Checking email...");
            RtdbRest.Result entry = await Rtdb(done => RtdbRest.Get(EmailPath(email), done));
            if (entry.succeeded && !IsOwnEntry(entry, username))
            {
                SetBusy(false, "That email is already used by another account.");
                return;
            }
            if (!entry.succeeded && entry.status != 404)
            {
                Debug.LogWarning("[LoginController] Email check failed: " + entry.error);
                SetBusy(false, "Couldn't check the email. Check your internet connection and try again.");
                return;
            }

            // Not proved yet: mail a code (unless one is still good) and wait for the panel's Verify
            if (requireEmailCode && email != mVerifiedEmail)
            {
                if (mOtp == null || mOtp.email != email) mOtp = new EmailOtp(email);
                if (!mOtp.Live)
                {
                    SetBusy(true, "Sending a code to " + email + "...");
                    string problem = await mOtp.SendAsync();
                    if (problem != null)
                    {
                        SetBusy(false, problem);
                        return;
                    }
                }
                SetBusy(true, "");
                ShowCodePanel();
                return;
            }

            SetBusy(true, "Creating account...");
            SignOutIfOtherAccount(username);
            bool created = false;
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                created = true;
            }

            if (!await ClaimEmailAsync(email, username))
            {
                // Undone so the username isn't used up by an account without its email
                if (created) await AuthenticationService.Instance.DeleteAccountAsync();
                return;
            }
            await FinishLoginAsync();
        });
    }

    // Right code: the email counts as proved and sign-up carries on to making the account
    void OnVerifyClicked()
    {
        string problem = mOtp.Check(mCodeInput.text);
        if (problem != null)
        {
            mCodeMessage.text = problem;
            return;
        }
        mVerifiedEmail = mOtp.email;
        HideCodePanel();
        OnSignUpClicked();
    }

    async void OnResendClicked()
    {
        if (mOtp.ResendIn > 0f)
        {
            mCodeMessage.text = $"Wait {Mathf.CeilToInt(mOtp.ResendIn)} s before asking for another code.";
            return;
        }

        mResendButton.interactable = false;
        mCodeMessage.text = "Sending a new code...";
        string problem = await mOtp.SendAsync();
        if (this == null) return; // scene left while sending
        mResendButton.interactable = true;
        mCodeMessage.text = problem ?? CodeSentText();
    }

    void OnCodeCancelled()
    {
        HideCodePanel();
        SetBusy(false, "");
    }

    // Writes emails/{hash} for the signed-in account; false (with the reason shown) if it couldn't
    async Task<bool> ClaimEmailAsync(string email, string username)
    {
        var fields = new Dictionary<string, object>
        {
            ["username"] = username,
            ["playerId"] = PlayerSession.Id,
            ["createdMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        RtdbRest.Result write = await Rtdb(done => RtdbRest.Create(Emails, EmailId(email), fields, done));
        if (!write.succeeded && write.status == 409)
        {
            // Already there: fine if it's this account's, from a sign-up that failed after this step
            RtdbRest.Result entry = await Rtdb(done => RtdbRest.Get(EmailPath(email), done));
            if (!(entry.succeeded && IsOwnEntry(entry, username)))
            {
                SetBusy(false, "That email is already used by another account.");
                return false;
            }
        }
        else if (!write.succeeded)
        {
            Debug.LogWarning("[LoginController] Couldn't save the email: " + write.error);
            SetBusy(false, "Couldn't save the email. Check your internet connection and try again.");
            return false;
        }

        // A copy on the account itself; the database entry is what counts, so a failure here only logs
        try
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { [EmailKey] = email });
        }
        catch (Exception e) when (e is CloudSaveException || e is RequestFailedException)
        {
            Debug.LogWarning("[LoginController] Couldn't copy the email to Cloud Save: " + e.Message);
        }
        return true;
    }

    static bool IsOwnEntry(RtdbRest.Result entry, string username) =>
        string.Equals(entry.fields.GetString("username"), username, StringComparison.OrdinalIgnoreCase);

    /* Emails are stored by SHA-256 of the lower-case address, so the readable database doesn't list
     anyone's email and the key has none of the characters Realtime Database keys can't hold */
    static string EmailId(string email)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }

    static string EmailPath(string email) => Emails + "/" + EmailId(email);

    // Runs an RtdbRest coroutine on this object and waits for its result
    Task<RtdbRest.Result> Rtdb(Func<Action<RtdbRest.Result>, IEnumerator> call)
    {
        var done = new TaskCompletionSource<RtdbRest.Result>();
        StartCoroutine(call(r => done.TrySetResult(r)));
        return done.Task;
    }

    // Copy of the username box, one row above it
    TMP_InputField MakeEmailInput()
    {
        TMP_InputField input = Instantiate(usernameInput, usernameInput.transform.parent);
        input.name = "Email";
        input.text = "";
        input.contentType = TMP_InputField.ContentType.EmailAddress;
        SetPlaceholder(input, "Email");

        var username = (RectTransform)usernameInput.transform;
        var password = (RectTransform)passwordInput.transform;
        ((RectTransform)input.transform).anchoredPosition =
            username.anchoredPosition + (username.anchoredPosition - password.anchoredPosition);
        return input;
    }

    static void SetPlaceholder(TMP_InputField input, string text)
    {
        if (input.placeholder is TMP_Text placeholder) placeholder.text = text;
    }

    bool ReadInputs(out string username, out string password)
    {
        username = usernameInput.text.Trim();
        password = passwordInput.text;
        if (username.Length > 0 && password.Length > 0 && (!mSignUpPage || emailInput.text.Trim().Length > 0)) return true;

        SetBusy(false, mSignUpPage ? "Enter your email, username and password." : "Enter your username or email and your password.");
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

        SetBusy(true, $"Connected!\nWelcome, {PlayerSession.Name}", SuccessColor);
        await Awaitable.WaitForSecondsAsync(welcomeSeconds);
        if (this == null) return; // scene left while waiting
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

    // A message shown while not busy is a problem (wrong password, taken username...), so it's red
    void SetBusy(bool busy, string status, Color? color = null)
    {
        if (this == null) return; // scene already unloaded
        loginButton.interactable = !busy;
        signUpButton.interactable = !busy;
        statusText.text = status;
        statusText.color = color ?? (busy ? mStatusColor : ErrorColor);
    }
}

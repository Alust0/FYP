using Unity.Services.Authentication;
using Unity.Services.Core;

// Player identity for rooms and decks: the Unity player ID, not the Firebase anonymous UID
public static class PlayerSession
{
    public static string Id => AuthenticationService.Instance.PlayerId;

    // Login username, shown to the opponent; "Player" when the scene was opened without logging in
    public static string Name
    {
        get
        {
            if (UnityServices.State != ServicesInitializationState.Initialized || !AuthenticationService.Instance.IsSignedIn)
                return "Player";

            string name = AuthenticationService.Instance.PlayerInfo?.Username;
            if (string.IsNullOrEmpty(name)) name = AuthenticationService.Instance.PlayerName;
            return string.IsNullOrEmpty(name) ? "Player" : name;
        }
    }
}

using Unity.Services.Authentication;

// Player identity for rooms and decks: the Unity player ID, not the Firebase anonymous UID
public static class PlayerSession
{
    public static string Id => AuthenticationService.Instance.PlayerId;
}

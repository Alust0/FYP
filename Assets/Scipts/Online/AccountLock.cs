using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !UNITY_WSA
using Firebase.Auth;
#endif

/* One running copy of the game per account. The Realtime Database entry sessions/{playerId} names the
 copy that's logged in (MatchSession.ClientId) and holds its heartbeat:
   clientId, device, beatMs
 Another copy can only log in once that heartbeat is older than StaleSeconds, i.e. after the first
 one logged out, closed or crashed. A copy that finds it was replaced (its heartbeat stopped long
 enough for another to log in) is sent back to the login screen. Created by LoginController. */
public class AccountLock : MonoBehaviour
{
    const string Sessions = "sessions";
    const float HeartbeatSeconds = 8f;
    const float StaleSeconds = 30f;
    const string LoginScene = "Login";

    static AccountLock sInstance;

    // Why this copy was sent back to the login screen, for LoginController to show; null if it wasn't
    public static string KickMessage;

    string mPlayerId; // the account this copy holds; null while it holds none
    Coroutine mHeartbeat;

    static long NowMs => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static string SessionPath(string playerId) => Sessions + "/" + playerId;

    static Dictionary<string, object> Claim() => new Dictionary<string, object>
    {
        ["clientId"] = MatchSession.ClientId,
        ["device"] = SystemInfo.deviceName,
        ["beatMs"] = NowMs,
    };

    // Claims the account for this copy. Null when it's ours, otherwise why not
    public static Task<string> ClaimAsync(string playerId)
    {
        if (sInstance == null)
        {
            GameObject obj = new GameObject("AccountLock");
            DontDestroyOnLoad(obj);
            sInstance = obj.AddComponent<AccountLock>();
        }

        var done = new TaskCompletionSource<string>();
        sInstance.StartCoroutine(sInstance.ClaimRoutine(playerId, done));
        return done.Task;
    }

    // On logout: frees the account for other copies straight away
    public static void Release()
    {
        if (sInstance == null) return;
        sInstance.ReleaseOwn();
        Destroy(sInstance.gameObject);
        sInstance = null;
    }

    IEnumerator ClaimRoutine(string playerId, TaskCompletionSource<string> done)
    {
        StopHeartbeat();
        string path = SessionPath(playerId);

        /* A few tries, for a dropped request or another copy writing between our read and our
         write. Each failure is logged with the server's answer. */
        RtdbRest.Result failed = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) yield return new WaitForSecondsRealtime(1f);

            RtdbRest.Result session = null;
            yield return RtdbRest.Get(path, r => session = r);

            RtdbRest.Result write = null;
            if (session.status == 404)
            {
                yield return RtdbRest.Create(Sessions, playerId, Claim(), r => write = r);
            }
            else if (!session.ok)
            {
                Debug.LogWarning("[AccountLock] Reading " + path + " failed (" + session.status + "): " + session.error);
                failed = session;
                if (session.status == 0) continue; // dropped: try again
                done.SetResult(Problem(session));
                yield break;
            }
            else if (session.fields.GetString("clientId") != MatchSession.ClientId &&
                     NowMs - session.fields.GetLong("beatMs") < StaleSeconds * 1000f)
            {
                string device = session.fields.GetString("device");
                done.SetResult("This account is already logged in" + (string.IsNullOrEmpty(device) ? "" : " on " + device) +
                    ". Log out there first, or wait " + (int)StaleSeconds + " seconds if that game was closed.");
                yield break;
            }
            else
            {
                // Ours already, or left behind by a copy that stopped: take it over, unless it just changed
                yield return RtdbRest.Patch(path, Claim(), new[] { "clientId", "device", "beatMs" }, r => write = r, session.version);
            }

            if (write.ok)
            {
                mPlayerId = playerId;
                mHeartbeat = StartCoroutine(Heartbeat());
                done.SetResult(null);
                yield break;
            }
            Debug.LogWarning("[AccountLock] Claiming " + path + " failed (" + write.status + "): " + write.error);
            failed = write;
            if (write.status == 403)
            {
                done.SetResult(Problem(write));
                yield break;
            }
        }
        done.SetResult(failed != null && failed.status == 0 ? Problem(failed) : "Couldn't log in. Please try again.");
    }

    // Keeps the claim fresh, and notices if another copy took the account over
    IEnumerator Heartbeat()
    {
        string path = SessionPath(mPlayerId);
        while (true)
        {
            yield return new WaitForSecondsRealtime(HeartbeatSeconds);

            RtdbRest.Result session = null;
            yield return RtdbRest.Get(path, r => session = r);

            if (session.status == 404)
            {
                yield return RtdbRest.Create(Sessions, mPlayerId, Claim(), null);
                continue; // if another copy got it first, the next beat sees that
            }
            if (!session.ok) continue; // a dropped read; the next beat tries again

            if (session.fields.GetString("clientId") != MatchSession.ClientId)
            {
                string device = session.fields.GetString("device");
                Kick("This account was logged in" + (string.IsNullOrEmpty(device) ? " somewhere else" : " on " + device) + ", so you were logged out here.");
                yield break;
            }

            var beat = new Dictionary<string, object> { ["beatMs"] = NowMs };
            yield return RtdbRest.Patch(path, beat, new[] { "beatMs" }, null, session.version);
        }
    }

    void Kick(string message)
    {
        mPlayerId = null; // the claim is the other copy's now, so it isn't released on the way out
        KickMessage = message;
        AuthenticationService.Instance.SignOut(true);
#if !UNITY_WSA
        FirebaseAuth.DefaultInstance.SignOut();
#endif
        Release();
        SceneManager.LoadScene(LoginScene);
    }

    void StopHeartbeat()
    {
        if (mHeartbeat != null) StopCoroutine(mHeartbeat);
        mHeartbeat = null;
        mPlayerId = null;
    }

    void ReleaseOwn()
    {
        if (mPlayerId == null) return;
        RtdbRest.DeleteWithoutWaiting(SessionPath(mPlayerId));
        StopHeartbeat();
    }

    // May not reach the server before the game closes; the claim then goes stale after StaleSeconds
    void OnApplicationQuit() => ReleaseOwn();

    static string Problem(RtdbRest.Result result)
    {
        if (result == null || result.status == 0) return "Couldn't reach the game database. Check your internet connection.";
        if (result.status == 403) return "The database refused the login check: its Realtime Database rules don't allow sessions yet.";
        return "The game database answered " + result.status + ". Please try again.";
    }
}

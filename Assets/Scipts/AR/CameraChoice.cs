using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Vuforia;

/* Which webcam Vuforia uses in the editor, chosen per editor window. Vuforia keeps a single choice
 in the shared editor prefs, so a Multiplayer Play Mode window would otherwise use the same camera
 as the main editor. Picked in the lobby; Player 2's window defaults to a camera the main editor
 isn't using. Builds pass the choice to BuildWebcamDriver instead. */
public static class CameraChoice
{
    const string MainWindow = "Main";

    // Multiplayer Play Mode runs each virtual player from its own copy under Library/VP/<id>
    public static string WindowId
    {
        get
        {
            string path = Application.dataPath.Replace('\\', '/');
            int vp = path.IndexOf("/Library/VP/");
            return vp < 0 ? MainWindow : path.Substring(vp + "/Library/VP/".Length).Split('/')[0];
        }
    }

    // The real project folder, also from a virtual player's copy
    static string ProjectRoot
    {
        get
        {
            string assets = Application.dataPath.Replace('\\', '/');
            int vp = assets.IndexOf("/Library/VP/");
            return vp >= 0 ? assets.Substring(0, vp) : Path.GetDirectoryName(assets);
        }
    }

    static string PrefKey(string window) => "CameraChoice." + window;

    public static string[] Devices => WebCamTexture.devices.Select(d => d.name).ToArray();

    // The camera this window uses; null when there's no camera at all
    public static string Current
    {
        get
        {
            string[] devices = Devices;
            if (devices.Length == 0) return null;

            string saved = PlayerPrefs.GetString(PrefKey(WindowId), "");
            if (devices.Contains(saved)) return saved;

            // Main editor: whatever Vuforia's own setting says. Player 2: something else, if there is
            string main = MainCamera(devices);
            if (WindowId == MainWindow) return main;
            return devices.FirstOrDefault(d => d != main) ?? main;
        }
    }

    public static void Choose(string device)
    {
        PlayerPrefs.SetString(PrefKey(WindowId), device);
        PlayerPrefs.Save();
        Apply();
    }

    /* Called before the first scene, by the lobby just before a match loads, and by DeckTargetLoader
     as the match scene wakes, so the name is in place whenever Vuforia creates its camera.

     Vuforia can throw its configuration away and reload it, losing a name set on the old copy; the
     reloaded one then reads the editor pref "DeviceNameSetInEditor", which both editor windows
     share. So the pref is written too, right before this window's Vuforia reads it. */
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Apply()
    {
        if (!Application.isEditor) return;
        PreloadVuforiaDriver();

        string device = Current;
        if (device == null) return;

        // Remember the main editor's camera before a virtual player changes the shared setting
        if (WindowId != MainWindow && !PlayerPrefs.HasKey(PrefKey(MainWindow)))
            PlayerPrefs.SetString(PrefKey(MainWindow), MainCamera(Devices));

        VuforiaConfiguration.Instance.WebCam.DeviceNameSetInEditor = device;
#if UNITY_EDITOR
        UnityEditor.EditorPrefs.SetString("DeviceNameSetInEditor", device);
#endif
        Debug.Log("[CameraChoice] " + WindowId + " window uses camera '" + device + "'.");
    }

    /* Vuforia waits for this (Delayed Initialization in its settings) instead of opening the camera
     at Play start. Two editor windows opening their cameras at the same moment broke one of them:
     stopping its camera failed ("Could not pause pControl") and it never sent pictures again. So in
     the editor the windows take turns, and a turn lasts until the camera is sending pictures. */
    public static IEnumerator StartVuforia()
    {
        if (!Application.isEditor)
        {
            yield return BuildWebcamDriver.Initialize(Current); // builds: Vuforia gets the chosen webcam through its driver
            yield break;
        }

        yield return WaitForCameraTurn();
        Apply();

        if (!VuforiaApplication.Instance.IsInitialized)
            VuforiaApplication.Instance.Initialize(); // VuforiaBehaviour starts the camera once this is done
    }

    /* Run by DeckTargetLoader once Vuforia's camera is running. Logs which webcam Vuforia really
     opened; if it's the wrong one, tries once to switch it. A camera that won't start (e.g. busy in
     the other editor window) delivers no frames, so then it goes back rather than freezing. Then
     ends this window's turn and keeps watching for a stalled camera (WatchForStall). */
    public static IEnumerator CheckVuforiaCamera()
    {
        if (!Application.isEditor) yield break;
        yield return new WaitForSeconds(0.5f);
        yield return SwitchIfWrong();
        yield return WaitForPictures(4f);
        EndCameraTurn();
        yield return WatchForStall();
    }

    const float TurnSeconds = 15f;     // a turn left behind (Play stopped mid-start) runs out after this
    const float MaxTurnWait = 20f;
    static bool sHasTurn;

    // In the real project's Library folder, which every editor window shares
    static string TurnFile => Path.Combine(ProjectRoot, "Library/CameraStartTurn.txt");

    static IEnumerator WaitForCameraTurn()
    {
        float giveUp = Time.realtimeSinceStartup + MaxTurnWait;
        bool told = false;

        while (!TryTakeTurn())
        {
            if (Time.realtimeSinceStartup > giveUp)
            {
                Debug.LogWarning("[CameraChoice] Waited too long for the other window's camera; starting this one anyway.");
                yield break;
            }
            if (!told) Debug.Log("[CameraChoice] " + WindowId + " window waits for the other window's camera to start.");
            told = true;
            yield return new WaitForSecondsRealtime(0.25f);
        }
    }

    static bool TryTakeTurn()
    {
        if (sHasTurn) return true;
        try
        {
            // A turn file that's ours (from an earlier Play) or too old is left over
            if (File.Exists(TurnFile) &&
                (File.ReadAllText(TurnFile) == WindowId || (DateTime.UtcNow - File.GetLastWriteTimeUtc(TurnFile)).TotalSeconds > TurnSeconds))
                File.Delete(TurnFile);

            // CreateNew fails if the other window made it first, so only one can hold the turn
            using (var file = new FileStream(TurnFile, FileMode.CreateNew))
            {
                byte[] id = System.Text.Encoding.UTF8.GetBytes(WindowId);
                file.Write(id, 0, id.Length);
            }
            sHasTurn = true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { } // being deleted by the other window
        return sHasTurn;
    }

    // Called once this window's camera is going, or the scene closes or Vuforia gives up
    public static void EndCameraTurn()
    {
        if (!sHasTurn) return;
        sHasTurn = false;
        try { File.Delete(TurnFile); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // A few new frames from the playing webcam, or the time runs out
    static IEnumerator WaitForPictures(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        int frames = 0;
        while (frames < 10 && Time.realtimeSinceStartup < deadline)
        {
            WebCamTexture texture = Resources.FindObjectsOfTypeAll<WebCamTexture>().FirstOrDefault(t => t.isPlaying);
            if (texture != null && texture.didUpdateThisFrame) frames++;
            yield return null;
        }
    }

    const float StallSeconds = 2f;
    const int MaxRestarts = 3;

    /* With two editor windows, the camera opened second (the host's, which loads the match up to a
     second after the guest) was seen dying right after it started: no frames, "Could not pause
     pControl" errors. Restarting it through Vuforia once the other camera has settled brings it
     back, so a camera with no new frame for StallSeconds is restarted, up to MaxRestarts times. */
    static IEnumerator WatchForStall()
    {
        WebCamTexture texture = null;
        float lastFrame = Time.realtimeSinceStartup;
        float nextLookup = 0f;
        int restarts = 0;

        while (restarts < MaxRestarts)
        {
            float now = Time.realtimeSinceStartup;

            // Vuforia can swap its texture, so look it up again now and then
            if (texture == null || !texture.isPlaying || now >= nextLookup)
            {
                texture = Resources.FindObjectsOfTypeAll<WebCamTexture>().FirstOrDefault(t => t.isPlaying);
                nextLookup = now + 1f;
            }

            if (texture != null && texture.didUpdateThisFrame) lastFrame = now;

            if (now - lastFrame > StallSeconds)
            {
                restarts++;
                Debug.LogWarning("[CameraChoice] " + WindowId + " window's camera stopped sending pictures; restarting it (" + restarts + "/" + MaxRestarts + ").");

                yield return WaitForCameraTurn();
                VuforiaBehaviour vuforia = VuforiaBehaviour.Instance;
                if (vuforia == null)
                {
                    EndCameraTurn();
                    yield break;
                }
                vuforia.enabled = false;
                yield return new WaitForSecondsRealtime(1f);
                vuforia.enabled = true;
                yield return WaitForPictures(3f);
                EndCameraTurn();

                texture = null;
                lastFrame = Time.realtimeSinceStartup + 2f; // time to start up again
            }

            yield return null;
        }

        Debug.LogWarning("[CameraChoice] " + WindowId + " window's camera kept stalling. Stop Play and try again, or use the F2 panel in this window.");
    }

    static IEnumerator SwitchIfWrong()
    {
        string wanted = Current;
        WebCamTexture[] playing = Resources.FindObjectsOfTypeAll<WebCamTexture>().Where(t => t.isPlaying).ToArray();
        Debug.Log("[CameraChoice] " + WindowId + " window check. Devices: " + string.Join(", ", Devices.Select(d => "'" + d + "'")) +
            ". Wanted '" + wanted + "'. Playing: " +
            (playing.Length == 0 ? "none" : string.Join(", ", playing.Select(t => "'" + t.deviceName + "' " + t.width + "x" + t.height))) + ".");

        if (wanted == null || playing.Length != 1 || playing[0].deviceName == wanted) yield break;

        WebCamTexture texture = playing[0];
        string was = texture.deviceName;
        texture.Stop();
        texture.deviceName = wanted;
        texture.Play();

        // Any new frame within 3 s means the switch took
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (texture.isPlaying && texture.didUpdateThisFrame)
            {
                Debug.Log("[CameraChoice] Vuforia was on '" + was + "'; switched it to '" + wanted + "'.");
                yield break;
            }
            yield return null;
        }

        texture.Stop();
        texture.deviceName = was;
        texture.Play();
        Debug.LogWarning("[CameraChoice] '" + wanted + "' gave no picture (in use elsewhere?), so this window stays on '" + was +
            "'. Pick the cameras in each window's lobby, then stop and press Play again.");
    }

#if UNITY_EDITOR_WIN
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibrary(string path);

    static bool sDriverLoaded;

    /* Vuforia loads its webcam driver by bare file name ("UnityDriver.dll"), which only resolves
     once Unity has loaded the package's plugins. A Multiplayer Play Mode window's copy of the
     project has an empty Packages folder, so there it can fail with "Vuforia driver library load
     error". Loading it by full path from the real project first makes the bare name resolve. */
    static void PreloadVuforiaDriver()
    {
        if (sDriverLoaded) return;

        string driver = Path.Combine(ProjectRoot, "Packages/com.ptc.vuforia.engine/Vuforia/Plugins/Windows/x64/UnityDriver.dll");
        if (!File.Exists(driver)) return; // package installed elsewhere; Unity finds it itself then

        sDriverLoaded = LoadLibrary(driver) != IntPtr.Zero;
        if (!sDriverLoaded) Debug.LogWarning("[CameraChoice] Couldn't preload " + driver + " (error " + Marshal.GetLastWin32Error() + ").");
    }
#else
    static void PreloadVuforiaDriver() { }
#endif

    static string MainCamera(string[] devices)
    {
        string saved = PlayerPrefs.GetString(PrefKey(MainWindow), "");
        if (devices.Contains(saved)) return saved;

        string vuforia = VuforiaConfiguration.Instance.WebCam.DeviceNameSetInEditor;
        return devices.Contains(vuforia) ? vuforia : devices[0];
    }
}

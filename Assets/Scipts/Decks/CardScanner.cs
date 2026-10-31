using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vuforia;

/* Scans cards with Vuforia Cloud Recognition for the deck builder. Every ticked CloudRecoBehaviour
 in the scene (one per Cloud database) searches at once, and each card found is passed to CardScanned.
 After a card it pauses, so the card can be moved away before the next search. Needs Vuforia's AR
 Camera in the scene. */
public class CardScanner : MonoBehaviour
{
    [Serializable]
    public class Database
    {
        public CloudRecoBehaviour cloudReco;
        public bool use = true;
    }

    [Tooltip("Seconds scanning waits after finding a card, to move it away before the next.")]
    [SerializeField] private float pauseSeconds = 3f;

    // Drawn as a tick box per database by CardScannerEditor
    [SerializeField] private List<Database> databases = new List<Database>();

    public event Action<string> CardScanned; // the card number
    public event Action<string> StatusChanged; // what scanning is doing, for the screen
    public event Action<string> Failed; // why scanning had to stop

    readonly List<CloudRecoBehaviour> mCloudReco = new List<CloudRecoBehaviour>();
    readonly List<Action> mUnregister = new List<Action>();
    bool mScanning;
    bool mCameraStarted;
    bool mObjectsOn;
    VuforiaBehaviour mArCamera;
    Coroutine mSteps;

    public bool IsScanning => mScanning;

    // False without the AR Camera or any Cloud Reco objects in the scene
    public bool CanScan => mCloudReco.Count > 0 && mArCamera != null;

    /* The ticked databases, inactive ones too; they only search while scanning. Unticked ones are
     switched off so they don't use the recognition allowance. With none listed (added in code),
     every one in the scene is used. */
    void Start()
    {
        foreach (Database database in databases)
        {
            if (database.cloudReco == null) continue;
            if (database.use) mCloudReco.Add(database.cloudReco);
            else database.cloudReco.enabled = false;
        }
        if (databases.Count == 0)
            mCloudReco.AddRange(FindObjectsByType<CloudRecoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        // Left inactive in the scene so Vuforia doesn't slow the scene's loading
        mArCamera = FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include);
        Debug.Log("[CardScanner] " + mCloudReco.Count + " Cloud Reco databases for scanning.");
    }

    // The first scan turns on the AR Camera and the Cloud Reco objects
    void TurnOnScanningObjects()
    {
        if (mObjectsOn) return;
        mObjectsOn = true;
        // URP: a second screen camera would clear over the AR camera's picture
        Camera screen = Camera.main;
        mArCamera.gameObject.SetActive(true);
        if (screen != null && screen.gameObject != mArCamera.gameObject) screen.gameObject.SetActive(false);

        foreach (CloudRecoBehaviour cloudReco in mCloudReco)
        {
            cloudReco.enabled = false;
            cloudReco.gameObject.SetActive(true);

            CloudRecoBehaviour captured = cloudReco;
            Action<CloudRecoBehaviour.CloudRecoSearchResult> onResult = OnResult;
            Action<CloudRecoBehaviour.InitError> onInitError = error => Debug.LogError("[CardScanner] Cloud Reco '" + captured.name + "' failed to start: " + error);
            Action<CloudRecoBehaviour.QueryError> onQueryError = error => Debug.LogWarning("[CardScanner] Cloud Reco '" + captured.name + "' query failed: " + error);
            captured.RegisterOnNewSearchResultEventHandler(onResult);
            captured.RegisterOnInitErrorEventHandler(onInitError);
            captured.RegisterOnUpdateErrorEventHandler(onQueryError);
            mUnregister.Add(() =>
            {
                if (captured == null) return;
                captured.UnregisterOnNewSearchResultEventHandler(onResult);
                captured.UnregisterOnInitErrorEventHandler(onInitError);
                captured.UnregisterOnUpdateErrorEventHandler(onQueryError);
            });
        }
    }

#if UNITY_EDITOR
    // Editor: lists every CloudRecoBehaviour in this scene, new ones ticked
    public void FindDatabases()
    {
        if (Application.isPlaying || !gameObject.scene.IsValid()) return;
        bool changed = databases.RemoveAll(database => database.cloudReco == null) > 0;
        foreach (CloudRecoBehaviour cloudReco in FindObjectsByType<CloudRecoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cloudReco.gameObject.scene != gameObject.scene || databases.Exists(database => database.cloudReco == cloudReco)) continue;
            databases.Add(new Database { cloudReco = cloudReco });
            changed = true;
        }
        if (!changed) return;
        databases.Sort((a, b) => string.CompareOrdinal(a.cloudReco.name, b.cloudReco.name));
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif

    void OnDestroy()
    {
        foreach (Action unregister in mUnregister) unregister();
        CameraChoice.EndCameraTurn(); // in case the scene closes while the camera starts
    }

    public void StartScanning()
    {
        if (mScanning || !CanScan) return;
        mScanning = true;
        mSteps = StartCoroutine(StartSteps());
    }

    public void StopScanning()
    {
        if (mSteps != null) StopCoroutine(mSteps);
        mSteps = null;
        mScanning = false;
        Search(false);
    }

    IEnumerator StartSteps()
    {
        TurnOnScanningObjects();
        if (!VuforiaApplication.Instance.IsRunning)
        {
            StatusChanged?.Invoke("Starting the camera...");
            if (!mCameraStarted)
            {
                mCameraStarted = true;
                yield return CameraChoice.StartVuforia(); // Vuforia waits for this (Delayed Initialization)
            }

            float giveUp = Time.realtimeSinceStartup + 20f;
            while (!VuforiaApplication.Instance.IsRunning && Time.realtimeSinceStartup < giveUp) yield return null;
            if (!VuforiaApplication.Instance.IsRunning)
            {
                CameraChoice.EndCameraTurn();
                StopScanning();
                Failed?.Invoke("The camera didn't start. Check no other program is using it.");
                yield break;
            }
            StartCoroutine(CameraChoice.CheckVuforiaCamera()); // editor: makes sure this window got its own webcam
        }
        Search(true);
    }

    // Each database searches while enabled
    void Search(bool on)
    {
        foreach (CloudRecoBehaviour cloudReco in mCloudReco)
            if (cloudReco != null) cloudReco.enabled = on;
        if (on) StatusChanged?.Invoke("Hold one card up to the camera...");
    }

    void OnResult(CloudRecoBehaviour.CloudRecoSearchResult result)
    {
        if (!mScanning) return;
        Search(false); // one card at a time; also saves the databases' recognition allowance

        Debug.Log("[CardScanner] Scanned '" + result.TargetName + "', metadata '" + result.MetaData + "'.");
        string number = CardNumber(result.MetaData) ?? CardNumber(result.TargetName);
        StatusChanged?.Invoke(number == null
            ? "Found \"" + result.TargetName + "\", but there's no picture of it in the game."
            : "Found " + number + ". Move it away; the next scan starts in a moment.");
        if (number != null) CardScanned?.Invoke(number);

        if (mSteps != null) StopCoroutine(mSteps);
        mSteps = StartCoroutine(ScanAgainSoon());
    }

    IEnumerator ScanAgainSoon()
    {
        yield return new WaitForSeconds(pauseSeconds);
        if (mScanning) Search(true);
    }

    // The card number in a scan's metadata or target name, if the game has its picture
    static string CardNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string number = text.Trim().Replace('/', '-');
        return CardImageLibrary.GetPath(number) != null ? number : null;
    }
}

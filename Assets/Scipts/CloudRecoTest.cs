using UnityEngine;
using Vuforia;

/*To confirm if CloudRecoBehaviour is actually recognizing
targets and returning the expected metadata (card ID), before linking to
Firestore or any UI. Attach this to  CloudRecoBehaviour, press Play, and 
point the camera at a real card that's in Database A's card pool. Watch 
the Console.*/
[RequireComponent(typeof(CloudRecoBehaviour))]
public class CloudRecoTest : MonoBehaviour
{
    private CloudRecoBehaviour cloudReco;

    void Start()
    {
        cloudReco = GetComponent<CloudRecoBehaviour>();
        cloudReco.RegisterOnInitializedEventHandler(OnCloudRecoInitialized);
        cloudReco.RegisterOnNewSearchResultEventHandler(OnNewSearchResult);
        Debug.Log("[CloudRecoTest] Handlers registered. Waiting for initialization...");
    }

    // Takes the CloudRecoBehaviour itself
    private void OnCloudRecoInitialized(CloudRecoBehaviour behaviour)
    {
        Debug.Log("[CloudRecoTest] Cloud Reco initialized. Scanning for targets...");
    }

    // Takes the nested CloudRecoSearchResult type
    private void OnNewSearchResult(CloudRecoBehaviour.CloudRecoSearchResult result)
    {
        Debug.Log("[CloudRecoTest] MATCH FOUND (place cursor after 'result.' below and press Ctrl+Space)");
        // result.
    }

        private void OnUpdateError(int error)
    {
        Debug.LogError($"[CloudRecoTest] Update error: {error}");
    }
}
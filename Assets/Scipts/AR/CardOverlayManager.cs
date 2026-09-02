using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// Spawns and manages one overlay panel per detected card using AR Foundation image tracking
public class CardOverlayManager : MonoBehaviour
{
    [Header("References")]
    public ARTrackedImageManager trackedImageManager;
    public GameObject            overlayPrefab;

    // Maps card name to its active overlay GameObject
    private Dictionary<string, GameObject> activeOverlays = new Dictionary<string, GameObject>();

    void OnEnable()
    {
        trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
    }

    void OnDisable()
    {
        trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    // Handles added, updated and removed tracked images
    void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        foreach (ARTrackedImage img in args.added)
            SpawnOverlay(img);   // new card detected

        foreach (ARTrackedImage img in args.updated)
            UpdateOverlayTransform(img);   // card moved or rotated

        foreach (ARTrackedImage img in args.removed)
            HideOverlay(img.referenceImage.name);   // card left camera view
    }

    // Instantiates an overlay anchored to the detected card
    void SpawnOverlay(ARTrackedImage trackedImage)
    {
        string   cardName = trackedImage.referenceImage.name;
        CardData data     = CardDatabase.Lookup(cardName);

        if (data == null)
        {
            Debug.LogWarning("No CardData found for: " + cardName);
            return;
        }

        if (activeOverlays.ContainsKey(cardName))
        {
            activeOverlays[cardName].SetActive(true);   // reactivate hidden overlay
            return;
        }

        GameObject overlay = Instantiate(overlayPrefab, trackedImage.transform);
        overlay.transform.localPosition = new Vector3(0f, 0f, -0.01f);   // float above card
        overlay.transform.localRotation = Quaternion.identity;

        CardOverlayUI ui = overlay.GetComponent<CardOverlayUI>();
        if (ui != null) ui.Populate(data);   // fill all UI fields

        activeOverlays[cardName] = overlay;

        // Detect stand vs rest orientation from card rotation
        UpdateCardOrientation(trackedImage);

        // Notify GameState this card has been detected
        GameState.OnRulePrompt?.Invoke(cardName, "Card detected: " + cardName);
        Debug.Log("[AR] Detected card: " + cardName);
    }

    // Updates the overlay position and orientation as the card moves
    void UpdateOverlayTransform(ARTrackedImage trackedImage)
    {
        string cardName = trackedImage.referenceImage.name;

        if (!activeOverlays.ContainsKey(cardName)) return;

        UpdateCardOrientation(trackedImage);
    }

    // Detects stand vs rest from the card's Z rotation angle
    void UpdateCardOrientation(ARTrackedImage trackedImage)
    {
        string cardName = trackedImage.referenceImage.name;
        if (!activeOverlays.ContainsKey(cardName)) return;

        float zRot    = trackedImage.transform.eulerAngles.z;
        bool  isRested = zRot > 60f && zRot < 120f;   // 90 degree rotation = rested

        CardOverlayUI ui = activeOverlays[cardName].GetComponent<CardOverlayUI>();
        if (ui != null) ui.SetRestIndicator(isRested);
    }

    // Hides the overlay when the card leaves camera view
    void HideOverlay(string cardName)
    {
        if (activeOverlays.ContainsKey(cardName))
            activeOverlays[cardName].SetActive(false);   // hide but preserve instance
    }

    // Updates the power display on a specific card overlay
    public void UpdateCardPower(string cardName, int basePower, int modifier)
    {
        if (!activeOverlays.TryGetValue(cardName, out GameObject obj)) return;
        CardOverlayUI ui = obj.GetComponent<CardOverlayUI>();
        if (ui != null) ui.UpdatePower(basePower, modifier);
    }

    // Shows a rule prompt above a specific card overlay
    public void ShowRulePrompt(string cardName, string promptText)
    {
        if (!activeOverlays.TryGetValue(cardName, out GameObject obj)) return;
        CardOverlayUI ui = obj.GetComponent<CardOverlayUI>();
        if (ui != null) ui.ShowRulePrompt(promptText);
    }
}

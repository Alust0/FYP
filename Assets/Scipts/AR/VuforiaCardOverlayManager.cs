using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Vuforia;

// Drives real card state — identity, zone placement, stand/rest orientation — from Vuforia
// Cloud Reco. Replaces the old ARFoundation-based CardOverlayManager/CardOverlaySetup, which
// tracked local reference images and can't run alongside Cloud Reco's own camera pipeline.
//
// Zone placement works by nearest-neighbor against BoardLayout.circles. For that to reflect
// reality, each zone key (e.g. "P1_Vanguard") needs to resolve to a GameObject whose transform
// tracks the real playmat — either a Vuforia Image Target placed at that physical zone
// (wire it up via BoardLayout.arZoneMarkers) or, for testing without a physical playmat, the
// procedural quad BoardLayout spawns by default.
public class VuforiaCardOverlayManager : MonoBehaviour
{
    [Header("Scene References")]
    public BoardLayout board;
    public GameObject overlayPrefab; // optional custom overlay prefab; a minimal one is built if left empty

    [Header("Cloud Databases")]
    [Tooltip("One CloudRecoBehaviour per Vuforia Cloud database (DSeries1/2/3, etc). Each database has its own Access/Secret key pair and Vuforia can't search more than one per query, so every database needs its own CloudRecoBehaviour running at once. Leave empty to auto-collect every CloudRecoBehaviour under this GameObject.")]
    public List<CloudRecoBehaviour> cloudRecoBehaviours = new List<CloudRecoBehaviour>();

    [Header("Zone Detection")]
    [Tooltip("Max distance, in the same units as the zone marker transforms, for a card to count as 'on' a zone. Needs tuning once measured against the real playmat.")]
    public float maxZoneDistance = 0.15f;

    readonly Dictionary<CloudRecoBehaviour, Action<CloudRecoBehaviour.CloudRecoSearchResult>> mHandlers =
        new Dictionary<CloudRecoBehaviour, Action<CloudRecoBehaviour.CloudRecoSearchResult>>();

    class TrackedCard
    {
        public CardInstance instance;
        public PlayerState owner;
        public ObserverBehaviour target;
        public CardOverlayUI overlayUI;
        public string currentZoneKey;
    }

    // Keyed by Cloud Reco's UniqueTargetId — one physical card, tracked continuously
    readonly Dictionary<string, TrackedCard> mTrackedCards = new Dictionary<string, TrackedCard>();

    void Awake()
    {
        if (cloudRecoBehaviours == null || cloudRecoBehaviours.Count == 0)
            cloudRecoBehaviours = new List<CloudRecoBehaviour>(GetComponentsInChildren<CloudRecoBehaviour>(true));
    }

    void OnEnable()
    {
        foreach (CloudRecoBehaviour cloudReco in cloudRecoBehaviours)
        {
            CloudRecoBehaviour captured = cloudReco;
            Action<CloudRecoBehaviour.CloudRecoSearchResult> handler = result => OnNewSearchResult(captured, result);
            mHandlers[captured] = handler;
            captured.RegisterOnNewSearchResultEventHandler(handler);
        }
    }

    void OnDisable()
    {
        foreach (KeyValuePair<CloudRecoBehaviour, Action<CloudRecoBehaviour.CloudRecoSearchResult>> kvp in mHandlers)
            kvp.Key.UnregisterOnNewSearchResultEventHandler(kvp.Value);
        mHandlers.Clear();
    }

    void OnNewSearchResult(CloudRecoBehaviour source, CloudRecoBehaviour.CloudRecoSearchResult result)
    {
        if (mTrackedCards.ContainsKey(result.UniqueTargetId)) return; // already tracking this physical card

        CardData data = ApiCardDatabase.Instance != null ? ApiCardDatabase.Instance.GetByNumber(result.MetaData) : null;
        if (data == null)
        {
            Debug.LogWarning("[VuforiaCardOverlayManager] No CardData found for scanned card: " + result.MetaData);
            return;
        }

        // A unique name per result (rather than one shared template GameObject) is what lets
        // multiple physical cards stay tracked on the board at the same time.
        ObserverBehaviour target = source.EnableObservers(result, "CardTarget_" + result.UniqueTargetId);
        if (target == null) return;

        GameObject overlay = BuildOverlay(target.transform);
        CardOverlayUI ui = overlay.GetComponent<CardOverlayUI>();
        ui?.Populate(data);

        mTrackedCards[result.UniqueTargetId] = new TrackedCard
        {
            instance = new CardInstance(data),
            target = target,
            overlayUI = ui
        };

        Debug.Log("[VuforiaCardOverlayManager] Now tracking " + data.cardName + " (" + result.MetaData + ")");
    }

    void Update()
    {
        if (board == null) return;

        foreach (TrackedCard tracked in mTrackedCards.Values)
        {
            if (tracked.target == null) continue;

            UpdateOrientation(tracked);
            UpdateZone(tracked);
        }
    }

    // Stand vs rest, based on the physical card's rotation as tracked by the camera.
    // The exact axis/threshold depends on how the target is defined in Vuforia and will
    // need tuning against the real cards/playmat.
    void UpdateOrientation(TrackedCard tracked)
    {
        float zRot = tracked.target.transform.eulerAngles.z;
        bool isRested = zRot > 60f && zRot < 120f;

        tracked.instance.orientation = isRested ? CardOrientation.Rest : CardOrientation.Stand;
        tracked.overlayUI?.SetRestIndicator(isRested);
    }

    // Finds the nearest registered board zone and, if within range, moves the card into it
    void UpdateZone(TrackedCard tracked)
    {
        string bestKey = null;
        float bestDist = maxZoneDistance;

        foreach (KeyValuePair<string, GameObject> kvp in board.circles)
        {
            float dist = Vector3.Distance(tracked.target.transform.position, kvp.Value.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestKey = kvp.Key;
            }
        }

        if (bestKey == tracked.currentZoneKey) return; // no change

        if (tracked.currentZoneKey != null)
        {
            board.RemoveCardFromCircle(tracked.currentZoneKey);
            tracked.owner?.RemoveCardFromAllZones(tracked.instance);
        }

        tracked.currentZoneKey = bestKey;

        if (bestKey == null)
        {
            tracked.owner = null;
        }
        else
        {
            string prefix = bestKey.Substring(0, 2); // "P1" or "P2"
            string suffix = bestKey.Substring(3); // after "P1_"/"P2_"
            GameState gs = GameState.Instance;
            tracked.owner = gs != null ? gs.players[prefix == "P1" ? 0 : 1] : null;

            if (tracked.owner != null && tracked.owner.PlaceCardInZone(tracked.instance, suffix))
                board.PlaceCardOnCircle(bestKey);
        }

        GameState.Instance?.BroadcastState();
    }

    // Builds a minimal overlay canvas when no custom prefab is assigned —
    // mirrors the layout previously hardcoded in the deleted CardOverlaySetup.cs
    GameObject BuildOverlay(Transform parent)
    {
        if (overlayPrefab != null)
        {
            GameObject instance = Instantiate(overlayPrefab, parent);
            instance.transform.localPosition = Vector3.zero;
            return instance;
        }

        GameObject canvasObj = new GameObject("CardOverlayCanvas");
        canvasObj.transform.SetParent(parent, false);
        canvasObj.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        canvasObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        canvasObj.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        canvasObj.AddComponent<GraphicRaycaster>();

        RectTransform canvasRT = canvasObj.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(630f, 880f);

        TextMeshProUGUI nameText = CreateText(canvasObj.transform, "CardName", new Vector2(0f, 0.85f), new Vector2(1f, 1f), 60, Color.white);
        TextMeshProUGUI powerText = CreateText(canvasObj.transform, "Power", new Vector2(0f, 0.4f), new Vector2(1f, 0.7f), 80, Color.white);
        TextMeshProUGUI critText = CreateText(canvasObj.transform, "Critical", new Vector2(0f, 0.25f), new Vector2(1f, 0.4f), 50, Color.red);

        CardOverlayUI ui = canvasObj.AddComponent<CardOverlayUI>();
        ui.cardNameText = nameText;
        ui.powerText = powerText;
        ui.criticalText = critText;

        return canvasObj;
    }

    TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int fontSize, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return tmp;
    }

    // Called externally (e.g. by AROverlayEventBridge) to push a power update onto a tracked card's overlay
    public void UpdateCardPower(string cardName, int basePower, int modifier)
    {
        foreach (TrackedCard tracked in mTrackedCards.Values)
            if (tracked.instance.data.cardName == cardName)
                tracked.overlayUI?.UpdatePower(basePower, modifier);
    }

    public void ShowRulePrompt(string cardName, string promptText)
    {
        foreach (TrackedCard tracked in mTrackedCards.Values)
            if (tracked.instance.data.cardName == cardName)
                tracked.overlayUI?.ShowRulePrompt(promptText);
    }
}

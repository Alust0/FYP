using System;
using System.Collections.Generic;
using UnityEngine;

/* Plug-in point for a card recogniser other than Vuforia, e.g. a second camera run by OpenCV.
 Vuforia (camera 1) keeps its own logic in VuforiaCardOverlayManager and doesn't use this.

 To plug one in: add a seat for its camera below, then from the recogniser call
  CardSightingReceiver.Instance.ReportSighting(seatIndex, trackId, cardNumber, screenPoint, angleFromUpright)
 every frame it sees a card, and ReportLost(seatIndex, trackId) when it stops seeing it.
 This turns those reports into zones, stand/rest and face up/down in GameState. */
public class CardSightingReceiver : MonoBehaviour
{
    public enum SeatSide { Player1, Player2, Both }

    [Serializable]
    public class CameraSeat
    {
        public string seatName = "Camera 2";
        [Tooltip("Unity camera whose screen space the recogniser reports in; board zones are projected through it.")]
        public Camera camera;
        [Tooltip("Which player's half of the board this camera watches. Zones on the other half are ignored.")]
        public SeatSide side = SeatSide.Player2;
    }

    public static CardSightingReceiver Instance { get; private set; }

    [SerializeField] private BoardLayout board;
    [SerializeField] private List<CameraSeat> seats = new List<CameraSeat>();

    [Header("Same thresholds as VuforiaCardOverlayManager")]
    [Range(10f, 90f)] public float restAngle = 55f;
    [Range(0f, 80f)] public float standAngle = 35f;
    public float zoneSettleSeconds = 0.4f;
    public float faceDownAfterSeconds = 1.5f;

    class Sighting
    {
        public CardInstance instance; // null until the card's data resolves
        public string zoneKey;
        public string pendingZoneKey;
        public float pendingSince;
        public bool visible;
        public float lastSeenTime;
    }

    // Keyed by seat index + the recogniser's own ID for one physical card
    readonly Dictionary<string, Sighting> mSightings = new Dictionary<string, Sighting>();

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /* trackId: the recogniser's ID for one physical card, stable while it stays in view.
     screenPoint: card center in pixels of the seat's camera.
     angleFromUpright: degrees the card is turned from upright (0 = standing, 90 = rested). */
    public void ReportSighting(int seatIndex, string trackId, string cardNumber, Vector2 screenPoint, float angleFromUpright)
    {
        if (seatIndex < 0 || seatIndex >= seats.Count || seats[seatIndex].camera == null) return;
        CameraSeat seat = seats[seatIndex];

        string key = seatIndex + "_" + trackId;
        if (!mSightings.TryGetValue(key, out Sighting sighting))
        {
            sighting = new Sighting();
            mSightings[key] = sighting;
            ResolveCardData(cardNumber, sighting);
        }

        sighting.visible = true;
        sighting.lastSeenTime = Time.time;

        GameState gs = GameState.Instance;
        if (sighting.instance == null || gs == null) return; // data still resolving

        gs.SetFacing(sighting.instance, CardFacing.FaceUp);

        CardOrientation orientation = sighting.instance.orientation; // kept between the two thresholds
        if (angleFromUpright > restAngle) orientation = CardOrientation.Rest;
        else if (angleFromUpright < standAngle) orientation = CardOrientation.Stand;
        gs.SetOrientation(sighting.instance, orientation);

        string zone = board != null ? board.ZoneAtScreenPoint(screenPoint, seat.camera, sighting.zoneKey) : null;
        if (zone != null && !SeatCovers(seat, zone)) zone = null;

        if (zone != sighting.pendingZoneKey)
        {
            sighting.pendingZoneKey = zone;
            sighting.pendingSince = Time.time;
        }
        if (Time.time - sighting.pendingSince >= zoneSettleSeconds)
            SetZone(sighting, zone);
    }

    public void ReportLost(int seatIndex, string trackId)
    {
        if (mSightings.TryGetValue(seatIndex + "_" + trackId, out Sighting sighting))
            sighting.visible = false;
    }

    // A damage-zone card that stays unseen counts as flipped face down (Counter Blast)
    void Update()
    {
        GameState gs = GameState.Instance;
        if (gs == null) return;

        foreach (Sighting sighting in mSightings.Values)
            if (!sighting.visible && sighting.instance != null && sighting.instance.zone == "Damage" &&
                Time.time - sighting.lastSeenTime > faceDownAfterSeconds)
                gs.SetFacing(sighting.instance, CardFacing.FaceDown);
    }

    static bool SeatCovers(CameraSeat seat, string zoneKey) =>
        seat.side == SeatSide.Both ||
        (seat.side == SeatSide.Player1 && zoneKey.StartsWith("P1")) ||
        (seat.side == SeatSide.Player2 && zoneKey.StartsWith("P2"));

    void SetZone(Sighting sighting, string zoneKey)
    {
        if (zoneKey == sighting.zoneKey) return;
        sighting.zoneKey = zoneKey;

        GameState gs = GameState.Instance;
        PlayerState owner = board != null ? board.ZoneOwner(gs, zoneKey) : null;
        gs.MoveCard(sighting.instance, owner, zoneKey?.Substring(3)); // suffix after "P1_"/"P2_"
    }

    // Same lookup order as VuforiaCardOverlayManager: Firestore first, local API cache as fallback
    void ResolveCardData(string cardNumber, Sighting sighting)
    {
        void Apply(CardData data)
        {
            if (data == null) Debug.LogWarning("[CardSightingReceiver] No CardData found for " + cardNumber);
            else sighting.instance = new CardInstance(data);
        }
        CardData FromLocalCache() => ApiCardDatabase.Instance != null ? ApiCardDatabase.Instance.GetByNumber(cardNumber) : null;

        if (CardFirestoreDatabase.Instance != null)
            CardFirestoreDatabase.Instance.GetCardByNumber(cardNumber, Apply, _ => Apply(FromLocalCache()));
        else
            Apply(FromLocalCache());
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/* Editor test aid, toggled with F2: places cards without a camera, e.g. in a second editor
 playing the other seat of an online match. Uses the same GameState calls as the trackers, so
 the rules, HUD and online sync treat these like scanned cards. Added by UISetup in the editor. */
public class DebugCardPlacer : MonoBehaviour
{
    public Key toggleKey = Key.F2;

    static readonly string[] Zones =
    {
        "Vanguard", "LeftFront", "RightFront", "LeftBack", "CenterBack", "RightBack",
        "Guardian", "Trigger", "Damage", "Drop", "Soul", "Order", "Bind",
    };
    static readonly string[] PlayerLabels = { "Player 1", "Player 2" };

    bool mOpen;
    Rect mWindow = new Rect(20f, 320f, 400f, 300f);
    string mCardNumber = "";
    int mZone;
    int mPlayer; // offline only; online it's always this PC's player
    string mStatus = "";

    // One instance per player and card number, like a deck target
    readonly Dictionary<string, CardInstance> mCards = new Dictionary<string, CardInstance>();

    PlayerState Player => GameState.Instance != null
        ? GameState.Instance.players[MatchSession.IsOnline ? MatchSession.LocalSeat : mPlayer]
        : null;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) mOpen = !mOpen;
    }

    void OnGUI()
    {
        if (mOpen) mWindow = GUILayout.Window(GetInstanceID(), mWindow, DrawWindow, "Test cards (F2)");
    }

    void DrawWindow(int id)
    {
        if (!MatchSession.IsOnline) mPlayer = GUILayout.Toolbar(mPlayer, PlayerLabels);

        GUILayout.Label("Card number, as in the deck list (e.g. D-SD01-001)");
        mCardNumber = GUILayout.TextField(mCardNumber);
        mZone = GUILayout.SelectionGrid(mZone, Zones, 3);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Place")) WithCard(card => GameState.Instance.MoveCard(card, Player, Zones[mZone]));
        if (GUILayout.Button("Rest/Stand")) WithCard(card => GameState.Instance.SetOrientation(card,
            card.orientation == CardOrientation.Rest ? CardOrientation.Stand : CardOrientation.Rest));
        if (GUILayout.Button("Flip")) WithCard(card => GameState.Instance.SetFacing(card,
            card.facing == CardFacing.FaceUp ? CardFacing.FaceDown : CardFacing.FaceUp));
        if (GUILayout.Button("Remove")) WithCard(card => GameState.Instance.MoveCard(card, null, null));
        GUILayout.EndHorizontal();

        GUILayout.Label(mStatus);
        GUI.DragWindow();
    }

    // Runs the action on this card's instance, looking its data up first if it's new
    void WithCard(Action<CardInstance> action)
    {
        string number = mCardNumber.Trim();
        PlayerState player = Player;
        if (number.Length == 0 || player == null) return;

        string key = player.playerIndex + "_" + number.ToUpperInvariant();
        if (mCards.TryGetValue(key, out CardInstance known))
        {
            action(known);
            return;
        }

        mStatus = "Looking up " + number + "...";
        void Apply(CardData data)
        {
            if (data == null)
            {
                mStatus = "No card found for " + number;
                return;
            }
            CardInstance card = new CardInstance(data);
            mCards[key] = card;
            mStatus = data.cardName;
            action(card);
        }
        CardData FromLocalCache() => ApiCardDatabase.Instance != null ? ApiCardDatabase.Instance.GetByNumber(number) : null;

        if (CardFirestoreDatabase.Instance != null)
            CardFirestoreDatabase.Instance.GetCardByNumber(number, Apply, _ => Apply(FromLocalCache()));
        else
            Apply(FromLocalCache());
    }
}

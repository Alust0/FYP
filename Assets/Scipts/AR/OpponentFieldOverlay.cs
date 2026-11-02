using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

// Online: lists the opponent's units, guardians and orders, which this PC's camera can't see
public class OpponentFieldOverlay : MonoBehaviour
{
    public TextMeshProUGUI fieldText;

    static readonly (string zone, string label)[] Circles =
    {
        ("Vanguard", "VG"), ("LeftFront", "Front L"), ("RightFront", "Front R"),
        ("LeftBack", "Back L"), ("CenterBack", "Back C"), ("RightBack", "Back R"),
    };

    void OnEnable()
    {
        GameState.OnGameStateUpdated += Refresh;
        GameState.OnCardPowerChanged += OnPowerChanged;
        GameState.OnCardOrientationChanged += OnCardChanged;
        GameState.OnBattleStarted += OnBattle;
    }

    void OnDisable()
    {
        GameState.OnGameStateUpdated -= Refresh;
        GameState.OnCardPowerChanged -= OnPowerChanged;
        GameState.OnCardOrientationChanged -= OnCardChanged;
        GameState.OnBattleStarted -= OnBattle;
    }

    void OnPowerChanged(string cardName, int basePower, int modifier) => Refresh(GameState.Instance);
    void OnCardChanged(CardInstance card) => Refresh(GameState.Instance);
    void OnBattle(int attack, int defence) => Refresh(GameState.Instance); // e.g. a boost

    void Refresh(GameState gs)
    {
        PlayerState opponent = gs != null ? gs.players[MatchSession.RemoteSeat] : null;
        if (fieldText == null || opponent == null) return;

        StringBuilder text = new StringBuilder();
        text.AppendLine("<b>" + opponent.playerName + "'s field</b>");

        foreach ((string zone, string label) in Circles)
        {
            CardInstance card = opponent.CardOnCircle(zone);
            text.AppendLine(label + ": " + (card == null ? "<color=#888>empty</color>" : Describe(card)));
        }

        if (opponent.guardianCircle.Count > 0)
            text.AppendLine("Guard: " + opponent.guardianCircle.Count + " (shield " + opponent.guardianCircle.Sum(c => c.data.shield) + ")");
        if (opponent.orderZone.Any(c => c.data.isOrderCard))
            text.AppendLine("Orders: " + string.Join(", ", opponent.orderZone.Where(c => c.data.isOrderCard).Select(c => c.data.DisplayName)));
        if (Prison.Imprisoned(opponent) > 0)
            text.AppendLine("Imprisoned: " + Prison.Imprisoned(opponent));
        text.Append("Soul " + opponent.CountInZone("Soul") + "   Damage " + opponent.CountInZone("Damage") + " (CB " + opponent.FaceUpDamage + ")");

        fieldText.text = text.ToString();
    }

    RulesEngine mRules;

    // A boosted attacker also shows the attack's power, e.g. "10000 (boosted 18000)"
    string Describe(CardInstance card)
    {
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();
        CardInstance booster = mRules != null && mRules.Attacker == card ? mRules.Booster : null;
        return card.data.DisplayName + " <color=#ff8>" + card.TotalPower +
            (booster != null ? " (boosted " + (card.TotalPower + booster.TotalPower) + ")" : "") + "</color>" +
            (card.orientation == CardOrientation.Rest ? " <color=#f88>[Rest]</color>" : "");
    }
}

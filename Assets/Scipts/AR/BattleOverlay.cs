using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Pops up during battle to show attack power vs defence power and the result
public class BattleOverlay : MonoBehaviour
{
    [Header("Panel")]
    public GameObject battlePanel;

    [Header("Text")]
    public TextMeshProUGUI attackerPowerText;
    public TextMeshProUGUI defenderPowerText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI shieldBreakdownText; // optional: shows guardian shield total

    void OnEnable()
    {
        GameState.OnBattleStarted += ShowBattleComparison;
        GameState.OnCloseStep += HideBattleComparison;
    }

    void OnDisable()
    {
        GameState.OnBattleStarted -= ShowBattleComparison;
        GameState.OnCloseStep -= HideBattleComparison;
    }

    // Shows the battle panel with power comparison and HIT or NO HIT result
    public void ShowBattleComparison(int attackPower, int defendPower)
    {
        if (battlePanel == null) return;

        battlePanel.SetActive(true);

        if (attackerPowerText != null) attackerPowerText.text = "ATK: " + attackPower;
        if (defenderPowerText != null) defenderPowerText.text = "DEF: " + defendPower;

        bool hits = attackPower >= defendPower; // attacker must meet or exceed

        if (resultText != null)
        {
            resultText.text = hits ? "HIT!" : "NO HIT";
            resultText.color = hits ? Color.red : Color.green; // red hit green blocked
        }
    }

    // Hides the battle panel after the close step
    public void HideBattleComparison()
    {
        if (battlePanel != null)
            battlePanel.SetActive(false);
    }
}

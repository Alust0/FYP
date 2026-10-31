using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* The battle's voice keywords as buttons, for when voice isn't working: only the ones the
 situation calls for, in a sidebar row (UISetup). First an [AUTO] skill's Activate / Skip / Later
 (SkillRules), or the step a used skill waits on (Done / Skip step, or which cost to pay), then picking an attack target, the defender's guard or no guard, the attacker's
 drive check, the defender's damage check; in the Main Phase, ready [ACT] skills and the
 Energy Blast draw (RulesEngine.EnergyDraw). A keyword button press goes
 through VoiceCommands like the spoken keyword, so it shows and reaches the other PC the same way.
 Online only this PC's player gets buttons; on one PC both players' do. */
public class ActionButtons : MonoBehaviour
{
    const int MaxButtons = 3;
    // Not voice keywords: answers to RulesEngine's question after a heal
    const string HealedFaceUp = "healed face up", HealedFaceDown = "healed face down";
    const string KeepTrigger = "keep trigger"; // and to leave a trigger's bonuses where they are
    const string EnergyDraw = "energy draw";
    const string LaterSkill = "later skill"; // ask about the next [AUTO] first

    readonly Button[] mButtons = new Button[MaxButtons];
    readonly TextMeshProUGUI[] mLabels = new TextMeshProUGUI[MaxButtons];
    readonly string[] mPhrases = new string[MaxButtons];
    TextMeshProUGUI mHint;
    RulesEngine mRules;

    // Called by UISetup once the row is in place
    public void Build(RectTransform row, TextMeshProUGUI hint)
    {
        mHint = hint;
        for (int i = 0; i < MaxButtons; i++)
        {
            GameObject obj = new GameObject("ActionButton" + i, typeof(RectTransform));
            obj.transform.SetParent(row, false);
            RectTransform rt = (RectTransform)obj.transform;
            rt.anchorMin = new Vector2(i / (float)MaxButtons, 0f);
            rt.anchorMax = new Vector2((i + 1) / (float)MaxButtons, 1f);
            rt.offsetMin = new Vector2(4f, 8f);
            rt.offsetMax = new Vector2(-4f, -8f);
            obj.AddComponent<Image>().color = new Color(0.85f, 0.35f, 0.2f, 0.95f);

            int index = i;
            Button button = obj.AddComponent<Button>();
            button.onClick.AddListener(() => Press(index));
            mButtons[i] = button;

            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(obj.transform, false);
            RectTransform labelRT = (RectTransform)labelObj.transform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = 22;
            label.raycastTarget = false;
            mLabels[i] = label;

            obj.SetActive(false);
        }
    }

    void Update()
    {
        if (mHint == null) return;
        if (mRules == null) mRules = FindFirstObjectByType<RulesEngine>();

        List<(string label, string phrase)> actions = Actions();
        for (int i = 0; i < MaxButtons; i++)
        {
            bool shown = i < actions.Count;
            if (mButtons[i].gameObject.activeSelf != shown) mButtons[i].gameObject.SetActive(shown);
            if (!shown) continue;
            mLabels[i].text = actions[i].label;
            mPhrases[i] = actions[i].phrase;
        }
        mHint.gameObject.SetActive(actions.Count == 0);
    }

    void Press(int index)
    {
        string phrase = mPhrases[index];
        if (phrase == HealedFaceUp) mRules?.SetHealedFacing(false);
        else if (phrase == HealedFaceDown) mRules?.SetHealedFacing(true);
        else if (phrase == KeepTrigger) mRules?.KeepPicks();
        else if (phrase == EnergyDraw) mRules?.EnergyDraw();
        else if (phrase == LaterSkill) mRules?.Skills?.Later();
        else if (phrase != null) VoiceCommands.Instance?.Press(phrase);
    }

    // The keywords that make sense right now, for players at this PC
    List<(string label, string phrase)> Actions()
    {
        var actions = new List<(string, string)>();
        GameState gs = GameState.Instance;
        if (mRules == null || mRules.Skills == null || gs == null || gs.players[0] == null) return actions;

        // An [AUTO] skill to answer comes before anything else, in any phase
        SkillRules skills = mRules.Skills;
        if (skills.Busy)
        {
            if (MatchSession.IsLocal(skills.PromptOwner))
            {
                actions.Add(("Activate" + (skills.PromptCost.Length > 0 ? "\n" + skills.PromptCost : ""), "activate skill"));
                actions.Add(("Skip", "skip skill"));
                if (skills.PromptCount > 1) actions.Add(("Later", LaterSkill));
            }
            return actions;
        }

        // A used skill's step waiting: which cost to pay, or done / skip
        if (skills.Stepping)
        {
            if (!MatchSession.IsLocal(skills.PromptOwner)) return actions;
            ChoiceCost choice = skills.ChoiceDue;
            if (choice != null)
            {
                actions.Add(("Pay\n" + choice.Options[0].ShortText, "pay first"));
                if (choice.Options.Count > 1) actions.Add(("Pay\n" + choice.Options[1].ShortText, "pay second"));
            }
            else
            {
                actions.Add(("Done", "skill done"));
                actions.Add(("Skip step", "skip step"));
            }
            return actions;
        }

        // [ACT] skills ready to use, then the Energy Blast draw
        if (gs.currentPhase == TurnPhase.MainPhase)
        {
            foreach ((CardInstance card, Skill skill) in skills.ReadyActs())
                actions.Add((card.data.cardName + "\n[ACT]" + (skill.Costs.Count > 0 ? " " + skill.ShortCostText : ""), VoiceCommands.ActivatePhrase(card.zone)));
            if (mRules.CanEnergyDraw) actions.Add(("Energy Blast " + RulesEngine.EnergyDrawCost + ":\nDraw 1", EnergyDraw));
            return actions;
        }
        if (gs.currentPhase != TurnPhase.BattlePhase) return actions;

        // A heal the camera couldn't place: which kind of damage card went
        PlayerState healer = mRules.HealFacingAsked;
        if (healer != null)
        {
            if (MatchSession.IsLocal(healer))
            {
                actions.Add(("Healed a\nface-up", HealedFaceUp));
                actions.Add(("Healed a\nface-down", HealedFaceDown));
            }
            return actions;
        }

        // A trigger being placed (on the lit circles): or leave it where it is
        if (mRules.PickNow != RulesEngine.TriggerPick.None)
        {
            if (MatchSession.IsLocal(mRules.PickOwner)) actions.Add(("Keep trigger\nwhere it is", KeepTrigger));
            return actions;
        }

        PlayerState attacker = mRules.AttackingPlayer;
        PlayerState defender = gs.Opponent;
        bool attackerHere = MatchSession.IsLocal(gs.CurrentPlayer);
        bool defenderHere = MatchSession.IsLocal(defender);

        if (attacker == null || mRules.Step == RulesEngine.BattleStep.Close) return actions; // no battle going

        if (mRules.IsChoosingTarget)
        {
            if (!attackerHere) return actions;
            if (defender.CardOnCircle("LeftFront") != null) actions.Add(("Attack\nLeft RG", "attack left"));
            actions.Add(("Attack\nVanguard", "attack vanguard"));
            if (defender.CardOnCircle("RightFront") != null) actions.Add(("Attack\nRight RG", "attack right"));
            return actions;
        }

        switch (mRules.Step)
        {
            case RulesEngine.BattleStep.Guard:
                if (!mRules.GuardSaid)
                {
                    if (defenderHere)
                    {
                        actions.Add(("Guard", "guard"));
                        actions.Add(("No Guard", "no guard"));
                    }
                }
                else if (mRules.VanguardAttacking)
                {
                    if (attackerHere) actions.Add(("Drive Check", "drive check")); // only once the guard is settled
                }
                else if (defenderHere && mRules.AttackHits) actions.Add(("Damage Check", "damage check")); // a rear-guard's attack that hits: no drive check
                break;

            case RulesEngine.BattleStep.Damage:
                if (mRules.DamageLeft == 0 && defenderHere) actions.Add(("Damage Check", "damage check"));
                break;
        }
        return actions;
    }
}

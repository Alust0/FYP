using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* The battle's voice keywords as buttons, for when voice isn't working: only the ones the
 situation calls for, in a sidebar row (UISetup). First an [AUTO] skill's Activate / Skip / Later
 (SkillRules), or the step a used skill waits on (Done / Skip step, or which cost to pay), then picking an attack target, the defender's guard or no guard, the attacker's
 drive check, the defender's damage check, and for a check card the camera didn't scan, whether it
 was a trigger and which (RulesEngine.UnscannedCheck); in the Main Phase, ready [ACT] skills and the
 Energy Blast draw (RulesEngine.EnergyDraw). A keyword button press goes
 through VoiceCommands like the spoken keyword, so it shows and reaches the other PC the same way.
 Online only this PC's player gets buttons; on one PC both players' do. */
public class ActionButtons : MonoBehaviour
{
    const int MaxButtons = 7; // up to 3 in one line, more in two
    // Not voice keywords: answers to RulesEngine's question after a heal
    const string HealedFaceUp = "healed face up", HealedFaceDown = "healed face down";
    const string KeepTrigger = "keep trigger"; // and to leave a trigger's bonuses where they are
    const string EnergyDraw = "energy draw";
    const string LaterSkill = "later skill"; // ask about the next [AUTO] first
    const string YourPartDone = "your part done"; // online: this PC's player did their part of the opponent's skill
    const string WhichTrigger = "which trigger", TriggerBack = "trigger back"; // the unscanned check card's trigger list, and back

    static readonly (string label, TriggerIcon icon)[] Triggers =
    {
        ("Critical", TriggerIcon.Critical), ("Draw", TriggerIcon.Draw), ("Front", TriggerIcon.Front),
        ("Heal", TriggerIcon.Heal), ("Over", TriggerIcon.Over), ("Stand", TriggerIcon.Stand),
    };

    readonly Button[] mButtons = new Button[MaxButtons];
    readonly TextMeshProUGUI[] mLabels = new TextMeshProUGUI[MaxButtons];
    readonly string[] mPhrases = new string[MaxButtons];
    TextMeshProUGUI mHint;
    RulesEngine mRules;
    bool mTriggerList; // "Trigger" pressed: the trigger types show
    int mLaidOut = -1;  // how many buttons the row is laid out for

    // Called by UISetup once the row is in place
    public void Build(RectTransform row, TextMeshProUGUI hint)
    {
        mHint = hint;
        for (int i = 0; i < MaxButtons; i++)
        {
            GameObject obj = new GameObject("ActionButton" + i, typeof(RectTransform));
            obj.transform.SetParent(row, false);
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
        if (actions.Count != mLaidOut) LayOut(actions.Count);
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

    // 3 to a line as before; more go in two lines
    void LayOut(int count)
    {
        mLaidOut = count;
        int rows = count > 3 ? 2 : 1;
        int columns = count > 3 ? (count + 1) / 2 : 3;
        float pad = rows > 1 ? 3f : 8f;
        for (int i = 0; i < MaxButtons; i++)
        {
            int row = i / columns, column = i % columns;
            RectTransform rt = (RectTransform)mButtons[i].transform;
            rt.anchorMin = new Vector2(column / (float)columns, 1f - (row + 1) / (float)rows);
            rt.anchorMax = new Vector2((column + 1) / (float)columns, 1f - row / (float)rows);
            rt.offsetMin = new Vector2(4f, pad);
            rt.offsetMax = new Vector2(-4f, -pad);
        }
    }

    void Press(int index)
    {
        string phrase = mPhrases[index];
        if (phrase == WhichTrigger || phrase == TriggerBack)
        {
            mTriggerList = phrase == WhichTrigger;
            return;
        }
        if (phrase != null && phrase.EndsWith(" trigger")) mTriggerList = false; // answered
        if (phrase == HealedFaceUp) mRules?.SetHealedFacing(false);
        else if (phrase == HealedFaceDown) mRules?.SetHealedFacing(true);
        else if (phrase == KeepTrigger) mRules?.KeepPicks();
        else if (phrase == EnergyDraw) mRules?.EnergyDraw();
        else if (phrase == LaterSkill) mRules?.Skills?.Later();
        else if (phrase == YourPartDone)
        {
            OnlineMatch.DoneOpponentStep();
            VoiceCommands.Instance?.Press("skill done"); // reaches the skill owner's PC (OpponentStep)
        }
        else if (phrase != null) VoiceCommands.Instance?.Press(phrase);
    }

    // The keywords that make sense right now, for players at this PC
    List<(string label, string phrase)> Actions()
    {
        var actions = new List<(string, string)>();
        GameState gs = GameState.Instance;
        if (mRules == null || mRules.Skills == null || gs == null || gs.players[0] == null) return actions;

        // Online: the opponent's skill waiting on this PC's player, first of all
        if (MatchSession.IsOnline && OnlineMatch.OpponentStepForYou != null)
        {
            actions.Add(("Done\n(your part)", YourPartDone));
            return actions;
        }

        // An [AUTO] skill to answer comes before anything else, in any phase
        SkillRules skills = mRules.Skills;
        if (skills.Busy)
        {
            if (MatchSession.IsLocal(skills.PromptOwner))
            {
                actions.Add(("Activate" + (skills.PromptCost.Length > 0 ? "\n" + skills.PromptCost : ""), "activate skill"));
                if (!skills.PromptMandatory) actions.Add(("Skip", "skip skill")); // one with no cost and no "may" has to be used
                if (skills.PromptCount > 1) actions.Add(("Later", LaterSkill));
            }
            return actions;
        }

        // A used skill's step waiting: which cost to pay, or done / skip
        if (skills.Stepping)
        {
            if (!MatchSession.IsLocal(skills.PromptOwner)) return actions;

            // The opponent's part: their Done, online on their PC (OnlineMatch); it can't be skipped
            if (skills.OpponentStepWaiting != null)
            {
                if (!MatchSession.IsOnline) actions.Add(("Done\n(opponent's part)", "skill done"));
                return actions;
            }
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

        // Another copy of the vanguard laid on it: the camera sees no change, so the player says
        if (gs.currentPhase == TurnPhase.RidePhase)
        {
            if (mRules.CanPersonaRide && MatchSession.IsLocal(gs.CurrentPlayer)) actions.Add(("Persona Ride\n(same card)", "persona ride"));
            return actions;
        }

        // [ACT] skills ready to use, then the Energy Blast draw
        if (gs.currentPhase == TurnPhase.MainPhase)
        {
            foreach ((CardInstance card, Skill skill) in skills.ReadyActs())
                actions.Add((card.data.DisplayName + "\n[ACT]" + (skill.Costs.Count > 0 ? " " + skill.ShortCostText : ""), VoiceCommands.ActivatePhrase(card.zone)));
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

        // A drive or damage check card due: if the camera didn't scan it, was it a trigger, and which
        PlayerState checker = mRules.CheckingPlayer;
        if (checker == null) mTriggerList = false;
        else
        {
            if (!MatchSession.IsLocal(checker)) return actions;
            if (!mTriggerList)
            {
                actions.Add(("Not scanned:\nNo trigger", VoiceCommands.TriggerPhrase(TriggerIcon.None)));
                actions.Add(("Not scanned:\nTrigger", WhichTrigger));
                return actions;
            }
            foreach ((string label, TriggerIcon icon) in Triggers) actions.Add((label, VoiceCommands.TriggerPhrase(icon)));
            actions.Add(("Back", TriggerBack));
            return actions;
        }

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
                    break;
                }
                // A perfect guard comes from a Sentinel placed on the Guardian circle, not a button
                if (mRules.VanguardAttacking)
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

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* Pops up when this PC's player Counter Blasts (a skill's cost or the HUD's CB -) or Counter Charges:
 their damage cards as DamageSlots remembers them, to pick which ones to flip. They pick, flip the
 real cards, and press Done; the damage bar (StatBar) then shows those gray (or red again). Added by
 UISetup. Its UI is built in CounterBlastPicker.UI.cs. */
public partial class CounterBlastPicker : MonoBehaviour
{
    GameObject mCanvasObj;
    TMP_Text mTitle, mHint;
    Button mDone;
    readonly List<(Button button, TMP_Text label, Image image)> mRows = new List<(Button, TMP_Text, Image)>();

    void Start() => BuildCanvas();

    void OnDestroy()
    {
        if (mCanvasObj != null) Destroy(mCanvasObj);
    }

    void Update()
    {
        PlayerState owner = DamageSlots.FlipOwner;
        bool show = owner != null && DamageSlots.FlipsDue != 0;
        if (mCanvasObj.activeSelf != show) mCanvasObj.SetActive(show);
        if (!show) return;

        int need = Mathf.Abs(DamageSlots.FlipsDue);
        bool down = DamageSlots.FlipsDue > 0;
        IReadOnlyList<DamageSlots.Slot> slots = DamageSlots.Of(owner);
        int canPick = slots.Count(DamageSlots.CanPick);
        int picked = DamageSlots.Picked.Count;

        mTitle.text = (down ? "Counter Blast " + need + ": choose " + need + " face-up damage card(s) to flip face down"
                            : "Counter Charge " + need + ": choose " + need + " face-down damage card(s) to turn face up");
        int left = Mathf.Min(need, canPick) - picked;
        mHint.text = left > 0 ? "Pick " + left + " more, flip the real card(s), then press Done." : "Flip the real card(s), then press Done.";
        mDone.interactable = left <= 0;

        for (int i = 0; i < mRows.Count; i++)
        {
            bool used = i < slots.Count;
            if (mRows[i].button.gameObject.activeSelf != used) mRows[i].button.gameObject.SetActive(used);
            if (!used) continue;

            DamageSlots.Slot slot = slots[i];
            bool isPicked = DamageSlots.Picked.Contains(slot);
            mRows[i].label.text = (i + 1) + ". " + (slot.card != null ? slot.card.DisplayName : "Unknown card") + (slot.faceDown ? "  (face down)" : "  (face up)") +
                (isPicked ? "  - chosen" : "");
            mRows[i].button.interactable = isPicked || DamageSlots.CanPick(slot);
            mRows[i].image.color = isPicked ? new Color(1f, 0.82f, 0.2f) : slot.faceDown ? new Color(0.45f, 0.45f, 0.5f) : new Color(0.75f, 0.12f, 0.12f);
            mRows[i].label.color = isPicked ? MenuUI.DarkText : Color.white;
        }
    }

    void Pick(int index)
    {
        PlayerState owner = DamageSlots.FlipOwner;
        if (owner == null || index >= DamageSlots.Of(owner).Count) return;
        DamageSlots.TogglePick(DamageSlots.Of(owner)[index]);
        GameState.Instance?.BroadcastState(); // the bars show the pick
    }

    void Done()
    {
        DamageSlots.FinishPicking();
        GameState.Instance?.BroadcastState();
    }
}

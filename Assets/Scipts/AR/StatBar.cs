using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/* A HUD count as a bar, in place of its text line (UISetup, PhaseHUDOverlay):
 - Damage: 6 slanted segments, black when empty, red for a face-up damage card, gray for a face-down
   one (Counter Blast), from DamageSlots; hovering one names the card. The number badge glows red at 6.
 - Energy: a battery of 10 green cells.
 The damage bar's border is blue for the first seat and red for the second; energy's is green. Its UI is built in StatBar.UI.cs. */
public partial class StatBar : MonoBehaviour
{
    static readonly Color Empty = new Color(0.08f, 0.08f, 0.09f);
    static readonly Color FaceUp = new Color(0.9f, 0.08f, 0.08f);
    static readonly Color FaceDown = new Color(0.55f, 0.55f, 0.58f);
    static readonly Color Picked = new Color(1f, 0.82f, 0.2f); // chosen in CounterBlastPicker, not flipped yet
    static readonly Color Energy = new Color(0.15f, 0.9f, 0.2f);
    static readonly Color EnergyEmpty = new Color(0.22f, 0.24f, 0.26f);
    public static readonly Color[] SeatBorder = { new Color(0.25f, 0.55f, 1f), new Color(1f, 0.25f, 0.25f) };
    static readonly Color EnergyBorder = new Color(0.2f, 0.85f, 0.3f);

    int mPlayer;
    bool mDamage;
    readonly List<ShapeGraphic> mSegments = new List<ShapeGraphic>();
    ShapeGraphic mBadge;
    TextMeshProUGUI mNumber;
    string mHover; // the hovered segment's text, or null

    public int Size => mDamage ? PlayerState.MaxDamage : RulesEngine.MaxEnergy;

    // After a count change (PhaseHUDOverlay)
    public void Refresh(GameState gs)
    {
        PlayerState player = gs.players[mPlayer];
        if (player == null) return;

        int count;
        if (mDamage)
        {
            DamageSlots.Reconcile(gs, player);
            IReadOnlyList<DamageSlots.Slot> slots = DamageSlots.Of(player);
            count = slots.Count;
            for (int i = 0; i < mSegments.Count; i++)
                mSegments[i].color = i >= slots.Count ? Empty
                    : IsPicked(slots[i]) ? Picked
                    : slots[i].faceDown ? FaceDown : FaceUp;
        }
        else
        {
            count = player.CountInZone("Energy");
            for (int i = 0; i < mSegments.Count; i++) mSegments[i].color = i < count ? Energy : EnergyEmpty;
        }

        mNumber.text = count.ToString();
        bool alarm = mDamage && count >= PlayerState.MaxDamage - 1;
        mBadge.color = alarm ? new Color(0.75f, 0.05f, 0.05f) : new Color(0.93f, 0.93f, 0.95f);
        mNumber.color = alarm ? Color.white : new Color(0.35f, 0.05f, 0.08f);
    }

    static bool IsPicked(DamageSlots.Slot slot)
    {
        foreach (DamageSlots.Slot picked in DamageSlots.Picked) if (picked == slot) return true;
        return false;
    }

    void Update()
    {
        // 6 damage: the badge pulses
        if (mDamage && mNumber.text == PlayerState.MaxDamage.ToString())
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            mBadge.color = Color.Lerp(new Color(0.6f, 0f, 0f), new Color(1f, 0.25f, 0.2f), pulse);
        }
        if (mHover != null) Tooltip.Show(mHover);
    }

    // A segment hovered (StatBarHover): its card for damage, the count for energy
    internal void Hover(int index, bool on)
    {
        if (!on)
        {
            mHover = null;
            Tooltip.Hide();
            return;
        }
        GameState gs = GameState.Instance;
        PlayerState player = gs != null ? gs.players[mPlayer] : null;
        if (player == null) return;
        if (!mDamage)
        {
            mHover = "Energy " + player.CountInZone("Energy") + " / " + RulesEngine.MaxEnergy;
            return;
        }
        IReadOnlyList<DamageSlots.Slot> slots = DamageSlots.Of(player);
        mHover = index < slots.Count ? "Damage " + (index + 1) + ": " + DamageSlots.Describe(slots[index]) : "Damage " + (index + 1) + ": empty";
    }

    void OnDisable()
    {
        if (mHover != null) Tooltip.Hide();
        mHover = null;
    }
}

// Tells its StatBar a segment is under the pointer
public class StatBarHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public StatBar bar;
    public int index;

    public void OnPointerEnter(PointerEventData eventData) => bar.Hover(index, true);
    public void OnPointerExit(PointerEventData eventData) => bar.Hover(index, false);
}

// One small text box by the mouse pointer, on its own canvas over everything
public static class Tooltip
{
    static RectTransform sBox;
    static TextMeshProUGUI sText;

    public static void Show(string text)
    {
        if (sBox == null) Build();
        sBox.gameObject.SetActive(true);
        sText.text = text;
        Vector2 mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Vector2 size = sText.GetPreferredValues(text, 360f, 0f) + new Vector2(16f, 10f);
        sBox.sizeDelta = new Vector2(Mathf.Min(size.x, 376f), size.y);
        // Left of the pointer, as the HUD is at the screen's right edge
        sBox.position = mouse + new Vector2(-sBox.sizeDelta.x - 12f, 12f);
    }

    public static void Hide()
    {
        if (sBox != null) sBox.gameObject.SetActive(false);
    }

    static void Build()
    {
        var canvasObj = new GameObject("TooltipCanvas", typeof(RectTransform));
        Object.DontDestroyOnLoad(canvasObj);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        sBox = MenuUI.AddBox(canvasObj.transform, "Tooltip", new Color(0.05f, 0.06f, 0.08f, 0.95f));
        sBox.anchorMin = sBox.anchorMax = sBox.pivot = Vector2.zero;
        sBox.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        sText = (TextMeshProUGUI)MenuUI.AddText(sBox, "", 16, Color.white, TextAlignmentOptions.Left);
        MenuUI.Place(sText.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 5f), new Vector2(-8f, -5f));
        sText.raycastTarget = false;
    }
}

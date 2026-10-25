using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using Vuforia;

/* Hand gestures: printed pads beside the mat (Printables/HoverPads_A4.pdf, images made by
 Tools/make_hover_pads.py) that Vuforia tracks like the deck cards. Holding a hand over a pad
 hides it from the camera; once it has stayed hidden for holdSeconds its action runs, the same
 as the HUD buttons. A pad has to be seen again before it can fire a second time. Added by UISetup. */
public class HoverPads : MonoBehaviour
{
    // Also the image names in StreamingAssets/HoverPads
    public enum PadAction { NextPhase, Draw, SoulCharge, CounterBlast }

    [Tooltip("Printed width of each pad in metres; the print sheet makes them 6 cm.")]
    public float padSize = 0.06f;
    [Tooltip("Seconds a pad must stay covered before its action runs.")]
    public float holdSeconds = 0.8f;
    [Tooltip("Seconds a pad must be seen before it can fire, so one flickering into view doesn't count.")]
    public float armSeconds = 1f;

    const string Folder = "HoverPads";
    const float CanvasSize = 600f;

    class Pad
    {
        public PadAction action;
        public bool everSeen;
        public bool tracked;
        public float trackedSince; // Time.time it was last found
        public float lostSince; // Time.time it was last lost
        public bool armed; // seen for armSeconds since it last fired
        public GameObject overlay;
        public RectTransform progressFill;
    }

    readonly List<Pad> mPads = new List<Pad>();

    IEnumerator Start()
    {
        if (VuforiaBehaviour.Instance == null)
        {
            enabled = false;
            yield break;
        }
        yield return new WaitUntil(() => VuforiaApplication.Instance.IsRunning);

        foreach (PadAction action in Enum.GetValues(typeof(PadAction)))
        {
            string path = Path.Combine(Application.streamingAssetsPath, Folder, action + ".png");
            if (!File.Exists(path))
            {
                Debug.LogWarning("[HoverPads] No pad image at " + path + " (run Tools/make_hover_pads.py)");
                continue;
            }

            ImageTargetBehaviour target = VuforiaBehaviour.Instance.ObserverFactory.CreateImageTarget(path, padSize, "HoverPad_" + action);
            if (target == null) continue;

            Pad pad = new Pad { action = action };
            BuildOverlay(pad, target);
            target.OnTargetStatusChanged += (_, status) => SetTracked(pad, status.Status == Status.TRACKED);
            mPads.Add(pad);
        }

        Debug.Log("[HoverPads] " + mPads.Count + " pads ready.");
    }

    void SetTracked(Pad pad, bool tracked)
    {
        if (tracked == pad.tracked) return;
        pad.tracked = tracked;
        pad.everSeen |= tracked;
        if (tracked) pad.trackedSince = Time.time;
        else pad.lostSince = Time.time;
    }

    void Update()
    {
        foreach (Pad pad in mPads)
        {
            if (pad.tracked)
            {
                if (!pad.armed && Time.time - pad.trackedSince >= armSeconds) pad.armed = true;
                ShowPad(pad, true, 0f);
                continue;
            }

            // Vuforia leaves a lost target where it was last seen, so the overlay can show the hold there
            if (!pad.armed || !OtherPadInView(pad))
            {
                ShowPad(pad, false, 0f);
                continue;
            }

            float held = (Time.time - pad.lostSince) / holdSeconds;
            ShowPad(pad, true, held);
            if (held < 1f) continue;

            pad.armed = false;
            Fire(pad.action);
        }
    }

    /* With more than one pad on the table, one going missing while the others stay in view is a
     hand; all of them going at once is the camera (moved, blocked, too dark), not a press. */
    bool OtherPadInView(Pad pad)
    {
        List<Pad> others = mPads.Where(p => p != pad && p.everSeen).ToList();
        return others.Count == 0 || others.Any(p => p.tracked);
    }

    void Fire(PadAction action)
    {
        GameState gs = GameState.Instance;
        if (gs == null || gs.players[0] == null) return;

        // Online it's always this PC's player; on one PC, the turn player's
        PlayerState player = MatchSession.IsOnline ? gs.players[MatchSession.LocalSeat] : gs.CurrentPlayer;

        switch (action)
        {
            case PadAction.NextPhase:
                FindFirstObjectByType<RulesEngine>()?.AdvancePhase();
                break;

            case PadAction.Draw:
                UISetup.AdjustZoneCount(player.playerIndex, "Deck", -1);
                Notice("Draw", player.playerName + " drew 1 card.");
                break;

            case PadAction.SoulCharge:
                UISetup.AdjustZoneCount(player.playerIndex, "Deck", -1);
                UISetup.AdjustZoneCount(player.playerIndex, "Soul", 1);
                Notice("Soul Charge", player.playerName + " put the top card of their deck into the soul.");
                break;

            case PadAction.CounterBlast:
                if (player.FaceUpDamage == 0)
                {
                    Notice("Counter Blast", player.playerName + " has no face-up damage to pay with.");
                    break;
                }
                UISetup.AdjustZoneCount(player.playerIndex, "CB", -1);
                Notice("Counter Blast", player.playerName + " paid 1. " + player.FaceUpDamage + " face-up damage left.");
                break;
        }
    }

    static void Notice(string title, string body)
    {
        Debug.Log("[HoverPads] " + title + ": " + body);
        GameState.OnRuleNotice?.Invoke(title, body);
    }

    void ShowPad(Pad pad, bool visible, float progress)
    {
        if (pad.overlay.activeSelf != visible) pad.overlay.SetActive(visible);
        pad.progressFill.localScale = new Vector3(Mathf.Clamp01(progress), 1f, 1f);
    }

    /* Flat on the pad like the card overlays: the pad's name just past its top edge, and a bar
     across the middle that fills while it's covered. */
    void BuildOverlay(Pad pad, ImageTargetBehaviour target)
    {
        GameObject canvasObj = new GameObject("HoverPadOverlay", typeof(RectTransform));
        canvasObj.transform.SetParent(target.transform, false);
        canvasObj.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        canvasObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        canvasObj.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasObj.transform).sizeDelta = new Vector2(CanvasSize, CanvasSize);

        float targetScale = target.transform.lossyScale.x;
        float width = target.GetSize().x;
        canvasObj.transform.localScale = Vector3.one * (width > 0f && targetScale > 0f ? width / (CanvasSize * targetScale) : 0.0001f);

        RectTransform label = AddRect(canvasObj.transform, "Label", new Vector2(0f, 1.02f), new Vector2(1f, 1.3f), new Color(0f, 0f, 0f, 0.7f));
        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(label, false);
        RectTransform textRT = (RectTransform)textObj.transform;
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = textRT.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = Label(pad.action);
        text.fontSize = 90;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;

        RectTransform bar = AddRect(canvasObj.transform, "ProgressBar", new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.58f), new Color(0f, 0f, 0f, 0.6f));
        pad.progressFill = AddRect(bar, "Fill", Vector2.zero, Vector2.one, new Color(0.2f, 1f, 0.4f, 0.9f));
        pad.progressFill.pivot = new Vector2(0f, 0.5f); // fills left to right

        pad.overlay = canvasObj;
        canvasObj.SetActive(false);
    }

    // A filled rectangle stretched between anchors (fractions of the parent)
    static RectTransform AddRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        obj.AddComponent<UnityEngine.UI.Image>().color = color; // Vuforia has an Image type too
        return rt;
    }

    static string Label(PadAction action)
    {
        switch (action)
        {
            case PadAction.NextPhase: return "Next Phase";
            case PadAction.SoulCharge: return "Soul Charge";
            case PadAction.CounterBlast: return "Counter Blast";
            default: return action.ToString();
        }
    }
}

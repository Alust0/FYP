using TMPro;
using UnityEngine;
using UnityEngine.UI;

/* CounterBlastPicker's canvas: a panel over the camera views with a row per damage card and Done.
 The rest of CounterBlastPicker is in CounterBlastPicker.cs. */
public partial class CounterBlastPicker
{
    const float RowHeight = 40f;

    void BuildCanvas()
    {
        mCanvasObj = new GameObject("CounterBlastCanvas", typeof(RectTransform));
        Canvas canvas = mCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // over the board pickers
        CanvasScaler scaler = mCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        mCanvasObj.AddComponent<GraphicRaycaster>();

        // In the middle of the camera views, left of the sidebar
        float centre = (1f - SplitScreenView.SidebarWidth) * 0.5f;
        float height = 94f + PlayerState.MaxDamage * (RowHeight + 6f) + 64f; // title and hint, the rows, Done
        RectTransform panel = MenuUI.AddBox(mCanvasObj.transform, "Panel", new Color(0.1f, 0.11f, 0.15f, 0.97f));
        MenuUI.Place(panel, new Vector2(centre, 0.5f), new Vector2(centre, 0.5f), new Vector2(-260f, -height * 0.5f), new Vector2(260f, height * 0.5f));

        mTitle = MenuUI.AddText(panel, "", 20, MenuUI.Accent, TextAlignmentOptions.TopLeft);
        MenuUI.PlaceTop(mTitle.rectTransform, 0f, 1f, 12f, 52f, 16f, 16f);
        mHint = MenuUI.AddText(panel, "", 16, MenuUI.Muted, TextAlignmentOptions.TopLeft);
        MenuUI.PlaceTop(mHint.rectTransform, 0f, 1f, 64f, 24f, 16f, 16f);

        for (int i = 0; i < PlayerState.MaxDamage; i++)
        {
            int index = i;
            Button row = MenuUI.AddButton(panel, "", () => Pick(index), Color.gray, Color.white, 18);
            MenuUI.PlaceTop((RectTransform)row.transform, 0f, 1f, 94f + i * (RowHeight + 6f), RowHeight, 16f, 16f);
            TMP_Text label = row.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.Left;
            label.margin = new Vector4(12f, 0f, 12f, 0f);
            mRows.Add((row, label, row.GetComponent<Image>()));
        }

        mDone = MenuUI.AddButton(panel, "Done", Done, new Color(0.85f, 0.35f, 0.2f), Color.white, 20);
        RectTransform done = (RectTransform)mDone.transform;
        MenuUI.Place(done, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-156f, 12f), new Vector2(-16f, 52f));

        mCanvasObj.SetActive(false);
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The sign-up page's "Check your email" panel, built in code over the scene's canvas
public partial class LoginController
{
    GameObject mCodePanel;
    TMP_Text mCodeMessage;
    TMP_InputField mCodeInput;
    Button mResendButton;

    void ShowCodePanel()
    {
        if (mCodePanel == null) BuildCodePanel();
        mCodePanel.transform.SetAsLastSibling();
        mCodePanel.SetActive(true);
        mCodeInput.text = "";
        mCodeMessage.text = CodeSentText();
        mCodeInput.ActivateInputField();
    }

    void HideCodePanel()
    {
        if (mCodePanel != null) mCodePanel.SetActive(false);
    }

    // The build's name (Player Settings' Product Name) across the top of the page
    void AddTitle()
    {
        TMP_Text title = MenuUI.AddText(statusText.canvas.transform, Application.productName, 72, MenuUI.Accent, TextAlignmentOptions.Center);
        title.name = "Title";
        title.fontStyle = FontStyles.Bold;
        title.enableAutoSizing = true;
        title.fontSizeMin = 36;
        title.fontSizeMax = 72;
        title.raycastTarget = false;
        // Margins clear SceneSettings' gear in the top-right corner
        MenuUI.PlaceTop(title.rectTransform, 0f, 1f, 30f, 100f, 120f, 120f);
    }

    string CodeSentText() =>
        $"We sent a 6-digit code to\n<b>{mOtp.email}</b>\nIt works for {EmailOtp.ValidMinutes} minutes. Check your spam folder too.";

    void BuildCodePanel()
    {
        // Dark cover over the whole page, so nothing behind it can be clicked
        RectTransform cover = MenuUI.AddBox(statusText.canvas.transform, "Email Code", new Color(0f, 0f, 0f, 0.6f));
        MenuUI.Place(cover, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        mCodePanel = cover.gameObject;

        Vector2 middle = new Vector2(0.5f, 0.5f);
        RectTransform panel = MenuUI.AddBox(cover, "Panel", MenuUI.PanelColor);
        MenuUI.Place(panel, middle, middle, new Vector2(-340f, -220f), new Vector2(340f, 220f));

        TMP_Text title = MenuUI.AddText(panel, "Check your email", 36, MenuUI.Accent, TextAlignmentOptions.Center);
        MenuUI.PlaceTop(title.rectTransform, 0f, 1f, 24f, 50f, 20f, 20f);

        mCodeMessage = MenuUI.AddText(panel, "", 24, Color.white, TextAlignmentOptions.Center);
        MenuUI.PlaceTop(mCodeMessage.rectTransform, 0f, 1f, 84f, 110f, 30f, 30f);

        mCodeInput = MenuUI.AddInput(panel, "6-digit code");
        mCodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        mCodeInput.characterLimit = 6;
        mCodeInput.textComponent.fontSize = 40;
        mCodeInput.textComponent.alignment = TextAlignmentOptions.Center;
        if (mCodeInput.placeholder is TMP_Text hint) hint.alignment = TextAlignmentOptions.Center;
        mCodeInput.onSubmit.AddListener(_ => OnVerifyClicked());
        MenuUI.PlaceTop((RectTransform)mCodeInput.transform, 0.5f, 0.5f, 210f, 70f, -160f, -160f);

        AddPanelButton(panel, "Verify", OnVerifyClicked, MenuUI.Accent, MenuUI.DarkText, -210f);
        mResendButton = AddPanelButton(panel, "Resend", OnResendClicked, MenuUI.RowColor, Color.white, 0f);
        AddPanelButton(panel, "Cancel", OnCodeCancelled, MenuUI.RowColor, Color.white, 210f);
    }

    // One of the three buttons along the panel's bottom, centred x px from the middle
    static Button AddPanelButton(RectTransform panel, string label, UnityEngine.Events.UnityAction onClick, Color background, Color text, float x)
    {
        Button button = MenuUI.AddButton(panel, label, onClick, background, text, 26);
        MenuUI.Place((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(x - 95f, 30f), new Vector2(x + 95f, 95f));
        return button;
    }
}

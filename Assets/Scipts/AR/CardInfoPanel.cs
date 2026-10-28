using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/* The sidebar's card panel: picture, stats, type and skill of the last card put on a zone by
 either player, or of a card whose i was tapped. Built by UISetup. */
public class CardInfoPanel : MonoBehaviour
{
    public RawImage art;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statsText;
    public TextMeshProUGUI typeText;
    public TextMeshProUGUI clanText;
    public TextMeshProUGUI skillText;

    public static CardInfoPanel Instance { get; private set; }

    // Card pictures by card number; null once a download failed
    static readonly Dictionary<string, Texture2D> sArt = new Dictionary<string, Texture2D>();
    static readonly HashSet<string> sLoading = new HashSet<string>();

    CardData mShown;
    Vector2 mArtBox;

    void Awake()
    {
        Instance = this;
        if (art != null) mArtBox = art.rectTransform.sizeDelta;
    }

    void OnEnable() => GameState.OnCardMoved += OnCardMoved;
    void OnDisable() => GameState.OnCardMoved -= OnCardMoved;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // A card landing on a zone, seen by this PC's camera or synced from the opponent's
    void OnCardMoved(CardInstance card, PlayerState previousOwner, string previousZone)
    {
        if (card == null || card.zone == null || card.zone == previousZone) return;
        if (card.facing == CardFacing.FaceDown) return;
        Show(card.data);
    }

    public void Show(CardData data)
    {
        if (data == null) return;
        mShown = data;

        string name = !string.IsNullOrEmpty(data.cardNameEn) ? data.cardNameEn : data.cardName;
        nameText.text = name;
        statsText.text = "G" + data.grade + "  /  " + data.power + "  /  " + data.shield + "  /  " + data.critical + "C";

        string role = data.hasBoost ? " / Boost" : data.hasIntercept ? " / Intercept" : data.drive >= 2 ? " / Twin Drive" : "";
        typeText.text = CardOverlayUI.TypeLabel(data) + role + (data.hasSentinel ? " / Sentinel" : "");
        clanText.text = string.Join(" / ", new[] { data.nation, data.clan, data.race }.Where(s => !string.IsNullOrEmpty(s)));

        string trigger = CardOverlayUI.TriggerEffect(data.triggerIcon);
        string skill = string.IsNullOrWhiteSpace(data.abilityText) ? "<i>No skill.</i>" : CardOverlayUI.FormatSkill(data.abilityText, "#7fb2ff");
        skillText.text = trigger == null ? skill
            : "<color=#ff8a80><b>" + data.triggerIcon + " Trigger:</b> " + trigger + "</color>\n\n" + skill;

        ShowArt(data);
    }

    void ShowArt(CardData data)
    {
        if (art == null) return;
        Texture2D texture = Art(data);
        art.texture = texture;
        art.color = texture != null ? Color.white : new Color(1f, 1f, 1f, 0.08f);

        // Sideways cards (e.g. some orders) fit the box's width instead of its height
        bool landscape = texture != null && texture.width > texture.height;
        art.rectTransform.sizeDelta = landscape ? new Vector2(mArtBox.x, mArtBox.x * texture.height / texture.width) : mArtBox;
    }

    // From StreamingAssets/cards first, else downloaded from the card's image link
    Texture2D Art(CardData data)
    {
        string number = data.cardNumber;
        if (string.IsNullOrEmpty(number)) return null;
        if (sArt.TryGetValue(number, out Texture2D texture)) return texture;

        string path = CardImageLibrary.GetPath(number);
        if (path != null)
        {
            texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(path));
            sArt[number] = texture;
            return texture;
        }

        if (!string.IsNullOrEmpty(data.imageUrl) && sLoading.Add(number))
            StartCoroutine(Download(data));
        return null;
    }

    IEnumerator Download(CardData data)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(data.imageUrl))
        {
            yield return request.SendWebRequest();
            sArt[data.cardNumber] = request.result == UnityWebRequest.Result.Success ? DownloadHandlerTexture.GetContent(request) : null;
        }
        if (mShown == data) ShowArt(data);
    }
}

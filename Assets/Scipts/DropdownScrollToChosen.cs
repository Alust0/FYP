using UnityEngine;
using UnityEngine.UI;

/* Goes on a TMP_Dropdown's template, so the list the dropdown copies from it opens scrolled to the
 chosen option (centred where it can be) instead of the top. Added by MenuUI.StyleDropdown */
public class DropdownScrollToChosen : MonoBehaviour
{
    // The dropdown has made its items by Start
    void Start()
    {
        ScrollRect scroll = GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null) return;
        RectTransform content = scroll.content;
        RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)transform;

        RectTransform chosen = null;
        foreach (Toggle item in content.GetComponentsInChildren<Toggle>())
            if (item.isOn) { chosen = (RectTransform)item.transform; break; }
        if (chosen == null) return;

        // Content's pivot is its top, so its y is how far it's scrolled down
        float fromTop = -(chosen.localPosition.y + chosen.rect.yMax);
        float view = viewport.rect.height;
        float y = Mathf.Clamp(fromTop - (view - chosen.rect.height) / 2f, 0f, Mathf.Max(0f, content.rect.height - view));
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
    }
}

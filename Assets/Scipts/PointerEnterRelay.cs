using System;
using UnityEngine;
using UnityEngine.EventSystems;

/* Runs an action when the pointer moves onto this UI object. Unlike EventTrigger it handles only
 that, so scroll and drag still reach the ScrollRect behind it. */
public class PointerEnterRelay : MonoBehaviour, IPointerEnterHandler
{
    public Action onEnter;

    public void OnPointerEnter(PointerEventData eventData) => onEnter?.Invoke();
}

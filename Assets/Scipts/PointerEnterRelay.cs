using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Runs an action on pointer enter, letting scroll and drag through
public class PointerEnterRelay : MonoBehaviour, IPointerEnterHandler
{
    public Action onEnter;

    public void OnPointerEnter(PointerEventData eventData) => onEnter?.Invoke();
}

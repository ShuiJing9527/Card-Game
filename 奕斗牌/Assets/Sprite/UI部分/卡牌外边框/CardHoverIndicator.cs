using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class CardHoverIndicator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    RectTransform rt;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 只有在有人正在拖拽时才响应
        if (CardDragHandler.currentDragging == null) return;
        if (CardDragHandler.currentDragging.transform == transform) return;

        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SetHoverOverride(rt);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (CardDragHandler.currentDragging == null) return;

        if (SelectionManager.Instance != null)
            SelectionManager.Instance.ClearHoverOverride();
    }

    void OnDisable()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.ClearHoverOverride();
    }
}
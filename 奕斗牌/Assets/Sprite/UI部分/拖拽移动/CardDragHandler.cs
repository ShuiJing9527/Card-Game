using UnityEngine;
using UnityEngine.EventSystems;

public class CardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Card identity (restored for external callers)")]
    public int cardId;               // <--- 恢复这个字段，外部脚本可直接访问
    public int CardId => cardId;     // 只读属性（兼容其它访问方式）

    [Header("Optional: 指定拖拽时放置卡片的 root（若为空会自动查找父 Canvas 的 transform）")]
    public Transform dragRoot;

    [Header("When true, attach selection to dragRoot during drag to avoid masking/sorting issues")]
    public bool attachSelectionToDragRoot = true;

    // 当前正在拖拽的卡片（全局便于其它系统查询）
    public static CardDragHandler currentDragging;

    RectTransform rectTransform;
    Canvas rootCanvas;
    RectTransform canvasRect;
    Vector2 pointerOffset;
    Transform originalParent;
    int originalSiblingIndex;
    CanvasGroup cg;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        rootCanvas = GetComponentInParent<Canvas>();

        if (rootCanvas != null && dragRoot == null)
        {
            dragRoot = rootCanvas.transform;
            canvasRect = dragRoot as RectTransform;
        }
        else if (dragRoot != null)
        {
            canvasRect = dragRoot as RectTransform;
            rootCanvas = dragRoot.GetComponentInParent<Canvas>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        currentDragging = this;

        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();

        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;

        // 若 inspector 没指定 dragRoot，尝试重新查找父 Canvas
        if (dragRoot == null)
        {
            rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null)
            {
                dragRoot = rootCanvas.transform;
                canvasRect = dragRoot as RectTransform;
            }
        }

        // 把卡片移到 root（保持世界位置）
        if (dragRoot != null)
            transform.SetParent(dragRoot, true);

        // 计算 pointerOffset（相对于 canvasRect 的 anchoredPosition 偏移）
        Vector2 localPointerPos;
        var cam = eventData.pressEventCamera;
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, cam, out localPointerPos))
        {
            pointerOffset = rectTransform.anchoredPosition - localPointerPos;
        }
        else
        {
            pointerOffset = Vector2.zero;
        }

        // 触发 SelectionManager：可选把 selection 附着到 dragRoot，随后显示 overlay
        if (SelectionManager.Instance != null)
        {
            if (attachSelectionToDragRoot && dragRoot != null)
            {
                SelectionManager.Instance.AttachSelectionTo(dragRoot, true);
            }
            SelectionManager.Instance.ShowFor(rectTransform);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (currentDragging != this) return;
        if (canvasRect == null) return;

        Vector2 localPointerPos;
        var cam = eventData.pressEventCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, cam, out localPointerPos))
        {
            rectTransform.anchoredPosition = localPointerPos + pointerOffset;

            // 立即同步 selection 的 transform（防止不跟随或滞后）
            if (SelectionManager.Instance != null && SelectionManager.Instance.ActiveSelection != null)
            {
                SelectionManager.Instance.UpdateSelectionTransform(SelectionManager.Instance.ActiveSelection, rectTransform);
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (currentDragging == this) currentDragging = null;

        if (cg != null) cg.blocksRaycasts = true;

        // 如果卡片当前仍在 dragRoot，则把它放回原父级（常规恢复逻辑）
        if (transform.parent == dragRoot)
        {
            if (originalParent != null)
            {
                transform.SetParent(originalParent, false);
                transform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount));
            }
        }

        // 隐藏 SelectionManager 的 overlay，并尝试恢复 selection 的原始附着（若之前用 AttachSelectionTo）
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }

    // 外部放置逻辑可调用：把卡片放到指定父级（比如 drop area），并让 SelectionManager 隐藏 overlay
    public void OnDroppedTo(Transform dropParent)
    {
        if (dropParent == null) dropParent = originalParent;
        transform.SetParent(dropParent, false);
        if (cg != null) cg.blocksRaycasts = true;

        // 可重置位置/旋转/缩放以适应放置点
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }
}
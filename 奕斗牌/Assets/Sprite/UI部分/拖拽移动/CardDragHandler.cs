using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class CardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Card identity (optional)")]
    public int cardId;
    public int CardId => cardId;

    [Header("Drag root (optional). If null, will use parent Canvas transform)")]
    public Transform dragRoot;

    [Header("Whether to attach selection to dragRoot during drag")]
    public bool attachSelectionToDragRoot = true;

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
        cg.blocksRaycasts = false; // 让射线穿透到下层

        if (dragRoot == null)
        {
            rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas != null)
            {
                dragRoot = rootCanvas.transform;
                canvasRect = dragRoot as RectTransform;
            }
        }

        if (dragRoot != null)
            transform.SetParent(dragRoot, true);

        // 计算偏移（以便鼠标抓在卡片任意位置时看起来自然）
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

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();

            if (attachSelectionToDragRoot && dragRoot != null)
            {
                SelectionManager.Instance.AttachSelectionTo(dragRoot, true);
            }

            // 一开始外框指向被拖拽的卡
            SelectionManager.Instance.SetHoverOverride(rectTransform);

            // 确保 ordering：先把 selection 放到最后，再把拖拽卡放到最后 -> 卡片在外框之上
            TrySafeBringToTop(SelectionManager.Instance.ActiveSelection);
            TrySafeBringToTop(transform);
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

            // 尽量同步外框位置（如果 selection 仍指向拖拽卡）
            if (SelectionManager.Instance != null && SelectionManager.Instance.ActiveSelection != null)
            {
                SelectionManager.Instance.UpdateSelectionTransform(SelectionManager.Instance.ActiveSelection, rectTransform);
            }
        }

        // 使用 EventSystem.RaycastAll 检测鼠标下的 UI（排除自己）
        if (EventSystem.current != null && SelectionManager.Instance != null)
        {
            PointerEventData ped = new PointerEventData(EventSystem.current);
            ped.position = eventData.position;
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);

            RectTransform hoveredCard = null;
            foreach (var r in results)
            {
                if (r.gameObject == null) continue;

                // 跳过自身（拖拽的卡片），因为我们要找下面的卡
                var handler = r.gameObject.GetComponentInParent<CardDragHandler>();
                if (handler != null)
                {
                    if (handler == this) continue; // 跳过自己
                    hoveredCard = handler.GetComponent<RectTransform>();
                    break;
                }

                // 如果卡片用不同脚本标识（比如 CardComponent），可以在此处改为 GetComponentInParent<CardComponent>()
            }

            if (hoveredCard != null)
            {
                // 当检测到其它卡片时，把外框临时指向它
                SelectionManager.Instance.SetHoverOverride(hoveredCard);

                // ordering: selection 下，拖拽卡在上
                TrySafeBringToTop(SelectionManager.Instance.ActiveSelection);
                TrySafeBringToTop(transform);
            }
            else
            {
                // 没有命中任何卡片 -> 显式把外框指回当前拖拽的卡片（而不是 ClearHoverOverride）
                SelectionManager.Instance.SetHoverOverride(rectTransform);

                TrySafeBringToTop(SelectionManager.Instance.ActiveSelection);
                TrySafeBringToTop(transform);
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (currentDragging == this) currentDragging = null;

        if (cg != null) cg.blocksRaycasts = true;

        // 把卡片放回原父级（如果仍在 dragRoot）
        if (transform.parent == dragRoot)
        {
            if (originalParent != null)
            {
                transform.SetParent(originalParent, false);
                transform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount));
            }
        }

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }

    // 如果放到某个 drop area（外部调用）
    public void OnDroppedTo(Transform dropParent)
    {
        if (dropParent == null) dropParent = originalParent;
        transform.SetParent(dropParent, false);
        if (cg != null) cg.blocksRaycasts = true;

        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }

    void OnDisable()
    {
        if (currentDragging == this) currentDragging = null;
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }

    void OnDestroy()
    {
        if (currentDragging == this) currentDragging = null;
    }

    // 把 transform 放到父级最后（安全检查父级是否激活）
    void TrySafeBringToTop(Transform t)
    {
        if (t == null || t.parent == null) return;
        if (!t.parent.gameObject.activeInHierarchy) return;

        // 有时 SetAsLastSibling 在父对象激活/停用流程里会抛错，已经做了 activeInHierarchy 保护
        t.SetAsLastSibling();
    }
}
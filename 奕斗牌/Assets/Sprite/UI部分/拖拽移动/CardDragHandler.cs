using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 放置目标接口（改名为 ICardDropTarget 避免与项目中其他同名接口冲突）
/// 在可放置的 UI 对象上实现此接口以接受卡牌并处理保存/更新逻辑
/// </summary>
public interface ICardDropTarget
{
    bool CanAccept(CardDragHandler card);
    void Accept(CardDragHandler card, PointerEventData eventData);
}

[RequireComponent(typeof(RectTransform))]
public class CardDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Overlay / 显示")]
    public Transform overlayRoot;        // 若不指定，脚本会尝试查找名为 "Overlay" 的对象
    public Canvas overlayCanvas;         // 可选，overlay 所属 Canvas（影响坐标转换与排序）

    [Header("拖拽行为")]
    public int overlaySortingOrder = 100;

    [Header("拖拽尺寸设置")]
    public bool useAbsoluteDragSize = true;        // true=固定拖拽尺寸，false=按比例缩放
    public Vector2 dragSize = new Vector2(240f, 240f); // 拖拽时的固定目标尺寸（默认240×240）
    public float dragScaleMultiplier = 1.2f;       // 按比例缩放时的倍率

    [Header("卡片信息节点定位（可手动指定）")]
    public Transform infoRoot;           // 优先使用（每张卡在 Inspector 指定它自己的 info 节点）
    public string infoNodeName = "卡片信息"; // 若未指定 infoRoot，会按名字在子节点中查找

    // 基本字段
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 originalSizeDelta;   // 记录原始卡片尺寸，用于结束拖拽后恢复
    private Vector3 originalLocalScale;  // 记录原始卡片缩放，用于结束拖拽后恢复
    private bool originalSizeSaved = false;

    private Transform originalParent;
    private int originalSiblingIndex;
    private GameObject placeholder;  // 卡片位置占位

    private bool isDragging = false;

    // Overlay Canvas 排序恢复用
    private bool originalCanvasOverrideSorting;
    private int originalCanvasSortingOrder;

    // info 剥离/恢复相关
    private Transform originalInfoTransform = null;
    private Transform originalInfoPrevParent = null;
    private int originalInfoPrevSibling = -1;
    private GameObject infoPlaceholder = null;

    // RectTransform 情况
    private RectTransform originalInfoRect = null;
    private RectTransformValues originalInfoRectValues = null;
    private bool originalInfoHadRect = false;

    // 非 UI（没有 RectTransform）备份
    private Vector3 originalInfoLocalPosition;
    private Quaternion originalInfoLocalRotation;
    private Vector3 originalInfoLocalScale;

    private class RectTransformValues
    {
        public Vector2 anchoredPosition;
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 sizeDelta;
        public Vector2 pivot;
        public Vector3 localScale;
    }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnPointerDown(PointerEventData eventData) { }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isDragging) return;

        // 自动查找 overlayRoot（若未指定）
        if (overlayRoot == null)
        {
            GameObject found = GameObject.Find("Overlay");
            if (found != null) overlayRoot = found.transform;
        }
        if (overlayCanvas == null && overlayRoot != null)
        {
            overlayCanvas = overlayRoot.GetComponentInParent<Canvas>();
        }

        // 记录卡片原始父物体与索引
        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();

        // 创建卡片占位，避免原容器布局坍塌
        placeholder = CreatePlaceholder(rectTransform, originalParent, originalSiblingIndex);

        // ---- 查找并临时剥离卡片信息 ----
        originalInfoTransform = null;
        if (infoRoot != null)
        {
            originalInfoTransform = infoRoot;
        }
        else
        {
            // 按名字查找子节点（精确匹配）
            foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
            {
                if (t == this.transform) continue;
                if (t.name == infoNodeName)
                {
                    originalInfoTransform = t;
                    break;
                }
            }

            // 如果仍未找到，可尝试更宽松的匹配（含 "信息" 或 "info"）
            if (originalInfoTransform == null)
            {
                foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
                {
                    if (t == this.transform) continue;
                    string nm = t.name.ToLower();
                    if (nm.Contains("信息") || nm.Contains("info"))
                    {
                        originalInfoTransform = t;
                        break;
                    }
                }
            }
        }

        // 仅当 info 存在且当前为激活态（可见）时才进行剥离；若 info 隐藏则不动
        if (originalInfoTransform != null && originalInfoTransform.gameObject.activeInHierarchy)
        {
            originalInfoPrevParent = originalInfoTransform.parent;
            originalInfoPrevSibling = originalInfoTransform.GetSiblingIndex();

            // 在原父位置创建 info 的占位以维持父级布局
            RectTransform infoSourceRect = originalInfoTransform.GetComponent<RectTransform>();
            infoPlaceholder = CreatePlaceholder(infoSourceRect != null ? infoSourceRect : originalInfoTransform as RectTransform,
                                                 originalInfoPrevParent, originalInfoPrevSibling);

            // 记录 transform/rect 数据以便恢复
            originalInfoRect = originalInfoTransform.GetComponent<RectTransform>();
            if (originalInfoRect != null)
            {
                originalInfoHadRect = true;
                originalInfoRectValues = new RectTransformValues()
                {
                    anchoredPosition = originalInfoRect.anchoredPosition,
                    anchorMin = originalInfoRect.anchorMin,
                    anchorMax = originalInfoRect.anchorMax,
                    sizeDelta = originalInfoRect.sizeDelta,
                    pivot = originalInfoRect.pivot,
                    localScale = originalInfoRect.localScale
                };
            }
            else
            {
                originalInfoHadRect = false;
                originalInfoLocalPosition = originalInfoTransform.localPosition;
                originalInfoLocalRotation = originalInfoTransform.localRotation;
                originalInfoLocalScale = originalInfoTransform.localScale;
            }

            // 将 info 移到稳定的 overlay 父级（若没有 overlay，则移到原父，不会发生移动）
            Transform stableParent = (overlayCanvas != null) ? overlayCanvas.transform :
                                     (overlayRoot != null) ? overlayRoot : originalInfoPrevParent;

            if (stableParent != null)
            {
                // 使用 worldPositionStays = true 保持视觉位置不变
                originalInfoTransform.SetParent(stableParent, true);
            }

            Debug.Log($"[CardDrag] Info '{originalInfoTransform.name}' 临时剥离到 '{(stableParent != null ? stableParent.name : "(null)")}'");
        }
        // ----------------------------------

        // 把卡片移到 overlayRoot（如果有），保留世界坐标
        if (overlayRoot != null)
        {
            transform.SetParent(overlayRoot, true);

            if (overlayCanvas != null)
            {
                originalCanvasOverrideSorting = overlayCanvas.overrideSorting;
                originalCanvasSortingOrder = overlayCanvas.sortingOrder;
                overlayCanvas.overrideSorting = true;
                overlayCanvas.sortingOrder = overlaySortingOrder;
            }
        }

        // 保存原始卡片尺寸/缩放，应用拖拽尺寸
        if (!originalSizeSaved && rectTransform != null)
        {
            originalSizeDelta = rectTransform.sizeDelta;
            originalLocalScale = rectTransform.localScale;
            originalSizeSaved = true;
        }
        ApplyDragSizeOrScale();

        // 禁用卡牌的 blocksRaycasts，以便下面的 UI 接收事件
        savedBlocksRaycasts = canvasGroup.blocksRaycasts;
        canvasGroup.blocksRaycasts = false;

        isDragging = true;
    }

    // 保存上一次 canvasGroup.blocksRaycasts 的状态
    private bool savedBlocksRaycasts = true;

    /// <summary>
    /// 应用拖拽时的尺寸或缩放
    /// </summary>
    private void ApplyDragSizeOrScale()
    {
        if (rectTransform == null) return;

        if (useAbsoluteDragSize)
        {
            float scaleFactor = overlayCanvas != null ? overlayCanvas.scaleFactor : 1f;
            Vector2 targetSize = dragSize / scaleFactor;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize.y);
        }
        else
        {
            rectTransform.localScale = originalLocalScale * dragScaleMultiplier;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        if (overlayCanvas != null)
        {
            RectTransform canvasRect = overlayCanvas.transform as RectTransform;
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, eventData.pressEventCamera, out localPoint))
            {
                rectTransform.localPosition = localPoint;
            }
        }
        else
        {
            Vector3 worldPos;
            if (eventData.pressEventCamera != null)
            {
                RectTransformUtility.ScreenPointToWorldPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out worldPos);
                rectTransform.position = worldPos;
            }
            else
            {
                rectTransform.position = eventData.position;
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;

        // 射线检测查找可放置目标（跳过占位）
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, raycastResults);

        ICardDropTarget targetSlot = null;
        foreach (var result in raycastResults)
        {
            if (result.gameObject == null || result.gameObject == placeholder)
                continue;

            targetSlot = result.gameObject.GetComponentInParent<ICardDropTarget>();
            if (targetSlot != null && targetSlot.CanAccept(this))
            {
                break;
            }
        }

        if (targetSlot != null)
        {
            targetSlot.Accept(this, eventData);
        }
        else
        {
            RestoreToOriginalPosition();
        }

        // 恢复卡片原始尺寸/缩放
        if (originalSizeSaved && rectTransform != null)
        {
            if (useAbsoluteDragSize)
            {
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalSizeDelta.x);
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalSizeDelta.y);
            }
            else
            {
                rectTransform.localScale = originalLocalScale;
            }
        }

        // 恢复 info（如果曾经剥离）
        if (originalInfoTransform != null)
        {
            if (originalInfoPrevParent != null)
            {
                // 把 info 放回原父（worldPositionStays = false），以便适配父级 Layout
                originalInfoTransform.SetParent(originalInfoPrevParent, false);

                // 恢复 sibling
                int idx = Mathf.Clamp(originalInfoPrevSibling, 0, originalInfoPrevParent.childCount);
                originalInfoTransform.SetSiblingIndex(idx);

                // 恢复 RectTransform 或普通 transform 数据
                if (originalInfoHadRect && originalInfoRect != null && originalInfoRectValues != null)
                {
                    originalInfoRect.pivot = originalInfoRectValues.pivot;
                    originalInfoRect.anchorMin = originalInfoRectValues.anchorMin;
                    originalInfoRect.anchorMax = originalInfoRectValues.anchorMax;
                    originalInfoRect.anchoredPosition = originalInfoRectValues.anchoredPosition;
                    originalInfoRect.sizeDelta = originalInfoRectValues.sizeDelta;
                    originalInfoRect.localScale = originalInfoRectValues.localScale;
                }
                else
                {
                    originalInfoTransform.localPosition = originalInfoLocalPosition;
                    originalInfoTransform.localRotation = originalInfoLocalRotation;
                    originalInfoTransform.localScale = originalInfoLocalScale;
                }

                // 销毁 info 占位
                if (infoPlaceholder != null)
                {
                    Destroy(infoPlaceholder);
                    infoPlaceholder = null;
                }

                // 强制刷新父级 Layout（兼容 LayoutGroup/ContentSizeFitter）
                Canvas.ForceUpdateCanvases();
                var parentRT = originalInfoPrevParent as RectTransform;
                if (parentRT != null)
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parentRT);
                }

                Debug.Log($"[CardDrag] 已恢复 info '{originalInfoTransform.name}' 到 {GetFullPath(originalInfoPrevParent)} idx={idx}");
            }
            else
            {
                Debug.LogWarning("[CardDrag] originalInfoPrevParent 为 null，无法恢复 info 的父级，信息仍然留在 overlay。");
            }
        }

        // 清理 info 上下文
        ClearInfoRestoreContext();

        // 恢复卡片自己的射线状态
        canvasGroup.blocksRaycasts = savedBlocksRaycasts;

        // 销毁卡片占位
        if (placeholder != null)
        {
            Destroy(placeholder);
            placeholder = null;
        }

        // 恢复 overlayCanvas 排序设置
        if (overlayCanvas != null)
        {
            overlayCanvas.overrideSorting = originalCanvasOverrideSorting;
            overlayCanvas.sortingOrder = originalCanvasSortingOrder;
        }
    }

    private void RestoreToOriginalPosition()
    {
        if (originalParent == null) return;
        transform.SetParent(originalParent, false);
        transform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount));
    }

    // 创建占位（用于卡与 info 两种占位）
    private GameObject CreatePlaceholder(RectTransform sourceRect, Transform parent, int siblingIndex)
    {
        if (parent == null) return null;

        GameObject ph = new GameObject((sourceRect != null ? sourceRect.name : "placeholder") + "_ph", typeof(RectTransform));
        RectTransform rt = ph.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount));

        if (sourceRect != null)
        {
            rt.anchorMin = sourceRect.anchorMin;
            rt.anchorMax = sourceRect.anchorMax;
            rt.anchoredPosition = sourceRect.anchoredPosition;
            rt.sizeDelta = sourceRect.sizeDelta;
            rt.pivot = sourceRect.pivot;
            rt.localScale = sourceRect.localScale;
        }
        else
        {
            // 若没有源 RectTransform，给一个小默认尺寸，避免某些 Layout 认为没有子项
            rt.sizeDelta = new Vector2(1, 1);
        }

        var cg = ph.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        return ph;
    }

    private void ClearInfoRestoreContext()
    {
        originalInfoTransform = null;
        originalInfoPrevParent = null;
        originalInfoPrevSibling = -1;
        originalInfoRect = null;
        originalInfoRectValues = null;
        originalInfoHadRect = false;
        originalInfoLocalPosition = Vector3.zero;
        originalInfoLocalRotation = Quaternion.identity;
        originalInfoLocalScale = Vector3.one;
        if (infoPlaceholder != null)
        {
            Destroy(infoPlaceholder);
            infoPlaceholder = null;
        }
    }

    private string GetFullPath(Transform t)
    {
        if (t == null) return "(null)";
        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    // 外部调用：把卡片放入某个父物体（供 ICardDropTarget 调用）
    public void PlaceInto(Transform targetParent, int siblingIndex = -1)
    {
        if (targetParent == null) return;
        transform.SetParent(targetParent, false);
        if (siblingIndex >= 0)
            transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, targetParent.childCount));
        else
            transform.SetAsLastSibling();
    }
}
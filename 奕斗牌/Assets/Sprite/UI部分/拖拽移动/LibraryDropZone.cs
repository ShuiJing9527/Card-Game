using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// LibraryDropZone - 接收拖拽并在 DragDropManager 回调中完成库内放回逻辑
/// </summary>
public class LibraryDropZone : MonoBehaviour, IDropHandler, IDropTarget
{
    [Header("UI")]
    [Tooltip("用于放置库中卡片的容器（通常带 LayoutGroup），若为空则使用本对象的 RectTransform")]
    public RectTransform libContainer;

    [Header("Managers/Models")]
    [Tooltip("用于刷新库显示/数量等")]
    public LibraryManager libraryManager;

    [Tooltip("临时卡组数据（放回库时要从临时 Deck 中移除）")]
    public DeckTempModel tempModel;

    [Header("Optional")]
    [Tooltip("如果希望放回库时用特定 prefab 显示，可以指定；否则会尝试重用 dragClone 或 originalGameObject")]
    public GameObject libraryCardPrefab;

    // 与 DeckDropZone 保持一致的 payload 类型（项目内可抽取为公用类型）
    public class DropPayload
    {
        public int cardId;
        public Transform from; // 原 parent（可能为 null）
        public GameObject originalGameObject; // 原始被拖对象引用（可为 null）
        public object extra; // 扩展字段
    }

    private void Awake()
    {
        if (libContainer == null)
        {
            var rt = GetComponent<RectTransform>();
            if (rt != null) libContainer = rt;
        }
    }

    /// <summary>
    /// IDropHandler 阶段：注册为 pending drop，实际 finalize 在 DragDropManager 中调用 OnDropAccept
    /// </summary>
    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null) return;
        var draggedGo = eventData.pointerDrag;
        if (draggedGo == null) return;

        var cardHandler = draggedGo.GetComponent<CardDragHandler>();
        if (cardHandler == null) return;

        if (DragDropManager.Instance == null)
        {
            Debug.LogWarning("[LibraryDropZone] DragDropManager.Instance is null - cannot register pending drop.");
            return;
        }

        var payload = new DropPayload
        {
            cardId = cardHandler.cardId,
            from = cardHandler.OriginalParent, // 使用 CardDragHandler 记录的原始父级
            originalGameObject = draggedGo
        };

        DragDropManager.Instance.SetPendingDrop(this, payload);
    }

    /// <summary>
    /// IDropTarget 回调：DragDropManager 最终决定把卡片放置到这里时调用
    /// </summary>
    /// <returns>是否接受该放置</returns>
    public bool OnDropAccept(CardDragHandler source, GameObject dragClone, object payload)
    {
        // 验证 payload
        var dropPayload = payload as DropPayload;
        if (dropPayload == null)
        {
            Debug.LogWarning("[LibraryDropZone] payload is not DropPayload.");
            return false;
        }

        int id = dropPayload.cardId;

        RectTransform parentRt = libContainer != null ? libContainer : (transform as RectTransform);
        if (parentRt == null)
        {
            Debug.LogWarning("[LibraryDropZone] No valid libContainer or RectTransform available.");
            return false;
        }

        bool originIsLibrary = IsChildOf(dropPayload.from, parentRt);

        // 如果 dragClone 存在，处理 clone；否则尝试移动 originalGameObject 回库
        if (dragClone != null)
        {
            if (originIsLibrary)
            {
                // 如果是从库里拖出来的（库保留原件），销毁 clone 即可，库项保持不变
                Object.Destroy(dragClone);
            }
            else
            {
                // 来自非库（例如 Deck），把 clone 放回库显示并恢复交互
                dragClone.transform.SetParent(parentRt, false);
                ResetRectTransform(dragClone);
                RestoreCanvasGroup(dragClone);
                EnsureCardDragHandler(dragClone, id);
            }
        }
        else if (dropPayload.originalGameObject != null)
        {
            // 没有 clone，处理原始对象
            if (originIsLibrary)
            {
                // 原本就在库里，直接确保其 parent/状态正确
                var orig = dropPayload.originalGameObject;
                orig.transform.SetParent(parentRt, false);
                ResetRectTransform(orig);
                RestoreCanvasGroup(orig);
                EnsureCardDragHandler(orig, id);
            }
            else
            {
                // 来自 Deck：优先使用 libraryCardPrefab（如果提供），否则直接把原对象移回库
                if (libraryCardPrefab != null)
                {
                    var go = Instantiate(libraryCardPrefab, parentRt, false);
                    EnsureCardDragHandler(go, id);
                    // 销毁原来的 deck 实例（如果原对象是 deck 内实例）
                    Object.Destroy(dropPayload.originalGameObject);
                }
                else
                {
                    var orig = dropPayload.originalGameObject;
                    orig.transform.SetParent(parentRt, false);
                    ResetRectTransform(orig);
                    RestoreCanvasGroup(orig);
                    EnsureCardDragHandler(orig, id);
                }
            }
        }
        else
        {
            Debug.LogWarning("[LibraryDropZone] Neither dragClone nor originalGameObject provided. No visual change.");
        }

        // 如果来源不是库（例如来自 Deck），需要从临时 deck 中移除并通知库更新
        if (!originIsLibrary)
        {
            if (tempModel != null)
            {
                tempModel.RemoveCard(id);
            }
            else
            {
                Debug.LogWarning("[LibraryDropZone] tempModel is not assigned - cannot remove card from temp deck.");
            }

            if (libraryManager != null)
            {
                libraryManager.OnCardReturnedToLibrary(id);
            }
            else
            {
                Debug.LogWarning("[LibraryDropZone] libraryManager is not assigned - library won't be notified.");
            }
        }

        return true;
    }

    // 辅助：判断 candidate 是否为 parent 的子层级（null 安全）
    private bool IsChildOf(Transform candidate, Transform parent)
    {
        if (candidate == null || parent == null) return false;
        var t = candidate;
        while (t != null)
        {
            if (t == parent) return true;
            t = t.parent;
        }
        return false;
    }

    // 恢复 CanvasGroup 的交互
    private void RestoreCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg != null) cg.blocksRaycasts = true;
    }

    // 确保有 CardDragHandler 以便后续还能拖动（并设置 cardId）
    private void EnsureCardDragHandler(GameObject go, int id)
    {
        var ch = go.GetComponent<CardDragHandler>();
        if (ch == null)
        {
            ch = go.AddComponent<CardDragHandler>();
            ch.cardId = id;
        }
        else
        {
            ch.cardId = id;
        }

        // 库内的卡通常保留 createCloneOnDrag = true（若希望库项拖动时创建 clone）
        ch.createCloneOnDrag = true;
    }

    // 可选：重置 RectTransform 位置/缩放以便在 LayoutGroup 中正确显示
    private void ResetRectTransform(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchoredPosition3D = Vector3.zero;
            rt.localScale = Vector3.one;
        }
    }
}
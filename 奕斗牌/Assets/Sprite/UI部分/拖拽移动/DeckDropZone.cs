using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// DeckDropZone - 接收拖拽并在 DragDropManager 回调中完成放置与模型更新
/// </summary>
public class DeckDropZone : MonoBehaviour, IDropHandler, IDropTarget
{
    [Header("UI")]
    [Tooltip("用于放置卡片的容器（通常带 LayoutGroup），若为空则使用本对象的 RectTransform")]
    public RectTransform deckContainer;

    [Header("Model")]
    [Tooltip("临时卡组数据模型（放置时会调用 AddCard ）")]
    public DeckTempModel tempModel;

    [Header("Optional")]
    [Tooltip("用于在放入卡组时实例化的卡片 prefab（若为空则尝试重用 dragClone 或 originalGameObject）")]
    public GameObject deckCardPrefab;

    // payload 的明确类型，便于扩展和序列化调试
    public class DropPayload
    {
        public int cardId;
        public Transform from; // 原 parent（可能为 null）
        public GameObject originalGameObject; // 原始被拖对象引用（可为 null）
        public object extra; // 扩展字段
    }

    private void Awake()
    {
        // 如果没有指定 deckContainer，尝试使用本对象的 RectTransform
        if (deckContainer == null)
        {
            var rt = GetComponent<RectTransform>();
            if (rt != null) deckContainer = rt;
        }
    }

    /// <summary>
    /// IDropHandler 接收阶段：把自己注册为 pending target，实际 finalize 在 DragDropManager 回调中执行
    /// </summary>
    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null) return;
        var draggedGo = eventData.pointerDrag;
        if (draggedGo == null) return;

        var cardHandler = draggedGo.GetComponent<CardDragHandler>();
        if (cardHandler == null)
        {
            // 不是卡片则忽略
            return;
        }

        if (DragDropManager.Instance == null)
        {
            Debug.LogWarning("[DeckDropZone] DragDropManager.Instance is null - cannot register pending drop.");
            return;
        }

        var payload = new DropPayload
        {
            cardId = cardHandler.cardId,
            from = cardHandler.OriginalParent, // 使用 CardDragHandler 记录的原始父级
            originalGameObject = draggedGo
        };

        // 将本 DropZone 与 payload 注册为 pending drop，DragDropManager 后续会调用 OnDropAccept
        DragDropManager.Instance.SetPendingDrop(this, payload);
    }

    /// <summary>
    /// IDropTarget 回调：DragDropManager 最终决定把卡片放置到这里时调用（source = 拖拽源的 CardDragHandler）
    /// dragClone: 用于展示的克隆对象（如果为 null，可根据需求 Instantiate prefab 或移动原对象）
    /// payload: 你在 OnDrop 时传入的 DropPayload
    /// 返回 true 表示接收
    /// </summary>
    public bool OnDropAccept(CardDragHandler source, GameObject dragClone, object payload)
    {
        // 验证 payload 类型
        var dropPayload = payload as DropPayload;
        if (dropPayload == null)
        {
            Debug.LogWarning("[DeckDropZone] payload is not DropPayload.");
            return false;
        }

        int id = dropPayload.cardId;

        // 目标容器校验
        RectTransform parentRt = deckContainer != null ? deckContainer : (transform as RectTransform);
        if (parentRt == null)
        {
            Debug.LogWarning("[DeckDropZone] No valid deckContainer or RectTransform available.");
            return false;
        }

        // 1) 在 UI 上创建/移动卡片项：优先使用 dragClone（由 Drag system 提供），否则尝试使用 originalGameObject 的移动
        GameObject finalGo = null;
        if (dragClone != null)
        {
            // 将 clone 放入 deck 容器并恢复交互与所需组件
            finalGo = dragClone;
            finalGo.transform.SetParent(parentRt, false);
            ResetRectTransform(finalGo);
            RestoreCanvasGroup(finalGo);
            EnsureCardDragHandler(finalGo, id, asDeckCard: true);
        }
        else if (dropPayload.originalGameObject != null)
        {
            // 如果存在 deckCardPrefab，优先 Instantiate 一份 deck 专用 prefab（避免直接重用库的原件）
            if (deckCardPrefab != null)
            {
                finalGo = Instantiate(deckCardPrefab, parentRt, false);
                EnsureCardDragHandler(finalGo, id, asDeckCard: true);
            }
            else
            {
                // 没有 prefab，则移动原对象到 deck 容器
                finalGo = dropPayload.originalGameObject;
                finalGo.transform.SetParent(parentRt, false);
                ResetRectTransform(finalGo);
                RestoreCanvasGroup(finalGo);
                EnsureCardDragHandler(finalGo, id, asDeckCard: true);
            }
        }
        else
        {
            Debug.LogWarning("[DeckDropZone] Neither dragClone nor originalGameObject provided. No visual card created.");
        }

        // 2) 更新临时 deck model
        if (tempModel != null)
        {
            tempModel.AddCard(id);
        }
        else
        {
            Debug.LogWarning("[DeckDropZone] tempModel is not assigned - deck model won't be updated.");
        }

        // 3) 可选：设置 siblingIndex / 排序 或 播放入堆动画
        // e.g. finalGo.transform.SetSiblingIndex(desiredIndex);

        return true; // accept
    }

    // 恢复 CanvasGroup 的交互
    private void RestoreCanvasGroup(GameObject go)
    {
        if (go == null) return;
        var cg = go.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.blocksRaycasts = true;
            cg.interactable = true; // 确保交互可用
            cg.alpha = 1f;
        }
    }

    // 确保有 CardDragHandler 以便后续还能拖动（并设置 cardId）
    private void EnsureCardDragHandler(GameObject go, int id, bool asDeckCard = false)
    {
        if (go == null) return;

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

        // 如果之前被禁用（例如 clone 创建时禁用），这里要重新启用
        if (!ch.enabled) ch.enabled = true;

        // 确保移回卡组后的默认拖拽策略
        if (asDeckCard)
        {
            ch.createCloneOnDrag = false;
        }

        // 确保 CanvasGroup 恢复交互（若没有就添加一个）
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = true;
        cg.interactable = true;
        cg.alpha = 1f;
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
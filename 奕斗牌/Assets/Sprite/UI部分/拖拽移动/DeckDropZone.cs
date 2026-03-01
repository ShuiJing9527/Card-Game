using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
            // 不是卡片则忽略（或你可以改为支持其它类型）
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
            from = draggedGo.transform.parent,
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
            finalGo = dragClone;
            finalGo.transform.SetParent(parentRt, false);
            // 可选：重置本地 transform
            RectTransform rt = finalGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition3D = Vector3.zero;
                rt.localScale = Vector3.one;
            }

            // 恢复交互能力
            var cg = finalGo.GetComponent<CanvasGroup>();
            if (cg != null) cg.blocksRaycasts = true;

            // 确保有 CardDragHandler（以便后续还可以拖拽）
            var ch = finalGo.GetComponent<CardDragHandler>();
            if (ch == null)
            {
                ch = finalGo.AddComponent<CardDragHandler>();
                ch.cardId = id;
            }
            else
            {
                ch.cardId = id;
            }
        }
        else if (dropPayload.originalGameObject != null)
        {
            // 如果没有 clone，则直接把原对象移动进容器（注意这在某些 Drag 实现里可能不是期望行为）
            finalGo = dropPayload.originalGameObject;
            finalGo.transform.SetParent(parentRt, false);
            var cg = finalGo.GetComponent<CanvasGroup>();
            if (cg != null) cg.blocksRaycasts = true;
        }
        else
        {
            // 这里可以选择 Instantiate 一个卡片 prefab（如果你有 prefab 的引用）
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
}
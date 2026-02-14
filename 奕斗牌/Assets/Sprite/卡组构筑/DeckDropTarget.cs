using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

public class DeckDropTarget : MonoBehaviour, IDropHandler
{
    [Header("可选：把拖拽物父到这个容器，便于视觉效果")]
    public RectTransform deckContainer;

    [Header("可选：指向你的 DeckManager（若有 BuildDeckFromPlayerData 方法会被自动调用）")]
    public MonoBehaviour deckManager;

    [Header("是否在拖拽后自动保存 PlayerData（默认 false）")]
    public bool autoSaveOnDrop = false;

    // OnDrop 实现（已修改：支持 DeckManager 编辑模式）
    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerDrag == null) return;
        var dragged = eventData.pointerDrag;
        Debug.Log($"[DeckDropTarget] OnDrop called. pointerDrag={dragged.name}");

        // 尝试从 CardDragHandler 提取 cardId/amount（适配你项目的 CardDragHandler）
        int cardId = -1;
        int amount = 1;
        var cdh = dragged.GetComponent<CardDragHandler>();
        if (cdh != null)
        {
            try
            {
                cardId = cdh.CardId;
                amount = (cdh.IsStack ? Mathf.Max(1, cdh.StackCount) : 1);
            }
            catch { }
        }
        else
        {
            var childCdh = dragged.GetComponentInChildren<CardDragHandler>();
            if (childCdh != null)
            {
                cardId = childCdh.CardId;
                amount = (childCdh.IsStack ? Mathf.Max(1, childCdh.StackCount) : 1);
            }
        }

        if (cardId < 0)
        {
            Debug.LogWarning("[DeckDropTarget] 无法提取 cardId，取消 Drop 操作");
            return;
        }

        // ✅ 检查是否由 DeckManager 进入了编辑模式 (使用公开属性 IsEditing)
        DeckManager dm = deckManager as DeckManager;
        if (dm != null && dm.IsEditing)
        {
            // 处于编辑模式：只修改 editingDeck，不触碰 PlayerDataManager
            dm.AddCardToEditingDeck(cardId, amount);
            Debug.Log($"[DeckDropTarget] ✅ 编辑模式：已将 cardId={cardId} x{amount} 加入 editingDeck");

            // 可选：若需在 UI 中插入到具体位置（如按鼠标位置），可用：
            // int insertIdx = CalculateInsertIndexByNearestChild(dm.deckPanel, eventData.position);
            // dm.AddCardToEditingDeck(cardId, amount, insertIdx);

            // 不需要实例化副本（因为 BuildDeckFromEditingDeck 已重建 UI）
            // 也不需要调用 pdm.SavePlayerData（等用户点击“保存”时再统一写回）

            return;
        }

        // ⚠️ 非编辑模式：走原逻辑（直接操作 PlayerDataManager）
        var pdm = PlayerDataManager.Instance;
        if (pdm == null)
        {
            Debug.LogError("[DeckDropTarget] PlayerDataManager.Instance == null");
            return;
        }

        bool success = false;
        try
        {
            success = pdm.TryTransferCardToDeckNoSave(cardId, amount);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DeckDropTarget] 调用 TryTransferCardToDeckNoSave 出现异常: {ex.Message}");
        }

        if (!success)
        {
            Debug.Log($"[DeckDropTarget] fallback: adding cardId={cardId} to deck directly (no inventory check)");
            try
            {
                pdm.AddDeckCardNoSave(cardId, amount); // 触发 OnDeckChanged → 自动刷新 UI
                success = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DeckDropTarget] AddDeckCardNoSave 失败: {ex.Message}");
                success = false;
            }
        }

        if (!success)
        {
            Debug.LogWarning("[DeckDropTarget] 无法将卡片加入卡组（操作失败）");
            return;
        }

        // 👇 以下逻辑仍保留：仅为非编辑模式生成 UI 副本（编辑模式下由 BuildDeckFromEditingDeck 负责创建 UI）
        if (deckContainer != null)
        {
            try
            {
                GameObject cardCopy = GameObject.Instantiate(dragged, deckContainer);
                var copyCdh = cardCopy.GetComponent<CardDragHandler>();
                if (copyCdh != null) Destroy(copyCdh);
                var canvasGroup = cardCopy.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.blocksRaycasts = true;
                    canvasGroup.interactable = false;
                }

                var rt = cardCopy.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DeckDropTarget] Instantiate card copy failed: {ex.Message}");
            }
        }

        // 尝试刷新 DeckManager（非编辑模式下，可能仍有此需求）
        if (deckManager != null)
        {
            var mi = deckManager.GetType().GetMethod("BuildDeckFromPlayerData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                try { mi.Invoke(deckManager, null); }
                catch (System.Exception ex) { Debug.LogWarning($"[DeckDropTarget] 调用 DeckManager.BuildDeckFromPlayerData 失败: {ex.Message}"); }
            }
        }

        // 可选：拖拽后自动保存（仅非编辑模式生效）
        if (autoSaveOnDrop && (dm == null || !dm.IsEditing))
        {
            try { pdm.SavePlayerData(); }
            catch (System.Exception ex) { Debug.LogWarning($"[DeckDropTarget] 自动保存失败: {ex.Message}"); }
        }

        Debug.Log($"[DeckDropTarget] Drop handled: cardId={cardId}, amount={amount}");
    }

    // 辅助方法（可选）：计算插入索引（供编辑模式使用）
    private int CalculateInsertIndexByNearestChild(RectTransform content, Vector2 screenPos)
    {
        if (content == null || content.childCount == 0) return 0;
        Canvas rootCanvas = content.GetComponentInParent<Canvas>();
        Camera cam = rootCanvas?.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPos, cam, out Vector2 localPoint);

        float minDist = float.MaxValue;
        int nearestIdx = 0;
        for (int i = 0; i < content.childCount; i++)
        {
            RectTransform child = content.GetChild(i) as RectTransform;
            if (child == null) continue;
            float d = Vector2.SqrMagnitude(localPoint - child.anchoredPosition);
            if (d < minDist)
            {
                minDist = d;
                nearestIdx = i;
            }
        }

        RectTransform nearest = content.GetChild(nearestIdx) as RectTransform;
        if (nearest != null && localPoint.x > nearest.anchoredPosition.x)
            return nearestIdx + 1;
        return nearestIdx;
    }

    // 调试工具：打印拖拽物上组件及其公共字段（可在 Inspector 调用）
    public void DumpComponentsAndMembers(GameObject go)
    {
        if (go == null)
        {
            Debug.Log("[DeckDropTarget_Debug] DumpComponentsAndMembers: go == null");
            return;
        }

        var comps = go.GetComponents<Component>();
        Debug.Log($"[DeckDropTarget_Debug] Components on '{go.name}' count={comps.Length}");
        foreach (var c in comps)
        {
            if (c == null) continue;
            Debug.Log($" - {c.GetType().FullName}");
        }
    }
}
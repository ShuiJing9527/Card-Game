using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 接受来自 CardDragHandler 的投放，将卡片记录到 PlayerDataManager 的 deck 中（并触发保存）
/// 已修改要点：
/// - 对来自卡池的堆叠项（card.IsStack == true）不移动原始 UI；通过数据接口减库存、加卡组并在 UI 上实例化一个条目（若提供 prefab）。
/// - 对非堆叠项允许移动原始对象到 deckContent（原有行为）。
/// - 在修改数据前做库存/上限检查，使用 PlayerDataManager 的 API 做所有数据变更（避免直接写文件/覆盖内存）。
/// </summary>
public class DeckDropTarget : MonoBehaviour, ICardDropTarget
{
    public enum PlaceMode
    {
        MoveDraggedCard, // 把拖拽对象直接放入 deckContent（会改变原对象的父级）
        InstantiatePrefab // 根据卡片 ID 实例化一个 deckEntryPrefab（原拖拽对象会被还原或销毁）
    }

    [Header("目标容器/显示")]
    public Transform deckContent;               // 卡组 UI 的容器（放入卡片或实例化项）
    public PlaceMode placeMode = PlaceMode.MoveDraggedCard;
    public GameObject deckEntryPrefab;          // 若使用 InstantiatePrefab 模式，需要指定 prefab（自行实现显示）
    public bool destroyDraggedOnDrop = false;   // 若 InstantiatePrefab，是否销毁原拖拽对象（否则会还原）

    [Header("逻辑约束")]
    public int addCount = 1;                    // 每次放入增加的数量
    public int maxPerCard = int.MaxValue;      // 每个卡片在 deck 中允许的最大数量（可选限制）
    public bool acceptOnlyValidCardId = true;   // 是否拒绝负 id 或无效 id

    // 可视/声音反馈（自行挂载）
    public AudioSource acceptSound;

    public bool CanAccept(CardDragHandler card)
    {
        if (card == null) return false;
        if (acceptOnlyValidCardId && card.CardId < 0) return false;

        // 如果需要读取当前数量并限制
        if (PlayerDataManager.Instance != null)
        {
            int curDeck = PlayerDataManager.Instance.GetDeckCount(card.CardId);
            if ((long)curDeck + addCount > maxPerCard) return false;

            // 若来自堆叠项（卡池），检查库存是否足够
            if (card.IsStack)
            {
                int inv = PlayerDataManager.Instance.GetCardCount(card.CardId);
                if (inv < addCount) return false;
            }
        }

        return true;
    }

    public void Accept(CardDragHandler card, PointerEventData eventData)
    {
        if (card == null) return;

        // 如果没有 PlayerDataManager，尽量只做视觉处理并警告
        if (PlayerDataManager.Instance == null)
        {
            Debug.LogWarning("DeckDropTarget: PlayerDataManager.Instance 为 null，无法更新 deck 数据。仅执行视觉放置。");
            HandleVisualPlacement(card, treatAsStack: false);
            return;
        }

        int cardId = card.CardId;

        // 再次防护：合法 id 检查
        if (acceptOnlyValidCardId && cardId < 0)
        {
            Debug.LogWarning($"DeckDropTarget: 拒绝无效 cardId={cardId}");
            HandleVisualPlacement(card, treatAsStack: false);
            return;
        }

        // 检查卡组上限
        int curDeck = PlayerDataManager.Instance.GetDeckCount(cardId);
        if ((long)curDeck + addCount > maxPerCard)
        {
            Debug.LogWarning($"DeckDropTarget: 超出每卡上限 cardId={cardId} curDeck={curDeck} add={addCount} max={maxPerCard}");
            // 恢复视觉
            HandleVisualPlacement(card, treatAsStack: card.IsStack);
            return;
        }

        // 如果是来自卡池的堆叠项：不要移动原始UI
        if (card.IsStack)
        {
            int curInv = PlayerDataManager.Instance.GetCardCount(cardId);
            if (curInv < addCount)
            {
                Debug.LogWarning($"DeckDropTarget: 卡池库存不足 cardId={cardId} inv={curInv} need={addCount}");
                HandleVisualPlacement(card, treatAsStack: true);
                return;
            }

            // 先修改数据：库存 - addCount，卡组 + addCount
            PlayerDataManager.Instance.SetCardCount(cardId, Math.Max(0, curInv - addCount));
            PlayerDataManager.Instance.AddDeckCard(cardId, addCount);
            // PlayerDataManager 内部会 SavePlayerData 并触发 OnDeckChanged

            // 视觉上不要把原始堆叠 UI 移到 deckContent，改为实例化一个 deckEntry 显示（如果提供 prefab）
            if (placeMode == PlaceMode.InstantiatePrefab && deckEntryPrefab != null && deckContent != null)
            {
                var go = Instantiate(deckEntryPrefab, deckContent);
                var entry = go.GetComponent<IDeckEntry>();
                if (entry != null)
                {
                    entry.InitCardEntry(cardId, PlayerDataManager.Instance.GetDeckCount(cardId));
                }
            }
            else
            {
                // fallback：不移动原始UI，啥也不做（CardDragHandler 会在拖拽结束后恢复原位）
            }

            // 播放反馈
            if (acceptSound != null) acceptSound.Play();

            return;
        }

        // 非堆叠项（正常把拖拽对象移动到卡组容器或根据 placeMode 实例化）
        // 首先更新数据
        PlayerDataManager.Instance.AddDeckCard(cardId, addCount);

        // 视觉处理（对非堆叠项可以实际移动对象）
        HandleVisualPlacement(card, treatAsStack: false);

        if (acceptSound != null) acceptSound.Play();
    }

    /// <summary>
    /// 视觉上将卡片放置到 deckContent 或恢复/实例化。
    /// treatAsStack: 如果 true，表示该卡来自堆叠池项，方法应避免移动原始 UI。
    /// </summary>
    private void HandleVisualPlacement(CardDragHandler card, bool treatAsStack)
    {
        if (card == null) return;

        // 若是堆叠来源，绝不 MoveDraggedCard（避免移动原始库 UI）
        if (treatAsStack && placeMode == PlaceMode.MoveDraggedCard)
        {
            // 优先实例化显示项，否则什么也不做（CardDragHandler 会恢复原始 UI）
            if (deckEntryPrefab != null && deckContent != null)
            {
                var go = Instantiate(deckEntryPrefab, deckContent);
                var entry = go.GetComponent<IDeckEntry>();
                if (entry != null)
                {
                    entry.InitCardEntry(card.CardId, PlayerDataManager.Instance.GetDeckCount(card.CardId));
                }
            }
            else
            {
                // 恢复原位：不移动原始库 UI
                // CardDragHandler 的 OnEndDrag 也会做恢复，通常不需重复
                // 但这里可以尽量不破坏原始层级，调用 PlaceInto 还原到当前 parent（安全）
                card.PlaceInto(card.transform.parent);
            }
            return;
        }

        // 非堆叠项或 treatAsStack == false，按配置移动或实例化
        if (placeMode == PlaceMode.MoveDraggedCard)
        {
            if (deckContent != null)
            {
                card.PlaceInto(deckContent);
            }
            else
            {
                // 无容器则恢复原位
                card.PlaceInto(card.transform.parent);
            }
        }
        else // InstantiatePrefab
        {
            if (deckEntryPrefab != null && deckContent != null)
            {
                var go = Instantiate(deckEntryPrefab, deckContent);
                var entry = go.GetComponent<IDeckEntry>();
                if (entry != null)
                {
                    entry.InitCardEntry(card.CardId, PlayerDataManager.Instance.GetDeckCount(card.CardId));
                }

                if (destroyDraggedOnDrop)
                {
                    // 仅在非堆叠项时允许销毁拖拽的原始对象（避免删除卡池 UI）
                    Destroy(card.gameObject);
                }
                else
                {
                    // 还原拖拽对象到原始位置（CardDragHandler 会在 OnEndDrag 恢复状态）
                    card.PlaceInto(card.transform.parent);
                }
            }
            else
            {
                // fallback：恢复拖拽对象
                card.PlaceInto(card.transform.parent);
            }
        }
    }
}

/// <summary>
/// 可选接口：deck entry prefab 可实现这个接口以便被初始化
/// </summary>
public interface IDeckEntry
{
    void InitCardEntry(int cardId, int currentDeckCount);
}
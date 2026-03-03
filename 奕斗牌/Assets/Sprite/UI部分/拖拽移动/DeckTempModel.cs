using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Deck/TempModel")]
public class DeckTempModel : ScriptableObject
{
    [Serializable]
    public class CardCountSerializable
    {
        public int cardId;
        public int count;
    }

    // 用于序列化与编辑器展示（会保存到 asset）
    public List<CardCountSerializable> serializedList = new List<CardCountSerializable>();

    // 运行时使用的字典（不会被 Unity 序列化到 asset）
    Dictionary<int, int> cardCounts = new Dictionary<int, int>();

    // 公开只读访问
    public IReadOnlyDictionary<int, int> CardCounts => cardCounts;

    // 变更事件（UI 等订阅以刷新显示）
    public event Action OnChanged;

    // 当 ScriptableObject 被加载/启用时，把 serializedList 的内容转为运行时字典
    void OnEnable()
    {
        // 初始化字典
        cardCounts = new Dictionary<int, int>();
        if (serializedList != null)
        {
            foreach (var entry in serializedList)
            {
                if (entry == null) continue;
                if (cardCounts.ContainsKey(entry.cardId)) cardCounts[entry.cardId] += entry.count;
                else cardCounts[entry.cardId] = Mathf.Max(0, entry.count);
            }
        }
    }

    // 运行时方法：添加卡
    public void AddCard(int id, int delta = 1)
    {
        if (delta == 0) return;
        if (!cardCounts.ContainsKey(id)) cardCounts[id] = 0;
        cardCounts[id] = Mathf.Max(0, cardCounts[id] + delta);
        OnChanged?.Invoke();
    }

    // 运行时方法：移除卡（如果 count <=0 则删除条目）
    public void RemoveCard(int id, int delta = 1)
    {
        if (!cardCounts.ContainsKey(id)) return;
        cardCounts[id] = Mathf.Max(0, cardCounts[id] - delta);
        if (cardCounts[id] <= 0) cardCounts.Remove(id);
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        if (cardCounts.Count == 0) return;
        cardCounts.Clear();
        OnChanged?.Invoke();
    }

    // 将运行时字典写回 serializedList（用于在编辑器中保存到 asset）
    public void SyncToSerializedList()
    {
        serializedList.Clear();
        foreach (var kv in cardCounts)
        {
            serializedList.Add(new CardCountSerializable { cardId = kv.Key, count = kv.Value });
        }
#if UNITY_EDITOR
        // 标记 asset 为脏以便保存（仅在编辑器下有效）
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // 把运行时结果提交给 PlayerDataManager（由 Save 按钮调用）
    public void CommitToPlayerData()
    {
        // 请确保 PlayerDataManager 实现 SetDeckCardCounts(Dictionary<int,int>)
        if (PlayerDataManager.Instance != null)
        {
            // 传入一个新的字典副本，避免外部修改内部结构
            var copy = new Dictionary<int, int>(cardCounts);
            PlayerDataManager.Instance.SetDeckCardCounts(copy);
        }
        else
        {
            Debug.LogWarning("[DeckTempModel] PlayerDataManager.Instance is null - cannot commit to player data.");
        }
    }

    // 实用方法：获取某卡的数量
    public int GetCount(int cardId)
    {
        if (cardCounts.TryGetValue(cardId, out var c)) return c;
        return 0;
    }
}
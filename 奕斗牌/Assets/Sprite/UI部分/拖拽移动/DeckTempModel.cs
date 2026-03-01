using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Deck/TempModel")]
public class DeckTempModel : ScriptableObject
{
    // 简单的 id -> count 映射（或你需要的结构）
    public Dictionary<int, int> cardCounts = new Dictionary<int, int>();

    public void AddCard(int id)
    {
        if (cardCounts.ContainsKey(id)) cardCounts[id] += 1;
        else cardCounts[id] = 1;
    }

    public void RemoveCard(int id)
    {
        if (cardCounts.ContainsKey(id))
        {
            cardCounts[id]--;
            if (cardCounts[id] <= 0) cardCounts.Remove(id);
        }
    }

    public void Clear()
    {
        cardCounts.Clear();
    }

    // Save 到 PlayerDataManager（在你点击保存按钮时调用）
    public void CommitToPlayerData()
    {
        // TODO: 把 cardCounts 写回玩家卡组数据结构
        PlayerDataManager.Instance.SetDeckCardCounts(cardCounts);
    }
}
using UnityEngine;

/// <summary>
/// 卡组 UI 条目接口：表示该条目能通过 InitCardEntry(cardId, count) 初始化或更新显示
/// 如果你的 deckEntryPrefab 有其它初始化方法，可让它实现此接口。
/// </summary>
public interface IDeckEntry
{
    void InitCardEntry(int cardId, int count);
}

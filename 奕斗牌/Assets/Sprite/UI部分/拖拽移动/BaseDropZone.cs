using UnityEngine;
using UnityEngine.EventSystems;

public abstract class BaseDropZone : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData eventData)
    {
        var dragged = eventData.pointerDrag;
        if (dragged == null) return;

        var cardDrag = dragged.GetComponent<CardDragHandler>();
        if (cardDrag != null)
        {
            // 让卡片自己负责清理 placeholder/恢复 state，并把它放入目标容器
            cardDrag.OnDroppedTo(transform);
            // 子类处理各自的业务逻辑（更新数据模型等）
            HandleDroppedCard(cardDrag);
        }
        else
        {
            // 兜底：直接设置 parent
            dragged.transform.SetParent(transform, false);
        }
    }

    // 子类实现特定逻辑（例如 Deck 要改变牌堆数据，Library 只改变 UI）
    protected abstract void HandleDroppedCard(CardDragHandler cardDrag);
}
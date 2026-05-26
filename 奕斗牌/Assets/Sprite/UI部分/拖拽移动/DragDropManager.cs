using UnityEngine;
using UnityEngine.EventSystems;

public class DragDropManager : MonoBehaviour
{
    public static DragDropManager Instance { get; private set; }

    // 存放 OnDrop 时记录的目标。Drop 区在 OnDrop 时设置这里，之后在拖拽结束时被消费（ConsumePendingDrop）。
    IDropTarget pendingDropTarget = null;
    object pendingDropPayload = null;

    void Awake() { Instance = this; }

    // Drop 区调用：当某个拖拽对象被放到本 Drop 区时，Drop 区把自己记录下来
    public void SetPendingDrop(IDropTarget target, object payload)
    {
        pendingDropTarget = target;
        pendingDropPayload = payload;
    }

    // 在 CardDragHandler.OnEndDrag 中被调用来“消费” drop
    // 返回：是否被接受
    public bool ConsumePendingDrop(CardDragHandler source, GameObject dragClone, PointerEventData eventData)
    {
        if (pendingDropTarget != null)
        {
            bool ok = pendingDropTarget.OnDropAccept(source, dragClone, pendingDropPayload);
            // 清理
            pendingDropTarget = null;
            pendingDropPayload = null;
            return ok;
        }
        return false;
    }
}
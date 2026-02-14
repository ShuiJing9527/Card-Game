using UnityEngine;
using UnityEngine.EventSystems;

public class CardPointer : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("可手动在 Inspector 指定，也会在运行时自动查找场景中的 SelectionManager")]
    public SelectionManager selectionManager;

    RectTransform rect;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        if (rect == null)
            Debug.LogError("CardPointer 必须挂在包含 RectTransform 的 UI 元素上。");
    }

    void Start()
    {
        // 尝试一次性自动查找（如果 Inspector 未设置）
        if (selectionManager == null)
        {
            selectionManager = FindObjectOfType<SelectionManager>();
            if (selectionManager == null)
                Debug.LogWarning("CardPointer: Start 未找到 SelectionManager，后续事件会继续尝试查找。");
        }
    }

    // 每次事件发生前都确保有一个有效的 selectionManager（防止创建顺序问题）
    void EnsureSelectionManager()
    {
        if (selectionManager == null)
        {
            selectionManager = FindObjectOfType<SelectionManager>();
            if (selectionManager == null)
            {
                // 不频繁打印警告，避免 Log 污染
                // Debug.LogWarning("CardPointer: 未在场景中找到 SelectionManager。");
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        EnsureSelectionManager();
        if (selectionManager != null)
        {
            // 仅调用 SelectionManager 处理显示与排序（不要在这里强制设置 sortingOrder）
            selectionManager.ShowFor(rect);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        EnsureSelectionManager();
        if (!eventData.dragging)
            selectionManager?.Hide();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        EnsureSelectionManager();
        if (selectionManager != null)
        {
            // 仅调用 SelectionManager 处理显示与排序（不要在这里强制设置 sortingOrder）
            selectionManager.ShowFor(rect);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        EnsureSelectionManager();
        selectionManager?.ShowFor(rect);
        // 在拖拽时 SelectionManager 会在 Update 跟随；CardDragHandler 也可能在 OnDrag 主动调用 UpdateSelectionTransform（如果存在）
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 拖拽期间如有额外同步需求，可在 CardDragHandler 中调用 SelectionManager.UpdateSelectionTransform
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        EnsureSelectionManager();
        selectionManager?.Hide();
    }
}
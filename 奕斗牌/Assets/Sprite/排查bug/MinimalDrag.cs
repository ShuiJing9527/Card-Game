using UnityEngine;
using UnityEngine.EventSystems;

public class MinimalDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    RectTransform rt;
    Canvas rootCanvas;
    RectTransform canvasRect;
    Vector2 pointerOffset;
    Transform originalParent;
    int originalIndex;
    Transform dragRoot;
    CanvasGroup cg;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null) { dragRoot = rootCanvas.transform; canvasRect = dragRoot as RectTransform; }
    }

    public void OnBeginDrag(PointerEventData e)
    {
        originalParent = transform.parent;
        originalIndex = transform.GetSiblingIndex();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        if (dragRoot != null) transform.SetParent(dragRoot, true);

        Vector2 localPointer;
        var cam = e.pressEventCamera;
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.position, cam, out localPointer))
            pointerOffset = rt.anchoredPosition - localPointer;
        else pointerOffset = Vector2.zero;

        Debug.Log($"MinimalDrag.OnBeginDrag for {name}");
    }

    public void OnDrag(PointerEventData e)
    {
        if (canvasRect == null) return;
        Vector2 localPointer; var cam = e.pressEventCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.position, cam, out localPointer))
            rt.anchoredPosition = localPointer + pointerOffset;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (cg != null) cg.blocksRaycasts = true;
        if (transform.parent == dragRoot && originalParent != null)
        {
            transform.SetParent(originalParent, false);
            transform.SetSiblingIndex(Mathf.Clamp(originalIndex, 0, originalParent.childCount));
        }
        Debug.Log($"MinimalDrag.OnEndDrag for {name}");
    }
}
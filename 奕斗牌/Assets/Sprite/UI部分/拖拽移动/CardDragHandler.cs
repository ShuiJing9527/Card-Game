using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class CardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Card identity (optional)")]
    public int cardId;
    public int CardId => cardId;

    [Header("Drag root (optional). If null, will use parent Canvas transform)")]
    public Transform dragRoot;

    [Header("Whether to attach selection to dragRoot during drag")]
    public bool attachSelectionToDragRoot = true;

    [Header("If true, dragging this card will create a visual clone and leave the original in place")]
    public bool createCloneOnDrag = false;

    [Header("Auto-size / clamp settings (混合方案)")]
    public Vector2 minCloneSize = new Vector2(160f, 160f);
    public Vector2 maxCloneSize = new Vector2(220f, 220f);
    public Vector2 fallbackCloneSize = new Vector2(220f, 220f); // 当无法计算时的回退尺寸

    [Header("Info-hide settings for clones")]
    // 匹配并隐藏这些关键字对应的子对象（忽略大小写、部分匹配）
    public string[] infoNameTokens = new[] { "卡片信息", "CardInfo", "cardInfo", "InfoPanel", "Card_Detail", "卡片详情", "DetailPanel", "Tooltip", "卡片信息面板" };
    [Tooltip("在创建克隆后重复强制隐藏信息面板的帧数（防止其它脚本在后续帧打开）")]
    public int enforceHideFrames = 3;

    public static CardDragHandler currentDragging;

    RectTransform rectTransform;
    Canvas rootCanvas;
    RectTransform canvasRect;
    Vector2 pointerOffset;
    Transform originalParent;
    int originalSiblingIndex;
    CanvasGroup cg;

    // clone-related
    GameObject dragCloneGO = null;
    RectTransform activeDragRect = null;   // the rect that is actually moved during dragging (either this.rectTransform or clone)
    bool usingClone = false;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        rootCanvas = GetComponentInParent<Canvas>();

        if (rootCanvas != null && dragRoot == null)
        {
            dragRoot = rootCanvas.transform;
            canvasRect = dragRoot as RectTransform;
        }
        else if (dragRoot != null)
        {
            canvasRect = dragRoot as RectTransform;
            rootCanvas = dragRoot.GetComponentInParent<Canvas>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        currentDragging = this;

        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();

        if (createCloneOnDrag)
        {
            usingClone = true;

            if (dragRoot == null)
            {
                rootCanvas = GetComponentInParent<Canvas>();
                if (rootCanvas != null)
                {
                    dragRoot = rootCanvas.transform;
                    canvasRect = dragRoot as RectTransform;
                }
            }

            // Instantiate visual clone
            dragCloneGO = Instantiate(gameObject);
            dragCloneGO.name = gameObject.name + "_DragClone";

            // 立即为克隆添加标记组件（你已创建 DragCloneMarker.cs）
            try
            {
                dragCloneGO.AddComponent<DragCloneMarker>();
            }
            catch
            {
                // 如果项目中未包含 DragCloneMarker（极少见），则忽略，不会阻塞后续逻辑
            }

            // 立即隐藏克隆中的信息面板相关子对象（不会禁用任何脚本）
            DisableInfoChildInClone(dragCloneGO);

            // 在随后若干帧再次强制隐藏，以防 OnEnable/Start 中被打开
            StartCoroutine(EnsureInfoRemainsHidden(dragCloneGO, enforceHideFrames));

            // Put clone under dragRoot WITHOUT preserving world position
            if (dragRoot != null)
                dragCloneGO.transform.SetParent(dragRoot, false);

            // Disable its CardDragHandler to avoid duplicate behavior
            var cd = dragCloneGO.GetComponent<CardDragHandler>();
            if (cd != null) cd.enabled = false;

            // Clone should not block raycasts (so raycasts hit underlying UI)
            var cloneCg = dragCloneGO.GetComponent<CanvasGroup>();
            if (cloneCg == null) cloneCg = dragCloneGO.AddComponent<CanvasGroup>();
            cloneCg.blocksRaycasts = false;
            cloneCg.interactable = false;

            // active drag rect is the clone's rect transform
            activeDragRect = dragCloneGO.GetComponent<RectTransform>();

            // Normalize anchors/pivot so anchoredPosition maps predictably to screen/local coords
            if (activeDragRect != null)
            {
                activeDragRect.pivot = new Vector2(0.5f, 0.5f);
                activeDragRect.anchorMin = activeDragRect.anchorMax = new Vector2(0.5f, 0.5f);

                // 强制重建布局以便计算 preferred size（若存在 LayoutGroup / ContentSizeFitter）
                LayoutRebuilder.ForceRebuildLayoutImmediate(activeDragRect);

                // 计算合适尺寸（自动 + 容错），然后 clamp 到 min/max
                Vector2 calcSize = CalculateCloneSize(activeDragRect, rootCanvas, fallbackCloneSize);
                float clampedW = Mathf.Clamp(calcSize.x, minCloneSize.x, maxCloneSize.x);
                float clampedH = Mathf.Clamp(calcSize.y, minCloneSize.y, maxCloneSize.y);
                activeDragRect.sizeDelta = new Vector2(clampedW, clampedH);
            }

            // 把 clone 立刻放到鼠标位置（BeginDrag 时）
            Vector2 localPointerPos;
            var cam = eventData.pressEventCamera;
            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, cam, out localPointerPos))
            {
                if (activeDragRect != null) activeDragRect.anchoredPosition = localPointerPos;
                pointerOffset = Vector2.zero; // 从鼠标位置开始拖拽
            }
            else
            {
                pointerOffset = Vector2.zero;
            }

            // keep original's raycast enabled (original remains interactive/hoverable in library)
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;
        }
        else
        {
            // ORIGINAL MOVE MODE
            usingClone = false;

            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false; // allow raycast to pass through

            if (dragRoot == null)
            {
                rootCanvas = GetComponentInParent<Canvas>();
                if (rootCanvas != null)
                {
                    dragRoot = rootCanvas.transform;
                    canvasRect = dragRoot as RectTransform;
                }
            }

            if (dragRoot != null)
                transform.SetParent(dragRoot, true);

            activeDragRect = rectTransform;

            Vector2 localPointerPos;
            var cam = eventData.pressEventCamera;
            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, cam, out localPointerPos))
            {
                pointerOffset = rectTransform.anchoredPosition - localPointerPos;
            }
            else
            {
                pointerOffset = Vector2.zero;
            }
        }

        // SelectionManager 操作（如果你的项目有 SelectionManager）
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();

            if (attachSelectionToDragRoot && dragRoot != null)
            {
                SelectionManager.Instance.AttachSelectionTo(dragRoot, true);
            }

            if (activeDragRect != null)
                SelectionManager.Instance.SetHoverOverride(activeDragRect);

            TrySafeBringToTop(SelectionManager.Instance.ActiveSelection);
            TrySafeBringToTop(activeDragRect != null ? activeDragRect.transform : transform);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (currentDragging != this) return;
        if (canvasRect == null) return;
        if (activeDragRect == null) return;

        Vector2 localPointerPos;
        var cam = eventData.pressEventCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, cam, out localPointerPos))
        {
            activeDragRect.anchoredPosition = localPointerPos + pointerOffset;

            if (SelectionManager.Instance != null && SelectionManager.Instance.ActiveSelection != null)
            {
                SelectionManager.Instance.UpdateSelectionTransform(SelectionManager.Instance.ActiveSelection, activeDragRect);
            }
        }

        // Raycast 排除 clone 自身
        if (EventSystem.current != null && SelectionManager.Instance != null)
        {
            PointerEventData ped = new PointerEventData(EventSystem.current);
            ped.position = eventData.position;
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);

            RectTransform hoveredCard = null;
            foreach (var r in results)
            {
                if (r.gameObject == null) continue;
                if (dragCloneGO != null && (r.gameObject == dragCloneGO || r.gameObject.transform.IsChildOf(dragCloneGO.transform))) continue;

                var handler = r.gameObject.GetComponentInParent<CardDragHandler>();
                if (handler != null)
                {
                    if (handler == this) continue;
                    hoveredCard = handler.GetComponent<RectTransform>();
                    break;
                }
            }

            if (hoveredCard != null)
            {
                SelectionManager.Instance.SetHoverOverride(hoveredCard);
                TrySafeBringToTop(SelectionManager.Instance.ActiveSelection);
                TrySafeBringToTop(activeDragRect != null ? activeDragRect.transform : transform);
            }
            else
            {
                SelectionManager.Instance.SetHoverOverride(activeDragRect);
                TrySafeBringToTop(SelectionManager.Instance.ActiveSelection);
                TrySafeBringToTop(activeDragRect != null ? activeDragRect.transform : transform);
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (currentDragging == this) currentDragging = null;

        if (usingClone)
        {
            if (dragCloneGO != null)
            {
                Destroy(dragCloneGO);
                dragCloneGO = null;
            }
            usingClone = false;
        }
        else
        {
            if (cg != null) cg.blocksRaycasts = true;

            if (transform.parent == dragRoot)
            {
                if (originalParent != null)
                {
                    transform.SetParent(originalParent, false);
                    transform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount));
                }
            }
        }

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }

        activeDragRect = null;
    }

    // 如果放到某个 drop area（外部调用）
    public void OnDroppedTo(Transform dropParent)
    {
        if (usingClone)
        {
            if (dragCloneGO != null)
            {
                Destroy(dragCloneGO);
                dragCloneGO = null;
            }
            usingClone = false;
        }
        else
        {
            if (dropParent == null) dropParent = originalParent;
            transform.SetParent(dropParent, false);
            if (cg != null) cg.blocksRaycasts = true;

            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }

        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }

    void OnDisable()
    {
        if (currentDragging == this) currentDragging = null;
        if (SelectionManager.Instance != null)
        {
            SelectionManager.Instance.ClearAllHoverEntries();
            SelectionManager.Instance.Hide();
            SelectionManager.Instance.RestoreSelection();
        }
    }

    void OnDestroy()
    {
        if (currentDragging == this) currentDragging = null;
    }

    // 把 transform 放到父级最后（安全检查父级是否激活）
    void TrySafeBringToTop(Transform t)
    {
        if (t == null || t.parent == null) return;
        if (!t.parent.gameObject.activeInHierarchy) return;
        t.SetAsLastSibling();
    }

    // 计算 clone 要用的尺寸（优先级：LayoutUtility preferred -> rectTransform.rect -> Image.sprite -> fallback）
    Vector2 CalculateCloneSize(RectTransform cloneRect, Canvas canvas, Vector2 fallback)
    {
        if (cloneRect == null) return fallback;

        float prefW = LayoutUtility.GetPreferredWidth(cloneRect);
        float prefH = LayoutUtility.GetPreferredHeight(cloneRect);

        if (!Mathf.Approximately(prefW, 0f) && !Mathf.Approximately(prefH, 0f))
        {
            return new Vector2(prefW, prefH);
        }

        Vector2 rectSize = cloneRect.rect.size;
        if (!Mathf.Approximately(rectSize.x, 0f) && !Mathf.Approximately(rectSize.y, 0f))
        {
            return rectSize;
        }

        var img = cloneRect.GetComponentInChildren<Image>();
        if (img != null && img.sprite != null)
        {
            var sp = img.sprite;
            float w = sp.rect.width / sp.pixelsPerUnit;
            float h = sp.rect.height / sp.pixelsPerUnit;
            if (!Mathf.Approximately(w, 0f) && !Mathf.Approximately(h, 0f))
            {
                return new Vector2(w, h);
            }
        }

        return fallback;
    }

    // ========== 新增：在克隆上隐藏信息面板的实现（不禁用任何组件） ==========
    void DisableInfoChildInClone(GameObject clone)
    {
        if (clone == null || infoNameTokens == null || infoNameTokens.Length == 0) return;

        var all = clone.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t == null || t.gameObject == null) continue;
            var nm = t.name ?? "";
            foreach (var token in infoNameTokens)
            {
                if (string.IsNullOrEmpty(token)) continue;
                if (nm.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t.gameObject.SetActive(false);
                    break;
                }
            }
        }
    }

    IEnumerator EnsureInfoRemainsHidden(GameObject clone, int framesToEnforce = 3)
    {
        if (clone == null || infoNameTokens == null || infoNameTokens.Length == 0) yield break;

        for (int i = 0; i < framesToEnforce; i++)
        {
            yield return new WaitForEndOfFrame();
            if (clone == null) yield break;
            DisableInfoChildInClone(clone);
        }
    }
}
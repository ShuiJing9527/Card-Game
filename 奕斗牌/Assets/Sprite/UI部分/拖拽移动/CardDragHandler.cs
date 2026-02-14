using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public interface ICardDropTarget
{
    bool CanAccept(CardDragHandler card);
    void Accept(CardDragHandler card, PointerEventData eventData);
}

public static class DragManager
{
    public static CardDragHandler CurrentDrag;
}

// 占位信息：记录来源、角色与创建者
public class PlaceholderInfo : MonoBehaviour
{
    public string sourceName;
    public string role; // "card" or "info"
    public GameObject owner;
}

[RequireComponent(typeof(RectTransform))]
public class CardDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Overlay / 显示")]
    public Transform overlayRoot;
    public Canvas overlayCanvas;

    [Header("拖拽行为")]
    public int overlaySortingOrder = 100;

    [Header("拖拽尺寸设置")]
    public bool useAbsoluteDragSize = true;
    public Vector2 dragSize = new Vector2(240f, 240f);
    public float dragScaleMultiplier = 1.2f;

    [Header("卡片数据")]
    public int CardId = -1;
    public bool IsStack = false;
    public int StackCount = 0;

    [Header("卡片信息节点定位")]
    public Transform infoRoot;
    public string infoNodeName = "卡片信息";

    // 公开兼容字段/方法（为外部 DropForwarder / DeckManager 提供接口）
    // 标记该实例是否为“预览”（外部放下后可能需要销毁）
    public bool isPreview = false;

    // 如果外部需要保存/还原父级，也提供访问方法
    public void SaveOriginalParent()
    {
        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();
    }

    public void RestoreToOriginalParentIfNeeded()
    {
        if (originalParent != null && transform.parent != originalParent)
        {
            transform.SetParent(originalParent, false);
            transform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount));
        }
    }

    // 基本字段
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 originalSizeDelta;
    private Vector3 originalLocalScale;
    private bool originalSizeSaved = false;

    private Transform originalParent;
    private int originalSiblingIndex;

    // card 占位
    private GameObject _placeholder;
    private bool placeholderOwned = false;

    // info 占位
    private GameObject infoPlaceholder;
    private bool infoPlaceholderOwned = false;

    private bool isDragging = false;

    // overlay 排序备份
    private bool originalCanvasOverrideSorting;
    private int originalCanvasSortingOrder;

    // info 剥离/恢复
    private Transform originalInfoTransform = null;
    private Transform originalInfoPrevParent = null;
    private int originalInfoPrevSibling = -1;

    private RectTransform originalInfoRect = null;
    private RectTransformValues originalInfoRectValues = null;
    private bool originalInfoHadRect = false;

    private Vector3 originalInfoLocalPosition;
    private Quaternion originalInfoLocalRotation;
    private Vector3 originalInfoLocalScale;

    private class RectTransformValues
    {
        public Vector2 anchoredPosition;
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 sizeDelta;
        public Vector2 pivot;
        public Vector3 localScale;
    }

    // clone 拖拽（用于 IsStack）
    private GameObject dragCloneVisual;
    private RectTransform dragCloneRect;
    private bool hasValidClone = false;

    private bool savedBlocksRaycasts = true;

    // ----- 新增字段：提升拖拽目标 Canvas 的支持 -----
    private Canvas tempDragCanvas;                    // the Canvas component currently used for elevating this dragged object
    private bool tempDragCanvasCreated = false;       // whether we created it ourselves
    private bool tempDragCanvasOriginalOverride;
    private int tempDragCanvasOriginalOrder;
    private RenderMode tempDragCanvasOriginalRenderMode;
    private Camera tempDragCanvasOriginalCamera;

    // 若 clone 也需要提升（用于 IsStack），记录它的临时 Canvas（若创建）
    private Canvas tempCloneCanvas;
    private bool tempCloneCanvasCreated = false;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void OnDisable()
    {
        ClearSelfPlaceholders();
        isDragging = false;
        if (DragManager.CurrentDrag == this) DragManager.CurrentDrag = null;

        // 确保临时 Canvas 被恢复 / 清理，防止在禁用时残留
        RestoreDragCanvasForThis();
        RestoreCloneCanvasIfAny();
    }

    private void OnDestroy()
    {
        ClearSelfPlaceholders();
        if (dragCloneVisual != null)
        {
            Destroy(dragCloneVisual);
            dragCloneVisual = null;
        }
        isDragging = false;
        if (DragManager.CurrentDrag == this) DragManager.CurrentDrag = null;

        RestoreDragCanvasForThis();
        RestoreCloneCanvasIfAny();
    }

    private void ClearSelfPlaceholders()
    {
        if (_placeholder != null)
        {
            var pi = _placeholder.GetComponent<PlaceholderInfo>();
            if (pi != null && pi.owner == this.gameObject)
            {
                Destroy(_placeholder);
            }
            _placeholder = null;
            placeholderOwned = false;
        }

        if (infoPlaceholder != null)
        {
            var pi = infoPlaceholder.GetComponent<PlaceholderInfo>();
            if (pi != null && pi.owner == this.gameObject)
            {
                Destroy(infoPlaceholder);
            }
            infoPlaceholder = null;
            infoPlaceholderOwned = false;
        }
    }

    public void OnPointerDown(PointerEventData eventData) { }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isDragging) return;

        if (overlayRoot == null)
        {
            GameObject found = GameObject.Find("Overlay");
            if (found != null) overlayRoot = found.transform;
        }
        if (overlayCanvas == null && overlayRoot != null)
        {
            overlayCanvas = overlayRoot.GetComponentInParent<Canvas>();
        }

        // 保存原父（兼容外部 SaveOriginalParent 调用）
        SaveOriginalParent();

        if (IsStack)
        {
            dragCloneVisual = CreateDragClone();
            if (dragCloneVisual != null) hasValidClone = true;

            // 若本组件此前创建了占位，则销毁（堆叠不移动原始 UI）
            if (_placeholder != null)
            {
                var pi = _placeholder.GetComponent<PlaceholderInfo>();
                if (pi != null && pi.owner == this.gameObject) Destroy(_placeholder);
                _placeholder = null;
                placeholderOwned = false;
            }
        }
        else
        {
            bool createdCard;
            _placeholder = CreatePlaceholder(rectTransform, originalParent, originalSiblingIndex, out createdCard, "card");
            placeholderOwned = createdCard;

            if (overlayRoot != null)
            {
                transform.SetParent(overlayRoot, true);

                if (overlayCanvas != null)
                {
                    originalCanvasOverrideSorting = overlayCanvas.overrideSorting;
                    originalCanvasSortingOrder = overlayCanvas.sortingOrder;
                    overlayCanvas.overrideSorting = true;
                    overlayCanvas.sortingOrder = overlaySortingOrder;
                }
            }

            if (!originalSizeSaved && rectTransform != null)
            {
                originalSizeDelta = rectTransform.sizeDelta;
                originalLocalScale = rectTransform.localScale;
                originalSizeSaved = true;
            }
            ApplyDragSizeOrScale();
        }

        // 在将对象移动到 overlayRoot 后，提升该对象的 Canvas 排序到最高（仅影响此卡片，不移动 info）。
        ElevateDragCanvasForThis();

        // 查找并剥离 info
        originalInfoTransform = null;
        if (infoRoot != null)
        {
            originalInfoTransform = infoRoot;
        }
        else
        {
            foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
            {
                if (t == this.transform) continue;
                if (t.name == infoNodeName) { originalInfoTransform = t; break; }
            }
            if (originalInfoTransform == null)
            {
                foreach (Transform t in transform.GetComponentsInChildren<Transform>(true))
                {
                    if (t == this.transform) continue;
                    string nm = t.name.ToLower();
                    if (nm.Contains("信息") || nm.Contains("info")) { originalInfoTransform = t; break; }
                }
            }
        }

        if (originalInfoTransform != null && originalInfoTransform.gameObject.activeInHierarchy)
        {
            Transform infoOrigParent = originalInfoTransform.parent;
            int infoOrigSibling = originalInfoTransform.GetSiblingIndex();

            originalInfoPrevParent = infoOrigParent;
            originalInfoPrevSibling = infoOrigSibling;

            // 关键：若 info 的目标父与 card 的原父相同，则不单独创建 info 占位（避免重复）
            bool sameParentAsCard = (originalInfoPrevParent == originalParent && originalParent != null);
            if (sameParentAsCard)
            {
                // reuse card placeholder instead of creating a second one
                infoPlaceholder = _placeholder;
                infoPlaceholderOwned = false; // 不归 info 单独销毁
                Debug.Log($"[CardDrag] Info 位于与 card 相同父级，复用 card 占位，跳过创建 info 占位");
            }
            else
            {
                // 仅在目标父不是 overlay 时才创建 info 占位
                bool needInfoPh = originalInfoPrevParent != null;
                if (needInfoPh)
                {
                    if ((overlayRoot != null && originalInfoPrevParent == overlayRoot) ||
                        (overlayCanvas != null && originalInfoPrevParent == overlayCanvas.transform))
                    {
                        needInfoPh = false;
                    }
                }

                if (needInfoPh)
                {
                    bool createdInfo;
                    RectTransform infoSourceRect = originalInfoTransform.GetComponent<RectTransform>();
                    infoPlaceholder = CreatePlaceholder(infoSourceRect != null ? infoSourceRect : originalInfoTransform as RectTransform,
                                                        originalInfoPrevParent, originalInfoPrevSibling, out createdInfo, "info");
                    infoPlaceholderOwned = createdInfo;
                }
                else
                {
                    infoPlaceholder = null;
                    infoPlaceholderOwned = false;
                }
            }

            // 保存 info 状态
            originalInfoRect = originalInfoTransform.GetComponent<RectTransform>();
            if (originalInfoRect != null)
            {
                originalInfoHadRect = true;
                originalInfoRectValues = new RectTransformValues()
                {
                    anchoredPosition = originalInfoRect.anchoredPosition,
                    anchorMin = originalInfoRect.anchorMin,
                    anchorMax = originalInfoRect.anchorMax,
                    sizeDelta = originalInfoRect.sizeDelta,
                    pivot = originalInfoRect.pivot,
                    localScale = originalInfoRect.localScale
                };
            }
            else
            {
                originalInfoHadRect = false;
                originalInfoLocalPosition = originalInfoTransform.localPosition;
                originalInfoLocalRotation = originalInfoTransform.localRotation;
                originalInfoLocalScale = originalInfoTransform.localScale;
            }

            Transform stableParent = (overlayCanvas != null) ? overlayCanvas.transform :
                                     (overlayRoot != null) ? overlayRoot : originalInfoPrevParent;

            if (stableParent != null)
            {
                originalInfoTransform.SetParent(stableParent, true);
            }

            Debug.Log($"[CardDrag] Info '{originalInfoTransform.name}' 临时剥离到 '{(stableParent != null ? stableParent.name : "(null)")}', infoPlaceholder={(infoPlaceholder != null ? infoPlaceholder.name : "(null)")} owned={infoPlaceholderOwned}");
        }

        savedBlocksRaycasts = canvasGroup.blocksRaycasts;
        canvasGroup.blocksRaycasts = false;

        isDragging = true;

        if (CardId == -1)
        {
            var match = Regex.Match(gameObject.name, @"\d{4,}");
            if (match.Success && int.TryParse(match.Value, out int parsedId)) CardId = parsedId;
        }

        DragManager.CurrentDrag = this;
        Debug.Log($"[CardDrag] BeginDrag CardId={CardId} IsStack={IsStack} path={GetFullPath(transform)} placeholderOwned={placeholderOwned}");
    }

    private GameObject CreateDragClone()
    {
        if (rectTransform == null) return null;

        GameObject clone = new GameObject("CardDragClone_" + CardId);
        clone.layer = gameObject.layer;

        RectTransform cloneRT = clone.AddComponent<RectTransform>();
        cloneRT.anchorMin = Vector2.one * 0.5f;
        cloneRT.anchorMax = Vector2.one * 0.5f;
        cloneRT.pivot = Vector2.one * 0.5f;

        if (useAbsoluteDragSize)
        {
            float scaleFactor = overlayCanvas != null ? overlayCanvas.scaleFactor : 1f;
            cloneRT.sizeDelta = dragSize / scaleFactor;
        }
        else
        {
            cloneRT.sizeDelta = rectTransform != null ? rectTransform.sizeDelta : new Vector2(100, 150);
        }

        Image sourceArt = null;
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            string nm = img.gameObject.name.ToLower();
            if (nm.Contains("art") || nm.Contains("thumb") || nm.Contains("card")) { sourceArt = img; break; }
            if (sourceArt == null) sourceArt = img;
        }
        if (sourceArt != null)
        {
            GameObject artGO = new GameObject("Art");
            artGO.layer = clone.layer;
            var artRT = artGO.AddComponent<RectTransform>();
            artRT.SetParent(cloneRT, false);
            var artImg = artGO.AddComponent<Image>();
            artImg.sprite = sourceArt.sprite;
            artImg.color = sourceArt.color;
            artImg.preserveAspect = sourceArt.preserveAspect;
            artRT.anchorMin = new Vector2(0, 0);
            artRT.anchorMax = new Vector2(1, 1);
            artRT.pivot = new Vector2(0.5f, 0.5f);
            artRT.anchoredPosition = Vector2.zero;
            artRT.sizeDelta = cloneRT.sizeDelta;
        }

        Text countText = null;
        foreach (var t in GetComponentsInChildren<Text>(true))
        {
            string nm = t.gameObject.name.ToLower();
            if (nm.Contains("count") || nm.Contains("stack")) { countText = t; break; }
        }
        if (countText != null)
        {
            GameObject ct = new GameObject("Count");
            ct.layer = clone.layer;
            var ctRT = ct.AddComponent<RectTransform>();
            ctRT.SetParent(cloneRT, false);
            var txt = ct.AddComponent<Text>();
            txt.text = countText.text;
            txt.font = countText.font;
            txt.fontSize = countText.fontSize;
            txt.color = countText.color;
            txt.alignment = countText.alignment;
            ctRT.anchorMin = new Vector2(1, 1);
            ctRT.anchorMax = new Vector2(1, 1);
            ctRT.pivot = new Vector2(1, 1);
            ctRT.anchoredPosition = new Vector2(-10, -10);
            ctRT.sizeDelta = new Vector2(60, 30);
        }

        if (overlayRoot != null) clone.transform.SetParent(overlayRoot, false);
        else if (overlayCanvas != null) clone.transform.SetParent(overlayCanvas.transform, false);
        else clone.transform.SetParent(transform.parent, false);

        CanvasGroup cg = clone.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        if (overlayCanvas != null)
        {
            Vector2 mousePos;
            RectTransform canvasRT = overlayCanvas.transform as RectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT,
                                                                   Input.mousePosition,
                                                                   overlayCanvas.worldCamera,
                                                                   out mousePos);
            cloneRT.anchoredPosition = mousePos;
        }
        else clone.transform.position = Input.mousePosition;

        dragCloneRect = cloneRT;

        // 确保 clone 也在最上层可见（如果需要）
        ElevateCloneCanvas(clone);

        return clone;
    }

    private void ApplyDragSizeOrScale()
    {
        if (rectTransform == null) return;

        if (useAbsoluteDragSize)
        {
            float scaleFactor = overlayCanvas != null ? overlayCanvas.scaleFactor : 1f;
            Vector2 targetSize = dragSize / scaleFactor;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize.y);
        }
        else rectTransform.localScale = originalLocalScale * dragScaleMultiplier;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        if (IsStack && hasValidClone && dragCloneRect != null)
        {
            if (overlayCanvas != null)
            {
                RectTransform canvasRect = overlayCanvas.transform as RectTransform;
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, eventData.pressEventCamera, out localPoint))
                    dragCloneRect.anchoredPosition = localPoint;
            }
            else
            {
                if (eventData.pressEventCamera != null)
                    RectTransformUtility.ScreenPointToWorldPointInRectangle(dragCloneRect, eventData.position, eventData.pressEventCamera, out Vector3 worldPos);
                else dragCloneRect.position = eventData.position;
            }
        }
        else
        {
            if (overlayCanvas != null)
            {
                RectTransform canvasRect = overlayCanvas.transform as RectTransform;
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, eventData.pressEventCamera, out localPoint))
                    rectTransform.localPosition = localPoint;
            }
            else
            {
                if (eventData.pressEventCamera != null)
                    RectTransformUtility.ScreenPointToWorldPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector3 worldPos);
                else rectTransform.position = eventData.position;
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;

        if (DragManager.CurrentDrag == this) DragManager.CurrentDrag = null;

        ICardDropTarget targetSlot = null;
        List<RaycastResult> raycastResults = new List<RaycastResult>();
        if (EventSystem.current != null)
            EventSystem.current.RaycastAll(eventData, raycastResults);

        foreach (var result in raycastResults)
        {
            if (result.gameObject == null) continue;
            if (_placeholder != null && result.gameObject == _placeholder) continue;

            targetSlot = result.gameObject.GetComponentInParent<ICardDropTarget>();
            if (targetSlot != null)
            {
                if (targetSlot.CanAccept(this)) break;
                else targetSlot = null;
            }
        }

        if (targetSlot != null)
        {
            try { targetSlot.Accept(this, eventData); }
            catch (Exception ex) { Debug.LogWarning($"[CardDrag] target.Accept 异常: {ex.Message}\n{ex.StackTrace}"); RestoreToOriginalPosition(); }
        }
        else RestoreToOriginalPosition();

        if (IsStack)
        {
            if (dragCloneVisual != null) { Destroy(dragCloneVisual); dragCloneVisual = null; }
            hasValidClone = false;
        }
        else
        {
            if (originalSizeSaved && rectTransform != null)
            {
                if (useAbsoluteDragSize)
                {
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalSizeDelta.x);
                    rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalSizeDelta.y);
                }
                else rectTransform.localScale = originalLocalScale;
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.overrideSorting = originalCanvasOverrideSorting;
                overlayCanvas.sortingOrder = originalCanvasSortingOrder;
            }
        }

        // 恢复被剥离的 info
        if (originalInfoTransform != null)
        {
            if (originalInfoPrevParent != null)
            {
                originalInfoTransform.SetParent(originalInfoPrevParent, false);
                int idx = Mathf.Clamp(originalInfoPrevSibling, 0, originalInfoPrevParent.childCount);
                originalInfoTransform.SetSiblingIndex(idx);

                if (originalInfoHadRect && originalInfoRect != null && originalInfoRectValues != null)
                {
                    originalInfoRect.pivot = originalInfoRectValues.pivot;
                    originalInfoRect.anchorMin = originalInfoRectValues.anchorMin;
                    originalInfoRect.anchorMax = originalInfoRectValues.anchorMax;
                    originalInfoRect.anchoredPosition = originalInfoRectValues.anchoredPosition;
                    originalInfoRect.sizeDelta = originalInfoRectValues.sizeDelta;
                    originalInfoRect.localScale = originalInfoRectValues.localScale;
                }
                else
                {
                    originalInfoTransform.localPosition = originalInfoLocalPosition;
                    originalInfoTransform.localRotation = originalInfoLocalRotation;
                    originalInfoTransform.localScale = originalInfoLocalScale;
                }

                // 销毁仅由本组件创建的 info 占位（若 infoPlaceholder == _placeholder 则 infoPlaceholderOwned==false，不会销毁）
                if (infoPlaceholder != null)
                {
                    var pi = infoPlaceholder.GetComponent<PlaceholderInfo>();
                    if (pi != null && pi.owner == this.gameObject && infoPlaceholderOwned)
                        Destroy(infoPlaceholder);
                }
                infoPlaceholder = null;
                infoPlaceholderOwned = false;

                Canvas.ForceUpdateCanvases();
                var parentRT = originalInfoPrevParent as RectTransform;
                if (parentRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRT);

                Debug.Log($"[CardDrag] 恢复 info '{originalInfoTransform.name}' 到 {GetFullPath(originalInfoPrevParent)} idx={idx}");
            }
            else
            {
                Debug.LogWarning("[CardDrag] originalInfoPrevParent 为 null，info 未能恢复父级。");
            }
        }

        // 恢复射线并销毁 card 占位（若本组件创建且归属）
        canvasGroup.blocksRaycasts = savedBlocksRaycasts;

        if (_placeholder != null)
        {
            var pi = _placeholder.GetComponent<PlaceholderInfo>();
            if (pi != null && pi.owner == this.gameObject && placeholderOwned)
                Destroy(_placeholder);
        }
        _placeholder = null;
        placeholderOwned = false;

        // 恢复并清理我们临时提升的 Canvas（卡片与 clone）
        RestoreDragCanvasForThis();
        RestoreCloneCanvasIfAny();

        ClearInfoRestoreContext();

        if (originalParent != null)
        {
            var rpRT = originalParent as RectTransform;
            if (rpRT != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rpRT);
        }
    }

    private void RestoreToOriginalPosition()
    {
        if (IsStack) { /* 不移动原始 UI */ }
        else
        {
            if (originalParent == null) return;
            transform.SetParent(originalParent, false);
            transform.SetSiblingIndex(Mathf.Clamp(originalSiblingIndex, 0, originalParent.childCount));
        }
    }

    // CreatePlaceholder: 名称包含 role，优先复用 PlaceholderInfo；否则按名字；都没有再创建
    private GameObject CreatePlaceholder(RectTransform sourceRect, Transform parent, int siblingIndex, out bool createdNew, string role = "card")
    {
        createdNew = false;
        if (parent == null) return null;

        string sourceName = sourceRect != null ? sourceRect.name : "placeholder";
        string phName = $"{sourceName}_{role}_ph";

        Debug.Log($"CreatePlaceholder called by '{gameObject.name}' role={role} phName={phName} parent={(parent != null ? parent.name : "(null)")}\nStack:\n{Environment.StackTrace}");

        // 1) 按 PlaceholderInfo 复用
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            var info = c.GetComponent<PlaceholderInfo>();
            if (info != null && info.sourceName == sourceName && info.role == role)
            {
                Debug.Log($"CreatePlaceholder: reuse existing (by PlaceholderInfo) '{c.name}' in '{parent.name}' owner={(info.owner != null ? info.owner.name : "(null)")}");
                createdNew = false;
                return c.gameObject;
            }
        }

        // 2) 再按名字查找
        Transform existingByName = parent.Find(phName);
        if (existingByName != null)
        {
            var info = existingByName.GetComponent<PlaceholderInfo>();
            if (info == null)
            {
                info = existingByName.gameObject.AddComponent<PlaceholderInfo>();
                info.sourceName = sourceName;
                info.role = role;
                info.owner = this.gameObject;
                Debug.Log($"CreatePlaceholder: found existing by name '{phName}' and attached PlaceholderInfo(owner={gameObject.name})");
            }
            else Debug.Log($"CreatePlaceholder: found existing by name '{phName}' with PlaceholderInfo(owner={(info.owner != null ? info.owner.name : "(null)")})");

            createdNew = false;
            return existingByName.gameObject;
        }

        // 3) 创建新占位
        GameObject ph = new GameObject(phName, typeof(RectTransform));
        ph.transform.SetParent(parent, false);
        var rt = ph.GetComponent<RectTransform>();
        rt.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount));

        if (sourceRect != null)
        {
            rt.anchorMin = sourceRect.anchorMin;
            rt.anchorMax = sourceRect.anchorMax;
            rt.anchoredPosition = sourceRect.anchoredPosition;
            rt.sizeDelta = sourceRect.sizeDelta;
            rt.pivot = sourceRect.pivot;
            rt.localScale = sourceRect.localScale;
        }
        else rt.sizeDelta = new Vector2(1, 1);

        var cg = ph.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var phInfo = ph.AddComponent<PlaceholderInfo>();
        phInfo.sourceName = sourceName;
        phInfo.role = role;
        phInfo.owner = this.gameObject;

        createdNew = true;
        Debug.Log($"CreatePlaceholder: created new '{phName}' in '{parent.name}' owner={gameObject.name}");
        return ph;
    }

    private void ClearInfoRestoreContext()
    {
        originalInfoTransform = null;
        originalInfoPrevParent = null;
        originalInfoPrevSibling = -1;
        originalInfoRect = null;
        originalInfoRectValues = null;
        originalInfoHadRect = false;
        originalInfoLocalPosition = Vector3.zero;
        originalInfoLocalRotation = Quaternion.identity;
        originalInfoLocalScale = Vector3.one;

        if (infoPlaceholder != null)
        {
            var pi = infoPlaceholder.GetComponent<PlaceholderInfo>();
            if (pi != null && pi.owner == this.gameObject && infoPlaceholderOwned) Destroy(infoPlaceholder);
        }
        infoPlaceholder = null;
        infoPlaceholderOwned = false;
    }

    private string GetFullPath(Transform t)
    {
        if (t == null) return "(null)";
        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    public void PlaceInto(Transform targetParent, int siblingIndex = -1)
    {
        if (targetParent == null) return;
        transform.SetParent(targetParent, false);
        if (siblingIndex >= 0) transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, targetParent.childCount));
        else transform.SetAsLastSibling();
    }

    // ---------- 新增方法：提升并恢复拖拽卡片 Canvas ----------

    // 在开始拖拽时调用：确保本对象的 Canvas overrideSorting=true，sortingOrder = maxExisting + 1
    private void ElevateDragCanvasForThis()
    {
        try
        {
            // 找到或创建 Canvas
            Canvas myCanvas = GetComponent<Canvas>();
            if (myCanvas == null)
            {
                tempDragCanvas = gameObject.AddComponent<Canvas>();
                tempDragCanvasCreated = true;
            }
            else
            {
                tempDragCanvas = myCanvas;
                tempDragCanvasCreated = false;
            }

            // 备份原值
            tempDragCanvasOriginalOverride = tempDragCanvas.overrideSorting;
            tempDragCanvasOriginalOrder = tempDragCanvas.sortingOrder;
            tempDragCanvasOriginalRenderMode = tempDragCanvas.renderMode;
            tempDragCanvasOriginalCamera = tempDragCanvas.worldCamera;

            // 计算场景中最大 sortingOrder，然后加 1
            int maxOrder = int.MinValue;
            foreach (var c in FindObjectsOfType<Canvas>())
            {
                // 优先考虑 overrideSorting，但 sortingOrder 是我们关心的数值
                if (c != null)
                {
                    maxOrder = Math.Max(maxOrder, c.sortingOrder);
                }
            }
            if (maxOrder == int.MinValue) maxOrder = 0;

            tempDragCanvas.overrideSorting = true;
            tempDragCanvas.sortingOrder = maxOrder + 1;

            // 与 overlayCanvas 保持 renderMode/camera 一致，避免不可见的问题
            if (overlayCanvas != null)
            {
                tempDragCanvas.renderMode = overlayCanvas.renderMode;
                if (overlayCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                    tempDragCanvas.worldCamera = overlayCanvas.worldCamera;
            }

            Debug.Log($"[CardDrag] Elevated drag canvas on '{gameObject.name}' to order {tempDragCanvas.sortingOrder} (created={tempDragCanvasCreated})");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDrag] ElevateDragCanvasForThis 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // 恢复或销毁我们临时创建/修改的 Canvas
    private void RestoreDragCanvasForThis()
    {
        if (tempDragCanvas == null) return;

        try
        {
            tempDragCanvas.overrideSorting = tempDragCanvasOriginalOverride;
            tempDragCanvas.sortingOrder = tempDragCanvasOriginalOrder;
            tempDragCanvas.renderMode = tempDragCanvasOriginalRenderMode;
            tempDragCanvas.worldCamera = tempDragCanvasOriginalCamera;

            if (tempDragCanvasCreated)
            {
                // 我们在运行时创建的 Canvas，销毁它
                Destroy(tempDragCanvas);
            }

            Debug.Log($"[CardDrag] Restored drag canvas on '{gameObject.name}' (created={tempDragCanvasCreated})");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDrag] RestoreDragCanvasForThis 异常: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            tempDragCanvas = null;
            tempDragCanvasCreated = false;
        }
    }

    // 对 clone 做相似的提升（如果 clone 存在）
    private void ElevateCloneCanvas(GameObject clone)
    {
        if (clone == null) return;
        try
        {
            Canvas c = clone.GetComponent<Canvas>();
            if (c == null)
            {
                c = clone.AddComponent<Canvas>();
                tempCloneCanvasCreated = true;
                tempCloneCanvas = c;
            }
            else
            {
                tempCloneCanvasCreated = false;
                tempCloneCanvas = c;
            }

            // 计算当前场景最大 order（与提升卡片时使用相同逻辑）
            int maxOrder = int.MinValue;
            foreach (var cc in FindObjectsOfType<Canvas>())
            {
                if (cc != null)
                {
                    maxOrder = Math.Max(maxOrder, cc.sortingOrder);
                }
            }
            if (maxOrder == int.MinValue) maxOrder = 0;

            tempCloneCanvas.overrideSorting = true;
            // 设为和临时 drag canvas 相同或稍高，确保 clone 可见
            tempCloneCanvas.sortingOrder = maxOrder + 1;

            if (overlayCanvas != null)
            {
                tempCloneCanvas.renderMode = overlayCanvas.renderMode;
                if (overlayCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                    tempCloneCanvas.worldCamera = overlayCanvas.worldCamera;
            }

            Debug.Log($"[CardDrag] Elevated clone canvas on '{clone.name}' to order {tempCloneCanvas.sortingOrder} (created={tempCloneCanvasCreated})");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDrag] ElevateCloneCanvas 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void RestoreCloneCanvasIfAny()
    {
        if (tempCloneCanvas == null) return;

        try
        {
            // 如果我们创建了 clone Canvas，则销毁；否则尽可能恢复 overrideSorting=false（但我们未备份 clone 原始值以简化）
            if (tempCloneCanvasCreated)
            {
                Destroy(tempCloneCanvas);
            }
            else
            {
                // 无备份的情况下，尽量取消 overrideSorting（注意：如果 clone 原本就有特定设置，此处可能覆盖——通常 clone 是我们新建的）
                tempCloneCanvas.overrideSorting = false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CardDrag] RestoreCloneCanvasIfAny 异常: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            tempCloneCanvas = null;
            tempCloneCanvasCreated = false;
        }
    }
}
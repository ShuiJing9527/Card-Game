using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance;

    [Header("References")]
    public RectTransform overlayRect;
    public RectTransform selectionPrefab;

    [Header("Settings")]
    public bool useScaleFactor = true;
    public float widthScale = 1.2f;
    public float heightScale = 1.2f;
    public float padX = 8f;
    public float padY = 8f;

    [Header("Design sizing")]
    // 卡牌在美术稿/设计稿中的尺寸（像 Inspector 上显示的设计尺寸，比如 220x220）
    public Vector2 cardDesignSize = new Vector2(220f, 220f);
    public enum DesignMatch { Width, Height, Max, Min }
    public DesignMatch designMatch = DesignMatch.Width;

    [Header("Alignment")]
    public Vector2 manualOffset = Vector2.zero;
    public bool pixelRound = true;

    [Header("Debug")]
    public bool debugLogs = false;
    public float debugLogMinInterval = 0.25f;

    Canvas overlayCanvas;
    Camera overlayCamera;

    RectTransform activeSel;
    RectTransform targetCard;

    RectTransform selParentRect;
    Canvas selParentCanvas;

    bool selUsingCardSibling = false;

    // 记录 selection 预制体实例的原始尺寸（local UI 单位）
    Vector2 selPrefabBaseSize = Vector2.zero;

    Transform selShowOriginalParent = null;
    int selShowOriginalSibling = -1;
    bool selShowReparented = false;

    Transform selOriginalParent;
    int selOriginalSibling = -1;
    Vector2 selOriginalAnchorMin, selOriginalAnchorMax, selOriginalPivot, selOriginalAnchoredPos, selOriginalSizeDelta;
    Canvas selOriginalCanvas;
    bool selOriginalCanvasOverride = false;
    int selOriginalSortingOrder = 0;
    int selOriginalSortingLayerID = 0;
    bool selectionAttached = false;

    string lastLoggedCardName = null;
    Vector2 lastLoggedLocalCenter = Vector2.zero;
    Vector2 lastLoggedSizeUI = Vector2.zero;
    int lastLoggedSelOrder = int.MinValue;
    float lastLogTime = 0f;
    const float EPS_POS = 0.5f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);

        if (overlayRect == null)
        {
            Debug.LogError("SelectionManager: overlayRect 未设置！");
            enabled = false;
            return;
        }

        overlayCanvas = overlayRect.GetComponentInParent<Canvas>();
        if (overlayCanvas == null)
        {
            Debug.LogError("SelectionManager: overlayRect 没有找到父级 Canvas！");
            enabled = false;
            return;
        }

        overlayCamera = (overlayCanvas.renderMode == RenderMode.ScreenSpaceCamera || overlayCanvas.renderMode == RenderMode.WorldSpace)
            ? overlayCanvas.worldCamera : null;

        selParentRect = overlayRect;
        selParentCanvas = overlayCanvas;
    }

    void Update()
    {
        if (activeSel != null && targetCard != null)
        {
            UpdateSelectionTransform(activeSel, targetCard);
        }
    }

    public RectTransform ActiveSelection => activeSel;

    public void ShowFor(RectTransform card)
    {
        if (card == null) return;
        EnsureActiveSelExists();

        ConfigureSelectionCanvasRelativeToCard(card);

        targetCard = card;
        UpdateSelectionTransform(activeSel, targetCard);
        activeSel.gameObject.SetActive(true);

        var cg = activeSel.GetComponent<CanvasGroup>();
        if (cg == null) cg = activeSel.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
    }

    public void ShowOnce(RectTransform card)
    {
        if (card == null) return;
        EnsureActiveSelExists();

        ConfigureSelectionCanvasRelativeToCard(card);

        UpdateSelectionTransform(activeSel, card);
        activeSel.gameObject.SetActive(true);
        targetCard = null;
    }

    public void Hide()
    {
        if (activeSel != null)
        {
            activeSel.gameObject.SetActive(false);
            targetCard = null;

            if (selShowReparented)
            {
                if (selShowOriginalParent != null)
                {
                    activeSel.SetParent(selShowOriginalParent, false);
                    activeSel.SetSiblingIndex(Mathf.Clamp(selShowOriginalSibling, 0, selShowOriginalParent.childCount));
                }
                else
                {
                    activeSel.SetParent(overlayRect, false);
                }

                selParentRect = overlayRect;
                selParentCanvas = overlayCanvas;

                selShowOriginalParent = null;
                selShowOriginalSibling = -1;
                selShowReparented = false;
                selUsingCardSibling = false;
            }
        }
    }

    public void AttachSelectionTo(Transform newParent, bool keepWorldPosition = true)
    {
        if (newParent == null) return;
        EnsureActiveSelExists();

        if (selectionAttached) RestoreSelection();

        selOriginalParent = activeSel.parent;
        selOriginalSibling = activeSel.GetSiblingIndex();
        selOriginalAnchorMin = activeSel.anchorMin;
        selOriginalAnchorMax = activeSel.anchorMax;
        selOriginalPivot = activeSel.pivot;
        selOriginalAnchoredPos = activeSel.anchoredPosition;
        selOriginalSizeDelta = activeSel.sizeDelta;

        selOriginalCanvas = activeSel.GetComponent<Canvas>();
        if (selOriginalCanvas != null)
        {
            selOriginalCanvasOverride = selOriginalCanvas.overrideSorting;
            selOriginalSortingOrder = selOriginalCanvas.sortingOrder;
            selOriginalSortingLayerID = selOriginalCanvas.sortingLayerID;
        }
        else
        {
            selOriginalCanvasOverride = false;
            selOriginalSortingOrder = 0;
            selOriginalSortingLayerID = 0;
        }

        targetCard = null;
        activeSel.SetParent(newParent, keepWorldPosition);
        activeSel.SetAsLastSibling();
        selectionAttached = true;
        activeSel.gameObject.SetActive(true);

        selParentRect = newParent as RectTransform;
        selParentCanvas = selParentRect != null ? selParentRect.GetComponentInParent<Canvas>() : null;
        if (selParentCanvas == null) selParentCanvas = overlayCanvas;

        selUsingCardSibling = false;
    }

    public void RestoreSelection()
    {
        if (!selectionAttached || activeSel == null) return;
        activeSel.SetParent(selOriginalParent, false);

        activeSel.anchorMin = selOriginalAnchorMin;
        activeSel.anchorMax = selOriginalAnchorMax;
        activeSel.pivot = selOriginalPivot;
        activeSel.sizeDelta = selOriginalSizeDelta;
        activeSel.anchoredPosition = selOriginalAnchoredPos;

        if (selOriginalParent != null)
            activeSel.SetSiblingIndex(Mathf.Clamp(selOriginalSibling, 0, selOriginalParent.childCount));

        Canvas selCanvas = activeSel.GetComponent<Canvas>();
        if (selOriginalCanvas != null)
        {
            if (selCanvas == null) selCanvas = activeSel.gameObject.AddComponent<Canvas>();
            selCanvas.overrideSorting = selOriginalCanvasOverride;
            selCanvas.sortingOrder = selOriginalSortingOrder;
            selCanvas.sortingLayerID = selOriginalSortingLayerID;
        }
        else
        {
            if (selCanvas != null) selCanvas.overrideSorting = false;
        }

        selectionAttached = false;

        selParentRect = overlayRect;
        selParentCanvas = overlayCanvas;
        selUsingCardSibling = false;

        var parentRt = selOriginalParent as RectTransform;
        if (parentRt != null) StartCoroutine(ForceRebuildNextFrame(parentRt));
    }

    IEnumerator ForceRebuildNextFrame(RectTransform rt)
    {
        yield return null;
        if (rt != null)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    void EnsureActiveSelExists()
    {
        if (activeSel == null)
        {
            if (selectionPrefab == null)
            {
                Debug.LogError("SelectionManager: selectionPrefab 未设置！");
                return;
            }

            GameObject go = Instantiate(selectionPrefab.gameObject, overlayRect, false);
            go.name = selectionPrefab.gameObject.name;
            activeSel = go.GetComponent<RectTransform>();
            activeSel.localScale = Vector3.one;

            var arf = activeSel.GetComponent<AspectRatioFitter>();
            if (arf != null) arf.enabled = false;
            var csf = activeSel.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;

            // 记录预制体基准尺寸（local rect 尺寸）
            selPrefabBaseSize = activeSel.rect.size;

            activeSel.anchorMin = activeSel.anchorMax = new Vector2(0.5f, 0.5f);
            activeSel.pivot = new Vector2(0.5f, 0.5f);

            EnsureSelectionIgnoreLayout();

            activeSel.gameObject.SetActive(false);

            selParentRect = overlayRect;
            selParentCanvas = overlayCanvas;
            selUsingCardSibling = false;
        }
    }

    void EnsureSelectionIgnoreLayout()
    {
        if (activeSel == null) return;
        var le = activeSel.GetComponent<LayoutElement>();
        if (le == null) le = activeSel.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        var arf = activeSel.GetComponent<AspectRatioFitter>();
        if (arf != null) arf.enabled = false;
        var csf = activeSel.GetComponent<ContentSizeFitter>();
        if (csf != null) csf.enabled = false;

        activeSel.anchorMin = activeSel.anchorMax = new Vector2(0.5f, 0.5f);
        activeSel.pivot = new Vector2(0.5f, 0.5f);
    }

    public void UpdateSelectionTransform(RectTransform selRect, RectTransform cardRect)
    {
        if (selRect == null || cardRect == null) return;

        // sibling 模式：直接复制 card 的局部 transform，但尺寸按"预制体基准尺寸 * 设计比例"计算
        if (selUsingCardSibling && selRect.parent == cardRect.parent)
        {
            selRect.localScale = Vector3.one;

            // match anchor/pivot to avoid anchor caused offsets
            selRect.anchorMin = cardRect.anchorMin;
            selRect.anchorMax = cardRect.anchorMax;
            selRect.pivot = cardRect.pivot;

            // 计算卡牌当前“可视”尺寸（考虑 loss y scale）
            float cardVisW = cardRect.rect.width * cardRect.lossyScale.x;
            float cardVisH = cardRect.rect.height * cardRect.lossyScale.y;

            // 计算基于设计稿的 scale
            float scaleX = cardDesignSize.x > 0f ? (cardVisW / cardDesignSize.x) : 1f;
            float scaleY = cardDesignSize.y > 0f ? (cardVisH / cardDesignSize.y) : 1f;
            float designScale = 1f;
            switch (designMatch)
            {
                case DesignMatch.Width: designScale = scaleX; break;
                case DesignMatch.Height: designScale = scaleY; break;
                case DesignMatch.Max: designScale = Mathf.Max(scaleX, scaleY); break;
                case DesignMatch.Min: designScale = Mathf.Min(scaleX, scaleY); break;
            }

            // 目标尺寸 = 预制体基准尺寸 * 设计 scale * width/heightScale
            float targetW = selPrefabBaseSize.x * designScale * widthScale;
            float targetH = selPrefabBaseSize.y * designScale * heightScale;
            selRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetW);
            selRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);

            // 直接复制 localPosition（并可加 manualOffset）
            Vector3 finalLocal = cardRect.localPosition + new Vector3(manualOffset.x, manualOffset.y, 0f);
            if (pixelRound) finalLocal = new Vector3(Mathf.Round(finalLocal.x), Mathf.Round(finalLocal.y), Mathf.Round(finalLocal.z));
            selRect.localPosition = finalLocal;

            if (debugLogs)
            {
                Debug.LogFormat("[SelectionManager - SIBLING] parent={0} localPos:{1} size:{2} (cardVis:{3},{4} cardDesign:{5},{6} selBase:{7})",
                    selRect.parent != null ? selRect.parent.name : "null",
                    selRect.localPosition,
                    new Vector2(targetW, targetH),
                    cardVisW, cardVisH,
                    cardDesignSize.x, cardDesignSize.y,
                    selPrefabBaseSize);
            }

            return;
        }

        // 非 sibling（保留原来逻辑）
        selRect.localScale = Vector3.one;
        selRect.pivot = new Vector2(0.5f, 0.5f);
        selRect.anchorMin = selRect.anchorMax = new Vector2(0.5f, 0.5f);

        Canvas cardCanvas = cardRect.GetComponentInParent<Canvas>();
        Camera cardCam = (cardCanvas != null && cardCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? cardCanvas.worldCamera : null;

        Vector3[] corners = new Vector3[4];
        cardRect.GetWorldCorners(corners);
        Vector3 worldCenter = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(cardCam, worldCenter);

        RectTransform parentRect = selParentRect != null ? selParentRect : overlayRect;
        Canvas targetParentCanvas = selParentCanvas != null ? selParentCanvas : overlayCanvas;

        Camera parentCam = null;
        if (targetParentCanvas != null && targetParentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            parentCam = targetParentCanvas.worldCamera;

        Vector2 localCenter;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenCenter, parentCam, out localCenter);

        Vector2 blScreen = RectTransformUtility.WorldToScreenPoint(cardCam, corners[0]);
        Vector2 trScreen = RectTransformUtility.WorldToScreenPoint(cardCam, corners[2]);
        Vector2 screenSize = trScreen - blScreen;
        float cardW_px = Mathf.Abs(screenSize.x);
        float cardH_px = Mathf.Abs(screenSize.y);

        float scaleFactor = targetParentCanvas != null ? Mathf.Max(targetParentCanvas.scaleFactor, 0.0001f) : 1f;
        Vector2 targetSizeUI;
        if (useScaleFactor)
            targetSizeUI = new Vector2(cardW_px * widthScale, cardH_px * heightScale) / scaleFactor;
        else
            targetSizeUI = (new Vector2(cardW_px, cardH_px) + new Vector2(padX * 2f, padY * 2f)) / scaleFactor;

        selRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSizeUI.x);
        selRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSizeUI.y);

        Vector2 finalPos = localCenter + manualOffset;
        if (pixelRound)
        {
            finalPos.x = Mathf.Round(finalPos.x);
            finalPos.y = Mathf.Round(finalPos.y);
        }

        selRect.anchoredPosition = finalPos;

        if (debugLogs)
        {
            Canvas selCanvas = selRect.GetComponent<Canvas>();
            int currentOrder = selCanvas != null ? selCanvas.sortingOrder : 0;
            bool cardChanged = lastLoggedCardName != cardRect.name;
            bool posChanged = Vector2.Distance(lastLoggedLocalCenter, finalPos) > EPS_POS;
            bool sizeChanged = Vector2.Distance(lastLoggedSizeUI, targetSizeUI) > EPS_POS;
            bool orderChanged = lastLoggedSelOrder != currentOrder;
            bool timeExceeded = (Time.time - lastLogTime) > debugLogMinInterval;

            if (cardChanged || posChanged || sizeChanged || orderChanged || timeExceeded)
            {
                Debug.LogFormat("[SelectionManager] parent={0} localCenter:{1} sizeUI:{2} card_px:{3}/{4} parentScale:{5} cardCanvas:{6} selOrder:{7}",
                    parentRect != null ? parentRect.name : "null",
                    finalPos, targetSizeUI, cardW_px, cardH_px,
                    scaleFactor,
                    cardCanvas != null ? cardCanvas.name : "null",
                    currentOrder);

                lastLoggedCardName = cardRect.name;
                lastLoggedLocalCenter = finalPos;
                lastLoggedSizeUI = targetSizeUI;
                lastLoggedSelOrder = currentOrder;
                lastLogTime = Time.time;
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void ConfigureSelectionCanvasRelativeToCard(RectTransform card)
    {
        if (activeSel == null || card == null) return;

        Canvas selCanvas = activeSel.GetComponent<Canvas>();
        if (selCanvas == null) selCanvas = activeSel.gameObject.AddComponent<Canvas>();

        int cardMaxOrder = int.MinValue;
        int cardLayerID = 0;
        Canvas parentCanvas = card.GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            cardLayerID = parentCanvas.sortingLayerID;
            cardMaxOrder = Mathf.Max(cardMaxOrder, parentCanvas.sortingOrder);
        }

        var canvasesInCard = card.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvasesInCard)
        {
            if (c == null) continue;
            if (c.overrideSorting)
            {
                cardMaxOrder = Mathf.Max(cardMaxOrder, c.sortingOrder);
                cardLayerID = c.sortingLayerID;
            }
        }

        if (cardMaxOrder == int.MinValue)
        {
            cardMaxOrder = overlayCanvas != null ? overlayCanvas.sortingOrder : 0;
            if (overlayCanvas != null) cardLayerID = overlayCanvas.sortingLayerID;
        }

        if (debugLogs)
        {
            Debug.LogFormat("[SelectionManager] Card canvas: {0}, overlayCanvas: {1}", parentCanvas != null ? parentCanvas.name : "null", overlayCanvas != null ? overlayCanvas.name : "null");
        }

        bool compatibleRenderPath = true;
        if (overlayCanvas != null && parentCanvas != null)
        {
            if (overlayCanvas.renderMode != parentCanvas.renderMode) compatibleRenderPath = false;
            else if (overlayCanvas.renderMode == RenderMode.ScreenSpaceCamera || overlayCanvas.renderMode == RenderMode.WorldSpace)
            {
                if (overlayCanvas.worldCamera != parentCanvas.worldCamera) compatibleRenderPath = false;
            }
        }

        bool usedCardSibling = false;
        Transform desiredParent = overlayRect;
        if (overlayCanvas != null && parentCanvas != null &&
            overlayCanvas.renderMode == RenderMode.ScreenSpaceOverlay &&
            parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            Transform cardContainer = card.parent;
            if (cardContainer != null)
            {
                desiredParent = cardContainer;
                usedCardSibling = true;
                if (debugLogs) Debug.Log("[SelectionManager] Using card's container as parent for selection (ScreenSpace-Overlay, sibling ordering).");
            }
        }
        else
        {
            if (!compatibleRenderPath && parentCanvas != null)
            {
                desiredParent = parentCanvas.transform;
                if (debugLogs)
                    Debug.LogFormat("[SelectionManager] Render path mismatch: overlayCanvas.renderMode={0}, cardCanvas.renderMode={1}. Reparenting selection to card's canvas for correct ordering.",
                        overlayCanvas != null ? overlayCanvas.renderMode.ToString() : "null",
                        parentCanvas != null ? parentCanvas.renderMode.ToString() : "null");
            }
            else
            {
                desiredParent = overlayRect;
            }
        }

        if (!selectionAttached)
        {
            if (activeSel.parent != desiredParent)
            {
                if (!selShowReparented)
                {
                    selShowOriginalParent = activeSel.parent;
                    selShowOriginalSibling = activeSel.GetSiblingIndex();
                }

                activeSel.SetParent(desiredParent, false);
                selShowReparented = true;
            }

            selParentRect = desiredParent as RectTransform;
            selParentCanvas = selParentRect != null ? selParentRect.GetComponentInParent<Canvas>() : null;
            if (selParentCanvas == null) selParentCanvas = overlayCanvas;

            if (usedCardSibling)
            {
                int cardIndex = card.GetSiblingIndex();
                activeSel.SetSiblingIndex(Mathf.Clamp(cardIndex, 0, desiredParent.childCount));

                if (selCanvas != null)
                {
                    selCanvas.overrideSorting = false;
                    selCanvas.sortingOrder = 0;
                    if (selParentCanvas != null)
                    {
                        selCanvas.renderMode = selParentCanvas.renderMode;
                        selCanvas.worldCamera = selParentCanvas.worldCamera;
                        selCanvas.planeDistance = selParentCanvas.planeDistance;
                    }
                }

                // 将 selection 的 anchor/pivot 与 card 匹配（位置一致），但尺寸按设计比例基于预制体基准尺寸进行缩放
                activeSel.anchorMin = card.anchorMin;
                activeSel.anchorMax = card.anchorMax;
                activeSel.pivot = card.pivot;

                // 先把大小设为预制基准（UpdateSelectionTransform 会再计算）
                activeSel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, selPrefabBaseSize.x);
                activeSel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, selPrefabBaseSize.y);

                Vector3 localPos = card.localPosition + new Vector3(manualOffset.x, manualOffset.y, 0f);
                if (pixelRound) localPos = new Vector3(Mathf.Round(localPos.x), Mathf.Round(localPos.y), Mathf.Round(localPos.z));
                activeSel.localPosition = localPos;

                selUsingCardSibling = true;

                if (debugLogs) Debug.LogFormat("[SelectionManager] Selection parented to card container '{0}', siblingIndex={1}", desiredParent.name, cardIndex);
            }
            else
            {
                selUsingCardSibling = false;
            }
        }

        if (!usedCardSibling)
        {
            selCanvas.overrideSorting = true;
            int selOrder = cardMaxOrder - 1;
            selCanvas.sortingOrder = selOrder;
            selCanvas.sortingLayerID = cardLayerID;

            if (desiredParent == parentCanvas?.transform)
            {
                selCanvas.renderMode = parentCanvas.renderMode;
                if (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera || parentCanvas.renderMode == RenderMode.WorldSpace)
                {
                    selCanvas.worldCamera = parentCanvas.worldCamera;
                    selCanvas.planeDistance = parentCanvas.planeDistance;
                }
            }
            else
            {
                if (overlayCanvas != null)
                {
                    selCanvas.renderMode = overlayCanvas.renderMode;
                    selCanvas.worldCamera = overlayCanvas.worldCamera;
                    selCanvas.planeDistance = overlayCanvas.planeDistance;
                }
            }

            if (debugLogs)
            {
                Debug.LogFormat("[SelectionManager] Configured selection canvas: selOrder={0}, cardMaxOrder={1}, layerID={2}, card={3}, desiredParent={4}",
                    selCanvas.sortingOrder, cardMaxOrder, cardLayerID, card != null ? card.name : "null", desiredParent != null ? desiredParent.name : "null");
            }

            selParentRect = desiredParent as RectTransform;
            selParentCanvas = selParentRect != null ? selParentRect.GetComponentInParent<Canvas>() : overlayCanvas;
            selUsingCardSibling = false;
        }

        var cg = activeSel.GetComponent<CanvasGroup>();
        if (cg == null) cg = activeSel.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
    }
}
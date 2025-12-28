using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class TooltipController : MonoBehaviour
{
    public static TooltipController Instance;

    [Header("UI 绑定 (手动拖拽推荐)")]
    public RectTransform rootRect;
    public RectTransform textBoxRect;     // 可选（承载文本的容器）
    public TextMeshProUGUI titleField;    // 可选
    public TextMeshProUGUI bodyField;     // 必需

    [Header("自动绑定（仅调试用）")]
    public bool autoBindIfMissing = true;

    [Header("尺寸与位置")]
    public Vector2 padding = new Vector2(16f, 12f);
    public float maxWidth = 840f;    // 可在 Inspector 调整，用于自动测量的最大宽度
    public float maxHeight = 220f;   // 可在 Inspector 调整，用于自动测量的最大高度
    public float aboveOffset = 8f;

    [Header("Root / TextBox 尺寸调整（可在 Inspector 里微调）")]
    [Tooltip("rootSize = textBoxSize + rootSizeDelta，允许为负值，使 root 比 textBox 小")]
    public Vector2 rootSizeDelta = new Vector2(-16f, -12f);
    [Tooltip("如果希望 textBox 的可用文字区域比默认再宽一些，可用此项微调")]
    public Vector2 extraTextBoxPadding = Vector2.zero;

    [Header("强制/手动 文本框尺寸")]
    public bool forceTextBoxSize = false;
    public Vector2 forcedTextBoxSize = new Vector2(860f, 240f);
    [Tooltip("把 forcedTextBoxSize 限制在 maxWidth/maxHeight 内（如果希望强制尺寸不超出 Canvas 约束，勾选）")]
    public bool clampForcedToMax = true;
    [Tooltip("如果勾选，则如果你在 Inspector 里手动调整了 textBoxRect 的尺寸，脚本会尊重该尺寸（并不每次覆盖）。仅在 textBoxRect 不为 null 时生效。")]
    public bool respectInspectorTextBoxSize = false;

    [Header("Canvas 排序")]
    public bool forceTopSorting = true;
    public int sortingOrder = 1000;

    [Header("悬停判定（只在此小区域内才显示 tooltip）")]
    public float hoverYOffsetMin = 5f;
    public float hoverYOffsetMax = 10f;
    public float horizontalTolerance = 60f;
    public bool allowOutsideHoverForDebug = false;

    [Header("稳定性/性能参数")]
    public float positionEpsilon = 3f;
    public float sizeEpsilon = 2f;
    public float minUpdateInterval = 0.05f;
    public bool ignoreRaycasts = true;

    [Header("调试")]
    public bool debugLogCombined = false;

    Canvas parentCanvas;
    RectTransform canvasRect;
    Canvas tooltipCanvas;
    CanvasGroup canvasGroup;

    private RectTransform lastTarget;
    private string lastTitle = "";
    private string lastCombinedBody = "";
    private Vector2 lastSize = Vector2.zero;
    private Vector2 lastAnchoredPos = Vector2.positiveInfinity;
    private float lastUpdateTime = -1f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        tooltipCanvas = GetComponentInParent<Canvas>();
        parentCanvas = tooltipCanvas;
        canvasRect = parentCanvas ? parentCanvas.GetComponent<RectTransform>() : null;

        if (autoBindIfMissing)
        {
            if (rootRect == null) rootRect = GetComponent<RectTransform>();
            if (bodyField == null || titleField == null)
            {
                var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
                if (tmps.Length > 0 && bodyField == null) bodyField = tmps[tmps.Length - 1];
                if (tmps.Length > 1 && titleField == null) titleField = tmps[0];
            }
            if (textBoxRect == null && bodyField != null) textBoxRect = bodyField.rectTransform.parent as RectTransform;
        }

        if (rootRect == null || bodyField == null)
        {
            Debug.LogError("TooltipController: 请在 Inspector 绑定 rootRect 与 bodyField(TextMeshProUGUI)。");
        }

        if (tooltipCanvas != null && forceTopSorting)
        {
            tooltipCanvas.overrideSorting = true;
            tooltipCanvas.sortingOrder = sortingOrder;
        }

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = !ignoreRaycasts;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 1f;

        if (rootRect != null) SetGraphicsRaycastTarget(rootRect, !ignoreRaycasts);

        if (bodyField != null)
        {
            bodyField.enableAutoSizing = true;
            if (bodyField.fontSizeMin <= 0) bodyField.fontSizeMin = 8;
            if (bodyField.fontSizeMax <= 0) bodyField.fontSizeMax = 40;
            bodyField.overflowMode = TextOverflowModes.Overflow;
        }
        if (titleField != null)
        {
            titleField.enableAutoSizing = true;
            if (titleField.fontSizeMin <= 0) titleField.fontSizeMin = 10;
            if (titleField.fontSizeMax <= 0) titleField.fontSizeMax = 48;
            titleField.overflowMode = TextOverflowModes.Overflow;
        }

        gameObject.SetActive(false);
    }

    public void ShowAbove(RectTransform targetRect, string body, float verticalOffset = 0f)
    {
        ShowAbove(targetRect, null, body, (string[])null, verticalOffset, false);
    }

    public void ShowAbove(RectTransform targetRect, string title, string body, float verticalOffset = 0f)
    {
        ShowAbove(targetRect, title, body, (string[])null, verticalOffset, false);
    }

    public void ShowAbove(RectTransform targetRect, string title, string body, string bondText, float verticalOffset = 0f)
    {
        ShowAbove(targetRect, title, body, new string[] { bondText }, verticalOffset, false);
    }

    public void ShowAbove(RectTransform targetRect, string title, string body, string[] extraLines, float verticalOffset = 0f, bool bypassHoverCheck = false)
    {
        if (rootRect == null || bodyField == null) { Debug.LogError("TooltipController.ShowAbove: rootRect or bodyField is null."); return; }
        if (targetRect == null) { Debug.LogWarning("TooltipController.ShowAbove: targetRect is null."); return; }

        string combinedBody = (body ?? "").TrimEnd();
        if (extraLines != null && extraLines.Length > 0)
        {
            List<string> nonEmpty = new List<string>();
            foreach (var ln in extraLines) if (!string.IsNullOrEmpty(ln)) nonEmpty.Add(ln);
            if (nonEmpty.Count > 0)
            {
                if (!string.IsNullOrEmpty(combinedBody)) combinedBody += "\n\n";
                combinedBody += string.Join("\n", nonEmpty);
            }
        }

        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners); // 0=bl,1=tl,2=tr,3=br
        Vector3 topCenterWorld = (corners[1] + corners[2]) * 0.5f;
        Camera cam = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? parentCanvas.worldCamera : null;
        Vector2 topCenterScreen = RectTransformUtility.WorldToScreenPoint(cam, topCenterWorld);
        Vector2 mouseScreen = Input.mousePosition;

        if (!bypassHoverCheck)
        {
            bool mouseOverTarget = RectTransformUtility.RectangleContainsScreenPoint(targetRect, mouseScreen, cam);
            Vector2 delta = mouseScreen - topCenterScreen;
            bool mouseAboveBand = (delta.y >= hoverYOffsetMin && delta.y <= hoverYOffsetMax && Mathf.Abs(delta.x) <= horizontalTolerance);
            bool inHoverBand = mouseOverTarget || mouseAboveBand;
            if (!inHoverBand && !allowOutsideHoverForDebug) { Hide(); return; }
        }

        float now = Time.unscaledTime;
        if (now - lastUpdateTime < minUpdateInterval) { gameObject.SetActive(true); return; }

        if (titleField != null)
        {
            bool hasTitle = !string.IsNullOrEmpty(title);
            titleField.gameObject.SetActive(hasTitle);
            titleField.text = title ?? "";
        }

        bodyField.text = combinedBody;

        if (canvasGroup != null) canvasGroup.alpha = 0f;
        bodyField.ForceMeshUpdate(true);
        if (titleField != null && titleField.gameObject.activeSelf) titleField.ForceMeshUpdate(true);
        Canvas.ForceUpdateCanvases();
        if (textBoxRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(textBoxRect);
        else LayoutRebuilder.ForceRebuildLayoutImmediate(bodyField.rectTransform);

        // 计算可测量的 max（以 canvas 宽度为准）
        float canvasW = (canvasRect != null) ? canvasRect.rect.width : maxWidth;
        float allowedMaxWidth = Mathf.Min(maxWidth, Mathf.Max(40f, canvasW - 20f));
        float measureWidth = Mathf.Max(40f, allowedMaxWidth - padding.x);

        Vector2 titlePref = Vector2.zero;
        if (titleField != null && titleField.gameObject.activeSelf) titlePref = titleField.GetPreferredValues(titleField.text, measureWidth, Mathf.Infinity);
        Vector2 bodyPref = bodyField.GetPreferredValues(bodyField.text, measureWidth, Mathf.Infinity);

        float contentW = Mathf.Max(titlePref.x, bodyPref.x);
        float targetW = Mathf.Clamp(contentW + padding.x, 40f, allowedMaxWidth);
        float contentH = titlePref.y + bodyPref.y;
        float targetH = Mathf.Clamp(contentH + padding.y, 18f, maxHeight);

        // 默认 boxSize 基于文本测量
        Vector2 boxSize = new Vector2(targetW, targetH);
        float innerW = Mathf.Max(8f, boxSize.x - padding.x + extraTextBoxPadding.x);
        float innerH = Mathf.Max(8f, boxSize.y - padding.y + extraTextBoxPadding.y);

        // 如果用户在 Inspector 手动设置了 textBoxRect 并选择尊重它
        if (respectInspectorTextBoxSize && textBoxRect != null)
        {
            Vector2 inspectorSize = new Vector2(textBoxRect.rect.width, textBoxRect.rect.height);
            // inspectorSize 视为 inner (即文本容器尺寸)
            innerW = Mathf.Max(8f, inspectorSize.x);
            innerH = Mathf.Max(8f, inspectorSize.y);
            boxSize = new Vector2(innerW + padding.x, innerH + padding.y);
        }

        // 强制尺寸分支
        if (forceTextBoxSize)
        {
            Vector2 forced = forcedTextBoxSize;
            if (clampForcedToMax)
            {
                forced.x = Mathf.Min(forced.x, maxWidth);
                forced.y = Mathf.Min(forced.y, maxHeight);
            }
            innerW = Mathf.Max(8f, forced.x);
            innerH = Mathf.Max(8f, forced.y);
            boxSize = new Vector2(innerW + padding.x, innerH + padding.y);
        }

        // 计算 root 大小（基于 boxSize + rootSizeDelta）
        Vector2 computedRootSize = new Vector2(
            Mathf.Max(8f, boxSize.x + rootSizeDelta.x),
            Mathf.Max(8f, boxSize.y + rootSizeDelta.y)
        );

        // 写入 root 大小
        rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, computedRootSize.x);
        rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, computedRootSize.y);

        // 写入 textBox / body 大小（根据上面的 innerW/innerH）
        if (textBoxRect != null)
        {
            textBoxRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerW);
            textBoxRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, innerH);
            textBoxRect.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(textBoxRect);
        }
        else
        {
            var brt = bodyField.rectTransform;
            brt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerW);
            brt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, innerH);
            brt.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(brt);
        }

        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // 计算位置并 Clamp（基于 root 尺寸）
        float offY = (verticalOffset != 0f) ? verticalOffset : aboveOffset;
        Vector2 finalScreen = RectTransformUtility.WorldToScreenPoint(cam, (targetRect.GetWorldCornersCachedTopCenter())); // helper below
        finalScreen += new Vector2(0f, offY);

        if (canvasRect == null) { Debug.LogError("TooltipController.ShowAbove: canvasRect is null. 确认 Tooltip 在 Canvas 下。"); return; }

        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.SetAsLastSibling();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, finalScreen, cam, out Vector2 localPoint);
        Vector2 desiredAnchoredPos = ClampToCanvas(localPoint, computedRootSize);

        float canvasTop = canvasRect.rect.height * 0.5f;
        float topLimitForPivotBottom = canvasTop - computedRootSize.y * (1f - rootRect.pivot.y) - 5f;
        if (desiredAnchoredPos.y >= topLimitForPivotBottom - 1f)
        {
            rootRect.pivot = new Vector2(0.5f, 1f);
            Vector2 finalScreenBelow = RectTransformUtility.WorldToScreenPoint(cam, (targetRect.GetWorldCornersCachedTopCenter())) - new Vector2(0f, offY + 2f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, finalScreenBelow, cam, out Vector2 localBelow);
            desiredAnchoredPos = ClampToCanvas(localBelow, computedRootSize);
        }

        bool sameTarget = (lastTarget == targetRect);
        bool sameContent = (lastTitle == (title ?? "") && lastCombinedBody == combinedBody);
        bool smallPosDiff = (lastAnchoredPos != Vector2.positiveInfinity) && (Vector2.Distance(lastAnchoredPos, desiredAnchoredPos) <= positionEpsilon);
        bool smallSizeDiff = (lastSize != Vector2.zero) && (Vector2.Distance(lastSize, computedRootSize) <= sizeEpsilon);

        if (gameObject.activeSelf && sameTarget && sameContent && smallPosDiff && smallSizeDiff)
        {
            lastUpdateTime = now;
            gameObject.SetActive(true);
            return;
        }

        rootRect.anchoredPosition = desiredAnchoredPos;
        Canvas.ForceUpdateCanvases();

        lastTarget = targetRect;
        lastTitle = title ?? "";
        lastCombinedBody = combinedBody;
        lastSize = computedRootSize;
        lastAnchoredPos = desiredAnchoredPos;
        lastUpdateTime = now;

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private Vector2 ClampToCanvas(Vector2 desiredLocalPos, Vector2 rootSize)
    {
        if (canvasRect == null) return desiredLocalPos;
        Vector2 pivot = rootRect != null ? rootRect.pivot : new Vector2(0.5f, 0f);

        float canvasW = canvasRect.rect.width;
        float canvasH = canvasRect.rect.height;

        float leftLimit = -canvasW * 0.5f + rootSize.x * pivot.x + 5f;
        float rightLimit = canvasW * 0.5f - rootSize.x * (1f - pivot.x) - 5f;
        float bottomLimit = -canvasH * 0.5f + rootSize.y * pivot.y + 5f;
        float topLimit = canvasH * 0.5f - rootSize.y * (1f - pivot.y) - 5f;

        float x = Mathf.Clamp(desiredLocalPos.x, leftLimit, rightLimit);
        float y = Mathf.Clamp(desiredLocalPos.y, bottomLimit, topLimit);
        return new Vector2(x, y);
    }

    private void SetGraphicsRaycastTarget(RectTransform parent, bool enable)
    {
        if (parent == null) return;
        var graphics = parent.GetComponentsInChildren<Graphic>(true);
        foreach (var g in graphics)
        {
            try { g.raycastTarget = enable; } catch { }
        }
    }
}

// 辅助扩展：便捷取得 targetRect 顶部中心的 WorldPoint（避免重复 GetWorldCorners 调用）
public static class RectTransformExtensions
{
    public static Vector3 GetWorldCornersCachedTopCenter(this RectTransform rt)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners); // 0=bl,1=tl,2=tr,3=br
        return (corners[1] + corners[2]) * 0.5f;
    }
}
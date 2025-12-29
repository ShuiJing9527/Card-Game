using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class TooltipController : MonoBehaviour
{
    public static TooltipController Instance;

    [Header("UI 绑定 (手动拖拽推荐)")]
    public RectTransform rootRect;
    public RectTransform textBoxRect;
    public TextMeshProUGUI titleField;
    public TextMeshProUGUI bodyField;

    [Header("自动绑定（仅调试用）")]
    public bool autoBindIfMissing = true;

    [Header("尺寸与位置")]
    public Vector2 padding = new Vector2(16f, 12f);
    public float maxWidth = 840f;
    public float maxHeight = 220f;
    public float aboveOffset = 8f;

    [Header("Root / TextBox 尺寸调整")]
    public Vector2 rootSizeDelta = new Vector2(-16f, -12f);
    public Vector2 extraTextBoxPadding = Vector2.zero;

    [Header("强制/手动 文本框尺寸")]
    public bool forceTextBoxSize = false;
    public Vector2 forcedTextBoxSize = new Vector2(860f, 240f);
    public bool clampForcedToMax = true;
    public bool respectInspectorTextBoxSize = false;

    [Header("元素颜色（按顺序：土, 木, 水, 火, 金, Spell）")]
    public Color[] elementColors = new Color[6]
    {
        new Color(0.85f,0.7f,0.45f,1f), // 土
        new Color(0.45f,0.8f,0.5f,1f),  // 木
        new Color(0.45f,0.7f,0.95f,1f), // 水
        new Color(1f,0.55f,0.4f,1f),    // 火
        new Color(0.9f,0.85f,0.5f,1f),  // 金
        new Color(0.75f,0.55f,0.95f,1f) // 术/咒
    };

    [Tooltip("要被改色的 Image（例如 Tooltip 的背景或标识图标）。可留空（则不会改色）。")]
    public Image bondTextImage;

    [Header("Canvas 排序")]
    public bool forceTopSorting = true;
    public int sortingOrder = 1000;

    [Header("悬停判定")]
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

    [Header("A方案开关")]
    [Tooltip("默认 true：只在 extraLines 非空或 targetRect 被标记为 BondZone 时才进行羁绊检测；关闭后会回退到从 body 提取候选行检测（更容易误判）。")]
    public bool requireExtraLinesForDetection = true;

    [Tooltip("备用：如果无法挂组件，可在目标 RectTransform 名称中包含这些关键字被识别为羁绊区域（不如组件稳妥）。")]
    public string[] bondZoneNameKeywords = new string[] { "bond", "link", "羁绊", "Link" };

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

    // 颜色恢复记录与检测缓存
    private Color originalBondImageColor = default(Color);
    private int currentElementIndex = -1; // -1 表示未应用任何元素色
    private int lastDetectedElement = -2; // 缓存上一次检测到的元素（-2 表示未检测）

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

        if (bondTextImage != null) originalBondImageColor = bondTextImage.color;
        gameObject.SetActive(false);
    }

    // 显式重载：外部可以直接传元素索引以强制着色（0..5）
    public void ShowAboveWithElement(RectTransform targetRect, string title, string body, string bondText, int elementIndex, float verticalOffset = 0f)
    {
        if (elementIndex >= 0 && elementIndex < elementColors.Length)
        {
            ApplyElementColor(elementIndex);
            lastDetectedElement = elementIndex;
        }
        else
        {
            RestoreBondImageColorIfNeeded();
            lastDetectedElement = -1;
        }
        ShowAbove(targetRect, title, body, new string[] { bondText }, verticalOffset, false);
    }

    // 兼容旧接口的重载
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

    // 主函数：改进后的判定逻辑（优先 extraLines；否则根据 targetRect 是否为 BondZone 决定）
    public void ShowAbove(RectTransform targetRect, string title, string body, string[] extraLines, float verticalOffset = 0f, bool bypassHoverCheck = false)
    {
        if (rootRect == null || bodyField == null) { Debug.LogError("TooltipController.ShowAbove: rootRect or bodyField is null."); return; }
        if (targetRect == null) { Debug.LogWarning("TooltipController.ShowAbove: targetRect is null."); return; }

        if (debugLogCombined)
        {
            string extrasPreview = (extraLines != null && extraLines.Length > 0) ? string.Join(" | ", extraLines) : "<no-extraLines>";
            string bodyPreview = (body != null && body.Length > 80) ? body.Substring(0, 80) + "..." : (body ?? "<null>");
            Debug.Log($"[Tooltip Debug] ShowAbove called for target={targetRect.name}, title={(title ?? "<null>")}, bodyPreview={bodyPreview}, extraLines={extrasPreview}");
        }

        // 组装最终显示文本（body + extraLines）
        string combinedBody = (body ?? "").TrimEnd();
        List<string> nonEmptyExtra = new List<string>();
        if (extraLines != null && extraLines.Length > 0)
        {
            foreach (var ln in extraLines) if (!string.IsNullOrEmpty(ln)) nonEmptyExtra.Add(ln);
            if (nonEmptyExtra.Count > 0)
            {
                if (!string.IsNullOrEmpty(combinedBody)) combinedBody += "\n\n";
                combinedBody += string.Join("\n", nonEmptyExtra);
            }
        }

        // ------------------ 元素检测与颜色应用（保证每次检测到就上色） ------------------
        int detectedElement = -1;

        if (nonEmptyExtra.Count > 0)
        {
            string bondTextCombined = string.Join("\n", nonEmptyExtra);
            detectedElement = DetermineElementFromText(bondTextCombined);
            if (debugLogCombined) Debug.Log($"[Tooltip] DetectedElementFromExtraLines = {detectedElement} for target={targetRect.name}");
        }
        else
        {
            bool isBondZone = false;
            if (targetRect != null)
            {
                if (targetRect.GetComponentInParent<BondZoneMarker>() != null) isBondZone = true;
                else
                {
                    string tname = targetRect.name ?? "";
                    foreach (var kw in bondZoneNameKeywords)
                    {
                        if (!string.IsNullOrEmpty(kw) && tname.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isBondZone = true;
                            break;
                        }
                    }
                }
            }

            if (isBondZone)
            {
                int elFromVisual = DetermineElementFromTargetRect(targetRect);
                if (elFromVisual >= 0)
                {
                    detectedElement = elFromVisual;
                    if (debugLogCombined) Debug.Log($"[Tooltip] DetectedElementFromTargetVisual = {detectedElement} for target={targetRect.name}");
                }
                else
                {
                    if (!string.IsNullOrEmpty(body))
                    {
                        detectedElement = DetermineElementFromText(body);
                        if (debugLogCombined) Debug.Log($"[Tooltip] DetectedElementFromBodyBecauseBondZone = {detectedElement} for target={targetRect.name}");
                    }
                    else detectedElement = -1;
                }
            }
            else
            {
                if (!requireExtraLinesForDetection)
                {
                    string bondCandidate = ExtractLikelyBondLinesFromBody(body);
                    if (!string.IsNullOrEmpty(bondCandidate))
                    {
                        detectedElement = DetermineElementFromText(bondCandidate);
                        if (debugLogCombined) Debug.Log($"[Tooltip] DetectedElementFromBodyCandidate = {detectedElement} for target={targetRect.name}; candidate='{Truncate(bondCandidate, 120)}'");
                    }
                    else
                    {
                        detectedElement = -1;
                        if (debugLogCombined) Debug.Log($"[Tooltip] No bond candidate lines found in body for target={targetRect.name}");
                    }
                }
                else
                {
                    detectedElement = -1;
                    if (debugLogCombined) Debug.Log($"[Tooltip] Skipping body detection for target={targetRect.name} (requireExtraLinesForDetection)");
                }
            }
        }

        // 强制确保检测到时应用颜色（避免 currentElementIndex 与实际颜色不同步）
        if (detectedElement >= 0)
        {
            ApplyElementColor(detectedElement);
            lastDetectedElement = detectedElement;
        }
        else
        {
            RestoreBondImageColorIfNeeded();
            lastDetectedElement = -1;
        }
        // ------------------------------------------------------------------------------

        // 布局/显示逻辑（与原来相同的顺序）
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
        if (now - lastUpdateTime < minUpdateInterval)
        {
            gameObject.SetActive(true);
            lastUpdateTime = now;
            return;
        }

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

        Vector2 boxSize = new Vector2(targetW, targetH);
        float innerW = Mathf.Max(8f, boxSize.x - padding.x + extraTextBoxPadding.x);
        float innerH = Mathf.Max(8f, boxSize.y - padding.y + extraTextBoxPadding.y);

        if (respectInspectorTextBoxSize && textBoxRect != null)
        {
            Vector2 inspectorSize = new Vector2(textBoxRect.rect.width, textBoxRect.rect.height);
            innerW = Mathf.Max(8f, inspectorSize.x);
            innerH = Mathf.Max(8f, inspectorSize.y);
            boxSize = new Vector2(innerW + padding.x, innerH + padding.y);
        }

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

        Vector2 computedRootSize = new Vector2(
            Mathf.Max(8f, boxSize.x + rootSizeDelta.x),
            Mathf.Max(8f, boxSize.y + rootSizeDelta.y)
        );

        rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, computedRootSize.x);
        rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, computedRootSize.y);

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

        float offY = (verticalOffset != 0f) ? verticalOffset : aboveOffset;
        Vector2 finalScreen = RectTransformUtility.WorldToScreenPoint(cam, (targetRect.GetWorldCornersCachedTopCenter()));
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
        if (debugLogCombined) Debug.Log("[Tooltip] combinedBody: " + combinedBody);
    }

    public void Hide()
    {
        RestoreBondImageColorIfNeeded();
        lastDetectedElement = -2;
        gameObject.SetActive(false);
    }

    // 从文本中识别元素（属性优先，术类后置）
    private int DetermineElementFromText(string txt)
    {
        if (string.IsNullOrEmpty(txt)) return -1;
        string lower = txt.ToLower();

        // 优先：显式的中文属性或“水系”等短标签
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*水|水系|水系怪兽|\[水\]|\(水\)|\b水\b)")) return 2;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*火|火系|火系怪兽|\[火\]|\(火\)|\b火\b)")) return 3;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*木|木系|木系怪兽|\[木\]|\(木\)|\b木\b)")) return 1;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*土|土系|土系怪兽|\[土\]|\(土\)|\b土\b)")) return 0;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*金|金系|金系怪兽|\[金\]|\(金\)|\b金\b)")) return 4;

        // 英文显式
        if (Regex.IsMatch(lower, @"\b(water|water-?type|water monster|water card)\b")) return 2;
        if (Regex.IsMatch(lower, @"\b(fire|fire-?type|fire monster|fire card)\b")) return 3;
        if (Regex.IsMatch(lower, @"\b(wood|earth|earth-?type|wood monster)\b"))
        {
            if (lower.Contains("wood")) return 1;
            if (lower.Contains("earth")) return 0;
        }

        // 显式编号/元素索引
        for (int i = 0; i < elementColors.Length; i++)
        {
            if (lower.Contains("element" + i)) return i;
            if (lower.Contains("[e" + i) || lower.Contains("(e" + i) || lower.Contains(" e" + i) || lower.Contains("{e" + i)) return i;
        }

        // 兜底：术/咒/法/skill/spell（最后匹配，避免覆盖显式属性）
        if (lower.Contains("咒") || lower.Contains("术") || lower.Contains("法") || lower.Contains("spell") || lower.Contains("curse") || lower.Contains("skill")) return 5;

        // 最后保底英文词
        if (lower.Contains("earth")) return 0;
        if (lower.Contains("wood")) return 1;
        if (lower.Contains("water")) return 2;
        if (lower.Contains("fire")) return 3;
        if (lower.Contains("metal") || lower.Contains("gold")) return 4;

        return -1;
    }

    // 从 targetRect 周边视觉元素尝试识别元素（优先短文本/属性图标名）
    private int DetermineElementFromTargetRect(RectTransform targetRect)
    {
        if (targetRect == null) return -1;

        var parentRects = targetRect.GetComponentsInParent<RectTransform>(true);

        // 1) 优先短文本（单字或短标签）
        foreach (var rt in parentRects)
        {
            var texts = rt.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string txt = (t.text ?? "").Trim();
                if (string.IsNullOrEmpty(txt)) continue;
                if (txt.Length <= 3)
                {
                    int el = DetermineElementFromText(txt);
                    if (el >= 0) return el;
                }
            }
        }

        // 2) 再看 sprite 名
        foreach (var rt in parentRects)
        {
            var imgs = rt.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var im in imgs)
            {
                if (im == null || im.sprite == null || string.IsNullOrEmpty(im.sprite.name)) continue;
                string sn = im.sprite.name.ToLower();
                if (sn.Contains("water") || sn.Contains("水")) return 2;
                if (sn.Contains("fire") || sn.Contains("火")) return 3;
                if (sn.Contains("wood") || sn.Contains("木")) return 1;
                if (sn.Contains("earth") || sn.Contains("土")) return 0;
                if (sn.Contains("metal") || sn.Contains("gold") || sn.Contains("金")) return 4;
                if (sn.Contains("spell") || sn.Contains("curse") || sn.Contains("skill") || sn.Contains("术") || sn.Contains("咒")) return 5;
            }
        }

        // 3) 回退到长文本（尽量避免）
        foreach (var rt in parentRects)
        {
            var texts = rt.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string txt = (t.text ?? "").Trim();
                if (string.IsNullOrEmpty(txt)) continue;
                int el = DetermineElementFromText(txt);
                if (el >= 0) return el;
            }
        }

        return -1;
    }

    // 应用颜色并记录原色以便恢复
    private void ApplyElementColor(int elementIndex)
    {
        if (bondTextImage == null) return;
        if (elementIndex < 0 || elementIndex >= elementColors.Length) return;

        // 仅在当前未记录过原色（首次）时记录
        if (currentElementIndex < 0)
        {
            originalBondImageColor = bondTextImage.color;
        }

        bondTextImage.color = elementColors[elementIndex];
        currentElementIndex = elementIndex;

        if (debugLogCombined) Debug.Log($"[Tooltip] Applied element color index={elementIndex}");
    }

    private void RestoreBondImageColorIfNeeded()
    {
        if (bondTextImage != null && currentElementIndex >= 0)
        {
            bondTextImage.color = originalBondImageColor;
            currentElementIndex = -1;
            if (debugLogCombined) Debug.Log("[Tooltip] Restored bond image color to original");
        }
    }

    // 若调用方未把羁绊单独传入 extraLines，则尝试从 body 中提取明显是羁绊的行（仅当允许回退时会调用）
    private string ExtractLikelyBondLinesFromBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        var lines = body.Split('\n');
        List<string> candidates = new List<string>();

        Regex prefixRegex = new Regex(@"^\s*([\u2460-\u2473]|\d+[\.\)：:，、]|[\(\[\{]?\d+[\)\]\}]|[①-⑳])\s*");

        foreach (var raw in lines)
        {
            string ln = raw.Trim();
            if (string.IsNullOrEmpty(ln)) continue;

            if (prefixRegex.IsMatch(ln))
            {
                candidates.Add(ln);
                continue;
            }
        }

        if (candidates.Count == 0) return null;
        return string.Join("\n", candidates);
    }

    private string Truncate(string s, int max)
    {
        if (s == null) return "";
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
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
        var graphics = parent.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
        foreach (var g in graphics)
        {
            try { g.raycastTarget = enable; } catch { }
        }
    }
}

public static class RectTransformExtensions
{
    public static Vector3 GetWorldCornersCachedTopCenter(this RectTransform rt)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners); // 0=bl,1=tl,2=tr,3=br
        return (corners[1] + corners[2]) * 0.5f;
    }
}

// ----------------------
// 极小标记组件：把它挂到表示羁绊文本区域的 RectTransform 上（bondZone）
// ----------------------
public class BondZoneMarker : MonoBehaviour { }
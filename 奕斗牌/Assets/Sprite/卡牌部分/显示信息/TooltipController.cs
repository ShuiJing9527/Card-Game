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

    [Header("稳定性/性能参数")]
    public float positionEpsilon = 3f;
    public float sizeEpsilon = 2f;
    public float minUpdateInterval = 0.05f;
    public bool ignoreRaycasts = true;

    [Header("调试")]
    public bool debugLogCombined = false;

    [Header("额外文本框")]
    [Tooltip("默认 true：只在 extraLines 非空或 targetRect 被标记为 BondZone 时才进行羁绊检测；关闭后会回退到从 body 提取候选行检测（更容易误判）。")]
    public bool requireExtraLinesForDetection = true;

    [Tooltip("备用：如果无法挂组件，可在目标 RectTransform 名称中包含这些关键字被识别为羁绊区域（不如组件稳妥）。")]
    public string[] bondZoneNameKeywords = new string[] { "bond", "link", "羁绊", "Link" };

    Canvas parentCanvas;
    RectTransform canvasRect;
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

        parentCanvas = GetComponentInParent<Canvas>();
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

        // 不再由脚本设置 Canvas.overrideSorting/sortingOrder，
        // 请通过 Inspector 为 tooltip 或其父 overlay Canvas 设置排序（若需要）。

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
        ShowAbove(targetRect, title, body, new string[] { bondText }, 0f, true);
    }

    // 兼容旧接口的重载
    public void ShowAbove(RectTransform targetRect, string body, float verticalOffset = 0f)
    {
        ShowAbove(targetRect, null, body, (string[])null, 0f, true);
    }
    public void ShowAbove(RectTransform targetRect, string title, string body, float verticalOffset = 0f)
    {
        ShowAbove(targetRect, title, body, (string[])null, 0f, true);
    }
    public void ShowAbove(RectTransform targetRect, string title, string body, string bondText, float verticalOffset = 0f)
    {
        ShowAbove(targetRect, title, body, new string[] { bondText }, 0f, true);
    }

    // 主函数：简化版（已移除运行时修改尺寸、悬停判定、显示偏移与 LayoutRebuilder 调用）
    // 参数 verticalOffset 与 bypassHoverCheck 已不再影响位置或判定（保留以兼容调用）
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

        // 元素检测与颜色应用（保证每次检测到就上色）
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

        // 布局/显示逻辑（已精简：不修改任何 RectTransform 尺寸，也不做悬停判定）
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners); // 0=bl,1=tl,2=tr,3=br
        Vector3 topCenterWorld = (corners[1] + corners[2]) * 0.5f;
        Camera cam = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? parentCanvas.worldCamera : null;

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
        // 移除了 LayoutRebuilder.ForceRebuildLayoutImmediate(...) 的所有调用，避免触发 LayoutGroup 改变尺寸
        Canvas.ForceUpdateCanvases();

        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // 不再使用任何显示偏移，固定为 0
        Vector2 finalScreen = RectTransformUtility.WorldToScreenPoint(cam, (targetRect.GetWorldCornersCachedTopCenter()));

        if (canvasRect == null) { Debug.LogError("TooltipController.ShowAbove: canvasRect is null. 确认 Tooltip 在 Canvas 下。"); return; }

        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.SetAsLastSibling();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, finalScreen, cam, out Vector2 localPoint);

        // 使用 Inspector 当前 rootRect 的尺寸做约束
        Vector2 currentRootSize = rootRect.rect.size;
        Vector2 desiredAnchoredPos = ClampToCanvas(localPoint, currentRootSize);

        float canvasTop = canvasRect.rect.height * 0.5f;
        float topLimitForPivotBottom = canvasTop - currentRootSize.y * (1f - rootRect.pivot.y) - 5f;
        if (desiredAnchoredPos.y >= topLimitForPivotBottom - 1f)
        {
            rootRect.pivot = new Vector2(0.5f, 1f);
            Vector2 finalScreenBelow = RectTransformUtility.WorldToScreenPoint(cam, (targetRect.GetWorldCornersCachedTopCenter()));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, finalScreenBelow, cam, out Vector2 localBelow);
            desiredAnchoredPos = ClampToCanvas(localBelow, currentRootSize);
        }

        bool sameTarget = (lastTarget == targetRect);
        bool sameContent = (lastTitle == (title ?? "") && lastCombinedBody == combinedBody);
        bool smallPosDiff = (lastAnchoredPos != Vector2.positiveInfinity) && (Vector2.Distance(lastAnchoredPos, desiredAnchoredPos) <= positionEpsilon);
        bool smallSizeDiff = (lastSize != Vector2.zero) && (Vector2.Distance(lastSize, currentRootSize) <= sizeEpsilon);

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
        lastSize = currentRootSize; // 记录当前 Inspector 下的 root 大小
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

        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*水|水系|水系怪兽|$$水$$|$水$|\b水\b)")) return 2;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*火|火系|火系怪兽|$$火$$|$火$|\b火\b)")) return 3;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*木|木系|木系怪兽|$$木$$|$木$|\b木\b)")) return 1;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*土|土系|土系怪兽|$$土$$|$土$|\b土\b)")) return 0;
        if (Regex.IsMatch(lower, @"\b(属性[:：]\s*金|金系|金系怪兽|$$金$$|$金$|\b金\b)")) return 4;

        if (Regex.IsMatch(lower, @"\b(water|water-?type|water monster|water card)\b")) return 2;
        if (Regex.IsMatch(lower, @"\b(fire|fire-?type|fire monster|fire card)\b")) return 3;
        if (Regex.IsMatch(lower, @"\b(wood|earth|earth-?type|wood monster)\b"))
        {
            if (lower.Contains("wood")) return 1;
            if (lower.Contains("earth")) return 0;
        }

        for (int i = 0; i < elementColors.Length; i++)
        {
            if (lower.Contains("element" + i)) return i;
            if (lower.Contains("[e" + i) || lower.Contains("(e" + i) || lower.Contains(" e" + i) || lower.Contains("{e" + i)) return i;
        }

        if (lower.Contains("咒") || lower.Contains("术") || lower.Contains("法") || lower.Contains("spell") || lower.Contains("curse") || lower.Contains("skill")) return 5;

        if (lower.Contains("earth")) return 0;
        if (lower.Contains("wood")) return 1;
        if (lower.Contains("water")) return 2;
        if (lower.Contains("fire")) return 3;
        if (lower.Contains("metal") || lower.Contains("gold")) return 4;

        return -1;
    }

    private int DetermineElementFromTargetRect(RectTransform targetRect)
    {
        if (targetRect == null) return -1;

        var parentRects = targetRect.GetComponentsInParent<RectTransform>(true);

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

    private void ApplyElementColor(int elementIndex)
    {
        if (bondTextImage == null) return;
        if (elementIndex < 0 || elementIndex >= elementColors.Length) return;

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

    private string ExtractLikelyBondLinesFromBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        var lines = body.Split('\n');
        List<string> candidates = new List<string>();

        Regex prefixRegex = new Regex(@"^\s*([\u2460-\u2473]|\d+[\.\)：:，、]|[$$$\{]?\d+[$$$\}]|[①-⑳])\s*");

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

public class BondZoneMarker : MonoBehaviour { }
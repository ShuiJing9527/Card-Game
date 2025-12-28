using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MonsterCardDisplay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("卡片类型（Inspector 可预设显示文本）")]
    public MonsterCardType monsterType = MonsterCardType.Effect;

    [Header("UI文本组件绑定")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI attackText;
    public TextMeshProUGUI mainLabelText;        // 显示“【效果】”或“【判定】”
    public TextMeshProUGUI mainDescriptionText;  // 效果/判定共用文本框（Tooltip 目标）
    public TextMeshProUGUI linkNameText;         // 仅修改羁绊名
    public TextMeshProUGUI linkDescriptionText;  // 羁绊效果
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI attributesText;
    public TextMeshProUGUI stackCountText;

    [Header("文本溢出缩放设置")]
    public bool useAutoSizing = false;   // 若启用 TMP 自动缩放则使用 TMP 的 AutoSizing
    public int linkShrinkThreshold = 40;   // linkDescription 超过多少字符开始缩小（仅用于决定步长）
    public int mainShrinkThreshold = 80;   // mainDescription 超过多少字符开始缩小（仅用于决定步长）
    public float linkMinFontSize = 6f;    // link 最小字号
    public float mainMinFontSize = 6f;    // main 最小字号

    [Header("缩放策略参数 (可在 Inspector 调整)")]
    public int originalMaxLinesDefault = 2;  // 默认字号下最多行数
    public int extraAllowedLinesAfterShrink = 2; // 缩小后最多额外允许多少行（default 2 -> 最多 4 行）
    public float shrinkStepNormal = 1f;   // 常规模拟缩小步长（pt）
    public float shrinkStepFast = 2f;     // 当文本长度超阈但增量 <=10 时使用更快步长

    [Header("Tooltip 设置")]
    public float tooltipAboveOffset = 8f; // tooltip 显示在目标上方时的垂直偏移（像素）
    public bool tooltipPreferMainDescription = true; // 鼠标悬停卡片时优先显示主描述，否则显示羁绊描述

    // 记录原字号以便还原
    private float originalLinkFontSize = -1f;
    private float originalMainFontSize = -1f;

    // 保存完整文本以供 Tooltip 使用
    private string fullMainText = "";
    private string fullLinkText = "";

    void Awake()
    {
        if (linkDescriptionText != null)
            originalLinkFontSize = linkDescriptionText.fontSize;
        if (mainDescriptionText != null)
            originalMainFontSize = mainDescriptionText.fontSize;
    }

    // 外部调用：把 MonsterCard 传进来并刷新 UI
    public void SetCard(MonsterCard m)
    {
        if (m == null) return;

        // 保存完整文本（用于 tooltip）
        fullMainText = m.Card_Description ?? "";
        fullLinkText = string.IsNullOrWhiteSpace(m.Card_LinkEffect) ? "" : m.Card_LinkEffect;

        // 名称
        if (cardNameText != null)
            cardNameText.text = m.Card_Name ?? "";

        // 攻击
        if (attackText != null)
            attackText.text = m.Card_Atk.ToString();

        // 等级
        if (levelText != null)
            levelText.text = $"Lv.{m.Card_Lv}";

        // 属性
        if (attributesText != null)
            attributesText.text = string.IsNullOrEmpty(m.Card_Attributes) ? "" : m.Card_Attributes;

        // 主标签（带中括号）
        var enumType = m.MonsterType;
        string mainTypeText = EnumTypeToDisplay(enumType);
        if (mainLabelText != null)
            mainLabelText.text = $"【{mainTypeText}】";

        // 主描述（效果/判定） - 使用自适应函数处理溢出（会尝试缩小字号以完整显示）
        AdjustTextSizeAndSet(mainDescriptionText, fullMainText, mainShrinkThreshold, mainMinFontSize, ref originalMainFontSize);

        // 羁绊效果文本（保持现有逻辑，但加上缩放处理）
        AdjustTextSizeAndSet(linkDescriptionText, fullLinkText, linkShrinkThreshold, linkMinFontSize, ref originalLinkFontSize);

        // 仅更新羁绊名节点（linkNameText），并且仅在 Card_Link 有值时覆盖 prefab 文本
        if (linkNameText != null)
        {
            var rawLink = m.Card_Link ?? "";

            if (string.IsNullOrWhiteSpace(rawLink))
            {
                // 不覆盖 prefab 内已有的占位文本
            }
            else
            {
                var parsedTypes = ParseTypes(rawLink);
                var linkName = ExtractNameFromLink(rawLink, parsedTypes);

                string finalLabel;
                if (parsedTypes.Count > 0)
                {
                    string typeDisplay = string.Join("/", parsedTypes);
                    finalLabel = string.IsNullOrEmpty(linkName) ? $"【{typeDisplay}】" : $"【{typeDisplay}】{linkName}";
                }
                else
                {
                    finalLabel = !string.IsNullOrWhiteSpace(linkName) ? linkName : rawLink;
                }

                linkNameText.text = finalLabel ?? "";
            }
        }

        // 叠放数量
        if (stackCountText != null)
            stackCountText.text = $"x{m.StackCount}";

        var cardLv = GetComponentInChildren<CardLv>(true); // 包含 inactive
        if (cardLv != null)
        {
            cardLv.SetLevel(m.Card_Lv);
        }
        else
        {
            Debug.LogWarning($"MonsterCardDisplay: 未找到 CardLv 组件，无法显示等级动画 (id={m.Card_ID})");
        }

        var chd = GetComponent<CardHoverDetector>();
        if (chd != null)
        {
            chd.titleText = m.Card_Name ?? "";
            chd.effectText = fullMainText;
            chd.bondText = fullLinkText;
        }
    }

    // ---------- 自适应文本逻辑 ----------
    // 手动逐步缩小、根据字号决定允许行数，并在最小字号仍超出时显示省略号
    private void AdjustTextSizeAndSet(TextMeshProUGUI t, string text, int threshold, float minFontSize, ref float originalFontSize)
    {
        if (t == null) return;

        // 记录原始字号
        if (originalFontSize <= 0f) originalFontSize = t.fontSize;

        // 如果启用 TMP AutoSizing，交给 TMP 处理
        if (useAutoSizing)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = originalFontSize;
            t.fontSizeMin = Mathf.Max(6f, minFontSize);
            t.enableWordWrapping = true;
            t.richText = true;
            t.text = text ?? "";
            Canvas.ForceUpdateCanvases();
            t.ForceMeshUpdate();
            t.maxVisibleLines = originalMaxLinesDefault;
            return;
        }

        // 使用手动缩放（以字体缩小为主）
        t.enableAutoSizing = false;
        t.enableWordWrapping = true;
        t.richText = true;
        t.overflowMode = TextOverflowModes.Overflow; // 先允许 overflow 以便测量
        t.text = text ?? "";

        Canvas.ForceUpdateCanvases();
        t.ForceMeshUpdate();

        float rectW = t.rectTransform.rect.width;
        float rectH = t.rectTransform.rect.height;

        // 初始化字体与行数
        t.fontSize = originalFontSize;
        t.maxVisibleLines = originalMaxLinesDefault;

        // 测量所需高度（无限高度）
        Vector2 pref = t.GetPreferredValues(t.text, rectW, 9999f);

        // 若一开始就 fit，则设置为原始样式并返回
        if (pref.y <= rectH)
        {
            t.fontSize = originalFontSize;
            t.maxVisibleLines = originalMaxLinesDefault;
            t.overflowMode = TextOverflowModes.Truncate;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(t.rectTransform);
            return;
        }

        // 计算“超出字符数”的近似（仅用于决定缩小步长）
        int extraChars = Math.Max(0, (t.text?.Length ?? 0) - threshold);

        // 逐步缩小字体直到 fit 或达到最小字号
        int safety = 0;
        while (true)
        {
            pref = t.GetPreferredValues(t.text, rectW, 9999f);
            if (pref.y <= rectH) break;
            if (t.fontSize <= minFontSize) break;
            if (safety++ > 400) break;

            float step = (extraChars > 0 && extraChars <= 10) ? shrinkStepFast : shrinkStepNormal;
            float newSize = Mathf.Max(minFontSize, t.fontSize - step);
            t.fontSize = newSize;

            // 根据当前字号决定允许的最大行数（如果需要）
            if (t.fontSize >= originalFontSize)
            {
                t.maxVisibleLines = originalMaxLinesDefault;
            }
            else
            {
                float drop = originalFontSize - t.fontSize;
                if (drop <= 2f) t.maxVisibleLines = originalMaxLinesDefault + 1;
                else t.maxVisibleLines = originalMaxLinesDefault + extraAllowedLinesAfterShrink;
            }

            t.ForceMeshUpdate();
        }

        // 最终测量
        pref = t.GetPreferredValues(t.text, rectW, 9999f);
        if (pref.y > rectH)
        {
            // 若仍然超出，使用省略号并保持当前字号/行数
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
        else
        {
            t.overflowMode = TextOverflowModes.Truncate;
        }

        // 强制刷新布局
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(t.rectTransform);
    }

    // ---------- 鼠标悬停 (Tooltip) ----------
    // 当鼠标进入卡片区域时显示 tooltip（在目标文本上方）
    public void OnPointerEnter(PointerEventData eventData)
    {
        // 优先显示主描述（如果为空则尝试羁绊效果）
        string toShow = null;
        RectTransform targetRect = null;

        if (tooltipPreferMainDescription)
        {
            if (!string.IsNullOrEmpty(fullMainText) && mainDescriptionText != null)
            {
                toShow = fullMainText;
                targetRect = mainDescriptionText.rectTransform;
            }
            else if (!string.IsNullOrEmpty(fullLinkText) && linkDescriptionText != null)
            {
                toShow = fullLinkText;
                targetRect = linkDescriptionText.rectTransform;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(fullLinkText) && linkDescriptionText != null)
            {
                toShow = fullLinkText;
                targetRect = linkDescriptionText.rectTransform;
            }
            else if (!string.IsNullOrEmpty(fullMainText) && mainDescriptionText != null)
            {
                toShow = fullMainText;
                targetRect = mainDescriptionText.rectTransform;
            }
        }

        if (!string.IsNullOrEmpty(toShow) && TooltipController.Instance != null && targetRect != null)
        {
            TooltipController.Instance.ShowAbove(targetRect, toShow, tooltipAboveOffset);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipController.Instance != null)
            TooltipController.Instance.Hide();
    }

    // ---------- 其余辅助函数（沿用并保留） ----------
    private string EnumTypeToDisplay(MonsterCardType t)
    {
        if (t == MonsterCardType.Effect) return "效果";
        if (t == MonsterCardType.Judge) return "判定";
        return t.ToString();
    }

    private List<string> ParseTypes(string raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        string s = TrimOuterBrackets(raw);

        var parts = s.Split(new[] { '/', '\\', '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();

        if (parts.Count == 1 && parts[0].Contains("或"))
            parts = parts[0].Split(new[] { '或' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();

        foreach (var p in parts)
        {
            if (p.Contains("效果")) AddOnce(result, "效果");
            else if (p.Contains("判定")) AddOnce(result, "判定");
            else if (p.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0) AddOnce(result, "Effect");
            else if (p.IndexOf("Judge", StringComparison.OrdinalIgnoreCase) >= 0) AddOnce(result, "Judge");
        }

        var ordered = new List<string>();
        if (result.Contains("效果")) ordered.Add("效果");
        if (result.Contains("判定")) ordered.Add("判定");
        if (result.Contains("Effect") && !ordered.Contains("Effect")) ordered.Add("Effect");
        if (result.Contains("Judge") && !ordered.Contains("Judge")) ordered.Add("Judge");
        return ordered;
    }

    private string ExtractNameFromLink(string raw, List<string> parsedTypes)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string s = TrimOuterBrackets(raw);

        foreach (var t in parsedTypes)
        {
            if (t == "效果" || t == "判定")
                s = s.Replace("效果", "").Replace("判定", "");
            else
                s = RemoveIgnoreCase(s, t);
        }

        s = RemoveIgnoreCase(s, "Effect");
        s = RemoveIgnoreCase(s, "Judge");

        s = s.Replace("/", " ").Replace("|", " ").Replace("\\", " ").Replace(",", " ").Replace(";", " ").Replace("：", " ").Replace(":", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    private string TrimOuterBrackets(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Trim();
        if (s.StartsWith("【") && s.EndsWith("】")) return s.Substring(1, s.Length - 2).Trim();
        if (s.StartsWith("[") && s.EndsWith("]")) return s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    private string RemoveIgnoreCase(string source, string toRemove)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(toRemove)) return source;
        int idx;
        string lower = source.ToLowerInvariant();
        string lowerRem = toRemove.ToLowerInvariant();
        while ((idx = lower.IndexOf(lowerRem, StringComparison.Ordinal)) >= 0)
        {
            source = source.Remove(idx, toRemove.Length);
            lower = source.ToLowerInvariant();
        }
        return source;
    }

    private void AddOnce(List<string> list, string v)
    {
        if (!list.Contains(v)) list.Add(v);
    }
}
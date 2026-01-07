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

    // 保存完整文本以供 Tooltip 使用
    private string fullMainText = "";
    private string fullLinkText = "";

    void Awake()
    {
        // 初始时根据 inspector 的 monsterType 更新显示（便于编辑器中可视化）
        UpdateTypeUI();
    }

    // 外部调用：把 MonsterCard 传进来并刷新 UI
    public void SetCard(MonsterCard m)
    {
        if (m == null) return;

        this.monsterType = m.MonsterType;
        UpdateTypeUI();

        fullMainText = m.Card_Description ?? "";
        fullLinkText = string.IsNullOrWhiteSpace(m.Card_LinkEffect) ? "" : m.Card_LinkEffect;

        if (cardNameText != null)
            cardNameText.text = m.Card_Name ?? "";

        if (attackText != null)
            attackText.text = m.Card_Atk.ToString();

        if (levelText != null)
            levelText.text = $"Lv.{m.Card_Lv}";

        if (attributesText != null)
            attributesText.text = string.IsNullOrEmpty(m.Card_Attributes) ? "" : m.Card_Attributes;

        // 直接赋值完整文本
        if (mainDescriptionText != null)
            mainDescriptionText.text = fullMainText;
        if (linkDescriptionText != null)
            linkDescriptionText.text = fullLinkText;

        if (linkNameText != null)
        {
            var rawLink = m.Card_Link ?? "";
            if (!string.IsNullOrWhiteSpace(rawLink))
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

        if (stackCountText != null)
            stackCountText.text = $"x{m.StackCount}";

        var cardLv = GetComponentInChildren<CardLv>(true);
        if (cardLv != null)
        {
            cardLv.SetLevel(m.Card_Lv);
        }
        else
        {
            Debug.LogWarning($"MonsterCardDisplay: 未找到 CardLv 组件，无法显示等级动画 (id={m.Card_ID})");
        }

        var chd = GetComponent<CardsTooltip>();
        if (chd != null)
        {
            chd.titleText = m.Card_Name ?? "";
            chd.effectText = fullMainText;
            chd.bondText = fullLinkText;
        }
    }

    public void UpdateTypeUI()
    {
        if (mainLabelText != null)
        {
            string mainTypeText = EnumTypeToDisplay(this.monsterType);
            mainLabelText.text = $"【{mainTypeText}】";
        }
    }

    // 鼠标悬停: 优先主描述，否则羁绊文本；直接调用 TooltipController 显示
    public void OnPointerEnter(PointerEventData eventData)
    {
        string toShow = null;
        RectTransform targetRect = null;

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

        if (!string.IsNullOrEmpty(toShow) && TooltipController.Instance != null && targetRect != null)
        {
            // 直接调用 TooltipController 显示（无额外 Inspector 配置）
            TooltipController.Instance.ShowAbove(targetRect, toShow);
            // 如果你的 TooltipController 需要偏移参数，请改为：
            // TooltipController.Instance.ShowAbove(targetRect, toShow, 8f);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipController.Instance != null)
            TooltipController.Instance.Hide();
    }

    // 以下为原有辅助方法（ParseTypes / ExtractNameFromLink 等）
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
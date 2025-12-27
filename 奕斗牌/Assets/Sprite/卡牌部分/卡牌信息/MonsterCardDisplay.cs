using UnityEngine;
using TMPro;

public class MonsterCardDisplay : MonoBehaviour
{
    [Header("卡片类型（Inspector 可预设显示文本）")]
    public MonsterCardType monsterType = MonsterCardType.Effect;

    [Header("UI文本组件绑定")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI attackText;
    public TextMeshProUGUI mainLabelText;        // 显示“效果”或“判定”
    public TextMeshProUGUI mainDescriptionText;  // 效果/判定共用文本框
    public TextMeshProUGUI linkLabelText;
    public TextMeshProUGUI linkDescriptionText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI attributesText;
    public TextMeshProUGUI stackCountText;

    // 外部调用：把 MonsterCard 传进来并刷新 UI
    public void SetCard(MonsterCard m)
    {
        if (m == null) return;

        // 名称
        if (cardNameText != null)
            cardNameText.text = m.Card_Name;

        // 攻击
        if (attackText != null)
            attackText.text = m.Card_Atk.ToString();

        // 等级
        if (levelText != null)
            levelText.text = $"Lv.{m.Card_Lv}";

        // 属性
        if (attributesText != null)
            attributesText.text = string.IsNullOrEmpty(m.Card_Attributes) ? "" : m.Card_Attributes;

        // 主标签（优先使用数据里的 MonsterType，否则使用 Inspector 预设）
        var typeToShow = m.MonsterType;
        if (mainLabelText != null)
            mainLabelText.text = typeToShow == MonsterCardType.Effect ? "效果" : "判定";

        // 主描述（Card_Description 用作效果/判定描述）
        if (mainDescriptionText != null)
            mainDescriptionText.text = m.Card_Description ?? "";

        // 羁绊标签与内容
        if (linkLabelText != null)
            linkLabelText.text = string.IsNullOrEmpty(m.Card_Link) ? "" : "羁绊";
        if (linkDescriptionText != null)
            linkDescriptionText.text = string.IsNullOrEmpty(m.Card_LinkEffect) ? (m.Card_Link ?? "") : m.Card_LinkEffect;

        // 叠放数量
        if (stackCountText != null)
            stackCountText.text = $"x{m.StackCount}";
    }
}
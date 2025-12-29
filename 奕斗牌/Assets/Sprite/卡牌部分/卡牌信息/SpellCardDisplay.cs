using TMPro;
using UnityEngine;

public class SpellCardDisplay : MonoBehaviour
{
    [Header("UI文本组件")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI magicDescriptionText;   // 法术描述（白色区域，卡面显示）
    public TextMeshProUGUI stackDescriptionText;   // 叠放描述（紫色区域，卡面显示）
    public TextMeshProUGUI usageLabelText;         // 使用方式标签（咒术/叠放）

    // 双参数重载：接收 SpellCard 和外部传入的叠放描述（优先显示）
    public void SetCard(SpellCard s, string stackDescription)
    {
        if (s == null) return;

        Debug.Log($"[SpellCardDisplay] SetCard called id={s.Card_ID} name='{s.Card_Name}' paramStackDesc(len={(stackDescription?.Length ?? 0)})='{stackDescription}'");

        if (cardNameText != null)
            cardNameText.text = s.Card_Name ?? "";

        if (magicDescriptionText != null)
            magicDescriptionText.text = s.Card_Description ?? "";  // 卡面白色区域（法术描述）

        // 优先使用外部传入的 stackDescription（CSV 中的短叠放描述），否则显示叠放数量
        string displayStack = !string.IsNullOrEmpty(stackDescription) ? stackDescription : $"叠放数: {s.StackCount}";
        if (stackDescriptionText != null)
            stackDescriptionText.text = displayStack;  // 卡面紫色区域（叠放描述）

        if (usageLabelText != null)
        {
            string usage = "";
            if (s.CanUseAsMagic) usage += "咒术";
            if (s.CanUseAsStack) usage += (usage.Length > 0 ? " / " : "") + "叠放";
            usageLabelText.text = usage;
        }

        // 关键修复：向 Tooltip 传递时，交换 effectText 和 bondText
        var hover = GetComponent<CardHoverDetector>();
        if (hover != null)
        {
            hover.titleText = s.Card_Name ?? "";  // 标题（不变）
            // 原错误：hover.effectText = s.Card_Description ?? "";
            // 原错误：hover.bondText = displayStack;
            hover.effectText = displayStack;  // 叠放描述 → 传给 Tooltip 的 bond 区域（紫色）
            hover.bondText = s.Card_Description ?? "";  // 法术描述 → 传给 Tooltip 的 effect 区域（白色）
        }

        Debug.Log($"[SpellCardDisplay] Display stack text: '{displayStack}'");
    }

    // 兼容旧调用（仅传入SpellCard，自动使用默认叠放数量文本）
    public void SetCard(SpellCard s)
    {
        SetCard(s, null);
    }
}
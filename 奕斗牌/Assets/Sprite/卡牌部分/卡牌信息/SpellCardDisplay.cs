using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class SpellCardDisplay : MonoBehaviour
{
    [Header("UI文本组件")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI magicDescriptionText;   // 法术描述
    public TextMeshProUGUI stackDescriptionText;   // 叠放数显示
    public TextMeshProUGUI usageLabelText;         // 可选：显示使用方式（咒术/叠放）

    // 外部调用：把 SpellCard 传进来并刷新 UI
    public void SetCard(SpellCard s)
    {
        if (s == null) return;

        if (cardNameText != null)
            cardNameText.text = s.Card_Name;

        if (magicDescriptionText != null)
            magicDescriptionText.text = s.Card_Description ?? "";

        if (stackDescriptionText != null)
            stackDescriptionText.text = $"叠放数: {s.StackCount}";

        if (usageLabelText != null)
        {
            string usage = "";
            if (s.CanUseAsMagic) usage += "咒术";
            if (s.CanUseAsStack) usage += (usage.Length > 0 ? " / " : "") + "叠放";
            usageLabelText.text = usage;
        }
    }
}
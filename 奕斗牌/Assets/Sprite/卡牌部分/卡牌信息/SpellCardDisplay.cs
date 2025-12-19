using TMPro;
using UnityEngine;

public class SpellCardDisplay : MonoBehaviour
{
    [Header("基础信息")]
    public int cardID;                  // 卡牌ID
    public string cardName;             // 卡牌名字

    [Header("UI文本组件")]
    public TextMeshProUGUI cardNameText;

    [Header("法术卡特有字段")]
    public TextMeshProUGUI magicDescriptionText;  // 替代descriptionText
    public TextMeshProUGUI stackDescriptionText;  // 替代stackCountText

    private void Start()
    {
        UpdateCardUI();
    }

    public void UpdateCardUI()
    {
        if (cardNameText != null)
            cardNameText.text = cardName;
        if (magicDescriptionText != null)
            magicDescriptionText.text = magicDescriptionText.text;  // 保持原样
        if (stackDescriptionText != null)
            stackDescriptionText.text = $"叠加数: {stackDescriptionText.text}";
    }
}
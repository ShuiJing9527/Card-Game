using UnityEngine;
using TMPro;

public class MonsterCardDisplay : MonoBehaviour
{
    public enum MonsterCardType
    {
        Effect,
        Judge
    }

    [Header("卡片类型")]
    public MonsterCardType monsterType = MonsterCardType.Effect;

    [Header("UI文本组件绑定")]
    public TextMeshProUGUI cardNameText;
    public TextMeshProUGUI attackText;
    public TextMeshProUGUI mainLabelText;        // 显示“效果”或“判定”
    public TextMeshProUGUI mainDescriptionText;  // 效果/判定共用文本框
    public TextMeshProUGUI linkLabelText;
    public TextMeshProUGUI linkDescriptionText;
    /// <summary>
    /// 外部调用这个方法赋值，效果/判定文本共用mainDescriptionText显示
    /// </summary>
    public void UpdateCardUI(string cardName, int attack, string mainDescription, string linkDescription)
    {
        if (cardNameText != null)
            cardNameText.text = cardName;
        if (attackText != null)
            attackText.text = attack.ToString();

        if (mainLabelText != null)
            mainLabelText.text = monsterType == MonsterCardType.Effect ? "效果" : "判定";

        if (mainDescriptionText != null)
            mainDescriptionText.text = mainDescription;

        if (linkLabelText != null)
            linkLabelText.text = "羁绊";

        if (linkDescriptionText != null)
            linkDescriptionText.text = linkDescription;
    }
}
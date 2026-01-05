using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 运行时控制卡片上的叠放数量显示（与 CSV 无关）。
/// - 优先使用 numericRoot（如果在 Inspector 指定）来显示/隐藏数字区域；
///   numericRoot 下可放一个 TextMeshProUGUI 或 UnityEngine.UI.Text 显示具体数字。
/// - 如果没有 numericRoot，则仅在已有的 TMP/Text 上设置文本，但不会隐藏整个 stackRoot（避免把描述文本也隐藏）。
/// - count <= 0 时不显示数字文本；count > 0 时显示数字文本（格式可修改）。
/// </summary>
public class CardStackController : MonoBehaviour
{
    [Tooltip("包含叠放显示的根对象（通常包含描述文本与数字区域）")]
    public GameObject stackRoot;

    [Tooltip("如果你把数字显示单独放到一个子对象里，请在这里指定该对象（数字区域将会被显示/隐藏）。")]
    public GameObject numericRoot;

    [Tooltip("TextMeshPro 文本（用于显示数字，可选）")]
    public TextMeshProUGUI stackCountTMP;

    [Tooltip("Unity UI Text 文本（用于显示数字，可选）")]
    public Text stackCountText;

    int _currentCount = 0;

    void Reset()
    {
        // 尝试自动查找 stackRoot（兼容多语言命名）
        if (stackRoot == null)
        {
            var t = transform.Find("叠放") ?? transform.Find("Stack") ?? transform.Find("stack") ?? transform.Find("StackRoot");
            if (t != null) stackRoot = t.gameObject;
        }

        // 如果 numericRoot 未指定，尝试在 stackRoot 中查找常见的数字子对象名称
        if (numericRoot == null && stackRoot != null)
        {
            var r = stackRoot.transform.Find("Number") ??
                    stackRoot.transform.Find("Count") ??
                    stackRoot.transform.Find("stackCountRoot") ??
                    stackRoot.transform.Find("StackNumber") ??
                    stackRoot.transform.Find("数字");
            if (r != null) numericRoot = r.gameObject;
        }

        // 若 numericRoot 指定则在其子对象中查找 TMP/Text
        if (numericRoot != null)
        {
            if (stackCountTMP == null)
                stackCountTMP = numericRoot.GetComponentInChildren<TextMeshProUGUI>(true);
            if (stackCountText == null)
                stackCountText = numericRoot.GetComponentInChildren<Text>(true);
        }
        else
        {
            // 否则尽量在整个对象树中找到可用的文本组件（不会用于隐藏根）
            if (stackCountTMP == null)
                stackCountTMP = GetComponentInChildren<TextMeshProUGUI>(true);
            if (stackCountText == null)
                stackCountText = GetComponentInChildren<Text>(true);
        }

        // Note: 自动查找可能会匹配到描述文本（CSV 填充的文本）。
        // 为避免覆盖描述，建议在 Prefab 中把“描述”和“数字”分到不同组件并手动绑定 numericRoot/stackCountTMP。
    }

    /// <summary>
    /// 设置叠放数量，count <= 0 时不显示数字文本（也会隐藏 numericRoot，如果存在）。
    /// </summary>
    public void SetStackCount(int count)
    {
        _currentCount = Mathf.Max(0, count);
        UpdateDisplay();
    }

    public int GetStackCount() => _currentCount;

    void UpdateDisplay()
    {
        // 如果有独立的 numericRoot，则用它来控制显示/隐藏（不会隐藏 stackRoot）
        if (numericRoot != null)
        {
            numericRoot.SetActive(_currentCount > 0);
        }

        // 更新 TMP 文本（如果存在）
        if (stackCountTMP != null)
        {
            stackCountTMP.text = _currentCount > 0 ? $"叠放{_currentCount}" : string.Empty;
        }

        // 更新 UI Text 文本（如果存在）
        if (stackCountText != null)
        {
            stackCountText.text = _currentCount > 0 ? $"叠放{_currentCount}" : string.Empty;
        }

        // 旧行为兼容提示：
        // - 之前如果你把数字和描述放在同一个文本组件上（并且依赖脚本隐藏整个 stackRoot），
        //   新脚本不会再自动隐藏 stackRoot（除非你把 numericRoot 指向 stackRoot），以免把 CSV 的 StackDescription 一并隐藏。
    }

    [ContextMenu("RefreshDisplay")]
    void ContextRefresh() => UpdateDisplay();
}
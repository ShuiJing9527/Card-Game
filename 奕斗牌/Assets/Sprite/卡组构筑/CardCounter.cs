using System;
using System.Collections.Generic;
using System.Text;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CardCounter : MonoBehaviour
{
    public enum EffectType { Effect, Judge, Other }

    [Header("Config (can be set in Inspector or via Initialize/SetInfo)")]
    public int cardId = 0;
    // 改为 cardName（用于显示卡名）
    public string cardName = "";
    public EffectType effectType = EffectType.Effect;

    [Header("UI References (TextMeshPro preferred)")]
    public TextMeshProUGUI cardNameTMP;
    public TextMeshProUGUI effectTypeTMP;
    public TextMeshProUGUI countTMP;

    // 回退到 Unity UI Text（如果项目中使用 Text）
    public Text cardNameText;
    public Text effectTypeText;
    public Text countText;

    [Header("PlayerData source (optional)")]
    public TextAsset playerDataCsv; // 仅做回退用：若 DeckManager 未注入静态缓存，CardCounter 会尝试解析

    private static Dictionary<int, int> s_playerCounts;
    private static bool s_playerCountsLoaded = false;

    // 外部覆盖数量（DeckManager 优先传入）
    private int overrideCount = -1; // >=0 则优先显示该数值

    void Awake()
    {
        Debug.Log($"[DBG CardCounter] Awake on '{gameObject.name}'");
        TryAutoBindUiFields();
    }

    void Start()
    {
        RefreshUI();
    }

    // 由 DeckManager 注入解析后的字典（只需调用一次）
    public static void SetPlayerCounts(Dictionary<int, int> counts)
    {
        if (counts == null)
        {
            s_playerCounts = new Dictionary<int, int>();
        }
        else
        {
            s_playerCounts = new Dictionary<int, int>(counts);
        }
        s_playerCountsLoaded = true;
        Debug.Log($"[CardCounter.SetPlayerCounts] injected counts for {s_playerCounts.Count} card ids");
    }

    // 保留旧的 Initialize 行为（仍可使用）
    public void Initialize(int id, string bond, EffectType et, TextAsset csvOverride = null)
    {
        cardId = id;
        cardName = bond ?? "";
        effectType = et;
        if (csvOverride != null) playerDataCsv = csvOverride;
        RefreshUI();
    }

    // 新签名：由外部直接设置 cardId/cardName/effect/数量（DeckManager 推荐使用）
    public void SetInfo(int id, string bond, EffectType et, int count = -1, TextAsset csvOverride = null)
    {
        cardId = id;
        cardName = bond ?? "";
        effectType = et;
        if (csvOverride != null) playerDataCsv = csvOverride;
        overrideCount = count;
        Debug.Log($"[CardCounter.SetInfo] id={cardId} cardName='{cardName}' effect={effectType} overrideCount={overrideCount}");
        RefreshUI();
    }

    // 只更新数量（运行时频繁更新时使用）
    // 重要：不会修改 cardName/effectType/cardId，仅写覆盖数量并刷新显示
    public void SetCount(int count)
    {
        overrideCount = count;
        Debug.Log($"[CardCounter.SetCount] id={cardId} newCount={count} (calling RefreshUI)");
        RefreshUI();
    }

    void TryAutoBindUiFields()
    {
        if (cardNameTMP != null && effectTypeTMP != null && countTMP != null) return;

        var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in tmps)
        {
            if (t == null || string.IsNullOrEmpty(t.gameObject.name)) continue;
            var nm = t.gameObject.name.ToLower();
            if (cardNameTMP == null && (nm.Contains("卡名") || nm.Contains("name") || nm.Contains("bond") || nm.Contains("羁"))) cardNameTMP = t;
            else if (effectTypeTMP == null && (nm.Contains("效") || nm.Contains("effect") || nm.Contains("判") || nm.Contains("咒"))) effectTypeTMP = t;
            else if (countTMP == null && (nm.Contains("数") || nm.Contains("count"))) countTMP = t;
        }

        if ((cardNameTMP == null || effectTypeTMP == null || countTMP == null) &&
            (cardNameText == null || effectTypeText == null || countText == null))
        {
            var texts = GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t == null || string.IsNullOrEmpty(t.gameObject.name)) continue;
                var nm = t.gameObject.name.ToLower();
                if (cardNameText == null && (nm.Contains("卡名") || nm.Contains("name") || nm.Contains("bond") || nm.Contains("羁"))) cardNameText = t;
                else if (effectTypeText == null && (nm.Contains("效") || nm.Contains("effect") || nm.Contains("判") || nm.Contains("咒"))) effectTypeText = t;
                else if (countText == null && (nm.Contains("数") || nm.Contains("count"))) countText = t;
            }
        }

        // 兜底：把子对象第 0/1/2 分配
        if (cardNameTMP == null && cardNameText == null)
        {
            var firstTmp = GetComponentInChildren<TextMeshProUGUI>(true);
            if (firstTmp != null) cardNameTMP = firstTmp;
            else
            {
                var firstText = GetComponentInChildren<Text>(true);
                if (firstText != null) cardNameText = firstText;
            }
        }
        if (effectTypeTMP == null && effectTypeText == null)
        {
            var found = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (found.Length > 1) effectTypeTMP = found[Mathf.Min(1, found.Length - 1)];
            else
            {
                var foundt = GetComponentsInChildren<Text>(true);
                if (foundt.Length > 1) effectTypeText = foundt[Mathf.Min(1, foundt.Length - 1)];
            }
        }
        if (countTMP == null && countText == null)
        {
            var found = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (found.Length > 2) countTMP = found[Mathf.Min(2, found.Length - 1)];
            else
            {
                var foundt = GetComponentsInChildren<Text>(true);
                if (foundt.Length > 2) countText = foundt[Mathf.Min(2, foundt.Length - 1)];
            }
        }

        Debug.Log($"[CardCounter.Awake] auto-bind results: cardNameTMP={(cardNameTMP != null)} effectTypeTMP={(effectTypeTMP != null)} countTMP={(countTMP != null)} cardNameText={(cardNameText != null)} effectTypeText={(effectTypeText != null)} countText={(countText != null)}");
    }

    public void RefreshUI()
    {
        // Effect -> 判定/效果， Other -> 咒术
        string typeLabel = effectType == EffectType.Judge ? "判定" : (effectType == EffectType.Effect ? "效果" : "咒术");

        Debug.Log($"[CardCounter.RefreshUI] id={cardId} cardName='{cardName}' effect={effectType} overrideCount={overrideCount} s_loaded={s_playerCountsLoaded}");

        // 显示卡名
        if (cardNameTMP != null) cardNameTMP.text = string.IsNullOrEmpty(cardName) ? "卡名" : cardName;
        if (effectTypeTMP != null) effectTypeTMP.text = $"[{typeLabel}]";
        if (cardNameText != null) cardNameText.text = string.IsNullOrEmpty(cardName) ? "卡名" : cardName;
        if (effectTypeText != null) effectTypeText.text = $"[{typeLabel}]";

        if (overrideCount >= 0)
        {
            UpdateCountText(overrideCount);
            return;
        }

        // 优先使用被注入的静态字典（DeckManager 注入）
        if (!s_playerCountsLoaded)
        {
            EnsurePlayerCountsLoaded(); // 作为回退，仅在静态字典尚未被注入时执行
        }

        int cnt = 0;
        if (s_playerCounts != null && s_playerCounts.TryGetValue(cardId, out int c)) cnt = c;
        UpdateCountText(cnt);
    }

    void UpdateCountText(int cnt)
    {
        if (countTMP != null) countTMP.text = cnt.ToString();
        if (countText != null) countText.text = cnt.ToString();
    }

    void EnsurePlayerCountsLoaded()
    {
        if (s_playerCountsLoaded) return;
        s_playerCounts = new Dictionary<int, int>();
        TextAsset csv = playerDataCsv;
        if (csv != null)
        {
            ParsePlayerDataCsv(csv.text, s_playerCounts);
            Debug.Log($"[CardCounter.EnsurePlayerCountsLoaded] parsed fallback playerDataCsv, entries={s_playerCounts.Count}");
        }
        else
        {
            Debug.LogWarning("[CardCounter] playerDataCsv 未设置且未注入静态字典，数量将显示为 0（建议由 DeckManager 注入）");
        }
        s_playerCountsLoaded = true;
    }

    void ParsePlayerDataCsv(string text, Dictionary<int, int> outMap)
    {
        outMap.Clear();
        if (string.IsNullOrEmpty(text)) return;
        var records = ParseCsvRecords(text);
        foreach (var row in records)
        {
            if (row == null || row.Count == 0) continue;
            var first = (row[0] ?? "").Trim();
            if (string.IsNullOrEmpty(first) || first.StartsWith("#")) continue;
            if (!first.Equals("card", StringComparison.OrdinalIgnoreCase)) continue;
            if (row.Count < 3) continue;
            if (!int.TryParse(row[1], out int id)) continue;
            if (!int.TryParse(row[2], out int cnt)) cnt = 0;
            if (outMap.ContainsKey(id)) outMap[id] += cnt; else outMap[id] = cnt;
        }
    }

    List<List<string>> ParseCsvRecords(string text)
    {
        var records = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return records;
        int i = 0, n = text.Length;
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;
        while (i < n)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    else { inQuotes = false; i++; continue; }
                }
                else { field.Append(c); i++; continue; }
            }
            else
            {
                if (c == '"') { inQuotes = true; i++; continue; }
                else if (c == ',') { row.Add(field.ToString()); field.Length = 0; i++; continue; }
                else if (c == '\r') { i++; if (i < n && text[i] == '\n') i++; row.Add(field.ToString()); field.Length = 0; records.Add(row); row = new List<string>(); continue; }
                else if (c == '\n') { i++; row.Add(field.ToString()); field.Length = 0; records.Add(row); row = new List<string>(); continue; }
                else { field.Append(c); i++; continue; }
            }
        }
        row.Add(field.ToString());
        if (row.Count > 1 || (row.Count == 1 && !string.IsNullOrEmpty(row[0]))) records.Add(row);
        return records;
    }
}
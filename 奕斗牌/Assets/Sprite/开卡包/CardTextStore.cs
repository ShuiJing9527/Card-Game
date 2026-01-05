using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 从 CSV(TextAsset) 加载卡牌文本（羁绊、效果、咒术描述、叠放说明文本）。
/// 说明：
/// - 默认显示应使用 StackDescription（文本），CSV 解析不会把叠放描述自动解析为数字写入 StackCount。
/// - 保留 StackCount(int?) 字段仅作向后兼容（如果需要手动填充可在其他流程中写入）。
/// - SetTexts 支持第6个可选参数 stackCount，以兼容旧调用，但 CSV 解析不会设置该值。
/// - 支持 quoted fields（字段内逗号/换行，双引号转义）。
/// </summary>
public class CardTextStore : MonoBehaviour
{
    [Serializable]
    public class CardTexts
    {
        public int CardId;
        public string LinkDescription;    // 羁绊描述
        public string MainDescription;    // 效果/判定描述（怪兽）
        public string MagicDescription;   // 咒术描述（咒术）
        public string StackDescription;   // 叠放描述（文本，来自 CSV）
        public int? StackCount;           // 可空的叠放数量，仅作兼容（CSV 不会自动填充）
    }

    private Dictionary<int, CardTexts> _texts = new Dictionary<int, CardTexts>();

    [Header("CSV Input (optional)")]
    public TextAsset csvFile;         // 把 CSV 放为 TextAsset 并拖进来
    public bool parseOnAwake = true;
    public bool debugMode = false;

    // ---------------- API ----------------
    public void Clear() => _texts.Clear();

    // SetTexts: 支持可选的 stackCount（兼容旧的 6 参数调用）
    // 顺序: cardId, linkDesc, mainDesc, magicDesc, stackDesc, [stackCount]
    public void SetTexts(int cardId, string linkDesc, string mainDesc, string magicDesc, string stackDesc, int? stackCount = null)
    {
        if (cardId <= 0) return;
        if (!_texts.TryGetValue(cardId, out var t))
        {
            t = new CardTexts { CardId = cardId };
            _texts[cardId] = t;
        }
        t.LinkDescription = linkDesc ?? string.Empty;
        t.MainDescription = mainDesc ?? string.Empty;
        t.MagicDescription = magicDesc ?? string.Empty;
        t.StackDescription = stackDesc ?? string.Empty;
        t.StackCount = stackCount;
    }

    public CardTexts GetCardTexts(int cardId)
    {
        _texts.TryGetValue(cardId, out var t);
        return t;
    }

    public string GetLink(int cardId) => _texts.TryGetValue(cardId, out var a) ? a.LinkDescription : null;
    public string GetMain(int cardId) => _texts.TryGetValue(cardId, out var b) ? b.MainDescription : null;
    public string GetMagic(int cardId) => _texts.TryGetValue(cardId, out var c) ? c.MagicDescription : null;
    public string GetStackDescription(int cardId) => _texts.TryGetValue(cardId, out var d) ? d.StackDescription : null;
    public int? GetStackCount(int cardId) => _texts.TryGetValue(cardId, out var e) ? e.StackCount : null;

    public bool TryGetCardTexts(int cardId, out CardTexts outTexts)
    {
        return _texts.TryGetValue(cardId, out outTexts);
    }

    // ---------------- CSV 解析 ----------------
    void Awake()
    {
        if (csvFile != null && parseOnAwake)
            ParseCsvAndPopulate(csvFile.text);
    }

    [ContextMenu("ParseCsvNow")]
    public void ParseCsvNow()
    {
        if (csvFile == null)
        {
            Debug.LogWarning("CardTextStore: csvFile 未设置，无法解析。");
            return;
        }
        ParseCsvAndPopulate(csvFile.text);
    }

    void ParseCsvAndPopulate(string csvText)
    {
        if (string.IsNullOrEmpty(csvText))
        {
            Debug.LogWarning("CardTextStore: CSV 文本为空。");
            return;
        }

        _texts.Clear();
        var records = ParseCsvRecords(csvText);
        int loaded = 0;

        foreach (var row in records)
        {
            if (row == null || row.Count == 0) continue;
            string first = (row[0] ?? "").Trim();
            if (first.StartsWith("#")) continue; // 注释行或表头跳过

            if (string.Equals(first, "monster", StringComparison.OrdinalIgnoreCase))
            {
                // 期待列: 0:type,1:CardID,2:Name,3:Attributes,4:Lv,5:Atk,6:Link,7:LinkDescription,8:Effect/Judge,9:mainDescription
                if (row.Count < 10)
                {
                    if (debugMode) Debug.LogWarning($"CardTextStore: monster 行列数过少 ({row.Count})，跳过。");
                    continue;
                }
                if (!int.TryParse(row[1], out int id))
                {
                    if (debugMode) Debug.LogWarning($"CardTextStore: monster id 解析失败: '{row[1]}'");
                    continue;
                }
                string linkDesc = row[7]?.Trim();
                string mainDesc = row[9]?.Trim();

                // monster 没有 stackCount（默认 null）
                SetTexts(id, linkDesc ?? string.Empty, mainDesc ?? string.Empty, null, null, null);
                loaded++;
                if (debugMode) Debug.Log($"CardTextStore: loaded monster id={id} main='{Truncate(mainDesc)}'");
            }
            else if (string.Equals(first, "spell", StringComparison.OrdinalIgnoreCase))
            {
                // 期待列: 0:type,1:CardID,2:Name,3:MagicDescription,4:StackDescription
                if (row.Count < 4)
                {
                    if (debugMode) Debug.LogWarning($"CardTextStore: spell 行列数过少 ({row.Count})，跳过。");
                    continue;
                }
                if (!int.TryParse(row[1], out int id))
                {
                    if (debugMode) Debug.LogWarning($"CardTextStore: spell id 解析失败: '{row[1]}'");
                    continue;
                }
                string magicDesc = row.Count > 3 ? row[3]?.Trim() : string.Empty;
                string stackDesc = row.Count > 4 ? row[4]?.Trim() : string.Empty;

                // 重要：不再根据 stackDesc 自动解析为数字。始终把原始文本作为 StackDescription。
                int? stackCount = null; // 保持 null（兼容字段保留）
                SetTexts(id, null, null, magicDesc ?? string.Empty, stackDesc ?? string.Empty, stackCount);
                loaded++;
                if (debugMode) Debug.Log($"CardTextStore: loaded spell id={id} magic='{Truncate(magicDesc)}' stackDesc='{Truncate(stackDesc)}'");
            }
            else
            {
                if (debugMode) Debug.Log($"CardTextStore: 未识别行类型 '{first}'，跳过。");
            }
        }

        if (debugMode) Debug.Log($"CardTextStore: 解析完成，加载 {loaded} 项。");
    }

    string Truncate(string s, int len = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= len ? s : s.Substring(0, len) + "...";
    }

    // 简单 RFC 风格 CSV 解析（支持双引号转义、字段内换行）
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
                    if (i + 1 < n && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                        continue;
                    }
                }
                else
                {
                    field.Append(c);
                    i++;
                    continue;
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                    continue;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    i++;
                    continue;
                }
                else if (c == '\r')
                {
                    i++;
                    if (i < n && text[i] == '\n') i++;
                    row.Add(field.ToString());
                    field.Length = 0;
                    records.Add(row);
                    row = new List<string>();
                    continue;
                }
                else if (c == '\n')
                {
                    i++;
                    row.Add(field.ToString());
                    field.Length = 0;
                    records.Add(row);
                    row = new List<string>();
                    continue;
                }
                else
                {
                    field.Append(c);
                    i++;
                    continue;
                }
            }
        }

        // 尾部加入最后字段/行
        row.Add(field.ToString());
        if (row.Count > 1 || (row.Count == 1 && !string.IsNullOrEmpty(row[0])))
            records.Add(row);

        return records;
    }
}
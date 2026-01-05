using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 从 CSV(TextAsset) 解析卡牌数据并生成 cardList，同时把所有描述写入 CardTextStore
/// 注意：需要在 Inspector 绑定 cardData (TextAsset) 与 textStore (CardTextStore)
/// 假设项目中已存在 CardMessage / MonsterCard / SpellCard / MonsterCardType 等类型。
/// </summary>
public class CardDrawStore : MonoBehaviour
{
    [Tooltip("CSV 文本（TextAsset）")]
    public TextAsset cardData;

    [Tooltip("解析生成的卡牌数据集合（可由其他代码访问）")]
    public List<CardMessage> cardList = new List<CardMessage>();

    [Tooltip("文本管理组件（用于保存羁绊/效果/咒术/叠放等描述）")]
    public CardTextStore textStore; // 在 Inspector 绑定

    [Tooltip("是否在 Start 时自动解析 CSV")]
    public bool parseOnStart = true;

    // 可打开以打印更多解析时的调试信息
    public bool debugLogging = false;

    void Start()
    {
        if (parseOnStart)
            LoadCardData();
    }

    /// <summary>
    /// 解析 CSV 并填充 cardList 与 textStore
    /// </summary>
    public void LoadCardData()
    {
        cardList.Clear();
        textStore?.Clear();

        if (cardData == null)
        {
            Debug.LogError("CardDrawStore: cardData 未设置（TextAsset 为空）");
            return;
        }

        var rows = ReadCsvRows(cardData.text);
        if (rows.Count == 0)
        {
            Debug.LogWarning("CardDrawStore: CSV 内容为空");
            return;
        }

        int headerRow = FindHeaderRow(rows);
        Dictionary<string, int> headerIndex = null;
        if (headerRow >= 0)
            headerIndex = BuildHeaderIndex(ParseCsvLine(rows[headerRow]));

        for (int r = 0; r < rows.Count; r++)
        {
            if (r == headerRow) continue;
            var raw = rows[r];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var cols = ParseCsvLine(raw);
            if (cols.Count == 0) continue;

            string tag = (cols.Count > 0 ? (cols[0] ?? "").Trim() : "");
            if (string.IsNullOrEmpty(tag) || tag.StartsWith("#")) continue;

            if (tag.Equals("monster", StringComparison.OrdinalIgnoreCase))
            {
                ParseMonster(cols, headerIndex);
            }
            else if (tag.Equals("spell", StringComparison.OrdinalIgnoreCase))
            {
                ParseSpell(cols, headerIndex);
            }
            else
            {
                // 未识别的 tag，跳过
                if (debugLogging) Debug.Log($"CardDrawStore: 未知 tag '{tag}'，跳过行: {raw}");
            }
        }

        Debug.Log($"CardDrawStore: 解析完成，共 {cardList.Count} 张卡");
    }

    // ===== CSV 解析与辅助方法 =====

    // 将整个文本拆分为行（支持引号内换行）
    List<string> ReadCsvRows(string text)
    {
        var rows = new List<string>();
        if (string.IsNullOrEmpty(text)) return rows;
        // 去掉可能的 BOM
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                // 双引号转义 ("" => ")
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                // 行结束（处理 CRLF）
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                rows.Add(sb.ToString());
                sb.Length = 0;
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) rows.Add(sb.ToString());
        return rows;
    }

    // 解析单行 CSV 字段（保留空字段），支持双引号与转义
    List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        if (line == null) return fields;

        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Length = 0;
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        // 修剪每个字段前后空白
        for (int i = 0; i < fields.Count; i++) fields[i] = fields[i]?.Trim();
        return fields;
    }

    // 查找可能的表头行（返回行索引），若找不到返回 -1
    int FindHeaderRow(List<string> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var cols = ParseCsvLine(rows[i]);
            if (cols.Exists(c =>
                !string.IsNullOrEmpty(c) &&
                (c.IndexOf("卡片ID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 c.IndexOf("卡名", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 c.StartsWith("#"))))
            {
                return i;
            }
        }
        return -1;
    }

    // 将 header 列名映射为索引（去除空格与 '#' 并转小写）
    Dictionary<string, int> BuildHeaderIndex(List<string> headers)
    {
        var dict = new Dictionary<string, int>();
        if (headers == null) return dict;
        for (int i = 0; i < headers.Count; i++)
        {
            var key = headers[i] ?? "";
            key = key.Replace(" ", "").Replace("#", "").ToLowerInvariant();
            if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
            {
                dict[key] = i;
            }
        }
        return dict;
    }

    // 在 header map 中按关键字查索引（支持部分匹配）
    int FindHeaderIndexByKeywords(Dictionary<string, int> idx, params string[] keywords)
    {
        if (idx == null) return -1;
        foreach (var kv in idx)
        {
            var key = kv.Key ?? "";
            foreach (var kw in keywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                var kwNorm = kw.Replace(" ", "").ToLowerInvariant();
                if (key.Contains(kwNorm))
                    return kv.Value;
            }
        }
        return -1;
    }

    // 从 cols 或 header map 中获取字段（header 存在则优先匹配 headerNameGuess，否则使用 fallbackIndex）
    string GetField(List<string> cols, Dictionary<string, int> idx, string headerNameGuess, int fallbackIndex)
    {
        if (idx != null)
        {
            string guess = (headerNameGuess ?? "").Replace(" ", "").Replace("#", "").ToLowerInvariant();
            foreach (var kv in idx)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.Contains(guess))
                {
                    int i = kv.Value;
                    if (i >= 0 && i < cols.Count) return cols[i];
                }
            }
        }
        if (fallbackIndex >= 0 && fallbackIndex < cols.Count) return cols[fallbackIndex];
        return "";
    }

    // 查找可能的叠放列并返回原始文本（更鲁棒的查找：支持 header 匹配、标签列+内容列、以及通用回退）
    string GetStackRawFromCols(List<string> cols, Dictionary<string, int> headerIndex)
    {
        if (cols == null) return "";

        // 1) 如果有 headerIndex，优先通过 header 关键字匹配
        string[] headerKeywords = new string[] { "叠放描述", "stackdescription", "stackdesc", "stack", "堆叠", "叠放" };
        int stackCol = FindHeaderIndexByKeywords(headerIndex, headerKeywords);
        if (stackCol >= 0 && stackCol < cols.Count)
        {
            string v = (cols[stackCol] ?? "").Trim();
            if (!string.IsNullOrEmpty(v))
            {
                if (debugLogging) Debug.Log($"CardDrawStore: Found stack column by header at index {stackCol} value='{TruncateForLog(v)}'");
                return v;
            }
        }

        // 2) 如果没有通过 header 找到，扫描每个字段寻找直接包含“叠放/stack”的单元
        for (int i = 0; i < cols.Count; i++)
        {
            var cell = (cols[i] ?? "").Trim();
            if (string.IsNullOrEmpty(cell)) continue;
            var lower = cell.ToLowerInvariant();
            if (lower.Contains("叠放") || lower.Contains("stack") || lower.Contains("堆叠"))
            {
                // 如果该单元是标签（长度短或形如【叠放】），尝试取下一列作为实际内容
                if (cell.Length <= 8 && i + 1 < cols.Count && !string.IsNullOrWhiteSpace(cols[i + 1]))
                {
                    var next = (cols[i + 1] ?? "").Trim();
                    if (!string.IsNullOrEmpty(next))
                    {
                        if (debugLogging) Debug.Log($"CardDrawStore: Found label '{cell}' at col {i}, using next col {i + 1} as stackRaw='{TruncateForLog(next)}'");
                        return next;
                    }
                }

                // 否则直接返回该单元（可能是 "叠放数: 1" 或 "叠放：描述" 等）
                if (debugLogging) Debug.Log($"CardDrawStore: Using cell at col {i} as stackRaw='{TruncateForLog(cell)}'");
                return cell;
            }
        }

        // 3) 回退查找：从常见位置寻找较长或带标点的描述性文本（通常描述会比较长或含中文标点）
        int[] fallbacks = { 4, 5, 6, 3, 7, 8 };
        foreach (var fi in fallbacks)
        {
            if (fi >= 0 && fi < cols.Count)
            {
                var raw = (cols[fi] ?? "").Trim();
                if (!string.IsNullOrEmpty(raw) && (raw.Length > 6 || raw.Contains("。") || raw.Contains("：") || raw.Contains("【") || raw.Contains("（")))
                {
                    if (debugLogging) Debug.Log($"CardDrawStore: Fallback picked col {fi} value='{TruncateForLog(raw)}'");
                    return raw;
                }
            }
        }

        // 4) 最后一搏：返回第一个非空字段（从 index 3 开始，避免取 tag/id/name）
        for (int i = 3; i < cols.Count; i++)
        {
            var raw = (cols[i] ?? "").Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                if (debugLogging) Debug.Log($"CardDrawStore: Final fallback picked col {i} value='{TruncateForLog(raw)}'");
                return raw;
            }
        }

        // 未找到
        if (debugLogging) Debug.Log($"CardDrawStore: 未找到合适的 stackRaw");
        return "";
    }

    // ===== 卡牌解析 =====

    void ParseMonster(List<string> cols, Dictionary<string, int> idx)
    {
        int id = SafeParseInt(GetField(cols, idx, "卡片ID", 1));
        string name = GetField(cols, idx, "卡名", 2);
        string attr = GetField(cols, idx, "属性", 3);
        string lvStr = GetField(cols, idx, "等级", 4);
        string atkStr = GetField(cols, idx, "战力", 5);
        string link = GetField(cols, idx, "羁绊", 6);
        string linkDesc = GetField(cols, idx, "羁绊描述", 7);
        string typeField = GetField(cols, idx, "类型", 8);
        string description = GetField(cols, idx, "效果", 9);
        // 有的 CSV 布局不同，使用 fallback 索引上面的值可能是空，已经通过 GetField 处理过

        int lv = SafeParseInt(lvStr, 0);
        int atk = SafeParseInt(atkStr, 0);

        // 补偿：有时属性在多个列里（尝试在附近列查找）
        if (string.IsNullOrWhiteSpace(attr))
        {
            for (int i = 2; i < cols.Count && i <= 6; i++)
            {
                var v = cols[i]?.Trim();
                if (v == "土" || v == "火" || v == "水" || v == "木" || v == "金")
                {
                    attr = v;
                    break;
                }
            }
        }

        var mType = ParseMonsterType(typeField);

        // 下面假设项目中存在 MonsterCard 构造函数；如不一致请按项目调整
        MonsterCard monsterCard = new MonsterCard(
            id,
            name ?? "",
            description ?? "",
            "monster",
            atk,
            lv,
            attr ?? "",
            link ?? "",
            linkDesc ?? "",
            null,
            monsterType: mType
        );
        cardList.Add(monsterCard);

        // 写入 LinkDescription & MainDescription 到 textStore（若存在）
        if (textStore != null)
        {
            // CardTextStore.SetTexts(cardId, linkDesc, mainDesc, magicDesc, stackDesc)
            textStore.SetTexts(id, linkDesc, description, null, null);
        }

        if (Application.isPlaying)
            Debug.Log($"Loaded Monster: id={id} name={name} attr='{attr}' atk={atk} lv={lv}");
    }

    void ParseSpell(List<string> cols, Dictionary<string, int> idx)
    {
        int id = SafeParseInt(GetField(cols, idx, "卡片ID", 1));
        string name = GetField(cols, idx, "卡名", 2);
        string magicDesc = GetField(cols, idx, "咒术描述", 3);

        // 只取原始叠放文本（更鲁棒的查找）
        string stackRaw = GetStackRawFromCols(cols, idx);
        stackRaw = (stackRaw ?? "").Trim();

        // 假设项目存在 SpellCard 构造函数；如不一致请按项目调整
        SpellCard spellCard = new SpellCard(id, name ?? "", magicDesc ?? "", "spell", false, false, null);
        cardList.Add(spellCard);

        // 写入到 textStore：仅包含 magicDesc 与 stackRaw（叠放说明文本）
        if (textStore != null)
        {
            // 仅写入文本，不写入数值兼容字段
            textStore.SetTexts(id, null, null, magicDesc, stackRaw);
        }

        if (Application.isPlaying)
            Debug.Log($"Loaded Spell: id={id} name={name} stackRaw='{stackRaw}'");
    }

    // ===== 其它辅助 =====

    int SafeParseInt(string s, int def = 0)
    {
        if (string.IsNullOrEmpty(s)) return def;
        return int.TryParse(s, out int v) ? v : def;
    }

    MonsterCardType ParseMonsterType(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return MonsterCardType.Effect;
        if (int.TryParse(s, out int vi) && Enum.IsDefined(typeof(MonsterCardType), vi))
            return (MonsterCardType)vi;
        if (Enum.TryParse<MonsterCardType>(s, true, out var res))
            return res;
        var t = s.ToLowerInvariant();
        if (t.Contains("判定") || t.Contains("judge")) return MonsterCardType.Judge;
        return MonsterCardType.Effect;
    }

    string TruncateForLog(string s, int max = 160)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }
}
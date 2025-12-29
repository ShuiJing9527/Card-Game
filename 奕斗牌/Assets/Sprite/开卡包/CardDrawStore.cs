using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class CardDrawStore : MonoBehaviour
{
    public TextAsset cardData;
    public List<CardMessage> cardList = new List<CardMessage>();

    // 存储 CSV 中原始的叠放描述：Key = 卡片ID, Value = 叠放描述字符串
    private Dictionary<int, string> _stackDescriptionMap = new Dictionary<int, string>();

    void Start()
    {
        LoadCardData();
    }

    // 支持引号内换行/逗号的行分隔
    List<string> ReadCsvRows(string text)
    {
        var rows = new List<string>();
        if (string.IsNullOrEmpty(text)) return rows;
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                rows.Add(sb.ToString());
                sb.Length = 0;
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) rows.Add(sb.ToString());
        return rows;
    }

    // 解析单行 CSV（保留空字段）
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
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Length = 0;
            }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        for (int i = 0; i < fields.Count; i++) fields[i] = fields[i].Trim().Trim('"');
        return fields;
    }

    int SafeParseInt(string s, int def = 0)
    {
        if (int.TryParse(s, out int v)) return v;
        return def;
    }

    MonsterCardType ParseMonsterType(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return MonsterCardType.Effect;
        if (int.TryParse(s, out int vi) && Enum.IsDefined(typeof(MonsterCardType), vi)) return (MonsterCardType)vi;
        if (Enum.TryParse<MonsterCardType>(s, true, out var res)) return res;
        var t = s.ToLower();
        if (t.Contains("判定") || t.Contains("judge")) return MonsterCardType.Judge;
        return MonsterCardType.Effect;
    }

    // 在 header map 中按关键字查找索引（如包含 "叠放" 或 "stack"）
    int FindHeaderIndexByKeywords(Dictionary<string, int> idx, params string[] keywords)
    {
        if (idx == null) return -1;
        foreach (var kv in idx)
        {
            var key = kv.Key?.ToLower() ?? "";
            foreach (var kw in keywords)
            {
                if (key.Contains(kw.ToLower())) return kv.Value;
            }
        }
        return -1;
    }

    public void LoadCardData()
    {
        cardList.Clear();
        _stackDescriptionMap.Clear();

        if (cardData == null) { Debug.LogError("cardData 为 null"); return; }

        var rows = ReadCsvRows(cardData.text);
        if (rows.Count == 0) { Debug.LogWarning("CSV 无内容"); return; }

        // 找 header 行（包含 "卡片ID" 或以 '#' 开头）
        int headerRow = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i])) continue;
            var cols = ParseCsvLine(rows[i]);
            if (cols.Count == 0) continue;
            if (cols.Exists(c => (!string.IsNullOrEmpty(c) && (c.Contains("卡片ID") || c.Contains("卡名") || c.Contains("卡片")))) || cols[0].StartsWith("#"))
            {
                headerRow = i;
                break;
            }
        }

        Dictionary<string, int> idx = new Dictionary<string, int>();
        if (headerRow >= 0)
        {
            var headers = ParseCsvLine(rows[headerRow]);
            for (int h = 0; h < headers.Count; h++)
            {
                var key = headers[h]?.Trim();
                if (string.IsNullOrEmpty(key)) continue;
                var norm = key.Replace(" ", "").Replace("#", "");
                idx[norm] = h;
            }

            Debug.Log("[CardDrawStore] Header mapping:");
            foreach (var kv in idx)
                Debug.Log($"  '{kv.Key}' => {kv.Value}");
        }
        else
        {
            Debug.LogWarning("[CardDrawStore] 未找到 header 行，使用纯位置索引解析");
        }

        for (int r = 0; r < rows.Count; r++)
        {
            if (r == headerRow) continue;
            var raw = rows[r];
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var cols = ParseCsvLine(raw);
            if (cols.Count == 0) continue;

            string tag = cols[0].Trim();
            if (string.IsNullOrEmpty(tag) || tag.StartsWith("#")) continue;

            if (tag.Equals("monster", StringComparison.OrdinalIgnoreCase))
            {
                int id = SafeParseInt(GetField(cols, idx, "卡片ID", 1));
                string name = GetField(cols, idx, "卡名", 2);
                string attr = GetField(cols, idx, "属性", 3);
                string lvStr = GetField(cols, idx, "等级", 4);
                string atkStr = GetField(cols, idx, "战力", 5);
                string link = GetField(cols, idx, "羁绊", 6);
                string linkDesc = GetField(cols, idx, "羁绊描述", 7);
                string typeField = GetField(cols, idx, "类型", 9);
                string description = GetField(cols, idx, "效果", 10);

                int lv = SafeParseInt(lvStr, 0);
                int atk = SafeParseInt(atkStr, 0);

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

                MonsterCard monsterCard = new MonsterCard(
                    id, name ?? "", description ?? "", "monster",
                    atk, lv, attr ?? "", link ?? "", linkDesc ?? "", null, monsterType: mType
                );
                cardList.Add(monsterCard);
                Debug.Log($"Loaded Monster: id={id} name={name} attr='{attr}' atk={atk} lv={lv}");
            }
            else if (tag.Equals("spell", StringComparison.OrdinalIgnoreCase))
            {
                int id = SafeParseInt(GetField(cols, idx, "卡片ID", 1));
                string name = GetField(cols, idx, "卡名", 2);
                string desc = GetField(cols, idx, "咒术描述", 3);

                // 读取 stackDesc：优先 header，通过关键字查找其索引；然后尝试多个 fallback 索引（5,4,3）
                string stackDesc = "";

                int headerIndex = FindHeaderIndexByKeywords(idx, "叠放", "stack", "bond", "堆叠");
                if (headerIndex >= 0 && headerIndex < cols.Count)
                    stackDesc = cols[headerIndex];

                if (string.IsNullOrEmpty(stackDesc))
                {
                    int[] fallbacks = new int[] { 5, 4, 3 };
                    foreach (var fi in fallbacks)
                    {
                        if (fi >= 0 && fi < cols.Count && !string.IsNullOrEmpty(cols[fi]))
                        {
                            stackDesc = cols[fi];
                            break;
                        }
                    }
                }

                stackDesc = (stackDesc ?? "").Trim();

                // 尝试把叠放描述写进 SpellCard（若类定义了字段/属性），以便后续直接使用；失败则忽略
                SpellCard spellCard = new SpellCard(id, name ?? "", desc ?? "", "spell", false, false, null);
                try
                {
                    var t = spellCard.GetType();
                    var prop = t.GetProperty("StackDescription");
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(spellCard, stackDesc);
                    }
                    else
                    {
                        var field = t.GetField("StackDescription");
                        if (field != null)
                            field.SetValue(spellCard, stackDesc);
                    }
                }
                catch (Exception)
                {
                    // 忽略，非必须
                }

                // 存存储到字典
                try
                {
                    _stackDescriptionMap[id] = stackDesc;
                    Debug.Log($"[CardDrawStore] Stored StackDesc for id={id} len={stackDesc?.Length ?? 0} val='{stackDesc}'");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"保存叠放描述失败 id={id} : {ex}");
                }

                cardList.Add(spellCard);
                Debug.Log($"Loaded Spell: id={id} name={name} (desc len={(desc?.Length ?? 0)})");
            }
            else
            {
                Debug.LogWarning($"未知 tag {tag} 跳过行 {r}");
            }
        }

        Debug.Log($"Loaded total {cardList.Count} cards.");
    }

    // 从 cols 或 header map 中获取字段（若 header 存在则用 header，否则 fallbackIndex）
    string GetField(List<string> cols, Dictionary<string, int> idx, string headerNameGuess, int fallbackIndex)
    {
        if (idx != null)
        {
            // 尝试按完全匹配（删除空格与#）
            foreach (var kv in idx)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.ToLower().Contains(headerNameGuess.Replace(" ", "").ToLower()))
                {
                    int i = kv.Value;
                    if (i >= 0 && i < cols.Count) return cols[i];
                }
            }
        }
        if (fallbackIndex >= 0 && fallbackIndex < cols.Count) return cols[fallbackIndex];
        return "";
    }

    public void TestLoad()
    {
        foreach (var item in cardList)
        {
            Debug.Log("卡牌: " + item.Card_ID + " " + item.Card_Name);
        }
    }

    public CardMessage RandomCard()
    {
        if (cardList == null || cardList.Count == 0)
        {
            Debug.LogWarning("cardList 为空，无法随机获取卡牌");
            return null;
        }
        return cardList[UnityEngine.Random.Range(0, cardList.Count)];
    }

    // 对外提供叠放描述查询
    public string GetStackDescriptionById(int cardId)
    {
        if (_stackDescriptionMap == null) return null;
        if (_stackDescriptionMap.TryGetValue(cardId, out string v))
            return string.IsNullOrEmpty(v) ? null : v;
        return null;
    }
}
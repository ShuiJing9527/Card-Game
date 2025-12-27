using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class CardDrawStore : MonoBehaviour
{
    public TextAsset cardData;
    public List<CardMessage> cardList = new List<CardMessage>();

    void Start()
    {
        LoadCardData();
        // TestLoad();
    }

    // 简单但支持引号内逗号/换行的行分割与字段解析
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

    bool ParseBoolLike(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().ToLower();
        return s == "1" || s == "true" || s == "yes" || s == "y" || s == "t" || s == "magic" || s == "stack";
    }

    MonsterCardType ParseMonsterType(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return MonsterCardType.Effect;
        if (int.TryParse(s, out int vi) && System.Enum.IsDefined(typeof(MonsterCardType), vi)) return (MonsterCardType)vi;
        if (System.Enum.TryParse<MonsterCardType>(s, true, out var res)) return res;
        var t = s.ToLower();
        if (t.Contains("判定") || t.Contains("judge")) return MonsterCardType.Judge;
        return MonsterCardType.Effect;
    }

    public void LoadCardData()
    {
        cardList.Clear();
        if (cardData == null) { Debug.LogError("cardData 为 null"); return; }

        var rows = ReadCsvRows(cardData.text);
        if (rows.Count == 0) { Debug.LogWarning("CSV 无内容"); return; }

        // 找 header 行（包含 "卡片ID" 或以 '#' 开头）
        int headerRow = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i])) continue;
            var cols = ParseCsvLine(rows[i]);
            if (cols.Exists(c => c.Contains("卡片ID") || c.Contains("卡名") || c.Contains("卡片") || cols[0].StartsWith("#")))
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
                key = key.Replace(" ", "").Replace("#", "");
                idx[key] = h;
            }
        }

        string Field(int rowIndex, string headerName, int fallbackIndex = -1)
        {
            var cols = ParseCsvLine(rows[rowIndex]);
            if (idx != null && idx.TryGetValue(headerName, out int i) && i >= 0 && i < cols.Count) return cols[i];
            if (fallbackIndex >= 0 && fallbackIndex < cols.Count) return cols[fallbackIndex];
            return "";
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

            if (tag.Equals("monster", System.StringComparison.OrdinalIgnoreCase))
            {
                int id = SafeParseInt(Field(r, "卡片ID", 1));
                string name = Field(r, "卡名Name", 2);
                string attr = Field(r, "属性Attributes", 3);
                string lvStr = Field(r, "等级Lv", 4);
                string atkStr = Field(r, "战力Atk", 5);
                string link = Field(r, "羁绊Link", 6);
                string linkDesc = Field(r, "羁绊描述LinkDescription", 7);
                string typeField = Field(r, "类型Effect/Judge", 9);
                string description = Field(r, "效果/判定描述mainDescription", 10);

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
                // 下面假设你的项目已定义 MonsterCard 构造或属性；如果不是，请调整为符合你项目的 MonsterCard 类型建立方式
                MonsterCard monsterCard = new MonsterCard(
                    id, name ?? "", description ?? "", "monster",
                    atk, lv, attr ?? "", link ?? "", linkDesc ?? "", null, monsterType: mType
                );
                cardList.Add(monsterCard);
                Debug.Log($"Loaded Monster: id={id} name={name} attr='{attr}' atk={atk} lv={lv}");
            }
            else if (tag.Equals("spell", System.StringComparison.OrdinalIgnoreCase))
            {
                int id = SafeParseInt(Field(r, "卡片ID", 1));
                string name = Field(r, "卡名Name", 2);
                string desc = Field(r, "咒术描述MagicDescription", 3);
                string stackDesc = Field(r, "叠放描述StackDescription", 4);
                // 假设项目已有 SpellCard 对应构造
                SpellCard spellCard = new SpellCard(id, name ?? "", desc ?? "", "spell", false, false, null);
                cardList.Add(spellCard);
                Debug.Log($"Loaded Spell: id={id} name={name}");
            }
            else
            {
                Debug.LogWarning($"未知 tag {tag} 跳过行 {r}");
            }
        }

        Debug.Log($"Loaded total {cardList.Count} cards.");
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
        return cardList[Random.Range(0, cardList.Count)];
    }
}
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

    // 简单把费用列（如果存在）解析为 List<CardCostInfo>
    List<CardCostInfo> ParseCosts(string value, string desc)
    {
        var costs = new List<CardCostInfo>();
        int cv = SafeParseInt(value, -1);
        if (cv >= 0)
        {
            costs.Add(new CardCostInfo(cv, desc ?? ""));
        }
        else
        {
            // 没有数字时，放默认无费用
            costs.Add(new CardCostInfo(0, "无费用"));
        }
        return costs;
    }

    public void LoadCardData()
    {
        cardList.Clear();
        if (cardData == null) { Debug.LogError("cardData 为 null"); return; }

        var rows = ReadCsvRows(cardData.text);
        foreach (var raw in rows)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var cols = ParseCsvLine(raw);
            if (cols.Count == 0) continue;

            // 单次定义 SafeAt，避免重复定义冲突
            string SafeAt(int idx) => (idx >= 0 && idx < cols.Count) ? cols[idx] : "";

            // 优先使用首列作为 tag（若 CSV 第一列始终是 tag）
            string tag = SafeAt(0).Trim();
            if (string.IsNullOrEmpty(tag) || tag.StartsWith("#")) continue;

            if (tag.Equals("monster", System.StringComparison.OrdinalIgnoreCase))
            {
                int id = SafeParseInt(SafeAt(1));
                string name = SafeAt(2);
                int atk = SafeParseInt(SafeAt(3));
                int lv = SafeParseInt(SafeAt(4));
                string attributes = SafeAt(5);
                string link = SafeAt(6);
                string linkEffect = SafeAt(7);
                string typeField = SafeAt(8);
                string description = SafeAt(9);

                var mType = ParseMonsterType(typeField);

                MonsterCard monsterCard = new MonsterCard(
                    id, name ?? "", description ?? "", "monster",
                    atk, lv, attributes ?? "", link ?? "", linkEffect ?? "", null, monsterType: mType
                );
                cardList.Add(monsterCard);
                Debug.Log($"Loaded Monster Card: {name} (type={mType})");
            }
            else if (tag.Equals("spell", System.StringComparison.OrdinalIgnoreCase))
            {
                // 假设 CSV 列顺序（如无 header 请按实际调整）：
                // tag(0), id(1), name(2), description(3), type(4),
                // canUseAsMagic(5), canUseAsStack(6), stackCount(7), atk(8), costValue(9), costDesc(10)

                int id = SafeParseInt(SafeAt(1));
                string name = SafeAt(2);
                string description = SafeAt(3);
                string type = SafeAt(4);

                bool canUseAsMagic = ParseBoolLike(SafeAt(5));
                bool canUseAsStack = ParseBoolLike(SafeAt(6));
                int stackCount = SafeParseInt(SafeAt(7), 1);
                int atk = SafeParseInt(SafeAt(8), 0);

                // 解析费用（简单实现：一列数值 + 一列描述）
                List<CardCostInfo> costs = null;
                if (!string.IsNullOrWhiteSpace(SafeAt(9)) || !string.IsNullOrWhiteSpace(SafeAt(10)))
                {
                    costs = ParseCosts(SafeAt(9), SafeAt(10));
                }

                // 创建 SpellCard：构造器 (int id, string name, string desc, string type, bool canUseAsMagic, bool canUseAsStack, List<CardCostInfo> costs = null)
                SpellCard spellCard = new SpellCard(id, name ?? "", description ?? "", string.IsNullOrEmpty(type) ? "spell" : type,
                    canUseAsMagic, canUseAsStack, costs);

                // 赋值额外字段
                spellCard.StackCount = Mathf.Max(1, stackCount);
                spellCard.Card_Atk = atk;

                cardList.Add(spellCard);
                Debug.Log($"Loaded Spell Card: {name} (id={id}, stack={spellCard.StackCount}, atk={spellCard.Card_Atk}, magic={canUseAsMagic}, stackable={canUseAsStack})");
            }
            else
            {
                Debug.LogWarning($"未识别的 tag '{tag}'，跳过行：{raw}");
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
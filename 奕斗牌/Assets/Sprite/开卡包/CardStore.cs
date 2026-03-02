using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 单例模式的卡片数据管理器，负责解析卡牌 CSV 并提供全局访问接口
/// 生命周期：启动时加载一次，DontDestroyOnLoad 跨场景复用
/// </summary>
public class CardStore : MonoBehaviour
{
    // 单例核心
    public static CardStore Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"CardStore: 检测到重复实例，自动销毁多余对象（场景可能重复放置了该组件）");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("CardStore: 单例初始化完成，生命周期绑定到全局");

        if (parseOnStart)
            LoadCardData();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            OnCardsReady = null;
            Instance = null;
        }
    }

    // 原有字段与功能
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

    // 卡片数据是否准备就绪
    public bool IsCardsReady { get; private set; } = false;

    // 卡片数据准备就绪事件（其他脚本可订阅）
    public event Action OnCardsReady;

    void Start()
    {
        // Awake 已处理自动加载
    }

    /// <summary>
    /// 解析 CSV 并填充 cardList 与 textStore（支持重复调用，但仅首次解析有效）
    /// </summary>
    public void LoadCardData()
    {
        if (IsCardsReady)
        {
            Debug.LogWarning("CardStore: 卡片数据已就绪，无需重复加载");
            NotifyCardsReady();
            return;
        }

        cardList.Clear();
        textStore?.Clear();
        IsCardsReady = false;

        if (cardData == null)
        {
            Debug.LogError("CardStore: cardData 未设置（TextAsset 为空）");
            NotifyCardsReady();
            return;
        }

        var rows = ReadCsvRows(cardData.text);
        if (rows.Count == 0)
        {
            Debug.LogWarning("CardStore: CSV 内容为空");
            NotifyCardsReady();
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
                ParseMonster(cols, headerIndex);
            else if (tag.Equals("spell", StringComparison.OrdinalIgnoreCase))
                ParseSpell(cols, headerIndex);
            else if (debugLogging)
                Debug.Log($"CardStore: 未知 tag '{tag}'，跳过行: {raw}");
        }

        Debug.Log($"CardStore: 解析完成，共 {cardList.Count} 张卡");
        NotifyCardsReady();
    }

    void NotifyCardsReady()
    {
        IsCardsReady = true;
        try
        {
            OnCardsReady?.Invoke();
            Debug.Log("CardStore: 已触发 OnCardsReady 事件，订阅者可开始使用数据");
        }
        catch (Exception ex)
        {
            Debug.LogError($"CardStore: 触发 OnCardsReady 事件失败: {ex.Message}");
        }
    }

    // 查询辅助方法
    public CardMessage GetCardById(int cardId)
    {
        if (cardList == null) return null;
        return cardList.Find(card => card != null && card.Card_ID == cardId);
    }

    public List<CardMessage> GetCardsByType(string type)
    {
        if (cardList == null) return new List<CardMessage>();
        if (string.IsNullOrEmpty(type)) return new List<CardMessage>(cardList);
        return cardList.FindAll(card => card != null &&
            string.Equals(card.Card_Type, type, StringComparison.OrdinalIgnoreCase));
    }

    // CSV 解析与其它辅助
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
        for (int i = 0; i < fields.Count; i++) fields[i] = fields[i]?.Trim();
        return fields;
    }

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

    string GetStackRawFromCols(List<string> cols, Dictionary<string, int> headerIndex)
    {
        if (cols == null) return "";

        string[] headerKeywords = new string[] { "叠放描述", "stackdescription", "stackdesc", "stack", "堆叠", "叠放" };
        int stackCol = FindHeaderIndexByKeywords(headerIndex, headerKeywords);
        if (stackCol >= 0 && stackCol < cols.Count)
        {
            string v = (cols[stackCol] ?? "").Trim();
            if (!string.IsNullOrEmpty(v))
            {
                if (debugLogging) Debug.Log($"CardStore: Found stack column by header at index {stackCol} value='{TruncateForLog(v)}'");
                return v;
            }
        }

        for (int i = 0; i < cols.Count; i++)
        {
            var cell = (cols[i] ?? "").Trim();
            if (string.IsNullOrEmpty(cell)) continue;
            var lower = cell.ToLowerInvariant();
            if (lower.Contains("叠放") || lower.Contains("stack") || lower.Contains("堆叠"))
            {
                if (cell.Length <= 8 && i + 1 < cols.Count && !string.IsNullOrWhiteSpace(cols[i + 1]))
                {
                    var next = (cols[i + 1] ?? "").Trim();
                    if (!string.IsNullOrEmpty(next))
                    {
                        if (debugLogging) Debug.Log($"CardStore: Found label '{cell}' at col {i}, using next col {i + 1} as stackRaw='{TruncateForLog(next)}'");
                        return next;
                    }
                }
                if (debugLogging) Debug.Log($"CardStore: Using cell at col {i} as stackRaw='{TruncateForLog(cell)}'");
                return cell;
            }
        }

        int[] fallbacks = { 4, 5, 6, 3, 7, 8 };
        foreach (var fi in fallbacks)
        {
            if (fi >= 0 && fi < cols.Count)
            {
                var raw = (cols[fi] ?? "").Trim();
                if (!string.IsNullOrEmpty(raw) && (raw.Length > 6 || raw.Contains("。") || raw.Contains("：") || raw.Contains("【") || raw.Contains("（")))
                {
                    if (debugLogging) Debug.Log($"CardStore: Fallback picked col {fi} value='{TruncateForLog(raw)}'");
                    return raw;
                }
            }
        }

        for (int i = 3; i < cols.Count; i++)
        {
            var raw = (cols[i] ?? "").Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                if (debugLogging) Debug.Log($"CardStore: Final fallback picked col {i} value='{TruncateForLog(raw)}'");
                return raw;
            }
        }

        if (debugLogging) Debug.Log($"CardStore: 未找到合适的 stackRaw");
        return "";
    }

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

        if (textStore != null)
            textStore.SetTexts(id, linkDesc, description, null, null);

        if (Application.isPlaying)
            Debug.Log($"Loaded Monster: id={id} name={name} attr='{attr}' atk={atk} lv={lv}");
    }

    void ParseSpell(List<string> cols, Dictionary<string, int> idx)
    {
        int id = SafeParseInt(GetField(cols, idx, "卡片ID", 1));
        string name = GetField(cols, idx, "卡名", 2);
        string magicDesc = GetField(cols, idx, "咒术描述", 3);
        string stackRaw = GetStackRawFromCols(cols, idx);
        stackRaw = (stackRaw ?? "").Trim();

        SpellCard spellCard = new SpellCard(id, name ?? "", magicDesc ?? "", "spell", false, false, null);
        cardList.Add(spellCard);

        if (textStore != null)
            textStore.SetTexts(id, null, null, magicDesc, stackRaw);

        if (Application.isPlaying)
            Debug.Log($"Loaded Spell: id={id} name={name} stackRaw='{stackRaw}'");
    }

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

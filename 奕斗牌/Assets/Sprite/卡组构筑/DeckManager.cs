using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class DeckManager : MonoBehaviour
{
    [Header("CSV Inputs")]
    public TextAsset cardDataCsv;
    public TextAsset playerDataCsv;

    [Header("UI")]
    public RectTransform libraryPanel;

    [Header("Prefabs")]
    public List<GameObject> monsterPrefabs = new List<GameObject>();
    public GameObject spellPrefab;

    [Header("Card Info (per-card, optional)")]
    public GameObject cardInfoPrefab; // optional per-card info prefab
    public bool onlyInstantiateInfoInPoolScene = true;
    public string poolSceneName = "CardPoolScene";
    public bool defaultShowInfo = true;

    [Header("Card Art Loading")]
    public bool useAddressables = true;
    public bool useResourcesForCardArts = true;

    private Dictionary<int, Sprite> _cardArtCache = new Dictionary<int, Sprite>();
    private Dictionary<string, AsyncOperationHandle<Sprite>> _addressableHandles = new Dictionary<string, AsyncOperationHandle<Sprite>>();

    [Header("Options")]
    public bool clearOnStart = true;

    private Dictionary<int, MonsterCard> _monsterDefs = new Dictionary<int, MonsterCard>();
    private Dictionary<int, (SpellCard spell, string stackDesc)> _spellDefs = new Dictionary<int, (SpellCard, string)>();

    void Start()
    {
        if (cardDataCsv == null)
        {
            Debug.LogError("[DeckManager] cardDataCsv 未绑定！");
            return;
        }
        if (playerDataCsv == null)
        {
            Debug.LogError("[DeckManager] playerDataCsv 未绑定！");
            return;
        }
        if (libraryPanel == null)
        {
            Debug.LogError("[DeckManager] libraryPanel 未绑定！");
            return;
        }
        if ((monsterPrefabs == null || monsterPrefabs.Count == 0) && spellPrefab == null)
        {
            Debug.LogWarning("[DeckManager] 未绑定任何 prefab（monsterPrefabs 或 spellPrefab），将无法实例化 UI。");
        }

        if (clearOnStart) ClearLibrary();

        ParseCardData(cardDataCsv.text);
        BuildLibraryFromPlayerData(playerDataCsv.text);
    }

    void OnDestroy()
    {
        foreach (var kv in _addressableHandles)
        {
            var h = kv.Value;
            if (h.IsValid())
            {
                try { Addressables.Release(h); } catch { }
            }
        }
        _addressableHandles.Clear();
        _cardArtCache.Clear();
    }

    void ParseCardData(string text)
    {
        _monsterDefs.Clear();
        _spellDefs.Clear();

        var rows = ParseCsvRecords(text);
        foreach (var row in rows)
        {
            if (row == null || row.Count == 0) continue;
            var first = (row[0] ?? "").Trim();
            if (string.IsNullOrEmpty(first)) continue;
            if (first.StartsWith("#")) continue;

            if (first.Equals("monster", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Count < 10)
                {
                    Debug.LogWarning("[DeckManager] monster 行字段数不足，跳过行: " + string.Join(",", row));
                    continue;
                }
                if (!int.TryParse(row[1], out int id))
                {
                    Debug.LogWarning("[DeckManager] monster 行 CardID 无法解析: " + row[1]);
                    continue;
                }
                string name = row[2];
                string attributes = row[3];
                int lv = TryParseInt(row[4], 1);
                int atk = TryParseInt(row[5], 0);
                string link = row[6];
                string linkDesc = row[7];
                string typeLabel = row[8] ?? "";
                string mainDesc = row[9] ?? "";

                MonsterCardType mtype = MonsterCardType.Effect;
                if (typeLabel.IndexOf("Judge", StringComparison.OrdinalIgnoreCase) >= 0 || typeLabel.Contains("判定")) mtype = MonsterCardType.Judge;
                else mtype = MonsterCardType.Effect;

                var mc = new MonsterCard(
                    id: id,
                    name: name,
                    description: mainDesc,
                    type: "monster",
                    atk: atk,
                    lv: lv,
                    attributes: attributes,
                    link: link,
                    linkEffect: linkDesc,
                    costs: null,
                    monsterType: mtype
                );
                mc.StackCount = 1;
                _monsterDefs[id] = mc;
            }
            else if (first.Equals("spell", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Count < 3)
                {
                    Debug.LogWarning("[DeckManager] spell 行字段数不足，跳过行: " + string.Join(",", row));
                    continue;
                }
                if (!int.TryParse(row[1], out int id))
                {
                    Debug.LogWarning("[DeckManager] spell 行 CardID 无法解析: " + row[1]);
                    continue;
                }
                string name = row.Count > 2 ? row[2] : "";
                string magicDesc = row.Count > 3 ? row[3] : "";
                string stackDesc = row.Count > 4 ? row[4] : "";

                var sc = new SpellCard(id, name, magicDesc, "spell", true, true, null);
                sc.StackCount = 1;
                _spellDefs[id] = (sc, stackDesc);
            }
            else
            {
                continue;
            }
        }

        Debug.Log($"[DeckManager] 解析卡片定义完成：Monster {_monsterDefs.Count}，Spell {_spellDefs.Count}");
    }

    bool ShouldAttachCardInfo()
    {
        if (!defaultShowInfo) return false;
        if (!onlyInstantiateInfoInPoolScene) return true;
        return SceneManager.GetActiveScene().name == poolSceneName;
    }

    void BuildLibraryFromPlayerData(string text)
    {
        var rows = ParseCsvRecords(text);
        int created = 0;

        // build playerCounts first
        var playerCounts = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            if (row == null || row.Count == 0) continue;
            var first = (row[0] ?? "").Trim();
            if (string.IsNullOrEmpty(first)) continue;
            if (first.StartsWith("#")) continue;

            if (first.Equals("card", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Count < 3) continue;
                if (!int.TryParse(row[1], out int cardId)) continue;
                int count = TryParseInt(row[2], 0);
                if (count <= 0) continue;
                if (playerCounts.ContainsKey(cardId)) playerCounts[cardId] += count; else playerCounts[cardId] = count;
            }
        }

        CardCounter.SetPlayerCounts(playerCounts);

        bool attachInfo = ShouldAttachCardInfo();

        foreach (var row in rows)
        {
            if (row == null || row.Count == 0) continue;
            var first = (row[0] ?? "").Trim();
            if (string.IsNullOrEmpty(first)) continue;
            if (first.StartsWith("#")) continue;

            if (first.Equals("coins", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Count >= 2 && int.TryParse(row[1], out int coins))
                    Debug.Log($"[DeckManager] 玩家金币: {coins}");
                continue;
            }

            if (first.Equals("card", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Count < 3)
                {
                    Debug.LogWarning("[DeckManager] player card 行字段不足: " + string.Join(",", row));
                    continue;
                }
                if (!int.TryParse(row[1], out int cardId))
                {
                    Debug.LogWarning("[DeckManager] player card ID 解析失败: " + row[1]);
                    continue;
                }
                int count = TryParseInt(row[2], 0);
                if (count <= 0) continue;

                if (_monsterDefs.TryGetValue(cardId, out MonsterCard mcDef))
                {
                    GameObject prefab = ChooseMonsterPrefabByAttribute(mcDef.Card_Attributes);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[DeckManager] 未找到怪兽 prefab, cardId={cardId} attr={mcDef.Card_Attributes}");
                        continue;
                    }
                    var go = Instantiate(prefab, libraryPanel, false);
                    go.name = $"Monster_{cardId}{mcDef.Card_Name}";
                    go.transform.localScale = Vector3.one;

                    ClearPrefabArt(go);

                    var mcInstance = CloneMonsterCardForPlayer(mcDef, count);
                    var md = go.GetComponent<MonsterCardDisplay>();
                    if (md != null)
                    {
                        md.SetCard(mcInstance);
                        StartCoroutine(ApplyCardArtCoroutine(go, cardId));
                        Debug.Log($"[DeckManager] Instantiated prefab={prefab.name} for id={cardId} name={mcDef.Card_Name} attr={mcDef.Card_Attributes}");
                    }
                    else
                    {
                        Debug.LogWarning($"[DeckManager] monster prefab 缺少 MonsterCardDisplay，无法注入数据 (id={cardId})");
                    }

                    var existingCounter = go.GetComponentInChildren<CardCounter>(true);
                    if (existingCounter != null)
                    {
                        Debug.Log($"[DeckManager] Found existing CardCounter in prefab for id={cardId}, will SetInfo on it");
                        string bondName = mcDef.Card_Name ?? "";
                        CardCounter.EffectType et = CardCounter.EffectType.Effect;
                        try
                        {
                            if (mcDef != null && mcDef.MonsterType == MonsterCardType.Judge)
                                et = CardCounter.EffectType.Judge;
                        }
                        catch { }
                        existingCounter.SetInfo(cardId, bondName, et, count, null);
                    }
                    else if (attachInfo && cardInfoPrefab != null)
                    {
                        var infoGO = Instantiate(cardInfoPrefab, go.transform, false);
                        infoGO.name = "CardInfo";
                        var rt = infoGO.GetComponent<RectTransform>();
                        if (rt != null) rt.anchoredPosition = Vector2.zero;

                        var counter = infoGO.GetComponent<CardCounter>();
                        Debug.Log($"[DeckManager] Instantiated CardInfo prefab for id={cardId} prefabHasCounter={(counter != null)} bondName='{(mcDef != null ? mcDef.Card_Name : "")}' count={count}");
                        if (counter != null)
                        {
                            string bondName = mcDef.Card_Name ?? "";

                            CardCounter.EffectType et = CardCounter.EffectType.Effect;
                            try
                            {
                                if (mcDef != null && mcDef.MonsterType == MonsterCardType.Judge)
                                    et = CardCounter.EffectType.Judge;
                            }
                            catch { /* ignore */ }

                            Debug.Log($"[DeckManager] Calling SetInfo(id={cardId}, bond='{bondName}', effect={et}, count={count}) on CardCounter");
                            counter.SetInfo(cardId, bondName, et, count, null);
                            Debug.Log($"[DeckManager] After SetInfo -> counter.cardId={counter.cardId} overrideCount={count}");
                        }
                        else
                        {
                            Debug.LogWarning("[DeckManager] cardInfoPrefab 未包含 CardCounter 组件，无法初始化数量/名称显示。");
                        }
                    }

                    created++;
                }
                else if (_spellDefs.TryGetValue(cardId, out var spTuple))
                {
                    GameObject prefab = spellPrefab;
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[DeckManager] 未绑定 spellPrefab, cardId={cardId}");
                        continue;
                    }
                    var go = Instantiate(prefab, libraryPanel, false);
                    go.name = $"Spell_{cardId}{spTuple.spell.Card_Name}";
                    go.transform.localScale = Vector3.one;
                    ClearPrefabArt(go);

                    var scDef = spTuple.spell;
                    string stackDesc = spTuple.stackDesc ?? "";
                    scDef.StackCount = count;
                    var sd = go.GetComponent<SpellCardDisplay>();
                    if (sd != null)
                    {
                        sd.SetCard(scDef, stackDesc);
                        StartCoroutine(ApplyCardArtCoroutine(go, cardId));
                    }
                    else
                    {
                        Debug.LogWarning($"[DeckManager] spell prefab 缺少 SpellCardDisplay, 无法注入数据 (id={cardId})");
                    }

                    // For spells: set as Other and use SetInfo to include name and id
                    var existingCounterSpell = go.GetComponentInChildren<CardCounter>(true);
                    if (existingCounterSpell != null)
                    {
                        Debug.Log($"[DeckManager] Found existing CardCounter in spell prefab for id={cardId}, will SetInfo on it");
                        existingCounterSpell.SetInfo(cardId, scDef.Card_Name ?? "", CardCounter.EffectType.Other, count, null);
                    }
                    else if (attachInfo && cardInfoPrefab != null)
                    {
                        var infoGO = Instantiate(cardInfoPrefab, go.transform, false);
                        infoGO.name = "CardInfo";
                        var rt = infoGO.GetComponent<RectTransform>();
                        if (rt != null) rt.anchoredPosition = Vector2.zero;

                        var counter = infoGO.GetComponent<CardCounter>();
                        Debug.Log($"[DeckManager] Instantiated CardInfo prefab for spell id={cardId} prefabHasCounter={(counter != null)} bondName='{(scDef != null ? scDef.Card_Name : "")}' count={count}");
                        if (counter != null)
                        {
                            counter.SetInfo(cardId, scDef.Card_Name ?? "", CardCounter.EffectType.Other, count, null);
                            Debug.Log($"[DeckManager] After SetInfo -> counter.cardId={counter.cardId} overrideCount={count} effectType={counter.effectType}");
                        }
                        else
                        {
                            Debug.LogWarning("[DeckManager] cardInfoPrefab 未包含 CardCounter 组件，无法初始化数量/名称显示。");
                        }
                    }

                    created++;
                }
                else
                {
                    Debug.LogWarning($"[DeckManager] 玩家持有的卡片 id 未在 CardData 中找到定义: {cardId}");
                }
            }
            else
            {
                continue;
            }
        }

        Debug.Log($"[DeckManager] 已生成卡片项: {created}");
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(libraryPanel);
    }

    GameObject ChooseMonsterPrefabByAttribute(string attr)
    {
        if (monsterPrefabs == null || monsterPrefabs.Count == 0) return null;
        if (string.IsNullOrEmpty(attr)) return monsterPrefabs[0];

        string a = attr.Trim();
        int idx = -1;

        if (a.Equals("土", StringComparison.OrdinalIgnoreCase) || a.IndexOf("earth", StringComparison.OrdinalIgnoreCase) >= 0) idx = 0;
        else if (a.Equals("木", StringComparison.OrdinalIgnoreCase) || a.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0) idx = 1;
        else if (a.Equals("水", StringComparison.OrdinalIgnoreCase) || a.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0) idx = 2;
        else if (a.Equals("火", StringComparison.OrdinalIgnoreCase) || a.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0) idx = 3;
        else if (a.Equals("金", StringComparison.OrdinalIgnoreCase) || a.IndexOf("gold", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0) idx = 4;

        if (idx == -1)
        {
            if (a.Contains("土")) idx = 0;
            else if (a.Contains("木")) idx = 1;
            else if (a.Contains("水")) idx = 2;
            else if (a.Contains("火")) idx = 3;
            else if (a.Contains("金")) idx = 4;
        }

        if (idx == -1) idx = Mathf.Abs(a.GetHashCode()) % monsterPrefabs.Count;
        idx = Mathf.Clamp(idx, 0, monsterPrefabs.Count - 1);

        Debug.Log($"ChooseMonsterPrefabByAttribute -> attr='{attr}', idx={idx}, prefab={monsterPrefabs[idx].name}");
        return monsterPrefabs[idx];
    }

    void ClearPrefabArt(GameObject root)
    {
        if (root == null) return;

        string[] artNames = new[] {
            "art", "artimage", "cardart", "artwork", "image_art", "spriteart",
            "art_img", "artImage", "Art",
            "卡图", "卡面", "卡牌图"
        };

        var images = root.GetComponentsInChildren<Image>(true);
        foreach (var img in images)
        {
            if (img == null) continue;
            var nm = img.gameObject.name ?? "";
            foreach (var k in artNames)
            {
                if (!string.IsNullOrEmpty(k) && nm.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    img.sprite = null;
                    Debug.Log($"ClearPrefabArt: cleared Image sprite on '{nm}' (matched='{k}')");
                    break;
                }
            }
        }

        var raws = root.GetComponentsInChildren<UnityEngine.UI.RawImage>(true);
        foreach (var ri in raws)
        {
            if (ri == null) continue;
            var nm = ri.gameObject.name ?? "";
            foreach (var k in artNames)
            {
                if (!string.IsNullOrEmpty(k) && nm.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ri.texture = null;
                    Debug.Log($"ClearPrefabArt: cleared RawImage texture on '{nm}' (matched='{k}')");
                    break;
                }
            }
        }
    }

    IEnumerator ApplyCardArtCoroutine(GameObject instance, int cardId)
    {
        if (instance == null) yield break;

        Sprite sprite = null;
        if (_cardArtCache.TryGetValue(cardId, out sprite) && sprite != null)
        {
            ApplySpriteToInstance(instance, sprite, cardId);
            yield break;
        }

        bool loaded = false;

        if (useAddressables)
        {
            string address = cardId.ToString();

            if (_addressableHandles.TryGetValue(address, out var existingHandle))
            {
                if (!existingHandle.IsDone)
                {
                    yield return existingHandle;
                }

                if (existingHandle.Status == AsyncOperationStatus.Succeeded && existingHandle.Result != null)
                {
                    sprite = existingHandle.Result;
                    _cardArtCache[cardId] = sprite;
                    ApplySpriteToInstance(instance, sprite, cardId);
                    yield break;
                }
                try { if (existingHandle.IsValid()) Addressables.Release(existingHandle); } catch { }
                _addressableHandles.Remove(address);
            }

            var handle = Addressables.LoadAssetAsync<Sprite>(address);
            _addressableHandles[address] = handle;
            yield return handle;

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                sprite = handle.Result;
                _cardArtCache[cardId] = sprite;
                loaded = true;
            }
            else
            {
                try { if (handle.IsValid()) Addressables.Release(handle); } catch { }
                _addressableHandles.Remove(address);
                Debug.Log($"ApplyCardArt: Addressables 未找到或加载失败 address='{address}' (id={cardId})");
            }
        }

        if (!loaded && useResourcesForCardArts)
        {
            try
            {
                var path = $"CardArts/{cardId}";
                var rs = Resources.Load<Sprite>(path);
                if (rs != null)
                {
                    sprite = rs;
                    _cardArtCache[cardId] = sprite;
                    loaded = true;
                }
                else
                {
                    Debug.Log($"ApplyCardArt: Resources 未找到 CardArts/{cardId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ApplyCardArt Resources.Load 异常: {ex.Message}");
            }
        }

        if (loaded && sprite != null)
        {
            ApplySpriteToInstance(instance, sprite, cardId);
        }
        else
        {
            Debug.LogWarning($"ApplyCardArt: 未找到卡图 id={cardId}（Addressables & Resources 都未命中）");
        }
    }

    void ApplySpriteToInstance(GameObject instance, Sprite sprite, int cardId)
    {
        if (instance == null || sprite == null) return;

        string[] artNames = new[] {
            "art", "artimage", "cardart", "artwork", "image_art", "spriteart",
            "art_img", "artImage", "Art",
            "卡图", "卡面", "卡牌图"
        };

        var images = instance.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        Image nameMatch = null;
        Image numericSpriteMatch = null;
        Image areaBest = null;

        Regex digitsRe = new Regex(@"\d+");
        float bestArea = -1f;

        foreach (var img in images)
        {
            if (img == null) continue;
            var nm = img.gameObject.name ?? "";

            foreach (var k in artNames)
            {
                if (!string.IsNullOrEmpty(k) && nm.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    nameMatch = img;
                    break;
                }
            }
            if (nameMatch != null) break;

            if (img.sprite != null)
            {
                var m = digitsRe.Match(img.sprite.name ?? "");
                if (m.Success)
                {
                    if (int.TryParse(m.Value, out int sid) && sid == cardId)
                    {
                        numericSpriteMatch = img;
                        break;
                    }
                    if (numericSpriteMatch == null) numericSpriteMatch = img;
                }
            }

            var rt = img.rectTransform;
            var area = Mathf.Abs(rt.rect.width * rt.rect.height);
            if (area > bestArea)
            {
                bestArea = area;
                areaBest = img;
            }
        }

        Image target = nameMatch ?? numericSpriteMatch ?? areaBest;

        if (target != null)
        {
            target.sprite = sprite;
            target.type = Image.Type.Simple;
            target.preserveAspect = true;
            Debug.Log($"ApplyCardArt: applied sprite id={cardId} to '{target.gameObject.name}' in instance '{instance.name}'");
            return;
        }

        var raws = instance.GetComponentsInChildren<UnityEngine.UI.RawImage>(true);
        UnityEngine.UI.RawImage rbest = null;
        foreach (var ri in raws)
        {
            if (ri == null) continue;
            var nm = ri.gameObject.name ?? "";
            foreach (var k in artNames)
            {
                if (!string.IsNullOrEmpty(k) && nm.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rbest = ri;
                    break;
                }
            }
            if (rbest != null) break;
        }
        if (rbest == null && raws.Length > 0) rbest = raws[0];

        if (rbest != null)
        {
            if (sprite.texture != null)
            {
                rbest.texture = sprite.texture;
                Debug.Log($"ApplyCardArt: applied raw texture id={cardId} to '{rbest.gameObject.name}'");
            }
            else
            {
                Debug.LogWarning($"ApplyCardArt: sprite.texture 为 null (id={cardId})");
            }
            return;
        }

        Debug.LogWarning($"ApplyCardArt: 未在实例中找到可设置的 Image/RawImage (id={cardId}, instance={instance.name})");
    }

    MonsterCard CloneMonsterCardForPlayer(MonsterCard def, int ownedCount)
    {
        var m = new MonsterCard(
            id: def.Card_ID,
            name: def.Card_Name,
            description: def.Card_Description,
            type: def.Card_Type,
            atk: def.Card_Atk,
            lv: def.Card_Lv,
            attributes: def.Card_Attributes,
            link: def.Card_Link,
            linkEffect: def.Card_LinkEffect,
            costs: def.Card_Costs,
            monsterType: def.MonsterType
        );
        m.StackCount = ownedCount;
        return m;
    }

    void ClearLibrary()
    {
        if (libraryPanel == null) return;
        for (int i = libraryPanel.childCount - 1; i >= 0; i--)
            Destroy(libraryPanel.GetChild(i).gameObject);
    }

    int TryParseInt(string s, int fallback)
    {
        if (int.TryParse((s ?? "").Trim(), out int v)) return v;
        return fallback;
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
        row.Add(field.ToString());
        if (row.Count > 1 || (row.Count == 1 && !string.IsNullOrEmpty(row[0])))
            records.Add(row);
        return records;
    }
}
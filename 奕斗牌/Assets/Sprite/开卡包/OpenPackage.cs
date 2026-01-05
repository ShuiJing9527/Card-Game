using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class OpenPackage : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject[] monsterPrefabs;
    public GameObject spellPrefab;

    [Header("References")]
    public CardDrawStore cardDrawStore;
    public CardTextStore textStore;
    public Transform cardParent;

    [Header("Card Image Loading (Addressables)")]
    public string addressPrefix = "";
    public Sprite placeholderSprite;

    [Header("Debug / Behavior")]
    public bool debugMode = false;
    public bool clearTextsBeforeFill = true;

    private List<GameObject> _spawnedCards = new List<GameObject>();
    private Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
    private Dictionary<string, AsyncOperationHandle<Sprite>> _loadingHandles = new Dictionary<string, AsyncOperationHandle<Sprite>>();

    void Awake()
    {
        if (cardDrawStore == null) cardDrawStore = FindObjectOfType<CardDrawStore>();
        if (textStore == null) textStore = FindObjectOfType<CardTextStore>();
    }

    public void OnClickOpen(int count = 5)
    {
        if (cardDrawStore == null)
        {
            Debug.LogError("OpenPackage: cardDrawStore 未设置");
            return;
        }

        ClearOldCards();

        Transform parent = cardParent != null ? cardParent : this.transform;

        for (int i = 0; i < count; i++)
        {
            var card = GetRandomCardFromStore();
            if (card == null) continue;

            if (card is MonsterCard monster)
                CreateMonsterCard(monster, parent);
            else if (card is SpellCard spell)
                CreateSpellCard(spell, parent);
            else
                Debug.LogWarning("OpenPackage: 未知卡片类型，跳过");
        }
    }

    CardMessage GetRandomCardFromStore()
    {
        if (cardDrawStore == null || cardDrawStore.cardList == null || cardDrawStore.cardList.Count == 0) return null;
        int idx = UnityEngine.Random.Range(0, cardDrawStore.cardList.Count);
        return cardDrawStore.cardList[idx];
    }

    void CreateMonsterCard(MonsterCard monster, Transform parent)
    {
        if (monster == null) return;

        GameObject prefab = GetMonsterPrefabByAttribute(monster.Card_Attributes, monster);
        if (prefab == null)
        {
            Debug.LogWarning($"OpenPackage: 未找到怪兽预制体 (attr={monster.Card_Attributes})");
            return;
        }

        GameObject go = Instantiate(prefab, parent);
        go.name = $"Monster_{monster.Card_ID}_{monster.Card_Name}";
        go.transform.localScale = Vector3.one;
        _spawnedCards.Add(go);

        var texts = textStore != null ? textStore.GetCardTexts(monster.Card_ID) : null;

        var displayComp = FindBestDisplayComponent(go, monster);
        bool used = TryCallSetCardOnBest(displayComp, go, monster, texts);

        if (!used)
        {
            if (debugMode) Debug.Log($"OpenPackage: 回退 FillCommonFields for monster id={monster.Card_ID}, name={monster.Card_Name}");
            if (clearTextsBeforeFill) ClearAllTextFields(go);
            FillCommonFields(go, monster.Card_Name, monster.Card_Attributes,
                monster.Card_Atk.ToString(), $"Lv.{monster.Card_Lv}", texts, isSpell: false);
        }

        SetCardImage(go, monster.Card_ID.ToString());

        if (debugMode) DumpAllTextFields(go, "After CreateMonsterCard");
    }

    void CreateSpellCard(SpellCard spell, Transform parent)
    {
        if (spell == null) return;
        if (spellPrefab == null)
        {
            Debug.LogWarning("OpenPackage: spellPrefab 未设置");
            return;
        }

        GameObject go = Instantiate(spellPrefab, parent);
        go.name = $"Spell_{spell.Card_ID}_{spell.Card_Name}";
        go.transform.localScale = Vector3.one;
        _spawnedCards.Add(go);

        var texts = textStore != null ? textStore.GetCardTexts(spell.Card_ID) : null;

        var displayComp = FindBestDisplayComponent(go, spell);
        bool used = TryCallSetCardOnBest(displayComp, go, spell, texts);

        if (!used)
        {
            if (debugMode) Debug.Log($"OpenPackage: 回退 FillCommonFields for spell id={spell.Card_ID}, name={spell.Card_Name}");
            if (clearTextsBeforeFill) ClearAllTextFields(go);
            string magic = texts != null ? texts.MagicDescription : spell.Card_Description;
            FillCommonFields(go, spell.Card_Name, null, null, null, texts, isSpell: true, magicOnly: true);
        }

        SetCardImage(go, spell.Card_ID.ToString());

        if (debugMode) DumpAllTextFields(go, "After CreateSpellCard");
    }

    MonoBehaviour FindBestDisplayComponent(GameObject go, CardMessage card)
    {
        if (go == null || card == null) return null;
        var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
        if (comps == null || comps.Length == 0) return null;

        MonoBehaviour best = null;
        int bestScore = int.MinValue;
        Type cardType = card.GetType();
        bool isSpell = card is SpellCard;

        foreach (var c in comps)
        {
            if (c == null) continue;
            int score = 0;
            var type = c.GetType();

            if (type.GetMethod("SetCard", new Type[] { cardType }) != null) score += 100;
            if (isSpell && type.GetMethod("SetCard", new Type[] { typeof(SpellCard), typeof(string) }) != null) score += 80;
            if (isSpell && type.GetMethod("SetCard", new Type[] { typeof(SpellCard) }) != null) score += 60;
            if (type.GetMethod("SetCard", new Type[] { typeof(CardMessage), typeof(CardTextStore.CardTexts) }) != null) score += 50;
            if (type.GetMethod("SetCard", new Type[] { typeof(CardMessage), typeof(string) }) != null) score += 40;
            if (type.GetMethod("SetCard", new Type[] { typeof(CardMessage) }) != null) score += 30;

            var any = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                          .FirstOrDefault(m => m.Name == "SetCard");
            if (any != null) score += 10;

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }

            if (debugMode && score > 0)
            {
                Debug.Log($"OpenPackage: Candidate component {type.Name} score={score} on GameObject '{c.gameObject.name}'");
            }
        }

        if (best != null && debugMode)
            Debug.Log($"OpenPackage: Selected display component {best.GetType().Name} on GameObject '{best.gameObject.name}' with score={bestScore}");

        return best;
    }

    bool TryCallSetCardOnBest(MonoBehaviour displayComponent, GameObject go, CardMessage card, CardTextStore.CardTexts texts)
    {
        if (card == null) return false;

        List<MonoBehaviour> tryList = new List<MonoBehaviour>();
        if (displayComponent != null) tryList.Add(displayComponent);
        if (displayComponent == null && go != null)
        {
            tryList.AddRange(go.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null));
        }

        foreach (var comp in tryList)
        {
            if (comp == null) continue;
            try
            {
                bool invoked = TryInvokeSetCard(comp, card, texts);
                if (invoked) return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"OpenPackage: 调用组件 {comp.GetType().Name} SetCard 时发生异常: {ex}");
            }
        }

        return false;
    }

    // 更稳健的 SetCard 调用：枚举所有 SetCard 重载，按兼容性优先匹配(2参 string优先 -> 单参 -> 回退)
    bool TryInvokeSetCard(MonoBehaviour comp, object cardObj, CardTextStore.CardTexts texts)
    {
        var compType = comp.GetType();
        var methods = compType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                              .Where(m => m.Name == "SetCard").ToArray();

        if (methods.Length == 0)
        {
            if (debugMode) Debug.Log($"OpenPackage: Component {compType.Name} has no SetCard methods.");
            return false;
        }

        if (debugMode)
        {
            Debug.Log($"OpenPackage: Component {compType.Name} SetCard overloads:");
            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                Debug.Log($"  {m.Name}({string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name))})");
            }
        }

        Type cardType = cardObj.GetType();

        // 1) 优先：找两个参数且第二个参数可接受 string，第一个参数能接受 cardObj 的重载
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length == 2)
            {
                var p0 = ps[0].ParameterType;
                var p1 = ps[1].ParameterType;
                bool p0Match = p0.IsAssignableFrom(cardType) || cardType.IsAssignableFrom(p0);
                bool p1IsString = p1 == typeof(string) || p1.IsAssignableFrom(typeof(string));
                if (p0Match && p1IsString)
                {
                    string arg = null;
                    if (cardObj is SpellCard)
                    {
                        arg = texts != null ? texts.StackDescription : null;
                        if (string.IsNullOrEmpty(arg) && texts != null) arg = texts.MagicDescription;
                    }
                    else
                    {
                        arg = texts != null ? texts.MainDescription : null;
                    }

                    try
                    {
                        m.Invoke(comp, new object[] { cardObj, arg });
                        if (debugMode) Debug.Log($"OpenPackage: Invoked {compType.Name}.{m.Name}(two-param) on '{comp.gameObject.name}' with arg='{TruncateDebug(arg)}'");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"OpenPackage: Exception invoking {m} : {ex}");
                    }
                }
            }
        }

        // 2) 次优：单参数且参数类型可接受 cardObj
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            if (ps.Length == 1)
            {
                var p0 = ps[0].ParameterType;
                if (p0.IsAssignableFrom(cardType) || cardType.IsAssignableFrom(p0) || p0 == typeof(CardMessage))
                {
                    try
                    {
                        m.Invoke(comp, new object[] { cardObj });
                        if (debugMode) Debug.Log($"OpenPackage: Invoked {compType.Name}.{m.Name}(one-param) on '{comp.gameObject.name}'");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"OpenPackage: Exception invoking {m} : {ex}");
                    }
                }
            }
        }

        // 3) 回退：尝试任意 SetCard（先传 card,texts 然后 card）
        foreach (var m in methods)
        {
            var ps = m.GetParameters();
            try
            {
                if (ps.Length == 2)
                {
                    m.Invoke(comp, new object[] { cardObj, texts });
                    if (debugMode) Debug.Log($"OpenPackage: Invoked fallback {compType.Name}.{m.Name}(card,texts) on '{comp.gameObject.name}'");
                    return true;
                }
                else if (ps.Length == 1)
                {
                    m.Invoke(comp, new object[] { cardObj });
                    if (debugMode) Debug.Log($"OpenPackage: Invoked fallback {compType.Name}.{m.Name}(card) on '{comp.gameObject.name}'");
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    void FillCommonFields(GameObject cardObj, string name, string attr, string atk, string lv,
    CardTextStore.CardTexts texts, bool isSpell = false, bool magicOnly = false)
    {
        if (cardObj == null) return;

        string mainText = isSpell ? (texts != null ? texts.MagicDescription : "") : (texts != null ? texts.MainDescription : "");

        var tmpName = FindInChildren<TextMeshProUGUI>(cardObj.transform, "cardNameText") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "Name") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "卡名");
        if (tmpName != null) tmpName.text = name ?? "";

        var uiName = FindInChildren<Text>(cardObj.transform, "Name") ?? FindInChildren<Text>(cardObj.transform, "卡名");
        if (uiName != null) uiName.text = name ?? "";

        if (!magicOnly)
        {
            var tmpAttr = FindInChildren<TextMeshProUGUI>(cardObj.transform, "attributesText") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "Attr") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "属性");
            if (tmpAttr != null) tmpAttr.text = attr ?? "";

            var tmpAtk = FindInChildren<TextMeshProUGUI>(cardObj.transform, "attackText") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "Atk") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "战力");
            if (tmpAtk != null) tmpAtk.text = atk ?? "";

            var tmpLv = FindInChildren<TextMeshProUGUI>(cardObj.transform, "levelText") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "Lv") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "等级");
            if (tmpLv != null) tmpLv.text = lv ?? "";
        }

        var tmpMain = FindInChildren<TextMeshProUGUI>(cardObj.transform, "mainDescriptionText") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "Description") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "效果");
        if (tmpMain != null) tmpMain.text = mainText ?? "";

        var uiMain = FindInChildren<Text>(cardObj.transform, "Description");
        if (uiMain != null) uiMain.text = mainText ?? "";

        var tmpLink = FindInChildren<TextMeshProUGUI>(cardObj.transform, "linkDescriptionText") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "Link") ?? FindInChildren<TextMeshProUGUI>(cardObj.transform, "羁绊描述");
        if (tmpLink != null) tmpLink.text = (texts != null ? texts.LinkDescription ?? "" : "");

        string stackText = (texts != null && !string.IsNullOrEmpty(texts.StackDescription)) ? texts.StackDescription : "";

        string[] tmpCandidates = new string[] {
            "stackDescriptionText", "stackEffect", "叠放效果", "StackText", "stackText",
            "StackDescription", "Stack", "叠放"
        };

        foreach (var nameCandidate in tmpCandidates)
        {
            var tmp = FindInChildren<TextMeshProUGUI>(cardObj.transform, nameCandidate);
            if (tmp != null)
            {
                tmp.text = stackText;
                if (debugMode) Debug.Log($"OpenPackage: Filled TMP '{nameCandidate}' with stackText (len={stackText?.Length ?? 0}) on '{cardObj.name}'");
            }
        }

        string[] uiCandidates = new string[] { "Stack", "叠放", "stackDescriptionText", "叠放效果", "stackEffect", "StackText", "stackText" };
        foreach (var nameCandidate in uiCandidates)
        {
            var ui = FindInChildren<Text>(cardObj.transform, nameCandidate);
            if (ui != null)
            {
                ui.text = stackText;
                if (debugMode) Debug.Log($"OpenPackage: Filled UI Text '{nameCandidate}' with stackText (len={stackText?.Length ?? 0}) on '{cardObj.name}'");
            }
        }
    }

    void ClearAllTextFields(GameObject go)
    {
        if (go == null) return;
        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in tmps) t.text = "";
        var uis = go.GetComponentsInChildren<Text>(true);
        foreach (var t in uis) t.text = "";
    }

    void DumpAllTextFields(GameObject go, string prefix = "")
    {
        if (!debugMode || go == null) return;
        var tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
        var uis = go.GetComponentsInChildren<Text>(true);
        Debug.Log($"[DumpAllTextFields] {prefix} on '{go.name}': TMP count={tmps.Length}, UI Text count={uis.Length}");
        foreach (var t in tmps) Debug.Log($"  TMP '{GetHierarchyPath(t.transform)}' text='{t.text}'");
        foreach (var t in uis) Debug.Log($"  UI  '{GetHierarchyPath(t.transform)}' text='{t.text}'");
    }

    string GetHierarchyPath(Transform t)
    {
        if (t == null) return "<null>";
        var parts = new List<string>();
        var cur = t;
        while (cur != null)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    T FindInChildren<T>(Transform parent, string name) where T : Component
    {
        if (parent == null) return null;
        var t = parent.Find(name);
        if (t != null) return t.GetComponent<T>();
        foreach (Transform c in parent)
        {
            var res = FindInChildren<T>(c, name);
            if (res != null) return res;
        }
        return null;
    }

    void SetCardImage(GameObject cardObj, string cardId)
    {
        if (cardObj == null) return;

        Image image = FindInChildren<Image>(cardObj.transform, "CardImage") ?? FindInChildren<Image>(cardObj.transform, "卡图");
        if (image == null) return;

        if (placeholderSprite != null) image.sprite = placeholderSprite;

        if (string.IsNullOrEmpty(cardId)) return;
        if (_spriteCache.TryGetValue(cardId, out var cached))
        {
            image.sprite = cached;
            return;
        }

        StartCoroutine(LoadSpriteAsync(cardId, image));
    }

    IEnumerator LoadSpriteAsync(string cardId, Image target)
    {
        string address = addressPrefix + cardId;
        if (_loadingHandles.TryGetValue(address, out var existing))
        {
            while (!existing.IsDone) yield return null;
            if (existing.Status == AsyncOperationStatus.Succeeded && existing.Result != null)
            {
                _spriteCache[cardId] = existing.Result;
                if (target != null) target.sprite = existing.Result;
            }
            yield break;
        }

        var handle = Addressables.LoadAssetAsync<Sprite>(address);
        _loadingHandles[address] = handle;
        yield return handle;

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            _spriteCache[cardId] = handle.Result;
            if (target != null) target.sprite = handle.Result;
        }
        else
        {
            Debug.LogWarning($"OpenPackage: 卡图加载失败 address={address}");
        }

        _loadingHandles.Remove(address);
    }

    GameObject GetMonsterPrefabByAttribute(string attr, MonsterCard card)
    {
        if (monsterPrefabs == null || monsterPrefabs.Length == 0) return null;

        if (string.IsNullOrEmpty(attr))
            return monsterPrefabs[Math.Abs(card != null ? card.Card_ID : UnityEngine.Random.Range(0, 1000)) % monsterPrefabs.Length];

        switch (attr)
        {
            case "土": return monsterPrefabs.Length > 0 ? monsterPrefabs[0] : null;
            case "木": return monsterPrefabs.Length > 1 ? monsterPrefabs[1] : monsterPrefabs[0];
            case "水": return monsterPrefabs.Length > 2 ? monsterPrefabs[2] : monsterPrefabs[0];
            case "火": return monsterPrefabs.Length > 3 ? monsterPrefabs[3] : monsterPrefabs[0];
            case "金": return monsterPrefabs.Length > 4 ? monsterPrefabs[4] : monsterPrefabs[0];
            default: return monsterPrefabs[Math.Abs(card != null ? card.Card_ID : UnityEngine.Random.Range(0, 1000)) % monsterPrefabs.Length];
        }
    }

    void ClearOldCards()
    {
        foreach (var g in _spawnedCards)
            if (g != null) Destroy(g);
        _spawnedCards.Clear();
    }

    void OnDestroy()
    {
        foreach (var kv in _loadingHandles)
            if (kv.Value.IsValid()) Addressables.Release(kv.Value);
        _loadingHandles.Clear();
        _spriteCache.Clear();
    }

    string TruncateDebug(string s, int len = 80)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= len ? s : s.Substring(0, len) + "...";
    }
}
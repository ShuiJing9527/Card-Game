using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DeckManager : MonoBehaviour, ICardDropTarget
{
    [Header("Source / UI")]
    public LibraryManager librarySource;
    public RectTransform deckPanel;
    public GameObject deckEntryPrefab;

    [Header("Fallback CSV (if no PlayerDataManager)")]
    public TextAsset playerDataCsv;

    [Header("Runtime Options")]
    public bool clearOnStart = true;

    public bool hideCardInfoInDeck = true;

    private CardStore _cardStore;
    private OpenPackage _openPackage;
    private PlayerDataManager _playerDataManager;

    // 新增：编辑缓冲区和编辑状态标志（isEditing 为私有）
    private Dictionary<int, int> editingDeck = new Dictionary<int, int>();
    private bool isEditing = false;

    // 公开只读属性，供外部（例如 DeckDropTarget）查询编辑状态
    public bool IsEditing => isEditing;

    private MethodInfo GetCardStoreInstantiateMethod(string name)
    {
        if (_cardStore == null) return null;
        try { return _cardStore.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance); }
        catch { return null; }
    }

    void OnValidate()
    {
        if (librarySource == null) librarySource = FindObjectOfType<LibraryManager>();
        if (_cardStore == null)
        {
            if (CardStore.Instance != null) _cardStore = CardStore.Instance;
            else
            {
                var cs = FindObjectOfType<CardStore>();
                if (cs != null) _cardStore = cs;
            }
        }

        if (_open_package_missing_check()) { }
        if (_playerDataManager == null)
        {
            var pd = FindObjectOfType<PlayerDataManager>();
            if (pd != null) _playerDataManager = pd;
        }
    }

    private bool _open_package_missing_check()
    {
        if (_openPackage == null && _cardStore != null)
            BindOpenPackageFromCardStore();

        if (_openPackage == null)
        {
            var op2 = FindObjectOfType<OpenPackage>();
            if (op2 != null) _openPackage = op2;
        }
        return true;
    }

    void Reset() { OnValidate(); }

    private void BindOpenPackageFromCardStore()
    {
        try { _openPackage = _cardStore.GetComponent<OpenPackage>(); } catch { _openPackage = null; }
    }

    void Start()
    {
        if (deckPanel == null) return;

        if (librarySource == null) librarySource = FindObjectOfType<LibraryManager>();
        if (_cardStore == null && CardStore.Instance != null) _cardStore = CardStore.Instance;
        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;

        if (_openPackage == null && _cardStore != null) BindOpenPackageFromCardStore();
        if (_openPackage == null) _openPackage = FindObjectOfType<OpenPackage>();

        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;
        if (_playerDataManager != null)
        {
            try { _playerDataManager.OnDeckChanged += OnPlayerDeckChanged; } catch { }
            try { _playerDataManager.OnPlayerDataLoaded += OnPlayerDataLoaded; } catch { }
        }

        if (clearOnStart) ClearDeckPanel();

        StartCoroutine(WaitThenBuild());
    }

    private void OnDestroy()
    {
        if (_playerDataManager != null)
        {
            try { _playerDataManager.OnDeckChanged -= OnPlayerDeckChanged; } catch { }
            try { _playerDataManager.OnPlayerDataLoaded -= OnPlayerDataLoaded; } catch { }
        }
    }

    IEnumerator WaitThenBuild()
    {
        float timeout = 3f;
        float t = 0f;
        bool readyFlag = false;
        Action onReadyHandler = null;

        Func<CardStore, bool> isReady = (cs) =>
        {
            if (cs == null) return true;
            try
            {
                var prop = cs.GetType().GetProperty("IsCardsReady");
                if (prop != null)
                {
                    var v = prop.GetValue(cs);
                    if (v is bool b) return b;
                }
                var field = cs.GetType().GetField("cardList", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    var v = field.GetValue(cs) as System.Collections.IEnumerable;
                    if (v != null)
                    {
                        foreach (var _ in v) return true;
                        return false;
                    }
                }
            }
            catch { }
            return true;
        };

        if (_cardStore != null && !isReady(_cardStore))
        {
            try
            {
                onReadyHandler = new Action(() => { readyFlag = true; });
                _cardStore.OnCardsReady += onReadyHandler;
            }
            catch { onReadyHandler = null; }
        }
        else readyFlag = true;

        while (!readyFlag && t < timeout)
        {
            if (_cardStore == null && CardStore.Instance != null)
            {
                _cardStore = CardStore.Instance;
                if (_openPackage == null) BindOpenPackageFromCardStore();
            }
            if (_cardStore != null && isReady(_cardStore))
            {
                readyFlag = true;
                break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (onReadyHandler != null && _cardStore != null)
        {
            try { _cardStore.OnCardsReady -= onReadyHandler; } catch { }
        }

        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;

        BuildDeckFromPlayerData();
        yield break;
    }

    private void OnPlayerDataLoaded()
    {
        // 如果正在编辑则忽略外部数据加载（避免中断编辑）
        if (isEditing) return;
        ClearDeckPanel();
        BuildDeckFromPlayerData();
    }

    private void OnPlayerDeckChanged(int cardId, int newCount)
    {
        if (isEditing)
        {
            // 正在编辑时忽略 PlayerDataManager 发来的变更，防止界面被重建
            return;
        }
        ClearDeckPanel();
        BuildDeckFromPlayerData();
    }

    void BuildDeckFromPlayerData()
    {
        var deckCounts = new Dictionary<int, int>();

        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;
        if (_playerDataManager != null)
        {
            try
            {
                if (_playerDataManager.playerDeckDict != null && _playerDataManager.playerDeckDict.Count > 0)
                {
                    foreach (var kv in _playerDataManager.playerDeckDict)
                        if (kv.Value > 0) deckCounts[kv.Key] = kv.Value;
                }
                else
                {
                    if (_playerDataManager.playerDeck != null && _playerDataManager.playerDeck.Length > 0)
                    {
                        for (int i = 0; i < _playerDataManager.playerDeck.Length; i++)
                            if (_playerDataManager.playerDeck[i] > 0) deckCounts[i] = _playerDataManager.playerDeck[i];
                    }
                }
            }
            catch { }
        }

        if (deckCounts.Count == 0)
        {
            if (_playerDataManager != null)
            {
                try
                {
                    var pd = _playerDataManager;
                    var prop = pd.GetType().GetProperty("playerDeckDict") ?? pd.GetType().GetProperty("PlayerDeckDict");
                    if (prop != null)
                    {
                        var dictObj = prop.GetValue(pd) as System.Collections.IDictionary;
                        if (dictObj != null)
                        {
                            foreach (var k in dictObj.Keys)
                            {
                                int id = Convert.ToInt32(k);
                                int cnt = Convert.ToInt32(dictObj[k]);
                                if (cnt > 0) deckCounts[id] = cnt;
                            }
                        }
                    }

                    if (deckCounts.Count == 0)
                    {
                        var f = pd.GetType().GetField("playerDeck") ?? pd.GetType().GetField("PlayerDeck");
                        if (f != null)
                        {
                            var arr = f.GetValue(pd) as int[];
                            if (arr != null)
                            {
                                for (int i = 0; i < arr.Length; i++)
                                    if (arr[i] > 0) deckCounts[i] = arr[i];
                            }
                        }
                    }

                    if (deckCounts.Count == 0)
                    {
                        var m = pd.GetType().GetMethod("GetPlayerDeckCounts");
                        if (m != null)
                        {
                            var res = m.Invoke(pd, null) as System.Collections.IDictionary;
                            if (res != null)
                            {
                                foreach (var k in res.Keys)
                                {
                                    int id = Convert.ToInt32(k);
                                    int cnt = Convert.ToInt32(res[k]);
                                    if (cnt > 0) deckCounts[id] = cnt;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        if (deckCounts.Count == 0 && playerDataCsv != null)
        {
            deckCounts = ParsePlayerDataCsv(playerDataCsv.text);
        }

        if (deckCounts.Count == 0) return;

        int created = 0;
        foreach (var kv in deckCounts)
        {
            int cardId = kv.Key;
            int cnt = kv.Value;
            if (cnt <= 0) continue;

            CardMessage def = null;
            if (_cardStore != null)
            {
                try { def = _cardStore.GetCardById(cardId); } catch { def = null; }
            }

            if (def == null) continue;

            for (int i = 0; i < cnt; i++)
            {
                if (TryInstantiateCard(def, 1, cardId)) created++;
            }
        }
    }

    bool TryInstantiateCard(CardMessage def, int count, int cardId)
    {
        if (def == null) return false;

        Transform parent = deckPanel;
        GameObject wrapper = null;
        if (deckEntryPrefab != null)
        {
            wrapper = Instantiate(deckEntryPrefab, deckPanel, false);
            parent = wrapper.transform;
        }

        if (_openPackage != null)
        {
            try
            {
                var go = _openPackage.InstantiateCardItem(def, parent, count, true);
                if (go != null)
                {
                    if (wrapper != null && go.transform.parent != parent)
                        go.transform.SetParent(parent, false);

                    PostProcessDeckInstance(go, cardId);
                    return true;
                }
            }
            catch (MissingMethodException) { }
            catch { }

            try
            {
                var mi = _open_package_reflection_method();
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    object[] args = BuildArgsForMethod(ps, def, parent, count, true);
                    var res = mi.Invoke(_openPackage, args) as GameObject;
                    if (res != null)
                    {
                        if (wrapper != null && res.transform.parent != parent)
                            res.transform.SetParent(parent, false);

                        PostProcessDeckInstance(res, cardId);
                        return true;
                    }
                }
            }
            catch { }
        }

        if (_cardStore != null)
        {
            try
            {
                MethodInfo mi = GetCardStoreInstantiateMethod("InstantiateCardItem");
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    object[] args = BuildArgsForMethod(ps, def, parent, count, true);
                    var res = mi.Invoke(_cardStore, args) as GameObject;
                    if (res != null)
                    {
                        if (wrapper != null && res.transform.parent != parent)
                            res.transform.SetParent(parent, false);

                        PostProcessDeckInstance(res, cardId);
                        return true;
                    }
                }
            }
            catch { }
        }

        try
        {
            var opType = _open_package_reflection_type();
            if (opType != null)
            {
                var monsterField = opType.GetField("monsterPrefabs", BindingFlags.Public | BindingFlags.Instance);
                var spellField = opType.GetField("spellPrefab", BindingFlags.Public | BindingFlags.Instance);
                if (monsterField != null && def is MonsterCard)
                {
                    var listObj = monsterField.GetValue(_openPackage) as IList;
                    GameObject prefab = null;
                    if (listObj != null && listObj.Count > 0)
                        prefab = listObj[Math.Abs(def.Card_ID) % listObj.Count] as GameObject;
                    if (prefab != null)
                    {
                        var go = Instantiate(prefab, parent, false);
                        PostProcessDeckInstance(go, cardId);
                        return true;
                    }
                }
                else if (spellField != null && def is SpellCard)
                {
                    var prefab = spellField.GetValue(_openPackage) as GameObject;
                    if (prefab != null)
                    {
                        var go = Instantiate(prefab, parent, false);
                        PostProcessDeckInstance(go, cardId);
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    // New: instantiate and return the created GameObject (or null)
    GameObject InstantiateDeckEntry(CardMessage def, int count, int cardId, int siblingIndex = -1)
    {
        if (def == null) return null;

        Transform parent = deckPanel;
        GameObject wrapper = null;
        if (deckEntryPrefab != null)
        {
            wrapper = Instantiate(deckEntryPrefab, deckPanel, false);
            parent = wrapper.transform;
        }

        GameObject created = null;

        if (_openPackage != null)
        {
            try
            {
                var go = _openPackage.InstantiateCardItem(def, parent, count, true);
                if (go != null)
                {
                    if (wrapper != null && go.transform.parent != parent)
                        go.transform.SetParent(parent, false);

                    created = go;
                }
            }
            catch { }
            if (created == null)
            {
                try
                {
                    var mi = _open_package_reflection_method();
                    if (mi != null)
                    {
                        var ps = mi.GetParameters();
                        object[] args = BuildArgsForMethod(ps, def, parent, count, true);
                        var res = mi.Invoke(_openPackage, args) as GameObject;
                        if (res != null)
                        {
                            if (wrapper != null && res.transform.parent != parent)
                                res.transform.SetParent(parent, false);
                            created = res;
                        }
                    }
                }
                catch { }
            }
        }

        if (created == null && _cardStore != null)
        {
            try
            {
                MethodInfo mi = GetCardStoreInstantiateMethod("InstantiateCardItem");
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    object[] args = BuildArgsForMethod(ps, def, parent, count, true);
                    var res = mi.Invoke(_cardStore, args) as GameObject;
                    if (res != null)
                    {
                        if (wrapper != null && res.transform.parent != parent)
                            res.transform.SetParent(parent, false);
                        created = res;
                    }
                }
            }
            catch { }
        }

        if (created == null)
        {
            try
            {
                var opType = _open_package_reflection_type();
                if (opType != null)
                {
                    var monsterField = opType.GetField("monsterPrefabs", BindingFlags.Public | BindingFlags.Instance);
                    var spellField = opType.GetField("spellPrefab", BindingFlags.Public | BindingFlags.Instance);
                    if (monsterField != null && def is MonsterCard)
                    {
                        var listObj = monsterField.GetValue(_openPackage) as IList;
                        GameObject prefab = null;
                        if (listObj != null && listObj.Count > 0)
                            prefab = listObj[Math.Abs(def.Card_ID) % listObj.Count] as GameObject;
                        if (prefab != null)
                        {
                            var go = Instantiate(prefab, parent, false);
                            created = go;
                        }
                    }
                    else if (spellField != null && def is SpellCard)
                    {
                        var prefab = spellField.GetValue(_openPackage) as GameObject;
                        if (prefab != null)
                        {
                            var go = Instantiate(prefab, parent, false);
                            created = go;
                        }
                    }
                }
            }
            catch { }
        }

        if (created != null)
        {
            try { PostProcessDeckInstance(created, cardId); } catch { }
            // place wrapper or created at sibling index
            if (siblingIndex >= 0)
            {
                // if wrapper exists, set wrapper's sibling; else set created's sibling
                if (wrapper != null)
                {
                    wrapper.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, deckPanel.childCount - 1));
                }
                else
                {
                    created.transform.SetParent(deckPanel, true);
                    created.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, deckPanel.childCount - 1));
                }
            }
            return created;
        }

        return null;
    }

    private MethodInfo _open_package_reflection_method()
    {
        try { return _openPackage?.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance); } catch { return null; }
    }
    private Type _open_package_reflection_type()
    {
        try { return _openPackage?.GetType(); } catch { return null; }
    }

    void PostProcessDeckInstance(GameObject go, int cardId)
    {
        if (go == null) return;

        try { librarySource?.ClearPrefabArtPublic(go); } catch { }

        try { librarySource?.ApplyCardArtToInstance(go, cardId, true); } catch { }

        string stackDesc = GetStackDescriptionForCard(cardId);
        if (string.IsNullOrEmpty(stackDesc)) stackDesc = "叠放数: 1";

        bool forced = false;
        try
        {
            object defObj = null;
            try { defObj = _cardStore != null ? _cardStore.GetCardById(cardId) : null; } catch { defObj = null; }

            forced = ForceApplyStackToDisplayComponents(go, defObj, stackDesc);
        }
        catch { }

        try
        {
            if (!forced)
            {
                if (!InstanceHasExistingStackText(go))
                {
                    ApplyStackDescriptionToInstance(go, cardId, stackDesc);
                }
            }
        }
        catch { }

        if (hideCardInfoInDeck)
        {
            try
            {
                var counters = go.GetComponentsInChildren<CardCounter>(true);
                foreach (var c in counters)
                {
                    if (c == null || c.gameObject == null) continue;
                    var nm = c.gameObject.name.ToLowerInvariant();
                    if (nm.Contains("stack") || nm.Contains("叠放") || nm.Contains("数量")) continue;
                    c.gameObject.SetActive(false);
                }
            }
            catch { }
        }
    }

    string GetStackDescriptionForCard(int cardId)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var ctsType = asm.GetType("CardTextStore") ?? asm.GetType("CardTexts") ?? asm.GetType("CardTextManager");
                if (ctsType == null) continue;

                UnityEngine.Object instance = null;
                try { instance = FindObjectOfType(ctsType); } catch { instance = null; }

                if (instance != null)
                {
                    var mi = ctsType.GetMethod("GetStackDescription", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                          ?? ctsType.GetMethod("GetStack", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                          ?? ctsType.GetMethod("GetCardText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                          ?? ctsType.GetMethod("GetText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                          ?? ctsType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (mi != null)
                    {
                        try
                        {
                            var res = mi.Invoke(instance, new object[] { cardId });
                            if (res is string s && !string.IsNullOrEmpty(s)) return s;
                        }
                        catch { }
                    }

                    try
                    {
                        var prop = ctsType.GetProperty("cardTexts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                ?? ctsType.GetProperty("texts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (prop != null)
                        {
                            var dict = prop.GetValue(instance) as System.Collections.IDictionary;
                            if (dict != null && dict.Contains(cardId))
                            {
                                var val = dict[cardId] as string;
                                if (!string.IsNullOrEmpty(val)) return val;
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        try
        {
            if (_cardStore != null)
            {
                var def = _cardStore.GetCardById(cardId);
                if (def != null)
                {
                    var t = def.GetType();
                    string[] candidateNames = new[] {
                "StackDescription","stackDescription","StackDesc","stackDesc",
                "StackText","stackText","Stack","stack","StackInfo","stackInfo",
                "PileDescription","PileDesc","Description","desc"
            };
                    foreach (var n in candidateNames)
                    {
                        try
                        {
                            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            if (p != null && p.PropertyType == typeof(string))
                            {
                                var v = p.GetValue(def) as string;
                                if (!string.IsNullOrEmpty(v)) return v;
                            }
                        }
                        catch { }
                        try
                        {
                            var f = t.GetField(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            if (f != null && f.FieldType == typeof(string))
                            {
                                var v2 = f.GetValue(def) as string;
                                if (!string.IsNullOrEmpty(v2)) return v2;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    bool ForceApplyStackToDisplayComponents(GameObject go, object defObj, string stackDesc)
    {
        if (go == null) return false;
        bool applied = false;

        var monos = go.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var m in monos)
        {
            if (m == null) continue;
            var t = m.GetType();
            var tn = t.Name.ToLowerInvariant();

            if (tn.Contains("spellcarddisplay") || tn.Contains("monstercarddisplay") || tn.Contains("carddisplay") || tn.Contains("cardinfodisplay"))
            {
                try
                {
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo setCardWithString = null;
                    MethodInfo setCardSingle = null;
                    foreach (var mm in methods)
                    {
                        if (string.Equals(mm.Name, "SetCard", StringComparison.OrdinalIgnoreCase))
                        {
                            var ps = mm.GetParameters();
                            if (ps.Length == 2 && (ps[1].ParameterType == typeof(string) || ps[1].ParameterType == typeof(object)))
                            {
                                setCardWithString = mm; break;
                            }
                            if (ps.Length == 1) setCardSingle = mm;
                        }
                    }
                    if (setCardWithString != null)
                    {
                        var ps = setCardWithString.GetParameters();
                        object p0 = null;
                        if (defObj != null && ps.Length >= 1 && ps[0].ParameterType.IsAssignableFrom(defObj.GetType()))
                            p0 = defObj;
                        else if (ps.Length >= 1 && ps[0].ParameterType == typeof(int))
                        {
                            try
                            {
                                var idProp = defObj?.GetType().GetProperty("Card_ID") ?? defObj?.GetType().GetProperty("CardId");
                                if (idProp != null) p0 = idProp.GetValue(defObj);
                            }
                            catch { p0 = null; }
                        }
                        try
                        {
                            setCardWithString.Invoke(m, new object[] { p0, stackDesc });
                            applied = true;
                            if (applied) return true;
                        }
                        catch { }
                    }

                    var setStackMethod = t.GetMethod("SetStackDescription", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                       ?? t.GetMethod("SetStackText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                       ?? t.GetMethod("SetStack", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                       ?? t.GetMethod("SetCount", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                                       ?? t.GetMethod("SetText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (setStackMethod != null)
                    {
                        var ps2 = setStackMethod.GetParameters();
                        if (ps2.Length == 1 && ps2[0].ParameterType == typeof(string))
                        {
                            try
                            {
                                setStackMethod.Invoke(m, new object[] { stackDesc });
                                applied = true;
                                if (applied) return true;
                            }
                            catch { }
                        }
                    }

                    string[] fieldCandidates = new[] { "stackText", "countText", "text", "stack", "count", "stackDesc", "stackDescription" };
                    foreach (var fn in fieldCandidates)
                    {
                        try
                        {
                            var fld = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            if (fld != null)
                            {
                                var val = fld.GetValue(m);
                                if (val is Text uiText)
                                {
                                    uiText.text = stackDesc; return true;
                                }
                                var tmpType = GetTMPTextType();
                                if (tmpType != null && val != null && tmpType.IsAssignableFrom(val.GetType()))
                                {
                                    var textProp = tmpType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                                    if (textProp != null)
                                    {
                                        textProp.SetValue(val, stackDesc);
                                        applied = true; return true;
                                    }
                                }
                                if (fld.FieldType == typeof(string))
                                {
                                    fld.SetValue(m, stackDesc);
                                    applied = true; return true;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            try
            {
                var genericSet = t.GetMethod("SetStackDescription", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                              ?? t.GetMethod("SetStack", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                              ?? t.GetMethod("SetStackText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                              ?? t.GetMethod("SetText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (genericSet != null)
                {
                    var ps = genericSet.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        try
                        {
                            genericSet.Invoke(m, new object[] { stackDesc });
                            applied = true; return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        try
        {
            var texts = go.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                var nm = t.gameObject.name.ToLower();
                if (nm.Contains("stack") || nm.Contains("count") || nm.Contains("叠放") || nm.Contains("数量"))
                {
                    t.text = stackDesc; return true;
                }
            }

            var tmpType = GetTMPTextType();
            if (tmpType != null)
            {
                var comps = go.GetComponentsInChildren<Component>(true);
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    if (tmpType.IsAssignableFrom(ct))
                    {
                        var nm = comp.gameObject.name.ToLowerInvariant();
                        if (nm.Contains("stack") || nm.Contains("count") || nm.Contains("叠放") || nm.Contains("数量"))
                        {
                            var prop = tmpType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                            if (prop != null)
                            {
                                prop.SetValue(comp, stackDesc);
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return applied;
    }

    Type GetTMPTextType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("TMPro.TMP_Text");
            if (t != null) return t;
        }
        return null;
    }

    bool InstanceHasExistingStackText(GameObject go)
    {
        if (go == null) return false;

        try
        {
            var texts = go.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                var nm = t.gameObject.name.ToLowerInvariant();
                if (nm.Contains("stack") || nm.Contains("count") || nm.Contains("叠放") || nm.Contains("数量"))
                {
                    if (!string.IsNullOrWhiteSpace(t.text)) return true;
                }
            }
            foreach (var t in texts)
            {
                if (t == null) continue;
                if (!string.IsNullOrWhiteSpace(t.text)) return true;
            }
        }
        catch { }

        try
        {
            Type tmpTextType = GetTMPTextType();
            PropertyInfo tmpProp = null;
            if (tmpTextType != null) tmpProp = tmpTextType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);

            if (tmpTextType != null && tmpProp != null)
            {
                var comps = go.GetComponentsInChildren<Component>(true);
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    if (tmpTextType.IsAssignableFrom(ct))
                    {
                        var nm = comp.gameObject.name.ToLowerInvariant();
                        var val = tmpProp.GetValue(comp) as string;
                        if (nm.Contains("stack") || nm.Contains("count") || nm.Contains("叠放") || nm.Contains("数量"))
                        {
                            if (!string.IsNullOrWhiteSpace(val)) return true;
                        }
                    }
                }
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    if (tmpTextType.IsAssignableFrom(ct))
                    {
                        var val = tmpProp.GetValue(comp) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    bool ApplyStackDescriptionToInstance(GameObject go, int cardId, string stackDesc)
    {
        if (go == null || string.IsNullOrEmpty(stackDesc)) return false;
        bool written = false;

        Type tmpTextType = GetTMPTextType();
        PropertyInfo tmpTextProp = null;
        if (tmpTextType != null) tmpTextProp = tmpTextType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);

        var monos = go.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var m in monos)
        {
            if (m == null) continue;
            var t = m.GetType();

            string[] fieldCandidates = new[] { "stackText", "countText", "text", "stack", "count", "stackDesc", "stackDescription" };
            foreach (var fn in fieldCandidates)
            {
                try
                {
                    var fld = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (fld != null)
                    {
                        var val = fld.GetValue(m);
                        if (val is Text uiText)
                        {
                            if (string.IsNullOrWhiteSpace(uiText.text)) uiText.text = stackDesc;
                            written = true; break;
                        }
                        if (val != null && tmpTextType != null && tmpTextType.IsAssignableFrom(val.GetType()))
                        {
                            try
                            {
                                var cur = tmpTextProp?.GetValue(val) as string;
                                if (string.IsNullOrWhiteSpace(cur)) tmpTextProp?.SetValue(val, stackDesc);
                                written = true; break;
                            }
                            catch { }
                        }
                        if (fld.FieldType == typeof(string))
                        {
                            try { fld.SetValue(m, stackDesc); written = true; break; } catch { }
                        }
                    }
                }
                catch { }
            }
            if (written) break;

            string[] propCandidates = new[] { "StackDescription", "StackText", "CountText", "Text" };
            foreach (var pn in propCandidates)
            {
                try
                {
                    var prop = t.GetProperty(pn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop != null)
                    {
                        if (prop.PropertyType == typeof(string))
                        {
                            try { prop.SetValue(m, stackDesc); written = true; break; } catch { }
                        }
                        else if (tmpTextType != null && prop.PropertyType == tmpTextType)
                        {
                            var obj = prop.GetValue(m);
                            if (obj != null)
                            {
                                try
                                {
                                    var cur = tmpTextProp?.GetValue(obj) as string;
                                    if (string.IsNullOrWhiteSpace(cur)) tmpTextProp?.SetValue(obj, stackDesc);
                                    written = true; break;
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }
            if (written) break;

            try
            {
                var method = t.GetMethod("SetStackDescription", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?? t.GetMethod("SetStack", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?? t.GetMethod("SetCount", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?? t.GetMethod("SetText", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (method != null)
                {
                    try { method.Invoke(m, new object[] { stackDesc }); written = true; break; } catch { }
                }
            }
            catch { }
            if (written) break;
        }

        if (written) return true;

        try
        {
            var texts = go.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                var nm = t.gameObject.name.ToLower();
                if (nm.Contains("stack") || nm.Contains("count") || nm.Contains("叠放") || nm.Contains("数量"))
                {
                    if (string.IsNullOrWhiteSpace(t.text)) t.text = stackDesc;
                    written = true; break;
                }
            }
            if (written) return true;

            foreach (var t in texts)
            {
                if (t == null) continue;
                if (string.IsNullOrWhiteSpace(t.text))
                {
                    t.text = stackDesc; written = true; break;
                }
            }
            if (written) return true;
        }
        catch { }

        if (tmpTextType != null && tmpTextProp != null)
        {
            try
            {
                var comps = go.GetComponentsInChildren<Component>(true);
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    if (tmpTextType.IsAssignableFrom(ct))
                    {
                        var nm = comp.gameObject.name.ToLowerInvariant();
                        if (nm.Contains("stack") || nm.Contains("count") || nm.Contains("叠放") || nm.Contains("数量"))
                        {
                            var cur = tmpTextProp.GetValue(comp) as string;
                            if (string.IsNullOrWhiteSpace(cur)) tmpTextProp.SetValue(comp, stackDesc);
                            written = true; break;
                        }
                    }
                }
                if (written) return true;

                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var ct = comp.GetType();
                    if (tmpTextType.IsAssignableFrom(ct))
                    {
                        var cur = tmpTextProp.GetValue(comp) as string;
                        if (string.IsNullOrWhiteSpace(cur))
                        {
                            tmpTextProp.SetValue(comp, stackDesc);
                            written = true; break;
                        }
                    }
                }
            }
            catch { }
        }

        return written;
    }

    object[] BuildArgsForMethod(ParameterInfo[] ps, CardMessage def, Transform parent, int count, bool attachInfo)
    {
        if (ps == null || ps.Length == 0) return new object[0];
        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var pType = ps[i].ParameterType;
            if (typeof(CardMessage).IsAssignableFrom(pType))
                args[i] = def;
            else if (typeof(Transform).IsAssignableFrom(pType))
                args[i] = parent;
            else if (pType == typeof(int))
                args[i] = count;
            else if (pType == typeof(bool))
                args[i] = attachInfo;
            else if (pType == typeof(object))
                args[i] = def;
            else if (pType == typeof(string))
                args[i] = null;
            else
                args[i] = null;
        }
        return args;
    }

    Dictionary<int, int> ParsePlayerDataCsv(string text)
    {
        var dict = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(text)) return dict;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            var tag = parts[0].Trim().ToLower();
            if (tag == "deck")
            {
                if (int.TryParse(parts[1].Trim(), out int id) && int.TryParse(parts[2].Trim(), out int cnt))
                {
                    if (cnt > 0)
                    {
                        if (dict.ContainsKey(id)) dict[id] += cnt; else dict[id] = cnt;
                    }
                }
            }
        }
        return dict;
    }

    void ClearDeckPanel()
    {
        if (deckPanel == null) return;
        for (int i = deckPanel.childCount - 1; i >= 0; i--)
            Destroy(deckPanel.GetChild(i).gameObject);
    }

    public bool CanAcceptCard(int cardId)
    {
        if (_cardStore == null && CardStore.Instance != null) _cardStore = CardStore.Instance;
        if (_cardStore == null) return false;
        try
        {
            var def = _cardStore.GetCardById(cardId);
            return def != null;
        }
        catch { return false; }
    }

    public bool AcceptCardById(int cardId)
    {
        if (_cardStore == null && CardStore.Instance != null) _cardStore = CardStore.Instance;
        if (_card_store_null_check()) return false;

        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;
        if (_playerDataManager == null) _playerDataManager = FindObjectOfType<PlayerDataManager>();

        if (_playerDataManager == null) return false;

        bool changed = false;

        try
        {
            changed = TryInvokePlayerDataTransfer(cardId, 1);
        }
        catch { changed = false; }

        if (!changed) return false;

        try { TryUpdateLibraryCount(cardId, -1); } catch { }

        try
        {
            ClearDeckPanel();
            BuildDeckFromPlayerData();
        }
        catch { }

        return true;
    }

    private bool _card_store_null_check()
    {
        if (_cardStore == null) return true;
        return false;
    }

    bool TryUpdateLibraryCount(int cardId, int delta)
    {
        if (librarySource == null) return false;

        Type t = librarySource.GetType();
        string[] candidateNames = new[] {
    "TryChangePlayerCount",
    "ChangePlayerCount",
    "ModifyPlayerCount",
    "TryModifyPlayerCount",
    "AdjustPlayerCount",
    "SetPlayerCount"
};

        foreach (var name in candidateNames)
        {
            var mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                var ps = mi.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int))
                {
                    try
                    {
                        mi.Invoke(librarySource, new object[] { cardId, delta });
                        return true;
                    }
                    catch { return false; }
                }
            }
        }

        foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var ps = mi.GetParameters();
            if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int))
            {
                try
                {
                    mi.Invoke(librarySource, new object[] { cardId, delta });
                    return true;
                }
                catch { }
            }
        }

        string[] otherCandidates = new[] { "UpdateLibraryInstancesForCardId", "RefreshCardCount", "UpdateCardCount" };
        foreach (var name in otherCandidates)
        {
            var mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                var ps = mi.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                {
                    try
                    {
                        mi.Invoke(librarySource, new object[] { cardId });
                        return true;
                    }
                    catch { }
                }
            }
        }

        return false;
    }

    private bool TryInvokePlayerDataTransfer(int cardId, int amount)
    {
        if (_playerDataManager == null) return false;
        var pdType = _playerDataManager.GetType();

        string[] preferredNames = new[] {
            "TransferCardFromLibraryToDeck",
            "TryTransferCardToDeck",
            "TransferCardToDeck",
            "TransferCard",
            "TryTransferCard",
            "MoveCardToDeck"
        };

        foreach (var name in preferredNames)
        {
            var methods = pdType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var mi in methods)
            {
                if (!string.Equals(mi.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                var ps = mi.GetParameters();
                object[] args = null;

                if (ps.Length == 3 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(bool))
                {
                    args = new object[] { cardId, amount, false };
                }
                else if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int))
                {
                    args = new object[] { cardId, amount };
                }
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                {
                    args = new object[] { cardId };
                }
                else
                {
                    continue;
                }

                try
                {
                    var res = mi.Invoke(_playerDataManager, args);
                    if (mi.ReturnType == typeof(bool))
                    {
                        return res is bool b && b;
                    }
                    else if (mi.ReturnType == typeof(int))
                    {
                        return Convert.ToInt32(res) > 0;
                    }
                    else if (mi.ReturnType == typeof(void))
                    {
                        return true;
                    }
                    else
                    {
                        if (res != null) return true;
                    }
                }
                catch { continue; }
            }
        }

        var allMethods = pdType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var mi in allMethods)
        {
            string lname = mi.Name.ToLowerInvariant();
            if (!lname.Contains("transfer") && !lname.Contains("deck") && !lname.Contains("move")) continue;

            var ps = mi.GetParameters();
            object[] args = null;
            if (ps.Length == 3 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(bool))
                args = new object[] { cardId, amount, false };
            else if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int))
                args = new object[] { cardId, amount };
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                args = new object[] { cardId };
            else
                continue;

            try
            {
                var res = mi.Invoke(_playerDataManager, args);
                if (mi.ReturnType == typeof(bool))
                {
                    return res is bool b && b;
                }
                else if (mi.ReturnType == typeof(int))
                {
                    return Convert.ToInt32(res) > 0;
                }
                else if (mi.ReturnType == typeof(void))
                {
                    return true;
                }
                else
                {
                    if (res != null) return true;
                }
            }
            catch { continue; }
        }

        return false;
    }

    public bool CanAccept(CardDragHandler card)
    {
        if (card == null) return false;
        return CanAcceptCard(card.CardId);
    }

    public void Accept(CardDragHandler card, PointerEventData eventData)
    {
        if (card == null) return;

        int insertIndex = -1;
        try
        {
            if (eventData != null && deckPanel != null)
            {
                insertIndex = CalculateInsertIndexByNearestChild(deckPanel, eventData.position);
            }
        }
        catch { insertIndex = -1; }

        bool ok = false;
        try
        {
            if (isEditing)
            {
                // 编辑模式：只修改 editingDeck 并刷新编辑视图（不更改 PlayerDataManager）
                AddCardToEditingDeck(card.CardId, 1, insertIndex);
                ok = true;
            }
            else
            {
                if (insertIndex >= 0)
                    ok = AddCardByIdAt(card.CardId, insertIndex);
                else
                    ok = AcceptCardById(card.CardId);
            }
        }
        catch { ok = false; }

        if (!ok)
        {

        }
    }

    public bool AddCardByIdAt(int cardId, int index)
    {
        if (_cardStore == null && CardStore.Instance != null) _cardStore = CardStore.Instance;
        if (_card_store_null_check()) return false;

        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;
        if (_playerDataManager == null) _playerDataManager = FindObjectOfType<PlayerDataManager>();

        if (_playerDataManager == null) return false;

        bool changed = false;

        try
        {
            changed = TryInvokePlayerDataTransfer(cardId, 1);
        }
        catch { changed = false; }

        if (!changed) return false;

        try { TryUpdateLibraryCount(cardId, -1); } catch { }

        try
        {
            CardMessage def = null;
            if (_cardStore != null)
            {
                try { def = _cardStore.GetCardById(cardId); } catch { def = null; }
            }
            if (def == null)
            {
                ClearDeckPanel();
                BuildDeckFromPlayerData();
                return true;
            }

            int childCount = deckPanel != null ? deckPanel.childCount : 0;
            int idx = Mathf.Clamp(index, 0, childCount);

            var go = InstantiateDeckEntry(def, 1, cardId, idx);
            if (go != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(deckPanel);
            }
            else
            {
                ClearDeckPanel();
                BuildDeckFromPlayerData();
            }
        }
        catch
        {
            try
            {
                ClearDeckPanel();
                BuildDeckFromPlayerData();
            }
            catch { }
        }

        return true;
    }

    int CalculateInsertIndexByNearestChild(RectTransform content, Vector2 screenPos)
    {
        if (content == null) return 0;
        Canvas rootCanvas = content.GetComponentInParent<Canvas>();
        Camera cam = rootCanvas != null ? rootCanvas.worldCamera : null;

        if (content.childCount == 0) return 0;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPos, cam, out Vector2 localPoint);

        float minDist = float.MaxValue;
        int nearestIndex = 0;
        for (int i = 0; i < content.childCount; i++)
        {
            RectTransform child = content.GetChild(i) as RectTransform;
            if (child == null) continue;
            Vector2 childCenter = child.anchoredPosition;
            float d = Vector2.SqrMagnitude(localPoint - childCenter);
            if (d < minDist)
            {
                minDist = d;
                nearestIndex = i;
            }
        }

        RectTransform nearest = content.GetChild(nearestIndex) as RectTransform;
        if (nearest == null) return content.childCount;

        bool testHorizontal = true;
        var grid = content.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            testHorizontal = true;
        }
        if (localPoint.x > nearest.anchoredPosition.x) return nearestIndex + 1;
        else return nearestIndex;
    }

    private string GetTransformPath(Transform t)
    {
        if (t == null) return "(null)";
        string path = t.name;
        var p = t.parent;
        int safety = 0;
        while (p != null && safety < 128)
        {
            path = p.name + "/" + path;
            p = p.parent;
            safety++;
        }
        return path;
    }
    // 调用此方法进入编辑模式（例如打开编辑面板时调用）
    public void OpenDeckEditor()
    {
        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;
        editingDeck.Clear();

        if (_playerDataManager != null)
        {
            try
            {
                if (_playerDataManager.playerDeckDict != null)
                {
                    foreach (var kv in _playerDataManager.playerDeckDict)
                        if (kv.Value > 0) editingDeck[kv.Key] = kv.Value;
                }
                else
                {
                    // 兼容反射读取
                    var prop = _playerDataManager.GetType().GetProperty("playerDeckDict");
                    if (prop != null)
                    {
                        var dictObj = prop.GetValue(_playerDataManager) as System.Collections.IDictionary;
                        if (dictObj != null)
                        {
                            foreach (var k in dictObj.Keys)
                            {
                                int id = Convert.ToInt32(k);
                                int cnt = Convert.ToInt32(dictObj[k]);
                                if (cnt > 0) editingDeck[id] = cnt;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        isEditing = true;
        BuildDeckFromEditingDeck();
    }

    // 用 editingDeck 完整构建 UI（和 BuildDeckFromPlayerData 类似）
    public void BuildDeckFromEditingDeck()
    {
        ClearDeckPanel();
        if (editingDeck == null || editingDeck.Count == 0) return;

        foreach (var kv in editingDeck)
        {
            int cardId = kv.Key;
            int cnt = kv.Value;
            if (cnt <= 0) continue;
            CardMessage def = null;
            try { if (_cardStore != null) def = _cardStore.GetCardById(cardId); } catch { def = null; }
            if (def == null) continue;
            for (int i = 0; i < cnt; i++) TryInstantiateCard(def, 1, cardId);
        }
    }

    // 增加卡到 editingDeck（可选 insertIndex，但这里默认简单重建）
    public void AddCardToEditingDeck(int cardId, int amount = 1, int insertIndex = -1)
    {
        if (!isEditing) OpenDeckEditor();
        if (editingDeck.ContainsKey(cardId)) editingDeck[cardId] += amount;
        else editingDeck[cardId] = amount;

        // 简单实现：重建列表（若需性能优化，可做增量 InstantiateDeckEntry）
        BuildDeckFromEditingDeck();
        Debug.Log($"[DeckManager] editingDeck add {cardId} x{amount}, total now {editingDeck[cardId]}");
    }

    public void RemoveCardFromEditingDeck(int cardId, int amount = 1)
    {
        if (!isEditing) return;
        if (!editingDeck.ContainsKey(cardId)) return;
        editingDeck[cardId] -= amount;
        if (editingDeck[cardId] <= 0) editingDeck.Remove(cardId);
        BuildDeckFromEditingDeck();
    }

    // 保存编辑结果到 PlayerDataManager
    public void SaveEditingDeckToPlayerData()
    {
        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;
        if (_playerDataManager == null) return;

        try
        {
            // 尝试调用 ClearDeck 或类似接口
            var clearMethod = _playerDataManager.GetType().GetMethod("ClearDeck") ?? _playerDataManager.GetType().GetMethod("ClearPlayerDeck");
            if (clearMethod != null) clearMethod.Invoke(_playerDataManager, null);
        }
        catch { }

        try
        {
            var prop = _playerDataManager.GetType().GetProperty("playerDeckDict");
            if (prop != null)
            {
                var dictObj = prop.GetValue(_playerDataManager) as System.Collections.IDictionary;
                if (dictObj != null)
                {
                    dictObj.Clear();
                    foreach (var kv in editingDeck) dictObj[kv.Key] = kv.Value;
                }
            }
            else
            {
                var fieldArr = _playerDataManager.GetType().GetField("playerDeck");
                if (fieldArr != null)
                {
                    var arr = fieldArr.GetValue(_playerDataManager) as int[];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++) arr[i] = 0;
                        foreach (var kv in editingDeck)
                        {
                            if (kv.Key >= 0 && kv.Key < arr.Length) arr[kv.Key] = kv.Value;
                        }
                        fieldArr.SetValue(_playerDataManager, arr);
                    }
                }
                var addMethod = _playerDataManager.GetType().GetMethod("AddDeckCardNoSave");
                if (addMethod != null)
                {
                    try
                    {
                        var clearM = _playerDataManager.GetType().GetMethod("ClearDeck");
                        if (clearM != null) clearM.Invoke(_playerDataManager, null);
                    }
                    catch { }
                    foreach (var kv in editingDeck)
                    {
                        addMethod.Invoke(_playerDataManager, new object[] { kv.Key, kv.Value });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DeckManager] SaveEditingDeckToPlayerData failed: {ex.Message}");
        }

        // 如果 PlayerDataManager 提供 SavePlayerData，调用之
        try
        {
            var m = _playerDataManager.GetType().GetMethod("SavePlayerData");
            if (m != null) m.Invoke(_playerDataManager, null);
        }
        catch { }

        isEditing = false;

        // 重建 UI 显示最终持久数据
        ClearDeckPanel();
        BuildDeckFromPlayerData();
    }

    public void CancelEditing()
    {
        isEditing = false;
        ClearDeckPanel();
        BuildDeckFromPlayerData();
    }
}
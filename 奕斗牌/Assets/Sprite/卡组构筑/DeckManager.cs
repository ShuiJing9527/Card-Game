using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

public class DeckManager : MonoBehaviour
{
    [Header("Source / UI")]
    public LibraryManage librarySource;     // optional: reference to LibraryManage (editor auto-bind)
    public RectTransform deckPanel;        // UI parent (must bind)
    public GameObject deckEntryPrefab;     // optional wrapper prefab per row

    [Header("Fallback CSV (if no PlayerDataManager)")]
    public TextAsset playerDataCsv;

    [Header("Runtime Options")]
    public bool clearOnStart = true;

    // If true, ensure CardCounter / card-info UI is hidden on deck items
    public bool hideCardInfoInDeck = true;

    private CardStore _cardStore;
    private OpenPackage _openPackage;
    private PlayerDataManager _playerDataManager;

    void OnValidate()
    {
        if (librarySource == null) librarySource = FindObjectOfType<LibraryManage>();
        if (_cardStore == null)
        {
            if (CardStore.Instance != null) _cardStore = CardStore.Instance;
            else
            {
                var cs = FindObjectOfType<CardStore>();
                if (cs != null) _cardStore = cs;
            }
        }

        if (_openPackage == null && _cardStore != null)
            _open_package_try_bind_from_cardstore();

        if (_openPackage == null)
        {
            var op2 = FindObjectOfType<OpenPackage>();
            if (op2 != null) _openPackage = op2;
        }

        if (_playerDataManager == null)
        {
            var pd = FindObjectOfType<PlayerDataManager>();
            if (pd != null) _playerDataManager = pd;
        }
    }

    void Reset() { OnValidate(); }

    void _open_package_try_bind_from_cardstore()
    {
        try { _openPackage = _cardStore.GetComponent<OpenPackage>(); } catch { _openPackage = null; }
    }

    void Start()
    {
        if (deckPanel == null)
        {
            Debug.LogError("[DeckManager] deckPanel 未绑定！");
            return;
        }

        if (librarySource == null) librarySource = FindObjectOfType<LibraryManage>();
        if (_cardStore == null && CardStore.Instance != null) _cardStore = CardStore.Instance;
        if (_playerDataManager == null && PlayerDataManager.Instance != null) _playerDataManager = PlayerDataManager.Instance;

        // Prefer OpenPackage on CardStore (your setup)
        if (_openPackage == null && _cardStore != null) _open_package_try_bind_from_cardstore();
        if (_openPackage == null) _openPackage = FindObjectOfType<OpenPackage>();

        if (clearOnStart) ClearDeckPanel();

        StartCoroutine(WaitThenBuild());
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
                if (_openPackage == null) _open_package_try_bind_from_cardstore();
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

    void BuildDeckFromPlayerData()
    {
        var deckCounts = new Dictionary<int, int>();
        string usedSource = "none";

        // 1) Try PlayerDataManager (strict: only deck-related members)
        if (_playerDataManager != null)
        {
            try
            {
                var pd = _playerDataManager;

                // 1.a Try property: playerDeckDict / PlayerDeckDict
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
                        if (deckCounts.Count > 0) usedSource = "PlayerDataManager.property.playerDeckDict";
                    }
                }

                // 1.b Try field: playerDeck / PlayerDeck (array or similar)
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
                            if (deckCounts.Count > 0) usedSource = "PlayerDataManager.field.playerDeck";
                        }
                    }
                }

                // 1.c Try method: ONLY GetPlayerDeckCounts (strict)
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
                            if (deckCounts.Count > 0) usedSource = "PlayerDataManager.method.GetPlayerDeckCounts";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DeckManager] 从 PlayerDataManager 读取 deck 数据时异常: {ex.Message}");
            }
        }

        // 2) Fallback: parse playerDataCsv (only 'deck' lines)
        if (deckCounts.Count == 0 && playerDataCsv != null)
        {
            deckCounts = ParsePlayerDataCsv(playerDataCsv.text);
            if (deckCounts.Count > 0) usedSource = "playerDataCsv";
        }

        // Debug: show what source and what we will instantiate
        var sb = new StringBuilder();
        sb.AppendFormat("[DeckManager] Deck source: {0}. Entries: ", usedSource);
        foreach (var kv in deckCounts) sb.AppendFormat("{0}x{1} ", kv.Key, kv.Value);
        Debug.Log(sb.ToString());

        if (deckCounts.Count == 0)
        {
            Debug.LogWarning("[DeckManager] 未找到玩家卡组数据，Deck 为空");
            return;
        }

        int created = 0;
        // 逐张实例化：每张卡都生成一个 UI 实例（如果 cnt = 3，就生成 3 个）
        foreach (var kv in deckCounts)
        {
            int cardId = kv.Key;
            int cnt = kv.Value;
            if (cnt <= 0) continue;

            CardMessage def = null;
            if (_cardStore != null)
            {
                try { def = _cardStore.GetCardById(cardId); }
                catch { def = null; }
            }

            if (def == null)
            {
                Debug.LogWarning($"[DeckManager] 未在 CardStore 中找到 cardId={cardId}，跳过");
                continue;
            }

            // 按数量逐个实例化（每次传 count=1）
            for (int i = 0; i < cnt; i++)
            {
                if (TryInstantiateCard(def, 1, cardId)) created++;
            }
        }

        Debug.Log($"[DeckManager] 已生成 Deck 项: {created}");
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

        // Prefer OpenPackage.InstantiateCardItem
        if (_openPackage != null)
        {
            try
            {
                var go = _openPackage.InstantiateCardItem(def, parent, count, false); // force attachInfo=false for deck
                if (go != null)
                {
                    PostProcessDeckInstance(go, cardId);
                    return true;
                }
            }
            catch (MissingMethodException) { }
            catch (Exception ex) { Debug.LogWarning($"[DeckManager] 调用 OpenPackage.InstantiateCardItem 出错: {ex.Message}"); }

            // reflection fallback
            try
            {
                var mi = _openPackage.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance);
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    object[] args = BuildArgsForMethod(ps, def, parent, count, false);
                    var res = mi.Invoke(_openPackage, args) as GameObject;
                    if (res != null)
                    {
                        PostProcessDeckInstance(res, cardId);
                        return true;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[DeckManager] 反射调用 OpenPackage.InstantiateCardItem 失败: {ex.Message}"); }
        }

        // CardStore proxy fallback
        if (_cardStore != null)
        {
            try
            {
                MethodInfo mi = _cardStore.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance);
                if (mi != null)
                {
                    var ps = mi.GetParameters();
                    object[] args = BuildArgsForMethod(ps, def, parent, count, false);
                    var res = mi.Invoke(_cardStore, args) as GameObject;
                    if (res != null)
                    {
                        PostProcessDeckInstance(res, cardId);
                        return true;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[DeckManager] 反射调用 CardStore.InstantiateCardItem 失败: {ex.Message}"); }
        }

        // last resort: try to instantiate a prefab from OpenPackage fields (best-effort)
        try
        {
            var opType = _openPackage != null ? _openPackage.GetType() : null;
            if (opType != null)
            {
                var monsterField = opType.GetField("monsterPrefabs", BindingFlags.Public | BindingFlags.Instance);
                var spellField = opType.GetField("spellPrefab", BindingFlags.Public | BindingFlags.Instance);
                if (monsterField != null && def is MonsterCard)
                {
                    var listObj = monsterField.GetValue(_open_package_try_get()) as IList;
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
                    var prefab = spellField.GetValue(_open_package_try_get()) as GameObject;
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

        Debug.LogWarning("[DeckManager] 无法实例化卡片 UI：请确保场景中有 OpenPackage（或 CardStore 提供 InstantiateCardItem）");
        return false;
    }

    // Helper to safely get _openPackage in last-resort prefab branch (keeps original behaviour)
    private object _open_package_try_get()
    {
        return _openPackage;
    }

    // Hide / disable CardCounter (or other info UI) on deck instances
    void PostProcessDeckInstance(GameObject go, int cardId)
    {
        if (go == null) return;

        // If your LibraryManage has helper ClearPrefabArtPublic, call it to remove any leftover art placeholders
        try { librarySource?.ClearPrefabArtPublic(go); } catch { }

        if (hideCardInfoInDeck)
        {
            // Find CardCounter components and disable their GameObject to hide info
            try
            {
                var counters = go.GetComponentsInChildren<CardCounter>(true);
                foreach (var c in counters)
                {
                    if (c != null && c.gameObject != null)
                        c.gameObject.SetActive(false);
                }
            }
            catch { /* safe-fail */ }
        }

        // Pass correct cardId so art/info is set correctly
        try
        {
            librarySource?.ApplyCardArtToInstance(go, cardId);
        }
        catch { }

        // After ApplyCardArtToInstance (which might create CardCounter), ensure they are hidden
        if (hideCardInfoInDeck)
        {
            try
            {
                var counters = go.GetComponentsInChildren<CardCounter>(true);
                foreach (var c in counters)
                {
                    if (c != null && c.gameObject != null)
                        c.gameObject.SetActive(false);
                }
            }
            catch { }
        }
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
            // STRICT: only accept 'deck' lines for deck manager
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
}
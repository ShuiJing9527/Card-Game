using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LibraryManage : MonoBehaviour
{
    [Header("CSV Inputs (fallback if no PlayerDataManager)")]
    public TextAsset playerDataCsv;

    [Header("UI")]
    public RectTransform libraryPanel;

    [Header("Attach Info Control (Library decides whether to ask OpenPackage to attach info)")]
    public bool defaultShowInfo = true;
    public bool onlyInstantiateInfoInPoolScene = true;
    public string poolSceneName = "CardPoolScene";

    // 新增：在 Inspector 中可以强制显示 info（用于测试/修复）
    [Header("Debug / Overrides")]
    public bool forceAttachInfo = false;

    [Header("References (auto-find if null)")]
    public OpenPackage openPackage;
    public CardStore cardStore;
    public PlayerDataManager playerDataManager;

    [Header("Options")]
    public bool clearOnStart = true;

    // ---------- 编辑器时也能自动绑定 ----------
    void OnValidate()
    {
        if (cardStore == null)
        {
            var cs = FindObjectOfType<CardStore>();
            if (cs != null) cardStore = cs;
        }

        if (openPackage == null && cardStore != null)
        {
            var op = cardStore.GetComponent<OpenPackage>();
            if (op != null) openPackage = op;
        }

        if (playerDataManager == null)
        {
            var pd = FindObjectOfType<PlayerDataManager>();
            if (pd != null) playerDataManager = pd;
        }
    }

    void Reset()
    {
        OnValidate();
    }

    [ContextMenu("AutoBindReferences")]
    void AutoBindReferencesContextMenu()
    {
        OnValidate();
        Debug.Log("[LibraryManage] AutoBindReferences executed (OnValidate)。");
    }
    // ----------------------------------------

    void Start()
    {
        if (libraryPanel == null)
        {
            Debug.LogError("[LibraryManage] libraryPanel 未绑定！");
            return;
        }

        if (cardStore == null && CardStore.Instance != null) cardStore = CardStore.Instance;
        if (playerDataManager == null && PlayerDataManager.Instance != null) playerDataManager = PlayerDataManager.Instance;

        if (openPackage == null)
        {
            if (cardStore != null)
                openPackage = cardStore.GetComponent<OpenPackage>();
        }
        if (openPackage == null)
            openPackage = FindObjectOfType<OpenPackage>();

        if (clearOnStart) ClearLibrary();

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
                    var val = prop.GetValue(cs);
                    if (val is bool b) return b;
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

        if (cardStore != null && !isReady(cardStore))
        {
            try
            {
                onReadyHandler = new Action(() => { readyFlag = true; });
                cardStore.OnCardsReady += onReadyHandler;
            }
            catch { onReadyHandler = null; }
        }
        else
        {
            readyFlag = true;
        }

        while (!readyFlag && t < timeout)
        {
            if (cardStore == null && CardStore.Instance != null)
            {
                cardStore = CardStore.Instance;
                if (openPackage == null && cardStore != null)
                    openPackage = cardStore.GetComponent<OpenPackage>() ?? openPackage;
            }
            if (cardStore != null && isReady(cardStore))
            {
                readyFlag = true;
                break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (onReadyHandler != null && cardStore != null)
        {
            try { cardStore.OnCardsReady -= onReadyHandler; } catch { }
        }

        if (playerDataManager == null && PlayerDataManager.Instance != null) playerDataManager = PlayerDataManager.Instance;

        BuildLibraryFromPlayerData();
        yield break;
    }

    [ContextMenu("RebuildLibrary")]
    public void RebuildLibrary()
    {
        ClearLibrary();
        BuildLibraryFromPlayerData();
    }

    void BuildLibraryFromPlayerData()
    {
        var playerCounts = new Dictionary<int, int>();

        if (playerDataManager != null)
        {
            try
            {
                var pd = playerDataManager;

                var prop = pd.GetType().GetProperty("PlayerDeckDict");
                if (prop != null)
                {
                    var dictObj = prop.GetValue(pd) as System.Collections.IDictionary;
                    if (dictObj != null)
                    {
                        foreach (var k in dictObj.Keys)
                        {
                            int id = Convert.ToInt32(k);
                            int cnt = Convert.ToInt32(dictObj[k]);
                            if (cnt > 0) playerCounts[id] = cnt;
                        }
                    }
                }

                if (playerCounts.Count == 0)
                {
                    var f = pd.GetType().GetField("PlayerDeck");
                    if (f != null)
                    {
                        var arr = f.GetValue(pd) as int[];
                        if (arr != null)
                        {
                            for (int i = 0; i < arr.Length; i++)
                                if (arr[i] > 0) playerCounts[i] = arr[i];
                        }
                    }
                }

                if (playerCounts.Count == 0)
                {
                    var m = pd.GetType().GetMethod("GetPlayerCardCounts");
                    if (m != null)
                    {
                        var res = m.Invoke(pd, null) as System.Collections.IDictionary;
                        if (res != null)
                        {
                            foreach (var k in res.Keys)
                            {
                                int id = Convert.ToInt32(k);
                                int cnt = Convert.ToInt32(res[k]);
                                if (cnt > 0) playerCounts[id] = cnt;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LibraryManage] 从 PlayerDataManager 读取玩家数据时发生异常: {ex.Message}");
            }
        }

        if (playerCounts.Count == 0 && playerDataCsv != null)
        {
            playerCounts = ParsePlayerDataCsvSimple(playerDataCsv.text);
        }

        try { CardCounter.SetPlayerCounts(playerCounts); } catch { }

        if (playerCounts.Count == 0)
        {
            Debug.LogWarning("[LibraryManage] 未找到玩家持有的卡片数据，Library 为空");
            return;
        }

        if (openPackage == null)
        {
            if (cardStore != null)
                openPackage = cardStore.GetComponent<OpenPackage>();
        }
        if (openPackage == null)
            openPackage = FindObjectOfType<OpenPackage>();

        bool attachInfo = ShouldAttachCardInfo();

        // debug：打印场景名、openPackage 状态以及 attachInfo 供排查
        Debug.Log($"[LibraryManage] Scene='{SceneManager.GetActiveScene().name}', openPackage_present={(openPackage != null)}, attachInfo={attachInfo}, forceAttachInfo={forceAttachInfo}");

        int created = 0;
        foreach (var kv in playerCounts)
        {
            int cardId = kv.Key;
            int count = kv.Value;
            if (count <= 0) continue;

            CardMessage def = null;
            if (cardStore != null)
            {
                try { def = cardStore.GetCardById(cardId); }
                catch { def = null; }
            }

            if (def == null)
            {
                Debug.LogWarning($"[LibraryManage] CardStore 中未找到 cardId={cardId} 的定义，跳过");
                continue;
            }

            bool instantiated = false;

            if (openPackage != null)
            {
                try
                {
                    // 直接调用（若实现了带 attachInfo 的签名）
                    var inst = openPackage.InstantiateCardItem(def, libraryPanel, count, attachInfo);
                    if (inst != null) { created++; instantiated = true; }
                }
                catch (MissingMethodException)
                {
                    instantiated = false;
                }
                catch (TargetParameterCountException)
                {
                    instantiated = false;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LibraryManage] 调用 OpenPackage.InstantiateCardItem 出错: {ex.Message}");
                    instantiated = false;
                }

                if (!instantiated)
                {
                    // reflection fallback: attempt to find suitable overload and pass args (incl. attachInfo when possible)
                    try
                    {
                        MethodInfo mi = openPackage.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance);
                        if (mi != null)
                        {
                            var ps = mi.GetParameters();
                            object[] args = BuildArgsForMethod(ps, def, libraryPanel, count, attachInfo);
                            var res = mi.Invoke(openPackage, args) as GameObject;
                            if (res != null) { created++; instantiated = true; }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[LibraryManage] 反射调用 OpenPackage.InstantiateCardItem 失败: {ex.Message}");
                    }
                }
            }

            if (!instantiated && cardStore != null)
            {
                try
                {
                    MethodInfo miStore = cardStore.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance);
                    if (miStore != null)
                    {
                        var ps = miStore.GetParameters();
                        object[] args = BuildArgsForMethod(ps, def, libraryPanel, count, attachInfo);
                        var res = miStore.Invoke(cardStore, args) as GameObject;
                        if (res != null) { created++; instantiated = true; }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LibraryManage] 反射调用 CardStore.InstantiateCardItem 失败: {ex.Message}");
                }
            }

            if (!instantiated)
            {
                Debug.LogWarning("[LibraryManage] 无法实例化卡片 UI：缺少 OpenPackage 或对应的 InstantiateCardItem 方法（或已实现但签名不匹配）。");
            }
        }

        Debug.Log($"[LibraryManage] 已生成卡片项: {created}");
    }

    // ---------- 以下为补充公有方法，供 DeckManager 等调用 ----------
    public void ClearPrefabArtPublic(GameObject go)
    {
        if (go == null) return;
        try
        {
            var counters = go.GetComponentsInChildren<CardCounter>(true);
            foreach (var c in counters)
            {
                try
                {
                    if (c != null && c.gameObject != null)
                        c.gameObject.SetActive(false);
                }
                catch { }
            }

            var imgs = go.GetComponentsInChildren<Image>(true);
            foreach (var img in imgs)
            {
                if (img == null) continue;
                var n = img.gameObject.name.ToLower();
                if (n.Contains("placeholder") || n.Contains("artplaceholder") || n.Contains("thumb") || n.Contains("cardart"))
                {
                    try { img.sprite = null; img.color = new Color(1, 1, 1, 0); } catch { }
                }
            }

            var texts = go.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                var n = t.gameObject.name.ToLower();
                if (n.Contains("name") || n.Contains("title") || n.Contains("desc") || n.Contains("count"))
                {
                    try { t.text = ""; } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LibraryManage] ClearPrefabArtPublic 异常: {ex.Message}");
        }
    }

    public void ApplyCardArtToInstance(GameObject go, int cardId, bool attachInfo = true)
    {
        if (go == null) return;

        try
        {
            CardMessage def = null;
            try { def = cardStore?.GetCardById(cardId); } catch { def = null; }

            bool applied = false;

            var mcd = go.GetComponentInChildren(typeof(MonsterCardDisplay), true) as Component;
            if (mcd != null && def != null && def.GetType().Name.ToLower().Contains("monster"))
            {
                TryInvokeSetCard(mcd, def);
                applied = true;
            }

            var scd = go.GetComponentInChildren(typeof(SpellCardDisplay), true) as Component;
            if (scd != null && def != null && def.GetType().Name.ToLower().Contains("spell"))
            {
                TryInvokeSetCard(scd, def);
                applied = true;
            }

            if (def != null)
            {
                string nameText = GetCardNameFromDef(def);
                string descText = GetCardDescFromDef(def);

                var texts = go.GetComponentsInChildren<Text>(true);
                foreach (var t in texts)
                {
                    if (t == null) continue;
                    var n = t.gameObject.name.ToLower();
                    if (n.Contains("name") || n.Contains("title"))
                    {
                        try { t.text = nameText ?? ""; } catch { }
                    }
                    else if (n.Contains("desc") || n.Contains("description"))
                    {
                        try { t.text = descText ?? ""; } catch { }
                    }
                    else if (n.Contains("count") || n.Contains("stack"))
                    {
                        try { t.text = ""; } catch { }
                    }
                }
            }

            var counters = go.GetComponentsInChildren<CardCounter>(true);
            foreach (var c in counters)
            {
                if (c == null || c.gameObject == null) continue;
                try
                {
                    if (attachInfo)
                    {
                        c.gameObject.SetActive(true);
                        TryInvokeSetCounter(c, cardId);
                    }
                    else
                    {
                        c.gameObject.SetActive(false);
                    }
                }
                catch { }
            }

            if (!applied && openPackage != null)
            {
                try
                {
                    MethodInfo mi = openPackage.GetType().GetMethod("ApplyCardArtToInstance", BindingFlags.Public | BindingFlags.Instance);
                    if (mi != null)
                    {
                        var ps = mi.GetParameters();
                        object[] args = null;
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(GameObject) && ps[1].ParameterType == typeof(int))
                            args = new object[] { go, cardId };
                        else if (ps.Length == 3 && ps[0].ParameterType == typeof(GameObject) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(bool))
                            args = new object[] { go, cardId, attachInfo };
                        if (args != null) mi.Invoke(openPackage, args);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LibraryManage] ApplyCardArtToInstance 异常: {ex.Message}");
        }
    }

    void TryInvokeSetCard(Component displayComp, object cardDef)
    {
        if (displayComp == null || cardDef == null) return;
        try
        {
            var t = displayComp.GetType();
            var mi1 = t.GetMethod("SetCard", new Type[] { cardDef.GetType() });
            if (mi1 != null)
            {
                mi1.Invoke(displayComp, new object[] { cardDef });
                return;
            }
            var mi2 = t.GetMethod("SetCard", new Type[] { typeof(object) });
            if (mi2 != null)
            {
                mi2.Invoke(displayComp, new object[] { cardDef });
                return;
            }
            var anyMi = t.GetMethod("SetCard", BindingFlags.Public | BindingFlags.Instance);
            if (anyMi != null)
            {
                var ps = anyMi.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var pType = ps[i].ParameterType;
                    if (pType.IsAssignableFrom(cardDef.GetType())) args[i] = cardDef;
                    else if (pType == typeof(string)) args[i] = null;
                    else if (pType == typeof(int)) args[i] = 0;
                    else args[i] = null;
                }
                anyMi.Invoke(displayComp, args);
            }
        }
        catch { }
    }

    void TryInvokeSetCounter(Component counterComp, int cardId)
    {
        if (counterComp == null) return;
        try
        {
            var t = counterComp.GetType();
            var mi = t.GetMethod("SetInfo", BindingFlags.Public | BindingFlags.Instance);
            if (mi != null)
            {
                var ps = mi.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i].ParameterType;
                    if (p == typeof(int)) args[i] = cardId;
                    else if (p == typeof(string)) args[i] = null;
                    else if (p == typeof(bool)) args[i] = false;
                    else args[i] = null;
                }
                mi.Invoke(counterComp, args);
                return;
            }

            var mi2 = t.GetMethod("SetCount", BindingFlags.Public | BindingFlags.Instance);
            if (mi2 != null)
            {
                int cnt = 0;
                try
                {
                    var gm = t.Assembly.GetType("CardCounter");
                    if (gm != null)
                    {
                        var gmi = gm.GetMethod("GetPlayerCount", BindingFlags.Public | BindingFlags.Static);
                        if (gmi != null) cnt = (int)gmi.Invoke(null, new object[] { cardId });
                    }
                }
                catch { cnt = 0; }
                mi2.Invoke(counterComp, new object[] { cnt });
                return;
            }
        }
        catch { }
    }

    string GetCardNameFromDef(object def)
    {
        if (def == null) return null;
        try
        {
            var t = def.GetType();
            var p = t.GetProperty("Card_Name") ?? t.GetProperty("Name") ?? t.GetProperty("cardName");
            if (p != null) return p.GetValue(def) as string;
            var f = t.GetField("Card_Name") ?? t.GetField("Name");
            if (f != null) return f.GetValue(def) as string;
        }
        catch { }
        return null;
    }

    string GetCardDescFromDef(object def)
    {
        if (def == null) return null;
        try
        {
            var t = def.GetType();
            var p = t.GetProperty("Card_Description") ?? t.GetProperty("Description") ?? t.GetProperty("Desc");
            if (p != null) return p.GetValue(def) as string;
            var f = t.GetField("Card_Description") ?? t.GetField("Description");
            if (f != null) return f.GetValue(def) as string;
        }
        catch { }
        return null;
    }
    // ---------- 补充/公用方法结束 ----------

    object[] BuildArgsForMethod(ParameterInfo[] ps, CardMessage def, Transform parent, int count, bool attachInfo)
    {
        if (ps == null || ps.Length == 0) return new object[0];

        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var pType = ps[i].ParameterType;
            if (typeof(CardMessage).IsAssignableFrom(pType))
            {
                args[i] = def;
            }
            else if (typeof(Transform).IsAssignableFrom(pType) || (typeof(UnityEngine.Object).IsAssignableFrom(pType) && pType.Name == "Transform"))
            {
                args[i] = parent;
            }
            else if (pType == typeof(int))
            {
                args[i] = count;
            }
            else if (pType == typeof(bool))
            {
                args[i] = attachInfo;
            }
            else if (pType == typeof(object))
            {
                args[i] = def;
            }
            else
            {
                if (pType == typeof(string)) args[i] = null;
                else args[i] = null;
            }
        }
        return args;
    }

    Dictionary<int, int> ParsePlayerDataCsvSimple(string text)
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
            if (tag == "card")
            {
                if (int.TryParse(parts[1].Trim(), out int id) && int.TryParse(parts[2].Trim(), out int cnt))
                {
                    if (cnt > 0) { if (dict.ContainsKey(id)) dict[id] += cnt; else dict[id] = cnt; }
                }
            }
        }
        return dict;
    }

    bool ShouldAttachCardInfo()
    {
        if (forceAttachInfo) return true;
        if (!defaultShowInfo) return false;
        if (!onlyInstantiateInfoInPoolScene) return true;
        return SceneManager.GetActiveScene().name == poolSceneName;
    }

    void ClearLibrary()
    {
        if (libraryPanel == null) return;
        for (int i = libraryPanel.childCount - 1; i >= 0; i--)
            Destroy(libraryPanel.GetChild(i).gameObject);
    }
}
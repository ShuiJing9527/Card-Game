using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DeckManager : MonoBehaviour
{
    [Header("UI")]
    public Transform contentParent;                 // ScrollView Content (必填)
    public TextMeshProUGUI debugText;               // 可选，显示调试信息

    [Header("Fallback CSV")]
    public TextAsset fallbackPlayerDataCsv;

    [Header("Attach options (when calling InstantiateCardItem)")]
    // 恢复默认：Attach Info 可由 Inspector 控制（通常为 true），
    // 我们会在实例化后精确隐藏信息面板，保留装饰（边框/勾玉/等级）
    public bool attachInfo = true;

    [Header("CSV merge strategy")]
    public CsvMergeStrategy csvMergeStrategy = CsvMergeStrategy.PreferDeckThenCard;

    public enum CsvMergeStrategy
    {
        PreferDeckThenCard,
        PreferCardThenDeck,
        SumBoth
    }

    [Header("Debug / Test Options")]
    public bool forceUseCsv = false;      // 强制使用 CSV（忽略 PlayerDataManager）用于调试
    public bool testOnlyDeckLines = true; // 解析 CSV 时只解析 tag == "deck"（用于定位问题）

    // cached instances
    CardStore cardStore => CardStore.Instance;
    PlayerDataManager pData => PlayerDataManager.Instance;

    void Start()
    {
        RefreshDeckUI();
    }

    // public entry
    public void RefreshDeckUI()
    {
        if (contentParent == null)
        {
            DebugLog("DeckManager: contentParent 未绑定");
            return;
        }

        ClearSlots();

        Dictionary<int, int> deckDict = null;
        string usedSource = "none";

        // 1) 尝试从 PlayerDataManager 读取（除非强制使用 CSV）
        if (!forceUseCsv && pData != null)
        {
            try
            {
                var fromPd = GetDeckFromPlayerDataManager();
                if (fromPd != null && fromPd.Count > 0)
                {
                    deckDict = fromPd;
                    usedSource = "PlayerDataManager";
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"DeckManager: 读取 PlayerDataManager 时异常: {ex.Message}");
            }
        }

        // 2) 回退到 CSV（或强制使用 CSV）
        if ((deckDict == null || deckDict.Count == 0) && fallbackPlayerDataCsv != null)
        {
            try
            {
                deckDict = ParsePlayerDataCsvSimple(fallbackPlayerDataCsv.text, csvMergeStrategy, testOnlyDeckLines);
                usedSource = "FallbackCSV";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"DeckManager: 解析 CSV 时异常: {ex.Message}");
            }
        }

        if (deckDict == null || deckDict.Count == 0)
        {
            DebugLog($"DeckManager: 未找到 deck 数据（source={usedSource}）");
            return;
        }

        // 调试输出读取到的条目
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"DeckManager: 使用数据源 = {usedSource}，共 {deckDict.Count} 条");
            int c = 0;
            foreach (var kv in deckDict)
            {
                sb.AppendLine($"  id={kv.Key} -> count={kv.Value}");
                if (++c > 50) { sb.AppendLine("  ..."); break; }
            }
            DebugLog(sb.ToString());
        }

        // 尝试按 CardStore.cardList 的顺序来排列（如果可用）
        List<KeyValuePair<int, int>> ordered;
        try
        {
            var indexMap = new Dictionary<int, int>();
            if (cardStore != null)
            {
                var csType = cardStore.GetType();
                IEnumerable<object> cardListEnum = null;

                var field = csType.GetField("cardList", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                            ?? csType.GetField("CardList", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    var val = field.GetValue(cardStore);
                    cardListEnum = val as IEnumerable<object>;
                }
                else
                {
                    var prop = csType.GetProperty("cardList", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                               ?? csType.GetProperty("CardList", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (prop != null)
                    {
                        var val = prop.GetValue(cardStore);
                        cardListEnum = val as IEnumerable<object>;
                    }
                }

                if (cardListEnum != null)
                {
                    int idx = 0;
                    foreach (var cdef in cardListEnum)
                    {
                        if (cdef == null) { idx++; continue; }
                        try
                        {
                            var t = cdef.GetType();
                            var pid = t.GetProperty("id") ?? t.GetProperty("Id") ?? t.GetProperty("cardId") ?? t.GetProperty("CardId");
                            int id = -1;
                            if (pid != null) id = Convert.ToInt32(pid.GetValue(cdef));
                            else
                            {
                                var fid = t.GetField("id") ?? t.GetField("Id") ?? t.GetField("cardId") ?? t.GetField("CardId");
                                if (fid != null) id = Convert.ToInt32(fid.GetValue(cdef));
                            }
                            if (id >= 0 && !indexMap.ContainsKey(id)) indexMap[id] = idx;
                        }
                        catch { }
                        idx++;
                    }
                }
            }

            ordered = deckDict.OrderBy(kv =>
            {
                if (indexMap.TryGetValue(kv.Key, out int i)) return i;
                // unknown ids go after known ones, but keep stable order by id
                return int.MaxValue - kv.Key;
            }).ToList();
        }
        catch
        {
            ordered = deckDict.OrderBy(kv => kv.Key).ToList();
        }

        // 遍历并实例化 UI
        int created = 0;
        foreach (var kv in ordered)
        {
            int cardId = kv.Key;
            int cnt = kv.Value;
            if (cnt <= 0) continue;

            object defObj = null;
            // 尝试通过 CardStore.GetCardById 获取定义（若可用）
            try
            {
                if (cardStore != null)
                {
                    var csType = cardStore.GetType();
                    var gm = csType.GetMethod("GetCardById", BindingFlags.Public | BindingFlags.Instance)
                             ?? csType.GetMethod("GetCard", BindingFlags.Public | BindingFlags.Instance);
                    if (gm != null)
                    {
                        defObj = gm.Invoke(cardStore, new object[] { cardId });
                    }
                    else
                    {
                        // 尝试直接在 cardList 中查找
                        try
                        {
                            var field = csType.GetField("cardList", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                                     ?? csType.GetField("CardList", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            if (field != null)
                            {
                                var listObj = field.GetValue(cardStore) as System.Collections.IEnumerable;
                                if (listObj != null)
                                {
                                    foreach (var item in listObj)
                                    {
                                        int id = -1;
                                        var t = item.GetType();
                                        var pid = t.GetProperty("id") ?? t.GetProperty("Id") ?? t.GetProperty("cardId");
                                        if (pid != null) id = Convert.ToInt32(pid.GetValue(item));
                                        else
                                        {
                                            var fid = t.GetField("id") ?? t.GetField("Id") ?? t.GetField("cardId");
                                            if (fid != null) id = Convert.ToInt32(fid.GetValue(item));
                                        }
                                        if (id == cardId) { defObj = item; break; }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { defObj = null; }

            GameObject instance = TryInstantiateViaCardStoreOrOpenPackage(defObj, cnt, attachInfo);
            if (instance == null)
            {
                instance = CreateFallbackTextItem(defObj, cardId, cnt);
            }

            if (instance != null)
            {
                instance.transform.SetParent(contentParent, false);

                // 双保险：实例化后立刻尝试隐藏“信息面板”相关子对象（但保留装饰）
                HideCardInfo(instance);

                created++;
            }
        }

        DebugLog($"DeckManager: 刷新完成，创建项={created}");
    }

    void ClearSlots()
    {
        if (contentParent == null) return;
        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            var c = contentParent.GetChild(i);
            if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
        }
    }

    // ========== Instantiate helpers ==========
    GameObject TryInstantiateViaCardStoreOrOpenPackage(object defObj, int count, bool attachInfoFlag)
    {
        // try CardStore.InstantiateCardItem
        if (cardStore != null)
        {
            MethodInfo mi = cardStore.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (mi != null)
            {
                try
                {
                    var args = BuildArgsForMethod(mi.GetParameters(), defObj, contentParent, count, attachInfoFlag);
                    var res = mi.Invoke(cardStore, args) as GameObject;
                    if (res != null) return res;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"DeckManager: CardStore.InstantiateCardItem 调用失败: {ex.Message}");
                }
            }
        }

        // try OpenPackage attached on CardStore (按类型名查找)
        if (cardStore != null)
        {
            var opComp = FindComponentOn(cardStore.gameObject, "OpenPackage");
            if (opComp != null)
            {
                var mi = opComp.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (mi != null)
                {
                    try
                    {
                        var args = BuildArgsForMethod(mi.GetParameters(), defObj, contentParent, count, attachInfoFlag);
                        var res = mi.Invoke(opComp, args) as GameObject;
                        if (res != null) return res;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"DeckManager: OpenPackage (from CardStore) InstantiateCardItem 调用失败: {ex.Message}");
                    }
                }
            }
        }

        // try global OpenPackage (搜索全局对象)
        var openPkg = FindComponentByTypeName("OpenPackage");
        if (openPkg != null)
        {
            var mi = openPkg.GetType().GetMethod("InstantiateCardItem", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (mi != null)
            {
                try
                {
                    var args = BuildArgsForMethod(mi.GetParameters(), defObj, contentParent, count, attachInfoFlag);
                    var res = mi.Invoke(openPkg, args) as GameObject;
                    if (res != null) return res;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"DeckManager: 全局 OpenPackage.InstantiateCardItem 调用失败: {ex.Message}");
                }
            }
        }

        return null;
    }

    // 构建反射参数：对 bool 参数使用传入的 attachInfoFlag（不要一律 false）
    object[] BuildArgsForMethod(ParameterInfo[] ps, object defObj, Transform parent, int count, bool attachInfoFlag)
    {
        if (ps == null || ps.Length == 0) return new object[0];
        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var pInfo = ps[i];
            var pType = pInfo.ParameterType;

            if (defObj != null && pType.IsAssignableFrom(defObj.GetType()))
            {
                args[i] = defObj;
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
                // 使用传入的 attachInfoFlag，让调用方决定是否生成附加信息（装饰/信息面板）
                args[i] = attachInfoFlag;
            }
            else if (pType == typeof(object))
            {
                args[i] = defObj;
            }
            else if (pType == typeof(string))
            {
                args[i] = null;
            }
            else
            {
                args[i] = null;
            }
        }
        return args;
    }

    // 从指定 GameObject 上按类型名查找组件（用于寻找 OpenPackage 在 cardStore 上）
    Component FindComponentOn(GameObject go, string typeName)
    {
        if (go == null || string.IsNullOrEmpty(typeName)) return null;
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;
            if (comp.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return comp as Component;
        }
        return null;
    }

    // 在全局范围按类型名查找组件实例
    Component FindComponentByTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        try
        {
            // 先尝试通过加载的程序集找类型
            Type found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t != null && string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        {
                            found = t;
                            break;
                        }
                    }
                }
                catch { }
                if (found != null) break;
            }

            if (found != null)
            {
                var obj = UnityEngine.Object.FindObjectOfType(found);
                return obj as Component;
            }

            // 降级：遍历所有 MonoBehaviour 实例
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                if (mb.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                    return mb;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"DeckManager: FindComponentByTypeName 异常: {ex.Message}");
        }
        return null;
    }

    // ========== Fallback 创建项 ==========
    GameObject CreateFallbackTextItem(object defObj, int cardId, int count)
    {
        var go = new GameObject($"Card_{cardId}");
        var rt = go.AddComponent<RectTransform>();
        TextMeshProUGUI tmp = null;
        try
        {
            tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 18;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            string nameText = null;
            try
            {
                if (defObj != null)
                {
                    var t = defObj.GetType();
                    var p = t.GetProperty("Card_Name") ?? t.GetProperty("Name") ?? t.GetProperty("cardName") ?? t.GetProperty("card_name");
                    if (p != null) nameText = p.GetValue(defObj) as string;
                }
            }
            catch { }
            tmp.text = $"{(nameText ?? ("ID" + cardId))}  ×{count}";
        }
        catch
        {
            var t = go.AddComponent<Text>();
            t.text = $"ID{cardId}  ×{count}";
            t.fontSize = 14;
            t.color = Color.white;
        }
        rt.sizeDelta = new Vector2(300, 40);
        return go;
    }

    // ========== HideCardInfo（精确） ==========
    void HideCardInfo(GameObject instance)
    {
        if (instance == null) return;

        // 只隐藏明确的“信息/详情/Tooltip”面板，不触碰装饰性节点（Border/勾玉/CardLv 等）
        var infoNames = new[] { "卡片信息", "CardInfo", "cardInfo", "InfoPanel", "Card_Detail", "卡片详情", "DetailPanel", "Tooltip", "卡片信息面板" };

        foreach (var t in instance.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.gameObject == null) continue;
            var nm = t.name ?? "";
            foreach (var name in infoNames)
            {
                if (nm.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t.gameObject.SetActive(false);
                    break;
                }
            }
        }

        // 保险：不要去模糊禁用其他脚本。只确保 CardLv 脚本处于启用状态（如果存在的话）
        try
        {
            foreach (var lv in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var t = lv.GetType();
                if (string.Equals(t.Name, "CardLv", StringComparison.OrdinalIgnoreCase))
                {
                    if (!lv.enabled) lv.enabled = true;
                }
            }
        }
        catch { }
    }

    // ========== Deck 获取逻辑 ==========
    Dictionary<int, int> GetDeckFromPlayerDataManager()
    {
        var result = new Dictionary<int, int>();
        if (pData == null) return result;
        try
        {
            // 常见属性/字段尝试读取
            var pdType = pData.GetType();

            // 1) PlayerDeckDict 属性/字段
            var prop = pdType.GetProperty("PlayerDeckDict", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? pdType.GetProperty("playerDeckDict", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                var dictObj = prop.GetValue(pData) as System.Collections.IDictionary;
                if (dictObj != null)
                {
                    foreach (var k in dictObj.Keys)
                    {
                        int id = Convert.ToInt32(k);
                        int cnt = Convert.ToInt32(dictObj[k]);
                        if (cnt > 0) result[id] = cnt;
                    }
                    if (result.Count > 0) return result;
                }
            }

            var field = pdType.GetField("PlayerDeck", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? pdType.GetField("playerDeck", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                var arr = field.GetValue(pData) as int[];
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] > 0) result[i] = arr[i];
                    if (result.Count > 0) return result;
                }
            }

            // 2) 常见方法尝试
            var methodsToTry = new string[] { "GetPlayerDeck", "GetPlayerCardCounts", "GetDeckDict", "GetDeck" };
            foreach (var mname in methodsToTry)
            {
                var m = pdType.GetMethod(mname, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    var res = m.Invoke(pData, null);
                    if (res is System.Collections.IDictionary dictRes)
                    {
                        foreach (var k in dictRes.Keys)
                        {
                            int id = Convert.ToInt32(k);
                            int cnt = Convert.ToInt32(dictRes[k]);
                            if (cnt > 0) result[id] = cnt;
                        }
                        if (result.Count > 0) return result;
                    }
                    else if (res is int[] arrRes)
                    {
                        for (int i = 0; i < arrRes.Length; i++)
                            if (arrRes[i] > 0) result[i] = arrRes[i];
                        if (result.Count > 0) return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"DeckManager: GetDeckFromPlayerDataManager 异常: {ex.Message}");
        }
        return result;
    }

    // ========== CSV 解析 ==========
    Dictionary<int, int> ParsePlayerDataCsvSimple(string text, CsvMergeStrategy mergeStrategy, bool onlyDeckLines = false)
    {
        var dictCard = new Dictionary<int, int>();
        var dictDeck = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(text)) return new Dictionary<int, int>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            var tag = parts[0].Trim().ToLower();
            if (onlyDeckLines && tag != "deck") continue;
            if ((tag == "card" || tag == "deck") && int.TryParse(parts[1].Trim(), out int id) && int.TryParse(parts[2].Trim(), out int cnt))
            {
                if (cnt <= 0) continue;
                if (tag == "card")
                {
                    if (dictCard.ContainsKey(id)) dictCard[id] += cnt; else dictCard[id] = cnt;
                }
                else
                {
                    dictDeck[id] = cnt;
                }
            }
        }

        var outDict = new Dictionary<int, int>();
        switch (mergeStrategy)
        {
            case CsvMergeStrategy.PreferDeckThenCard:
                foreach (var kv in dictDeck) outDict[kv.Key] = kv.Value;
                foreach (var kv in dictCard) if (!outDict.ContainsKey(kv.Key)) outDict[kv.Key] = kv.Value;
                break;
            case CsvMergeStrategy.PreferCardThenDeck:
                foreach (var kv in dictCard) outDict[kv.Key] = kv.Value;
                foreach (var kv in dictDeck) if (!outDict.ContainsKey(kv.Key)) outDict[kv.Key] = kv.Value;
                break;
            case CsvMergeStrategy.SumBoth:
                foreach (var kv in dictCard) outDict[kv.Key] = kv.Value;
                foreach (var kv in dictDeck) if (outDict.ContainsKey(kv.Key)) outDict[kv.Key] += kv.Value; else outDict[kv.Key] = kv.Value;
                break;
        }
        return outDict;
    }

    void DebugLog(string s)
    {
        if (debugText != null)
        {
            try { debugText.text = s; } catch { }
        }
        Debug.Log(s);
    }
}
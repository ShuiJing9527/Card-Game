using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LibraryManager : MonoBehaviour
{
    [Header("UI")]
    public Transform contentParent; // ScrollView Content (必填)
    public TextMeshProUGUI debugText; // 可选，显示调试信息

    [Header("Fallback CSV")]
    public TextAsset fallbackPlayerDataCsv;

    [Header("Attach options (when calling InstantiateCardItem)")]
    public bool attachInfo = true;

    [Header("CSV merge strategy")]
    public CsvMergeStrategy csvMergeStrategy = CsvMergeStrategy.PreferCardThenDeck;

    public enum CsvMergeStrategy
    {
        PreferDeckThenCard,
        PreferCardThenDeck,
        SumBoth
    }

    [Header("Debug / Test Options")]
    public bool forceUseCsv = false;      // 强制使用 CSV（忽略 PlayerDataManager）用于调试
    public bool onlyCardLines = true;     // 解析 CSV 时只解析 tag == "card"（默认 true）

    [Header("Library display options")]
    public bool forceShowCardInfo = true; // 卡池需要显示卡片信息面板（运行时强制 SetActive(true)）

    // cached instances
    CardStore cardStore => CardStore.Instance;
    PlayerDataManager pData => PlayerDataManager.Instance;

    // 记录最近一次从 CSV 解析得到的行顺序（id -> index）
    Dictionary<int, int> lastCsvOrderMap = null;

    void Start()
    {
        RefreshLibraryUI();
    }

    // public entry
    public void RefreshLibraryUI()
    {
        if (contentParent == null)
        {
            DebugLog("LibraryManager: contentParent 未绑定");
            return;
        }

        ClearSlots();

        Dictionary<int, int> cardDict = null;
        string usedSource = "none";

        // 1) 尝试从 PlayerDataManager 读取（除非强制使用 CSV）
        if (!forceUseCsv && pData != null)
        {
            try
            {
                var fromPd = GetCardsFromPlayerDataManager();
                if (fromPd != null && fromPd.Count > 0)
                {
                    cardDict = fromPd;
                    usedSource = "PlayerDataManager";
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LibraryManager: 读取 PlayerDataManager 时异常: {ex.Message}");
            }
        }

        // 2) 回退到 CSV（或强制使用 CSV）
        if ((cardDict == null || cardDict.Count == 0) && fallbackPlayerDataCsv != null)
        {
            try
            {
                Dictionary<int, int> csvOrder;
                cardDict = ParsePlayerDataCsvForCards(fallbackPlayerDataCsv.text, csvMergeStrategy, onlyCardLines, out csvOrder);
                lastCsvOrderMap = csvOrder;
                usedSource = "FallbackCSV";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LibraryManager: 解析 CSV 时异常: {ex.Message}");
            }
        }

        if (cardDict == null || cardDict.Count == 0)
        {
            DebugLog($"LibraryManager: 未找到 card 数据（source={usedSource}）");
            return;
        }

        // 调试输出读取到的条目
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"LibraryManager: 使用数据源 = {usedSource}，共 {cardDict.Count} 条");
            int c = 0;
            foreach (var kv in cardDict)
            {
                sb.AppendLine($"  id={kv.Key} -> count={kv.Value}");
                if (++c > 50) { sb.AppendLine("  ..."); break; }
            }
            DebugLog(sb.ToString());
        }

        // 排序：如果数据来自 CSV 且我们有 csv 行序映射，按 CSV 顺序；
        // 否则按 CardStore.cardList 顺序，未在 CardStore 中的按 id 正序追加
        List<KeyValuePair<int, int>> ordered;
        try
        {
            if (usedSource == "FallbackCSV" && lastCsvOrderMap != null && lastCsvOrderMap.Count > 0)
            {
                ordered = cardDict.OrderBy(kv =>
                {
                    if (lastCsvOrderMap.TryGetValue(kv.Key, out int idx)) return idx;
                    // 未在 csvOrder 中的放到后面，按 id 正序
                    return int.MaxValue / 2 + kv.Key;
                }).ToList();
            }
            else
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
                        var prop = csType.GetProperty("cardlist", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
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

                ordered = cardDict.OrderBy(kv =>
                {
                    if (indexMap.TryGetValue(kv.Key, out int i)) return i;
                    // unknown ids go after known ones, but keep stable order by id (正序)
                    return int.MaxValue / 2 + kv.Key;
                }).ToList();
            }
        }
        catch
        {
            ordered = cardDict.OrderBy(kv => kv.Key).ToList();
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

                // 标记为来自库的项，并设置 CardDragHandler 为克隆拖拽模式
                MarkAsLibraryItem(instance, cardId);

                // 卡池需要显示卡片信息时，运行时强制激活信息面板；否则隐藏信息面板（与 DeckManager 行为不同）
                if (forceShowCardInfo) ShowCardInfo(instance);
                else HideCardInfo(instance);
                created++;
            }
        }

        DebugLog($"LibraryManager: 刷新完成，创建项={created}");
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

    // ========== 新增方法：标记为库项，并确保 CardDragHandler.createCloneOnDrag = true ==========
    void MarkAsLibraryItem(GameObject instance, int cardId)
    {
        if (instance == null) return;

        // 添加标记组件（便于在其它地方做判断）
        var libMark = instance.GetComponent<LibraryItem>();
        if (libMark == null) libMark = instance.AddComponent<LibraryItem>();
        libMark.cardId = cardId;

        // 确保有 CardDragHandler，并设置为在拖拽时创建 clone 的模式
        var ch = instance.GetComponent<CardDragHandler>();
        if (ch == null)
        {
            ch = instance.AddComponent<CardDragHandler>();
        }
        ch.cardId = cardId;

        // 仅库项启用 createCloneOnDrag
        ch.createCloneOnDrag = true;
    }

    // 兼容：当我们不知道 cardId（如从外部直接拖回 GameObject），也提供一个不带 cardId 的标记函数
    void MarkAsLibraryItem(GameObject instance)
    {
        MarkAsLibraryItem(instance, -1);
    }

    // LibraryItem 标记类
    public class LibraryItem : MonoBehaviour
    {
        public int cardId;
    }

    // ========== Instantiate helpers (与 DeckManager 一致) ==========
    GameObject TryInstantiateViaCardStoreOrOpenPackage(object defObj, int count, bool attachInfoFlag)
    {
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
                    Debug.LogWarning($"LibraryManager: CardStore.InstantiateCardItem 调用失败: {ex.Message}");
                }
            }
        }

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
                        Debug.LogWarning($"LibraryManager: OpenPackage (from CardStore) InstantiateCardItem 调用失败: {ex.Message}");
                    }
                }
            }
        }

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
                    Debug.LogWarning($"LibraryManager: 全局 OpenPackage.InstantiateCardItem 调用失败: {ex.Message}");
                }
            }
        }

        return null;
    }

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

    Component FindComponentByTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        try
        {
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

            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                if (mb.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                    return mb;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LibraryManager: FindComponentByTypeName 异常: {ex.Message}");
        }
        return null;
    }

    // ========== Fallback 创建项 ==========
    GameObject CreateFallbackTextItem(object defObj, int cardId, int count)
    {
        var go = new GameObject($"LibraryCard_{cardId}");
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

    // ========== Show / Hide Card Info（Library 会使用 ShowCardInfo） ==========
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

    void ShowCardInfo(GameObject instance)
    {
        if (instance == null) return;

        // 关键：如果这是拖拽生成的 clone，则跳过，不为其打开信息面板
        if (IsDragClone(instance)) return;

        var infoNames = new[] {
        "卡片信息", "CardInfo", "cardInfo", "InfoPanel", "Card_Detail",
        "卡片详情", "DetailPanel", "Tooltip", "卡片信息面板"
    };

        try
        {
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == null) continue;
                var nm = t.name ?? "";
                foreach (var name in infoNames)
                {
                    if (nm.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.gameObject.SetActive(true);
                        break;
                    }
                }
            }
        }
        catch { }

        // 确保 CardLv 脚本处于启用状态
        try
        {
            foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (string.Equals(t.Name, "CardLv", StringComparison.OrdinalIgnoreCase))
                {
                    if (!mb.enabled) mb.enabled = true;
                }
            }
        }
        catch { }
    }

    // 判断是否为拖拽 clone：优先检测 DragCloneMarker 组件，其次兼容名字包含 "_DragClone"
    bool IsDragClone(GameObject go)
    {
        if (go == null) return false;

        // 优先检测你单独创建的 DragCloneMarker 组件
        try
        {
            if (go.GetComponent<DragCloneMarker>() != null) return true;
        }
        catch { }

        // 兼容：名字标记
        var nm = go.name ?? "";
        if (nm.IndexOf("_DragClone", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        // 额外保险：检测任意组件名为 DragCloneMarker（防止命名空间差异）
        try
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (string.Equals(comp.GetType().Name, "DragCloneMarker", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }

        return false;
    }

    // ========== 从 PlayerDataManager 获取卡片数据（尝试常见字段/方法） ==========
    Dictionary<int, int> GetCardsFromPlayerDataManager()
    {
        var result = new Dictionary<int, int>();
        if (pData == null) return result;
        try
        {
            var pdType = pData.GetType();

            // 1) PlayerCardDict / PlayerCardCounts 等常见命名尝试
            var prop = pdType.GetProperty("PlayerCardDict", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? pdType.GetProperty("playerCardDict", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? pdType.GetProperty("PlayerCardCounts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? pdType.GetProperty("playerCardCounts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
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

            var field = pdType.GetField("PlayerCards", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? pdType.GetField("playerCards", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? pdType.GetField("PlayerCardCounts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? pdType.GetField("playerCardCounts", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
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
            var methodsToTry = new string[] { "GetPlayerCards", "GetPlayerCardCounts", "GetCardDict", "GetCards", "GetAllCards" };
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
            Debug.LogWarning($"LibraryManager: GetCardsFromPlayerDataManager 异常: {ex.Message}");
        }
        return result;
    }

    // ========== CSV 解析（专注 card 行），并返回行顺序映射 outOrderMap ==========
    Dictionary<int, int> ParsePlayerDataCsvForCards(string text, CsvMergeStrategy mergeStrategy, bool onlyCardLines, out Dictionary<int, int> outOrderMap)
    {
        var dictCard = new Dictionary<int, int>();
        var dictDeck = new Dictionary<int, int>();
        outOrderMap = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(text)) return new Dictionary<int, int>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int orderCounter = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            var tag = parts[0].Trim().ToLower();
            if (onlyCardLines && tag != "card") continue;
            if ((tag == "card" || tag == "deck") && int.TryParse(parts[1].Trim(), out int id) && int.TryParse(parts[2].Trim(), out int cnt))
            {
                if (cnt <= 0) continue;
                if (tag == "card")
                {
                    if (dictCard.ContainsKey(id)) dictCard[id] += cnt;
                    else
                    {
                        dictCard[id] = cnt;
                        // 记录 CSV 中 card 首次出现的顺序（仅在 tag == "card" 时）
                        if (!outOrderMap.ContainsKey(id))
                        {
                            outOrderMap[id] = orderCounter++;
                        }
                    }
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

    // ===================== 新增：当卡从卡组移回库时调用 =====================
    /// <summary>
    /// 仅内存层面处理：把一张卡从 playerDeck 移回 playerCards（不写盘）。
    /// 返回 true 表示成功（caller 可据此决定后续行为）。
    /// 会刷新 Library UI（简单实现，必要时可优化成只更新单项）。
    /// </summary>
    public bool OnCardReturnedToLibraryNoSave(int cardId)
    {
        if (cardId < 0) return false;
        if (pData == null)
        {
            DebugLog($"LibraryManager: 无 PlayerDataManager，无法把 cardId={cardId} 返回到库");
            return false;
        }

        bool ok = false;
        try
        {
            ok = pData.TryTransferCardFromDeckNoSave(cardId, 1);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LibraryManager: OnCardReturnedToLibraryNoSave 调用 PlayerDataManager 异常: {ex.Message}");
            ok = false;
        }

        if (ok)
        {
            // 简单刷新整个库 UI（如果频繁操作可以换成更细粒度更新）
            RefreshLibraryUI();
        }
        else
        {
            DebugLog($"LibraryManager: 返回卡片到库失败 id={cardId}");
        }

        return ok;
    }

    /// <summary>
    /// 把卡从卡组移回库（兼容 autoSave）：调用 TryTransferCardFromDeck（会根据 PlayerDataManager.autoSave 决定是否写盘）
    /// 并刷新 Library UI。
    /// </summary>
    public void OnCardReturnedToLibrary(int cardId)
    {
        if (cardId < 0) return;
        if (pData == null)
        {
            DebugLog($"LibraryManager: 无 PlayerDataManager，无法把 cardId={cardId} 返回到库");
            return;
        }

        bool ok = false;
        try
        {
            ok = pData.TryTransferCardFromDeck(cardId, 1);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LibraryManager: OnCardReturnedToLibrary 调用 PlayerDataManager 异常: {ex.Message}");
            ok = false;
        }

        if (ok)
        {
            RefreshLibraryUI();
        }
        else
        {
            DebugLog($"LibraryManager: 返回卡片到库失败 id={cardId}");
        }
    }

    // ========== 兼容重载：接受 GameObject / Transform / CardDragHandler ==========
    public void OnCardReturnedToLibrary(GameObject card)
    {
        if (card == null) return;

        // 把对象放回库的 contentParent（不改变世界坐标上的显示方式）
        if (contentParent != null)
        {
            card.transform.SetParent(contentParent, false);
            card.transform.SetAsLastSibling();
        }

        // 标记为库项并隐藏信息
        MarkAsLibraryItem(card);
        HideCardInfo(card);
    }

    public void OnCardReturnedToLibrary(Transform t)
    {
        if (t == null) return;
        OnCardReturnedToLibrary(t.gameObject);
    }

    public void OnCardReturnedToLibrary(CardDragHandler handler)
    {
        if (handler == null) return;
        OnCardReturnedToLibrary(handler.gameObject);
    }
}
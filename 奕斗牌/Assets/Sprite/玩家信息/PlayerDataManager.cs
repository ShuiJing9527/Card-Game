using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(0)]
public class PlayerDataManager : MonoBehaviour
{
    public static PlayerDataManager Instance { get; private set; }

    [Header("玩家数据")]
    public int playerCoins = 0;

    [HideInInspector]
    public Dictionary<int, int> playerCards = new Dictionary<int, int>(); // 库存/卡池

    [Header("玩家卡组（兼容老数组）")]
    public int[] playerDeck;

    [HideInInspector]
    public Dictionary<int, int> playerDeckDict = new Dictionary<int, int>(); // 卡组（cardId -> count）

    [SerializeField, Tooltip("仅用于在 Inspector 查看（通过 SyncDictionaryToEditorLists 同步）")]
    private List<int> editorKeys = new List<int>();
    [SerializeField, Tooltip("仅用于在 Inspector 查看（通过 SyncDictionaryToEditorLists 同步）")]
    private List<int> editorValues = new List<int>();

    [Header("依赖与配置")]
    public CardStore cardStore;
    public TextAsset playerData;

    [Tooltip("启动时是否用 playerData 覆盖磁盘文件（谨慎使用，建议默认 false）")]
    public bool overwriteFromTextAssetOnStart = false;

    [Header("抽卡/开包配置")]
    public int openCost = 10;

    [Header("自动保存控制（新增）")]
    [Tooltip("开启后，修改数据会自动触发写盘（模拟旧行为）。建议默认关闭，由上层显式调用 SavePlayerData。")]
    public bool autoSave = false;
    [Tooltip("开启后，仅当数据实际改变时才写盘（避免重复写入相同内容）")]
    public bool onlySaveOnChange = true;

    [Header("金币保存控制")]
    [Tooltip("如果勾选：每次保存都会把当前 Inspector 中的 playerCoins 写入磁盘（旧行为）。\n建议默认不勾选。")]
    public bool autoUpdateCoinsOnSave = false;

    [Tooltip("手动用法：在 Inspector 修改 playerCoins 后，勾选此项并触发一次保存 (Save / 任何触发保存的操作)，会写入你在 Inspector 中设置的值，保存后此选项会自动取消。")]
    public bool applyInspectorCoinsOnce = false;

    public string saveFileName = "playerdata.csv";

    public event Action OnPlayerDataLoaded;
    string savePath => GetSavePath();

    // 事件：卡组变化（cardId, newCount）
    public event Action<int, int> OnDeckChanged;
    // 事件：库存变化（cardId, newCount）
    public event Action<int, int> OnInventoryChanged;
    // 事件：保存完成
    public event Action OnPlayerDataSaved;

    // 防重复初始化标志
    private bool dataInitialized = false;
    // 数据变更标记（仅当 onlySaveOnChange 启用时使用）
    private bool dataChanged = false;
    // 上一次保存的 Hash，用于检测相同内容（仅在 onlySaveOnChange 启用时维护）
    private int lastSavedHash = 0;

    void Awake()
    {
        // 更严格的单例检查：发现重复则立即销毁当前实例
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"PlayerDataManager: 检测到重复实例。销毁当前实例 name={gameObject.name} id={GetInstanceID()} 主实例 id={Instance.GetInstanceID()}");
#if UNITY_EDITOR
            DestroyImmediate(gameObject);
#else
            Destroy(gameObject);
#endif
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (cardStore == null)
            cardStore = FindObjectOfType<CardStore>();

        SetupCardStoreListener();
    }

    void SetupCardStoreListener()
    {
        if (cardStore == null)
        {
            Debug.Log("PlayerDataManager: 未找到 CardStore，直接初始化玩家数据（card 校验将不可用）");
            InitPlayerData();
            return;
        }

        try
        {
            if (cardStore.IsCardsReady)
            {
                InitPlayerData();
                return;
            }

            cardStore.OnCardsReady += OnCardStoreReady;
        }
        catch (Exception)
        {
            // 兼容 CardStore 可能缺少 IsCardsReady / OnCardsReady 的情况
            InitPlayerData();
        }
    }

    void OnCardStoreReady()
    {
        if (cardStore == null) return;
        try { cardStore.OnCardsReady -= OnCardStoreReady; } catch { }
        InitPlayerData();
    }

    void InitPlayerData()
    {
        if (dataInitialized)
        {
            Debug.Log("PlayerDataManager: InitPlayerData 已初始化，跳过重复调用");
            return;
        }
        dataInitialized = true;

        EnsurePlayerDeckInitialized();

        WriteBundledTextAssetToDisk();
        LoadPlayerData();
    }

    string GetSavePath()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.dataPath, "Datas/Player", saveFileName);
#else
        return Path.Combine(Application.persistentDataPath, saveFileName);
#endif
    }

    void WriteBundledTextAssetToDisk()
    {
        if (playerData == null) return;

        try
        {
            string dir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(savePath))
            {
                File.WriteAllText(savePath, playerData.text);
                Debug.Log($"PlayerDataManager: 初始内置 CSV 写入磁盘: {savePath}");
#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif
                return;
            }

#if UNITY_EDITOR
            if (overwriteFromTextAssetOnStart)
            {
                Debug.LogWarning("PlayerDataManager: overwriteFromTextAssetOnStart 为 true，将用内置 CSV 覆盖磁盘文件。");
                File.WriteAllText(savePath, playerData.text);
                AssetDatabase.Refresh();
            }
#else
            if (overwriteFromTextAssetOnStart)
            {
                Debug.LogWarning("PlayerDataManager: 在非编辑器环境中启用了 overwriteFromTextAssetOnStart，这可能覆盖玩家数据，建议关闭。");
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"PlayerDataManager: 写入内置 CSV 失败: {ex.Message}");
        }
    }

    public void LoadPlayerData()
    {
        try
        {
            Debug.Log($"PlayerDataManager: 开始加载玩家数据 -> {savePath}");
            string[] rows = null;

            if (playerData != null && !File.Exists(savePath))
            {
                rows = playerData.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log("PlayerDataManager: 从内置 CSV 加载玩家数据（磁盘文件不存在）");
            }
            else if (File.Exists(savePath))
            {
                rows = File.ReadAllLines(savePath);
                Debug.Log($"PlayerDataManager: 从磁盘文件加载玩家数据: {savePath}");
            }
            else
            {
                Debug.Log("PlayerDataManager: 未找到玩家数据文件，加载默认空数据");
                playerCards = new Dictionary<int, int>();
                playerDeckDict = new Dictionary<int, int>();
                EnsurePlayerDeckInitialized();
                return;
            }

            EnsurePlayerDeckInitialized();

            // reset
            playerCards = new Dictionary<int, int>();
            playerDeckDict = new Dictionary<int, int>();
            if (playerDeck != null)
            {
                for (int i = 0; i < playerDeck.Length; i++) playerDeck[i] = 0;
            }
            playerCoins = 0;

            foreach (var rawRow in rows)
            {
                if (string.IsNullOrWhiteSpace(rawRow)) continue;
                string line = rawRow.Trim().Trim('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 2) continue;

                string key = parts[0].Trim().ToLower();

                if (key == "coins")
                {
                    if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int coins))
                    {
                        playerCoins = coins;
                        Debug.Log($"PlayerDataManager: 加载到 coins = {coins}");
                    }
                    continue;
                }

                if (key == "card" && parts.Length >= 3)
                {
                    if (!int.TryParse(parts[1].Trim(), out int id)) continue;
                    if (!int.TryParse(parts[2].Trim(), out int count)) continue;

                    if (count > 0) playerCards[id] = count;
                    continue;
                }

                if (key == "deck" && parts.Length >= 3)
                {
                    if (!int.TryParse(parts[1].Trim(), out int id)) continue;
                    if (!int.TryParse(parts[2].Trim(), out int num)) continue;

                    if (num > 0) playerDeckDict[id] = num;
                    else playerDeckDict.Remove(id);

                    if (playerDeck != null && id >= 0 && id < playerDeck.Length)
                    {
                        playerDeck[id] = Math.Max(0, num);
                    }
                    continue;
                }
            }

            // 加载完成后初始化最后保存 Hash
            lastSavedHash = CalculateDataHash();
            dataChanged = false;

            Debug.Log($"PlayerDataManager: 加载完成 -> coins={playerCoins} inventoryEntries={playerCards.Count} deckEntries={playerDeckDict.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"PlayerDataManager: 加载玩家数据失败: {ex.Message}\n{ex.StackTrace}");
        }

        // 在 LoadPlayerData 结束前触发（确保订阅者知道数据已就绪）
        try
        {
            OnPlayerDataLoaded?.Invoke();
        }
        catch { }
    }

    // ---------- 新增：计算当前数据的 Hash（仅用于 onlySaveOnChange 检测） ----------
    private int CalculateDataHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + playerCoins.GetHashCode();
            if (playerCards != null)
            {
                foreach (var kv in playerCards)
                {
                    hash = hash * 31 + kv.Key.GetHashCode();
                    hash = hash * 31 + kv.Value.GetHashCode();
                }
            }
            if (playerDeckDict != null)
            {
                foreach (var kv in playerDeckDict)
                {
                    hash = hash * 31 + kv.Key.GetHashCode();
                    hash = hash * 31 + kv.Value.GetHashCode();
                }
            }
            return hash;
        }
    }

    // 公开接口：coins - 分离内存操作与写盘
    public void SetCoins(int coins, bool saveOverride = false)
    {
        coins = Mathf.Max(0, coins);
        if (playerCoins == coins)
        {
            // 无变更，不触发任何操作
            return;
        }

        int old = playerCoins;
        playerCoins = coins;
        Debug.Log($"PlayerDataManager: SetCoins {old} -> {playerCoins} (autoSave={autoSave} saveOverride={saveOverride})");

        dataChanged = true;
        // 按优先级决定是否保存：显式 override > autoSave
        if (saveOverride || autoSave)
        {
            SavePlayerData();
        }
    }

    public int GetCoins() => playerCoins;

    // 公开接口：库存/卡组 - 分离内存操作与写盘
    public int GetCardCount(int cardId)
    {
        if (cardId < 0) return 0;
        if (playerCards == null) playerCards = new Dictionary<int, int>();
        if (playerCards.TryGetValue(cardId, out int c)) return c;
        return 0;
    }

    /// <summary>
    /// 设置库存数量（仅修改内存，不写盘） - 由上层控制何时保存
    /// </summary>
    public void SetCardCountNoSave(int cardId, int count)
    {
        if (cardId < 0) return;
        if (playerCards == null) playerCards = new Dictionary<int, int>();
        count = Math.Max(0, count);

        int oldCount = GetCardCount(cardId);
        if (oldCount == count)
        {
            // 避免无谓操作
            return;
        }

        if (count == 0)
            playerCards.Remove(cardId);
        else
            playerCards[cardId] = count;

        dataChanged = true;
        OnInventoryChanged?.Invoke(cardId, GetCardCount(cardId));
    }

    /// <summary>
    /// 设置库存数量（原行为）：自动按 autoSave 决定是否写盘
    /// </summary>
    public void SetCardCount(int cardId, int count)
    {
        SetCardCountNoSave(cardId, count);
        if (autoSave)
        {
            SavePlayerData();
        }
    }

    public int GetDeckCount(int cardId)
    {
        if (cardId < 0) return 0;
        if (playerDeckDict != null && playerDeckDict.TryGetValue(cardId, out int v)) return v;
        if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length) return playerDeck[cardId];
        return 0;
    }

    /// <summary>
    /// 设置卡组数量（仅修改内存，不写盘） - 由上层控制何时保存
    /// </summary>
    public void SetDeckCountNoSave(int cardId, int count)
    {
        if (cardId < 0) return;
        if (playerDeckDict == null) playerDeckDict = new Dictionary<int, int>();

        count = Math.Max(0, count);

        int oldCount = GetDeckCount(cardId);
        if (oldCount == count)
        {
            // 避免无谓操作
            return;
        }

        if (count == 0)
        {
            playerDeckDict.Remove(cardId);
            if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length) playerDeck[cardId] = 0;
        }
        else
        {
            playerDeckDict[cardId] = count;
            if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length) playerDeck[cardId] = count;
        }

        dataChanged = true;
        OnDeckChanged?.Invoke(cardId, GetDeckCount(cardId));
    }

    /// <summary>
    /// 设置卡组数量（原行为）：自动按 autoSave 决定是否写盘
    /// </summary>
    public void SetDeckCount(int cardId, int count)
    {
        SetDeckCountNoSave(cardId, count);
        if (autoSave)
        {
            SavePlayerData();
        }
    }

    /// <summary>
    /// 增加卡组卡片（仅内存操作）
    /// </summary>
    public void AddDeckCardNoSave(int cardId, int add = 1)
    {
        if (cardId < 0 || add <= 0) return;
        int cur = GetDeckCount(cardId);
        SetDeckCountNoSave(cardId, cur + add);
    }

    /// <summary>
    /// 增加卡组卡片（原行为） - 兼容旧调用
    /// </summary>
    public void AddDeckCard(int cardId, int add = 1)
    {
        AddDeckCardNoSave(cardId, add);
        if (autoSave)
        {
            SavePlayerData();
        }
    }

    /// <summary>
    /// 移除卡组卡片（仅内存）
    /// </summary>
    public void RemoveDeckCardNoSave(int cardId, int remove = int.MaxValue)
    {
        if (cardId < 0) return;
        int cur = GetDeckCount(cardId);
        if (cur <= 0) return;

        if (remove >= cur) SetDeckCountNoSave(cardId, 0);
        else SetDeckCountNoSave(cardId, cur - remove);
    }

    /// <summary>
    /// 移除卡组卡片（原行为）
    /// </summary>
    public void RemoveDeckCard(int cardId, int remove = int.MaxValue)
    {
        RemoveDeckCardNoSave(cardId, remove);
        if (autoSave)
        {
            SavePlayerData();
        }
    }

    public void RemoveCard(int cardId)
    {
        if (playerCards == null) return;
        if (playerCards.Remove(cardId))
        {
            dataChanged = true;
            if (autoSave) SavePlayerData();
            OnInventoryChanged?.Invoke(cardId, 0);
        }
    }

    public void AddDrawnCards(List<int> drawnIds)
    {
        if (drawnIds == null || drawnIds.Count == 0) return;
        if (playerCards == null) playerCards = new Dictionary<int, int>();

        foreach (var id in drawnIds)
        {
            if (id < 0) continue;
            if (playerCards.TryGetValue(id, out int cur)) playerCards[id] = cur + 1;
            else playerCards[id] = 1;
            OnInventoryChanged?.Invoke(id, playerCards[id]);
        }

        dataChanged = true;
        if (autoSave) SavePlayerData();
    }

    public void ClearAllCards()
    {
        if (playerCards == null) return;
        playerCards.Clear();
        dataChanged = true;
        if (autoSave) SavePlayerData();
        // Could notify inventory clear - but callers can rebuild UI via OnPlayerDataLoaded or similar
    }

    public bool CanAffordOpen() => playerCoins >= openCost;

    public bool TryConsumeCoinsForOpen(int cost = -1)
    {
        int actualCost = cost < 0 ? openCost : cost;
        if (playerCoins < actualCost)
        {
            Debug.LogWarning($"PlayerDataManager: 金币不足，当前 {playerCoins}，需要 {actualCost}");
            return false;
        }

        playerCoins -= actualCost;
        if (playerCoins < 0) playerCoins = 0;
        dataChanged = true;
        if (autoSave) SavePlayerData();
        Debug.Log($"PlayerDataManager: 扣除金币 {actualCost}，剩余金币 {playerCoins}");
        return true;
    }

    // ---------- 原子化：把卡从库存转到卡组（检查库存、更新两处、触发事件） ----------
    /// <summary>
    /// 尝试把卡从库存转到卡组，仅修改内存并触发事件（不保存）
    /// 返回 true 表示操作成功并已修改内存
    /// </summary>
    public bool TryTransferCardToDeckNoSave(int cardId, int amount = 1)
    {
        if (cardId < 0 || amount <= 0) return false;
        if (playerCards == null) playerCards = new Dictionary<int, int>();
        if (playerDeckDict == null) playerDeckDict = new Dictionary<int, int>();

        int inv = GetCardCount(cardId);
        if (inv < amount) return false;

        int newInv = inv - amount;
        if (newInv <= 0) playerCards.Remove(cardId);
        else playerCards[cardId] = newInv;

        int curDeck = GetDeckCount(cardId);
        int newDeck = curDeck + amount;
        playerDeckDict[cardId] = newDeck;
        if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length)
            playerDeck[cardId] = newDeck;

        dataChanged = true;

        OnInventoryChanged?.Invoke(cardId, newInv);
        OnDeckChanged?.Invoke(cardId, newDeck);
        return true;
    }

    /// <summary>
    /// 原 TryTransferCardToDeck：按 autoSave 决定是否写盘（默认兼容旧调用）
    /// </summary>
    public bool TryTransferCardToDeck(int cardId, int amount = 1)
    {
        bool ok = TryTransferCardToDeckNoSave(cardId, amount);
        if (!ok) return false;

        if (autoSave) SavePlayerData();
        return true;
    }

    // 反向转移：把卡从卡组移回库存（例如用户撤销/从卡组删除）
    /// <summary>
    /// 仅内存操作（不写盘）
    /// </summary>
    public bool TryTransferCardFromDeckNoSave(int cardId, int amount = 1)
    {
        if (cardId < 0 || amount <= 0) return false;
        if (playerDeckDict == null) playerDeckDict = new Dictionary<int, int>();
        if (playerCards == null) playerCards = new Dictionary<int, int>();

        int deckCount = GetDeckCount(cardId);
        if (deckCount < amount) return false;

        int newDeck = deckCount - amount;
        if (newDeck <= 0) playerDeckDict.Remove(cardId);
        else playerDeckDict[cardId] = newDeck;
        if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length)
            playerDeck[cardId] = newDeck;

        int inv = GetCardCount(cardId);
        int newInv = inv + amount;
        playerCards[cardId] = newInv;

        dataChanged = true;

        OnDeckChanged?.Invoke(cardId, newDeck);
        OnInventoryChanged?.Invoke(cardId, newInv);
        return true;
    }

    /// <summary>
    /// 原 TryTransferCardFromDeck：按 autoSave 决定是否写盘（默认兼容旧调用）
    /// </summary>
    public bool TryTransferCardFromDeck(int cardId, int amount = 1)
    {
        bool ok = TryTransferCardFromDeckNoSave(cardId, amount);
        if (!ok) return false;

        if (autoSave) SavePlayerData();
        return true;
    }

    // ✅ 新增：批量设置卡组字典（供 DeckTempModel.CommitToPlayerData 使用）
    /// <summary>
    /// 设置整个卡组字典（仅内存操作，不写盘）—— 用于临时模型提交
    /// 触发 OnDeckChanged 事件（每个 ID 单独触发）
    /// </summary>
    public void SetDeckCardCountsNoSave(Dictionary<int, int> counts)
    {
        if (counts == null)
        {
            playerDeckDict.Clear();
            dataChanged = true;
            // 若需要通知清空，可在此加广播；但通常 UI 直接重建更高效
            return;
        }

        // 先清空旧数据（避免残留）
        var oldDict = new Dictionary<int, int>(playerDeckDict);
        playerDeckDict.Clear();

        // 逐项写入并触发事件
        foreach (var kv in counts)
        {
            int cardId = kv.Key;
            int count = kv.Value;

            if (count <= 0) continue;

            int oldCount = oldDict.ContainsKey(cardId) ? oldDict[cardId] : 0;
            if (oldCount != count)
            {
                playerDeckDict[cardId] = count;
                dataChanged = true;
                OnDeckChanged?.Invoke(cardId, count);
            }
            else
            {
                playerDeckDict[cardId] = count; // 仍需保留（防止被意外清空）
            }

            // 同步老数组（兼容）
            if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length)
                playerDeck[cardId] = count;
        }

        // 清理不再存在的卡片（oldDict 中有但新 counts 中没有的）
        foreach (var cardId in oldDict.Keys)
        {
            if (!counts.ContainsKey(cardId))
            {
                dataChanged = true;
                playerDeckDict.Remove(cardId);
                if (playerDeck != null && cardId >= 0 && cardId < playerDeck.Length)
                    playerDeck[cardId] = 0;
                OnDeckChanged?.Invoke(cardId, 0);
            }
        }
    }

    /// <summary>
    /// 设置整个卡组字典（兼容 autoSave 行为）
    /// </summary>
    public void SetDeckCardCounts(Dictionary<int, int> counts)
    {
        SetDeckCardCountsNoSave(counts);
        if (autoSave)
        {
            SavePlayerData();
        }
    }

    // Save（保持 merge 策略） - 仅允许主实例执行保存
    public void SavePlayerData()
    {
        try
        {
            if (Instance != this)
            {
                Debug.LogWarning($"PlayerDataManager.SavePlayerData: 被非主实例调用，已忽略。thisId={GetInstanceID()} mainId={Instance?.GetInstanceID()}");
                Debug.Log($"调用堆栈:\n{Environment.StackTrace}");
                return;
            }

            // onlySaveOnChange 检查：如果启用并且数据未改变，跳过写盘
            if (onlySaveOnChange)
            {
                int currentHash = CalculateDataHash();
                if (!dataChanged && currentHash == lastSavedHash)
                {
                    Debug.Log("PlayerDataManager: SavePlayerData 跳过（onlySaveOnChange, 无数据变更）");
                    return;
                }
            }

            Debug.Log($"PlayerDataManager: SavePlayerData 开始 -> coins={playerCoins} inventory={playerCards?.Count} deck={playerDeckDict?.Count}");

            var existingData = new Dictionary<string, string>();
            string existingCoinsLine = null;

            if (File.Exists(savePath))
            {
                var existingLines = File.ReadAllLines(savePath);
                foreach (var rawLine in existingLines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string line = rawLine.Trim();
                    var parts = line.Split(',');
                    if (parts.Length < 2) continue;
                    string key = parts[0].Trim().ToLower();

                    if (key == "coins")
                    {
                        existingCoinsLine = line;
                    }
                    else
                    {
                        string idPart = parts[1].Trim();
                        string uniqueKey = $"{key},{idPart}";
                        existingData[uniqueKey] = line;
                    }
                }
            }

            // 决定 coins 行是否被覆盖：
            string coinsLineToWrite = null;
            if (autoUpdateCoinsOnSave)
            {
                coinsLineToWrite = $"coins,{playerCoins}";
            }
            else if (applyInspectorCoinsOnce)
            {
                coinsLineToWrite = $"coins,{playerCoins}";

                // 自动取消一次性应用标志
                applyInspectorCoinsOnce = false;
#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
#endif
                Debug.Log("PlayerDataManager: applyInspectorCoinsOnce 被使用，已写入 Inspector 中的 coins，并将该选项重置为 false。");
            }
            else
            {
                // 如果磁盘已有 coins 行，则保留它（不被覆盖）
                if (!string.IsNullOrEmpty(existingCoinsLine))
                {
                    coinsLineToWrite = existingCoinsLine;
                    Debug.Log("PlayerDataManager: 保存时未启用 coins 覆盖，保留磁盘原有 coins 值。");
                }
                else
                {
                    // 首次保存（磁盘无 coins 行），写入当前 coins
                    coinsLineToWrite = $"coins,{playerCoins}";
                }
            }

            if (playerCards != null)
            {
                foreach (var kv in playerCards)
                {
                    string uniqueKey = $"card,{kv.Key}";
                    if (kv.Value > 0)
                        existingData[uniqueKey] = $"card,{kv.Key},{kv.Value}";
                    else
                        existingData.Remove(uniqueKey);
                }
            }

            var writtenDeckIds = new HashSet<int>();
            if (playerDeckDict != null)
            {
                foreach (var kv in playerDeckDict)
                {
                    string uniqueKey = $"deck,{kv.Key}";
                    if (kv.Value > 0)
                    {
                        existingData[uniqueKey] = $"deck,{kv.Key},{kv.Value}";
                        writtenDeckIds.Add(kv.Key);
                    }
                    else
                    {
                        existingData.Remove(uniqueKey);
                    }
                }
            }

            if (playerDeck != null)
            {
                for (int i = 0; i < playerDeck.Length; i++)
                {
                    int cnt = playerDeck[i];
                    if (cnt > 0 && !writtenDeckIds.Contains(i))
                    {
                        string uniqueKey = $"deck,{i}";
                        existingData[uniqueKey] = $"deck,{i},{cnt}";
                    }
                }
            }

            var outLines = new List<string>();
            outLines.Add(coinsLineToWrite);
            foreach (var kv in existingData)
            {
                outLines.Add(kv.Value);
            }

            string dir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllLines(savePath, outLines.ToArray());
            Debug.Log($"PlayerDataManager: 已保存玩家数据到: {savePath} (entries={outLines.Count})");

            // 更新保存状态
            lastSavedHash = CalculateDataHash();
            dataChanged = false;

#if UNITY_EDITOR
            if (overwriteFromTextAssetOnStart) AssetDatabase.Refresh();
#endif

            OnPlayerDataSaved?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"PlayerDataManager: 保存玩家数据失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    [ContextMenu("TrimUnknownCardIds")]
    public void TrimUnknownCardIds()
    {
#if UNITY_EDITOR
        if (cardStore == null || cardStore.cardList == null)
        {
            Debug.LogWarning("TrimUnknownCardIds: cardStore 不可用，无法修剪");
            return;
        }
        var valid = new HashSet<int>();
        foreach (var c in cardStore.cardList) valid.Add(c.Card_ID);

        var toRemove = new List<int>();
        foreach (var kv in playerCards)
            if (!valid.Contains(kv.Key)) toRemove.Add(kv.Key);

        foreach (var id in toRemove) playerCards.Remove(id);

        toRemove.Clear();
        foreach (var kv in playerDeckDict)
            if (!valid.Contains(kv.Key)) toRemove.Add(kv.Key);
        foreach (var id in toRemove) playerDeckDict.Remove(id);

        SavePlayerData();
        Debug.Log($"TrimUnknownCardIds: 移除了 {toRemove.Count} 个未定义的 card id");
#else
        Debug.LogWarning("TrimUnknownCardIds 仅在编辑器可用");
#endif
    }

    [ContextMenu("SyncDictionaryToEditorLists")]
    public void SyncDictionaryToEditorLists()
    {
#if UNITY_EDITOR
        editorKeys.Clear();
        editorValues.Clear();
        if (playerCards != null)
        {
            foreach (var kv in playerCards)
            {
                editorKeys.Add(kv.Key);
                editorValues.Add(kv.Value);
            }
        }

        EditorUtility.SetDirty(this);
        Debug.Log($"PlayerDataManager: 已同步字典到 editor lists (count={editorKeys.Count})");
#else
        Debug.LogWarning("SyncDictionaryToEditorLists 仅在编辑器可用");
#endif
    }

    [ContextMenu("SyncEditorListsToDictionary")]
    public void SyncEditorListsToDictionary()
    {
#if UNITY_EDITOR
        playerCards = new Dictionary<int, int>();
        int n = Math.Min(editorKeys.Count, editorValues.Count);
        for (int i = 0; i < n; i++)
        {
            int id = editorKeys[i];
            int cnt = editorValues[i];
            if (cnt > 0) playerCards[id] = cnt;
        }
        SavePlayerData();
        EditorUtility.SetDirty(this);
        Debug.Log($"PlayerDataManager: 已从 editor lists 同步到字典 (count={playerCards.Count})");
#else
        Debug.LogWarning("SyncEditorListsToDictionary 仅在编辑器可用");
#endif
    }

    void EnsurePlayerDeckInitialized()
    {
        int desired = 0;
        if (cardStore != null)
        {
            try
            {
                var csType = cardStore.GetType();
                var fd = csType.GetField("cardData");
                if (fd != null)
                {
                    var val = fd.GetValue(cardStore) as System.Collections.ICollection;
                    if (val != null) desired = val.Count;
                }

                if (desired == 0)
                {
                    var fl = csType.GetField("cardList");
                    if (fl != null)
                    {
                        var val2 = fl.GetValue(cardStore) as System.Collections.ICollection;
                        if (val2 != null) desired = val2.Count;
                    }
                }

                if (desired == 0)
                {
                    var pd = csType.GetProperty("cardData");
                    if (pd != null)
                    {
                        var val3 = pd.GetValue(cardStore, null) as System.Collections.ICollection;
                        if (val3 != null) desired = val3.Count;
                    }
                }
                if (desired == 0)
                {
                    var pl = csType.GetProperty("cardList");
                    if (pl != null)
                    {
                        var val4 = pl.GetValue(cardStore, null) as System.Collections.ICollection;
                        if (val4 != null) desired = val4.Count;
                    }
                }
            }
            catch { }
        }

        if (desired <= 0)
        {
            if (playerDeck == null) playerDeck = new int[0];
            return;
        }

        if (playerDeck == null || playerDeck.Length != desired)
        {
            playerDeck = new int[desired];
            Debug.Log($"PlayerDataManager: playerDeck 已初始化，长度 = {desired}");
        }
    }
}
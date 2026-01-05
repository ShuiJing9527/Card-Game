using System;
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

    // 使用 Dictionary 存储 cardId -> count（避免大 id 导致数组膨胀）
    [HideInInspector]
    public Dictionary<int, int> playerCards = new Dictionary<int, int>();

    // 编辑器辅助：把 dictionary 同步到这两个 list 以便在 Inspector 查看（仅用于调试/查看，不作为源数据）
    [SerializeField, Tooltip("仅用于在 Inspector 查看（通过 SyncDictionaryToEditorLists 同步）")]
    private List<int> editorKeys = new List<int>();
    [SerializeField, Tooltip("仅用于在 Inspector 查看（通过 SyncDictionaryToEditorLists 同步）")]
    private List<int> editorValues = new List<int>();

    [Header("依赖与配置")]
    public CardStore cardStore;
    public TextAsset playerData;
    public bool preferAssetsFolder = true; // true -> 写入 Assets/Datas/Player/playerdata.csv（编辑器可见）
    public bool overwriteFromTextAssetOnStart = true; // 启动时是否用 playerData 覆盖磁盘文件（谨慎使用）

    [Header("抽卡/开包配置")]
    public int openCost = 10;

    string saveFileName = "playerdata.csv";
    string savePath => GetSavePath();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
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
            Debug.LogWarning("PlayerDataManager: 未找到 CardDrawStore，直接初始化玩家数据（card 校验将不可用）");
            InitPlayerData();
            return;
        }

        if (cardStore.IsCardsReady)
        {
            InitPlayerData();
            return;
        }

        cardStore.OnCardsReady += OnCardStoreReady;
    }

    void OnCardStoreReady()
    {
        if (cardStore == null) return;
        cardStore.OnCardsReady -= OnCardStoreReady;
        InitPlayerData();
    }

    void InitPlayerData()
    {
        // 如果需要每次用内置 CSV 覆盖磁盘（开发阶段可能会开启），先写入
        WriteBundledTextAssetToDisk();
        LoadPlayerData();
    }

    string GetSavePath()
    {
#if UNITY_EDITOR
        if (preferAssetsFolder)
            return Path.Combine(Application.dataPath, "Datas/Player", saveFileName);
        else
#endif
            return Path.Combine(Application.persistentDataPath, saveFileName);
    }

    void WriteBundledTextAssetToDisk()
    {
        if (playerData == null || !overwriteFromTextAssetOnStart) return;

        try
        {
            string dir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(savePath, playerData.text);
            Debug.Log($"PlayerDataManager: 内置 CSV 已写入磁盘: {savePath}");

#if UNITY_EDITOR
            if (preferAssetsFolder) AssetDatabase.Refresh();
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
            string[] rows = null;

            if (playerData != null)
            {
                rows = playerData.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log("PlayerDataManager: 从内置 CSV 加载玩家数据");
            }
            else if (File.Exists(savePath))
            {
                rows = File.ReadAllLines(savePath);
                Debug.Log($"PlayerDataManager: 从磁盘文件加载玩家数据: {savePath}");
            }
            else
            {
                Debug.Log("PlayerDataManager: 未找到玩家数据，使用默认值");
                playerCards = new Dictionary<int, int>();
                return;
            }

            playerCards = new Dictionary<int, int>();
            playerCoins = 0;

            // 如果 cardStore 可用，准备一个快速查找集合用于校验 id（可选）
            HashSet<int> validCardIds = null;
            if (cardStore != null && cardStore.cardList != null)
            {
                validCardIds = new HashSet<int>();
                foreach (var c in cardStore.cardList) validCardIds.Add(c.Card_ID);
            }

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
                        playerCoins = coins;
                    continue;
                }

                if (key == "card" && parts.Length >= 3)
                {
                    if (!int.TryParse(parts[1].Trim(), out int id))
                    {
                        Debug.LogWarning($"PlayerDataManager: 跳过无法解析的 card id 行: '{line}'");
                        continue;
                    }
                    if (!int.TryParse(parts[2].Trim(), out int count))
                    {
                        Debug.LogWarning($"PlayerDataManager: 跳过无法解析的 card count 行: '{line}'");
                        continue;
                    }

                    // 如果 cardStore 可用，建议校验 id（如果你希望严格过滤未定义 id，可取消下面注释）
                    if (validCardIds != null && !validCardIds.Contains(id))
                    {
                        Debug.LogWarning($"PlayerDataManager: CSV 中发现未在 cardStore 定义的 id={id}，仍将保留（如需可调用 TrimUnknownCardIds）");
                    }

                    if (count > 0)
                        playerCards[id] = count;
                }
            }

            Debug.Log($"PlayerDataManager: 加载完成，玩家金币={playerCoins}，已记录卡种数={playerCards.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"PlayerDataManager: 加载玩家数据失败: {ex.Message}");
        }
    }

    // 获取某张卡数量（安全）
    public int GetCardCount(int cardId)
    {
        if (cardId < 0) return 0;
        if (playerCards == null) playerCards = new Dictionary<int, int>();
        if (playerCards.TryGetValue(cardId, out int c)) return c;
        return 0;
    }

    // 覆盖设置某张卡的数量（count >= 0）
    public void SetCardCount(int cardId, int count)
    {
        if (cardId < 0) return;
        if (playerCards == null) playerCards = new Dictionary<int, int>();
        count = Math.Max(0, count);
        if (count == 0)
            playerCards.Remove(cardId);
        else
            playerCards[cardId] = count;

        SavePlayerData();
    }

    // 删除某个卡记录
    public void RemoveCard(int cardId)
    {
        if (playerCards == null) return;
        if (playerCards.Remove(cardId)) SavePlayerData();
    }

    // 添加抽到的卡（累加），并保存
    public void AddDrawnCards(List<int> drawnIds)
    {
        if (drawnIds == null || drawnIds.Count == 0) return;
        if (playerCards == null) playerCards = new Dictionary<int, int>();

        foreach (var id in drawnIds)
        {
            if (id < 0) continue;
            if (playerCards.TryGetValue(id, out int cur)) playerCards[id] = cur + 1;
            else playerCards[id] = 1;
        }

        SavePlayerData();
    }

    // 清空所有卡（谨慎）
    public void ClearAllCards()
    {
        if (playerCards == null) return;
        playerCards.Clear();
        SavePlayerData();
    }

    // 检查是否有足够金币
    public bool CanAffordOpen() => playerCoins >= openCost;

    // 尝试扣金币（默认使用 openCost），成功返回 true 并保存
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
        SavePlayerData();
        Debug.Log($"PlayerDataManager: 扣除金币 {actualCost}，剩余金币 {playerCoins}");
        return true;
    }

    // 保存到 CSV（coins + 每个非零卡）
    public void SavePlayerData()
    {
        try
        {
            var lines = new List<string>();
            lines.Add($"coins,{playerCoins}");

            if (playerCards != null)
            {
                foreach (var kv in playerCards)
                {
                    if (kv.Value > 0)
                        lines.Add($"card,{kv.Key},{kv.Value}");
                }
            }

            string dir = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(savePath, lines.ToArray());
            Debug.Log($"PlayerDataManager: 已保存玩家数据到: {savePath}");

#if UNITY_EDITOR
            if (preferAssetsFolder) AssetDatabase.Refresh();
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"PlayerDataManager: 保存玩家数据失败: {ex.Message}");
        }
    }

    // 清理字典中不在 cardStore 的 id（可选，谨慎调用）
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

        SavePlayerData();
        Debug.Log($"TrimUnknownCardIds: 移除了 {toRemove.Count} 个未定义的 card id");
#else
        Debug.LogWarning("TrimUnknownCardIds 仅在编辑器可用");
#endif
    }

    // 编辑器辅助：把 Dictionary 同步到 editorKeys/editorValues（用于 Inspector 查看）
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

        // 标记改动
        EditorUtility.SetDirty(this);
        Debug.Log($"PlayerDataManager: 已同步字典到 editor lists (count={editorKeys.Count})");
#else
        Debug.LogWarning("SyncDictionaryToEditorLists 仅在编辑器可用");
#endif
    }

    // 编辑器辅助：把 editorKeys/editorValues 写回 Dictionary（仅当你在 Inspector 编辑了 lists 后调用）
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
}
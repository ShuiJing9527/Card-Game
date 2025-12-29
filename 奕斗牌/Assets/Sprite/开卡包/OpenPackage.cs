using System.Collections.Generic;
using UnityEngine;

public class OpenPackage : MonoBehaviour
{
    [Header("按顺序填写：Element0=土, Element1=木, Element2=水, Element3=火, Element4=金")]
    public GameObject[] monsterPrefabs; // Inspector 中已按顺序放好
    public GameObject spellPrefab;

    public CardDrawStore cardDrawStore; // 可在 Inspector 手动指定（若不指定会自动查找）
    public Transform cardParent;        // 备选父对象（当 cardPool 未设置时使用）
    public GameObject cardPool;         // 优先把生成的卡放到 cardPool.transform

    private readonly List<GameObject> _generatedCards = new List<GameObject>();

    void Awake()
    {
        if (cardDrawStore == null)
            cardDrawStore = GetComponent<CardDrawStore>() ?? FindObjectOfType<CardDrawStore>();
    }

    // 在 Inspector 按钮或 UI 事件调用此方法以“开包”
    public void OnClickOpen()
    {
        // 每次开包前先清理旧卡
        ClearOldCards();

        if (cardDrawStore == null)
        {
            Debug.LogError("OpenPackage: cardDrawStore 未找到，请在 Inspector 指定或确保场景存在 CardDrawStore");
            return;
        }

        if ((monsterPrefabs == null || monsterPrefabs.Length == 0) && spellPrefab == null)
        {
            Debug.LogError("OpenPackage: 未设置任何 prefab（monsterPrefabs 或 spellPrefab）");
            return;
        }

        Transform targetParent = GetTargetParent();

        // 示例：开 5 张卡（如需不同数量可改）
        for (int i = 0; i < 5; i++)
        {
            CardMessage msg = cardDrawStore.RandomCard();
            if (msg == null) continue;

            if (msg is MonsterCard m)
            {
                GameObject prefab = GetMonsterPrefabByAttr(m.Card_Attributes, m);
                if (prefab == null)
                {
                    Debug.LogWarning($"OpenPackage: 未找到匹配的怪兽 prefab (attr='{m.Card_Attributes}'), 回退使用第一个 prefab");
                    if (monsterPrefabs != null && monsterPrefabs.Length > 0) prefab = monsterPrefabs[0];
                    else continue;
                }

                var go = Instantiate(prefab);
                if (targetParent != null) go.transform.SetParent(targetParent, false);
                else Debug.LogWarning("OpenPackage: cardPool 和 cardParent 均未设置，生成的卡牌未被父化");

                go.transform.localScale = Vector3.one;
                go.name = $"Monster_{m.Card_ID}_{m.Card_Name}";

                var md = go.GetComponent<MonsterCardDisplay>() ?? go.GetComponentInChildren<MonsterCardDisplay>(true);
                if (md != null) md.SetCard(m);
                else Debug.LogWarning($"OpenPackage: prefab '{prefab.name}' 缺少 MonsterCardDisplay，无法赋数据");

                _generatedCards.Add(go);

                Debug.Log($"OpenPackage: Instantiate Monster '{m.Card_Name}' attr='{m.Card_Attributes}' -> prefab '{prefab.name}'");
            }
            else if (msg is SpellCard s)
            {
                if (spellPrefab == null)
                {
                    Debug.LogWarning("OpenPackage: 要生成咒术卡，但 spellPrefab 未设置，跳过");
                    continue;
                }

                var go = Instantiate(spellPrefab);
                if (targetParent != null) go.transform.SetParent(targetParent, false);
                else Debug.LogWarning("OpenPackage: cardPool 和 cardParent 均未设置，生成的卡牌未被父化");

                go.transform.localScale = Vector3.one;
                go.name = $"Spell_{s.Card_ID}_{s.Card_Name}";

                // 从 CardDrawStore 获取 CSV 中的原始叠放描述（若有）
                string stackDesc = null;
                try
                {
                    if (cardDrawStore != null)
                        stackDesc = cardDrawStore.GetStackDescriptionById(s.Card_ID);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"OpenPackage: 获取 StackDescription 失败 id={s.Card_ID} : {ex}");
                }

                Debug.Log($"[OpenPackage] For spell id={s.Card_ID} name='{s.Card_Name}' stackDesc(len={(stackDesc?.Length ?? 0)})='{stackDesc}'");

                var sd = go.GetComponent<SpellCardDisplay>() ?? go.GetComponentInChildren<SpellCardDisplay>(true);
                if (sd != null)
                    sd.SetCard(s, stackDesc);
                else
                    Debug.LogWarning($"OpenPackage: spell prefab '{spellPrefab.name}' 缺少 SpellCardDisplay，无法赋数据");

                _generatedCards.Add(go);

                Debug.Log($"OpenPackage: Instantiate Spell '{s.Card_Name}' (stackDesc len={(stackDesc?.Length ?? 0)})");
            }
            else
            {
                Debug.LogWarning("OpenPackage: RandomCard 返回未知 CardMessage 类型，跳过");
            }
        }
    }

    Transform GetTargetParent()
    {
        if (cardPool != null) return cardPool.transform;
        if (cardParent != null) return cardParent;
        return null;
    }

    // 按属性返回对应 prefab（使用 Inspector 中按约定顺序的 monsterPrefabs）
    GameObject GetMonsterPrefabByAttr(string attr, MonsterCard m)
    {
        if (monsterPrefabs == null || monsterPrefabs.Length == 0) return null;

        if (string.IsNullOrWhiteSpace(attr))
        {
            return monsterPrefabs[Mathf.Abs(m.Card_ID) % monsterPrefabs.Length];
        }

        attr = attr.Trim();

        if (attr.Contains("土")) return monsterPrefabs.Length > 0 ? monsterPrefabs[0] : monsterPrefabs[0];
        if (attr.Contains("木")) return monsterPrefabs.Length > 1 ? monsterPrefabs[1] : monsterPrefabs[0];
        if (attr.Contains("水")) return monsterPrefabs.Length > 2 ? monsterPrefabs[2] : monsterPrefabs[0];
        if (attr.Contains("火")) return monsterPrefabs.Length > 3 ? monsterPrefabs[3] : monsterPrefabs[0];
        if (attr.Contains("金")) return monsterPrefabs.Length > 4 ? monsterPrefabs[4] : monsterPrefabs[0];

        return monsterPrefabs[Mathf.Abs(m.Card_ID) % monsterPrefabs.Length];
    }

    private void ClearOldCards()
    {
        for (int i = 0; i < _generatedCards.Count; i++)
        {
            var card = _generatedCards[i];
            if (card != null)
            {
                Destroy(card);
            }
        }
        _generatedCards.Clear();
    }
}

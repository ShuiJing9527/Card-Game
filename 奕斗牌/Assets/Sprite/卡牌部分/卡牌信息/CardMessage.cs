using System;
using System.Collections.Generic;
using UnityEngine;

#region 基础卡片数据结构
/// <summary>
/// 卡牌费用信息
/// </summary>
[Serializable]
public class CardCostInfo
{
    public int CostValue;      // 费用值，默认0表示无费用
    public string Description; // 费用描述
    public CardCostInfo(int costValue, string description)
    {
        CostValue = costValue;
        Description = description;
    }
}
/// <summary>
/// 卡牌基类，管理通用信息
/// </summary>
public class CardMessage
{
    public int Card_ID;                    // 卡牌唯一ID
    public List<CardCostInfo> Card_Costs; // 费用（默认无费用）
    public string Card_Name;               // 卡牌名称
    public string Card_Description;       // 卡牌描述文本
    public string Card_Type;               // 卡牌类型
    public int Card_Atk = 0;              // 攻击力，默认0，法术卡默认为0
    public CardMessage(int id, string name, string description, string type)
    {
        Card_ID = id;
        Card_Name = name;
        Card_Description = description;
        Card_Type = type;

        // 默认无费用
        Card_Costs = new List<CardCostInfo> { new CardCostInfo(0, "无费用") };
    }
}
#endregion

#region 怪兽卡及枚举
/// <summary>
/// 怪兽卡类型枚举
/// </summary>
public enum MonsterCardType
{
    Effect,          // 效果
    Judge            // 判定
}
/// <summary>
/// 怪兽卡类，继承自CardMessage，扩展怪兽专属信息
/// </summary>
public class MonsterCard : CardMessage
{
    public int Card_Lv;                // 等级 1-5级
    public string Card_Attributes;     // 属性
    public string Card_Link;           // 怪兽羁绊名称
    public string Card_LinkEffect;     // 新增：羁绊效果描述
    public MonsterCardType MonsterType;  // 怪兽类型
    public int StackCount = 1;          // 当前叠放数量，默认1只
    public MonsterCard(int id, string name, string description, string type,
        int atk, int lv, string attributes, string link, string linkEffect = "",
        List<CardCostInfo> costs = null,
        MonsterCardType monsterType = MonsterCardType.Effect)
        : base(id, name, description, type)
    {
        Card_Atk = atk;
        Card_Lv = lv;
        Card_Attributes = attributes;
        Card_Link = link;
        Card_LinkEffect = linkEffect;
        MonsterType = monsterType;
        if (costs != null && costs.Count > 0)
            Card_Costs = costs;
        else
            Card_Costs = new List<CardCostInfo> { new CardCostInfo(0, "无费用") };
    }
}
#endregion

#region 场地管理器，实现等级怪兽出场与叠放数量控制（按等级叠放非同ID） 
public class FieldMonsterManager
{
    // 等级对应最大出场数量限制
    private readonly Dictionary<int, int> levelMaxOnField = new Dictionary<int, int>()
    {
        { 1, 5 },
        { 2, 4 },
        { 3, 3 },
        { 4, 2 },
        { 5, 1 }
    };
    // 等级对应最大叠放数量限制（叠放时的最大堆叠数）
    private readonly Dictionary<int, int> levelMaxStack = new Dictionary<int, int>()
    {
        { 1, 4 },
        { 2, 3 },
        { 3, 2 },
        { 4, 1 },
        { 5, 0 } // 5级不可叠放
    };
    // 当前每个等级的怪兽出场牌面数量（怪兽卡实例数量）
    private readonly Dictionary<int, int> currentOnFieldCounts = new Dictionary<int, int>();
    // 场地怪兽列表，含堆叠计数
    private readonly List<MonsterCard> monstersOnField = new List<MonsterCard>();
    /// <summary>
    /// 尝试放置怪兽卡到场上（同等级怪兽可叠放，非同ID均可）
    /// 叠放规则仅针对玩家手动操作（调用此方法即视为手动操作）
    /// </summary>
    /// <param name="monster">怪兽实例</param>
    /// <param name="stackCount">本次叠放数量（含本体）</param>
    /// <returns>是否成功放置</returns>
    public bool TryAddMonsterToField(MonsterCard monster, int stackCount = 1)
    {
        if (stackCount < 1)
        {
            Debug.LogWarning("stackCount至少为1");
            return false;
        }
        int lvl = monster.Card_Lv;
        if (!levelMaxStack.TryGetValue(lvl, out int maxStack))
            maxStack = 0;
        if (stackCount > maxStack + 1)
        {
            Debug.LogWarning($"等级{lvl}怪兽叠放数量{stackCount}超出允许最大堆叠数{maxStack + 1}");
            return false;
        }
        if (!levelMaxOnField.TryGetValue(lvl, out int maxOnField))
            maxOnField = int.MaxValue;
        int currentCount = currentOnFieldCounts.GetValueOrDefault(lvl, 0);
        if (currentCount >= maxOnField)
        {
            Debug.LogWarning($"等级{lvl}怪兽已达最大出场数量{maxOnField}，无法再放置");
            return false;
        }
        // 根据等级查找一个怪兽，用于叠放（不用ID匹配，只限制等级相同）
        MonsterCard existingStackMonster = monstersOnField.Find(m => m.Card_Lv == lvl);
        if (existingStackMonster != null)
        {
            int proposedStack = existingStackMonster.StackCount + stackCount;
            if (proposedStack > maxStack + 1)
            {
                Debug.LogWarning($"等级{lvl}怪兽叠放数{proposedStack}超出允许最大堆叠数{maxStack + 1}");
                return false;
            }
            existingStackMonster.StackCount = proposedStack;
            Debug.Log($"等级{lvl}怪兽叠放成功，当前堆叠数：{existingStackMonster.StackCount}");
        }
        else
        {
            monster.StackCount = stackCount;
            monstersOnField.Add(monster);
            currentOnFieldCounts[lvl] = currentCount + 1;
            Debug.Log($"{monster.Card_Name}成功放置，等级{lvl}，堆叠数量：{stackCount}，当前该等级出场数量：{currentOnFieldCounts[lvl]}");
        }
        return true;
    }
    /// <summary>
    /// 移除场上的怪兽或减少叠放数量
    /// </summary>
    /// <param name="monster">怪兽实例</param>
    /// <param name="removeStackCount">欲移除叠放数，默认1</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveMonsterFromField(MonsterCard monster, int removeStackCount = 1)
    {
        if (!monstersOnField.Contains(monster))
        {
            Debug.LogWarning($"{monster.Card_Name}不在场上，无法移除");
            return false;
        }
        if (removeStackCount < 1)
            removeStackCount = 1;
        if (removeStackCount >= monster.StackCount)
        {
            monstersOnField.Remove(monster);
            int lvl = monster.Card_Lv;
            int currentCount = currentOnFieldCounts.GetValueOrDefault(lvl, 0);
            currentOnFieldCounts[lvl] = Mathf.Max(currentCount - 1, 0);
            Debug.Log($"{monster.Card_Name}全部移除，等级{lvl}剩余出场数量：{currentOnFieldCounts[lvl]}");
        }
        else
        {
            monster.StackCount -= removeStackCount;
            Debug.Log($"{monster.Card_Name}减少叠放数{removeStackCount}，剩余叠放数：{monster.StackCount}");
        }
        return true;
    }
    /// <summary>
    /// 获取当前某等级怪兽的出场数量（怪兽数量，不含叠放数）
    /// </summary>
    /// <param name="level">等级</param>
    /// <returns>数量</returns>
    public int GetCurrentOnFieldCount(int level)
    {
        return currentOnFieldCounts.GetValueOrDefault(level, 0);
    }
    /// <summary>
    /// 获取场上所有怪兽信息
    /// </summary>
    public IReadOnlyList<MonsterCard> GetMonstersOnField()
    {
        return monstersOnField.AsReadOnly();
    }
}
#endregion

#region 咒术卡及枚举
/// <summary>
/// 咒术卡的使用方式（直接使用或叠放为装备）
/// </summary>
public enum SpellUsageType
{
    Magic,   // 咒术，直接使用效果
    Stack    // 叠放，作为装备卡放置
}
/// <summary>
/// 咒术卡，继承自卡牌基类，包含咒术专属字段
/// 叠放不限制数量，且默认攻击力为0，可手动赋值特殊法术攻击力
/// </summary>
public class SpellCard : CardMessage
{
    public bool CanUseAsMagic;  // 可作为法术方式使用
    public bool CanUseAsStack;  // 可作为叠放装备使用
    // 叠放数量，默认为1，无上限
    public int StackCount = 1;
    public SpellCard(int id, string name, string description, string type,
        bool canUseAsMagic, bool canUseAsStack,
        List<CardCostInfo> costs = null)
        : base(id, name, description, type)
    {
        CanUseAsMagic = canUseAsMagic;
        CanUseAsStack = canUseAsStack;
        // 法术卡默认攻击力为0，如需特殊攻击力请手动赋值 Card_Atk
        Card_Atk = 0;

        if (costs != null && costs.Count > 0)
            Card_Costs = costs;
        else
            Card_Costs = new List<CardCostInfo> { new CardCostInfo(0, "无费用") };
    }
    /// <summary>
    /// 使用该法术卡的效果触发方法
    /// </summary>
    /// <param name="useAsStack">是否作为叠放装备使用</param>
    public void UseSpell(bool useAsStack = false)
    {
        if (useAsStack && CanUseAsStack)
        {
            Debug.Log($"{Card_Name} 作为装备叠放使用，叠放数量：{StackCount}");
            // 叠放时装备特有的逻辑（法术攻击力由 Card_Atk 乘 StackCount 计算）
            // 这里可写叠放叠加效果，比如影响持有怪兽某些属性等
        }
        else if (!useAsStack && CanUseAsMagic)
        {
            Debug.Log($"{Card_Name} 直接作为咒术施放");
            // 直接使用法术效果，可以写具体逻辑
        }
        else
        {
            Debug.LogWarning($"{Card_Name} 不支持该使用方式");
        }
    }
    /// <summary>
    /// 获取当前法术卡叠放后的总攻击力，统一乘法叠加
    /// </summary>
    public int GetTotalAttack()
    {
        return Card_Atk * StackCount; // 法术卡默认Atk=0，特殊情况外部赋值并计算
    }
}
#endregion
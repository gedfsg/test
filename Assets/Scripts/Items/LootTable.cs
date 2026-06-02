using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class LootEntry
{
    public ItemData item;
    [Min(0f)] public float weight = 1f;
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;

    public int RollAmount() => Random.Range(minAmount, maxAmount + 1);
}

[CreateAssetMenu(fileName = "New Loot Table", menuName = "Inventory/Loot Table")]
public class LootTable : ScriptableObject
{
    [Tooltip("이 테이블에서 한 번에 뽑을 아이템 개수 범위")]
    [Min(1)] public int minRolls = 2;
    [Min(1)] public int maxRolls = 4;

    public List<LootEntry> entries = new();

    /// <summary>가중치 기반으로 아이템 N개 뽑기 (중복 허용)</summary>
    public List<(ItemData item, int amount)> Roll()
    {
        var results = new List<(ItemData, int)>();
        if (entries == null || entries.Count == 0) return results;

        int rollCount = Random.Range(minRolls, maxRolls + 1);
        for (int i = 0; i < rollCount; i++)
        {
            LootEntry picked = PickOne();
            if (picked != null && picked.item != null)
                results.Add((picked.item, picked.RollAmount()));
        }
        return results;
    }

    private LootEntry PickOne()
    {
        float total = 0f;
        foreach (var e in entries) total += e.weight;
        if (total <= 0f) return null;

        float pick = Random.Range(0f, total);
        float cursor = 0f;
        foreach (var e in entries)
        {
            cursor += e.weight;
            if (pick < cursor) return e;
        }
        return entries[^1];
    }
}
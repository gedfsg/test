using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 루팅 설정 한 곳에 모음 (Resources/LootDatabase.asset).
/// 상자 3티어 / 건물 종류별 / 좀비 종류별 루팅 테이블 + 월드 상자 개수.
/// 씬 참조 없이 Resources.Load로 읽어서 LootWorldSpawner, ZombieController가 사용.
/// 에셋은 Tools/Items/Build Loot Tables로 자동 생성.
/// </summary>
[CreateAssetMenu(fileName = "LootDatabase", menuName = "Inventory/Loot Database")]
public class LootDatabase : ScriptableObject
{
    public enum BuildingKind { House, Police, Mart, Church, Warehouse }

    [System.Serializable]
    public class BuildingRule
    {
        public BuildingKind kind;
        [Tooltip("건물 하나에 바닥에 놓을 아이템 수 = table의 minRolls~maxRolls")]
        public LootTable table;
        [Tooltip("게임 시작 시 건물 안에 추가로 스폰할 좀비 수")]
        public int extraZombies;
    }

    [System.Serializable]
    public class ZombieDropRule
    {
        [Tooltip("좀비 오브젝트 이름에 이 문자열이 들어가면 적용 (예: Police, Fat, Girl, Normal)")]
        public string nameContains;
        [Range(0f, 1f)] public float chance;
        [Tooltip("성공하면 이 테이블에서 1개 뽑아 바닥에 드랍")]
        public LootTable table;
    }

    [Header("상자 (나무=Common / 금속=Rare 섞임 / 군용=Epic 보장)")]
    public LootTable crateWood;
    public LootTable crateMetal;
    public LootTable crateMilitary;
    [Tooltip("군용 상자는 여기서 1개를 먼저 뽑음 (Epic 보장)")]
    public LootTable crateMilitaryGuaranteed;

    [Header("월드 상자 개수")]
    public int woodCrateCount = 50;
    public int metalCrateCount = 10;
    public int militaryCrateCount = 2;

    [Header("상자 열면 주변에 좀비 스폰")]
    public int crateZombiesMin = 2;
    public int crateZombiesMax = 3;

    [Header("건물별 루팅")]
    public List<BuildingRule> buildingRules = new List<BuildingRule>();

    [Header("좀비 드랍")]
    public List<ZombieDropRule> zombieDrops = new List<ZombieDropRule>();

    static LootDatabase cached;
    public static LootDatabase Instance => cached != null ? cached : (cached = Resources.Load<LootDatabase>("LootDatabase"));

    public BuildingRule GetRule(BuildingKind kind) => buildingRules.Find(r => r.kind == kind);

    /// <summary>건물 이름/메시 이름으로 종류 판별 (경찰서/마트/교회/창고, 나머지는 집)</summary>
    public static BuildingKind Classify(BuildingInterior b)
    {
        string all = b.name;
        foreach (var mf in b.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) all += "|" + mf.sharedMesh.name;
        all = all.ToLowerInvariant();

        if (all.Contains("police")) return BuildingKind.Police;
        if (all.Contains("kay lousberg") || all.Contains("mart")) return BuildingKind.Mart; // Sketchfab 마트 모델
        if (all.Contains("church")) return BuildingKind.Church;
        if (all.Contains("structure") || all.Contains("farm")) return BuildingKind.Warehouse; // 창고/헛간
        return BuildingKind.House;
    }

    /// <summary>좀비가 죽을 때 호출 - 이름이 맞는 규칙마다 확률 체크 후 바닥에 드랍</summary>
    public void DropForZombie(GameObject zombie)
    {
        foreach (var rule in zombieDrops)
        {
            if (rule.table == null || string.IsNullOrEmpty(rule.nameContains)) continue;
            if (!zombie.name.Contains(rule.nameContains)) continue;
            if (Random.value > rule.chance) continue;

            foreach (var (item, amount) in rule.table.Roll())
                WorldItemDrop.Spawn(zombie.transform.position + Random.insideUnitSphere * 0.5f, item, amount);
        }
    }
}

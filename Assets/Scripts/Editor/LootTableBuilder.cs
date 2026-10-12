using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 루팅 테이블 + 넝마 아이템 + Resources/LootDatabase 자동 생성.
/// 메뉴: Tools/Items/Build Loot Tables (다시 실행하면 값만 갱신)
/// 먼저 Build Liberator Weapons / Build Consumables가 돼 있어야 함.
///
/// 밸런스 기준 (좀보이드/배그 참고):
///  - 음식/음료/붕대가 제일 흔하고, 총은 드묾. 집에서 권총은 "가끔"(약 3%)
///  - 나무 상자 = Common 위주, 금속 = Rare 섞임, 군용 = Epic 총 1개 보장 + 탄약/의료
///  - 탄약 amount = 탄 개수 (바닥 탄약 상자로 떨어지고 가까이 가면 자동 획득)
/// </summary>
public static class LootTableBuilder
{
    const string LootDir = "Assets/Items/Loot";
    const string C = "Assets/Items/Consumables/";
    const string W = "Assets/Items/Weapons/Liberator/";

    struct E
    {
        public string path; public float w; public int min, max;
        public E(string path, float w, int min = 1, int max = 1) { this.path = path; this.w = w; this.min = min; this.max = max; }
    }

    static E PAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/Pistol Ammo.asset", w, min, max);
    static E ARAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/AR Ammo.asset", w, min, max);
    static E SGAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/Shotgun Ammo.asset", w, min, max);

    static readonly string[] CommonGuns = { "G17", "P9", "S12P8", "AK-556", "AR-15", "G36K", "FAMAS", "Skorpion", "UMP45", "MP7" };
    static readonly string[] RareGuns = { "Five-seveN", "RARAN_1130", "XMA-10", "SAIGA-12", "L85A2", "AUG", "Galil", "Groza", "Vector", "P90" };
    static readonly string[] EpicGuns = { "Deagle", "BandJ_357", "AA-12", "SCAR-L", "SCAR-H", "G3", "AS-762", "Honey_Badger", "SR-3M" };

    [MenuItem("Tools/Items/Build Loot Tables")]
    public static void BuildAll()
    {
        System.IO.Directory.CreateDirectory(LootDir);
        System.IO.Directory.CreateDirectory("Assets/Resources");
        string rag = BuildRag();

        var foods = new List<E>
        {
            new E(C + "Apple.asset", 10), new E(C + "Bread.asset", 8), new E(C + "CannedFood.asset", 6), new E(C + "Ramen.asset", 5),
            new E(C + "Water.asset", 10), new E(C + "Cola.asset", 6), new E(C + "Coffee.asset", 5),
        };

        // ── 상자 ──
        var wood = new List<E>(foods) { new E(C + "Bandage.asset", 10), PAmmo(10, 10, 20), ARAmmo(8, 15, 30), SGAmmo(6, 4, 8) };
        AddGuns(wood, new[] { "G17", "P9" }, 3f); AddGuns(wood, new[] { "Skorpion", "S12P8" }, 1f);
        var crateWood = Table("Crate_Wood", 2, 5, wood);

        var metal = new List<E>
        {
            new E(C + "Bandage.asset", 8), new E(C + "MedKit.asset", 6), new E(C + "Painkiller.asset", 4),
            new E(C + "CannedFood.asset", 6), new E(C + "Ramen.asset", 6), new E(C + "Water.asset", 6),
            PAmmo(8, 15, 30), ARAmmo(10, 30, 60), SGAmmo(6, 6, 12),
        };
        AddGuns(metal, new[] { "G17", "S12P8", "AK-556", "AR-15", "UMP45" }, 2f); AddGuns(metal, RareGuns, 1.5f);
        var crateMetal = Table("Crate_Metal", 2, 5, metal);

        var military = new List<E> { new E(C + "MedKit.asset", 6), new E(C + "Painkiller.asset", 4), PAmmo(8, 20, 30), ARAmmo(12, 30, 60), SGAmmo(8, 8, 16) };
        AddGuns(military, RareGuns, 2f);
        var crateMilitary = Table("Crate_Military", 1, 4, military);   // + 보장 1개 = 2~5개
        var epic = new List<E>(); AddGuns(epic, EpicGuns, 1f);
        var crateMilitaryG = Table("Crate_Military_Guaranteed", 1, 1, epic);

        // ── 건물 ──
        var house = new List<E>(foods) { new E(C + "Bandage.asset", 6), PAmmo(4, 8, 15) };
        AddGuns(house, new[] { "G17", "P9" }, 1.5f); // 권총 가끔 (~3%)
        var police = new List<E> { ARAmmo(20, 30, 60), PAmmo(15, 20, 40), SGAmmo(12, 8, 16), new E(C + "Bandage.asset", 5), new E(C + "MedKit.asset", 3) };
        AddGuns(police, new[] { "AK-556", "AR-15", "G36K", "FAMAS", "S12P8" }, 4f); AddGuns(police, new[] { "L85A2", "AUG", "SAIGA-12", "XMA-10" }, 1.5f); AddGuns(police, new[] { "G17" }, 3f);
        var mart = new List<E>(foods);
        var church = new List<E> { new E(C + "Bandage.asset", 10), new E(C + "MedKit.asset", 6), new E(C + "Painkiller.asset", 4), new E(C + "Water.asset", 3) };

        var db = LoadOrCreate<LootDatabase>("Assets/Resources/LootDatabase.asset");
        db.crateWood = crateWood; db.crateMetal = crateMetal; db.crateMilitary = crateMilitary; db.crateMilitaryGuaranteed = crateMilitaryG;
        db.woodCrateCount = 50; db.metalCrateCount = 10; db.militaryCrateCount = 2;
        db.crateZombiesMin = 2; db.crateZombiesMax = 3;
        db.buildingRules = new List<LootDatabase.BuildingRule>
        {
            Rule(LootDatabase.BuildingKind.House, Table("Bldg_House", 1, 3, house), 0),
            Rule(LootDatabase.BuildingKind.Police, Table("Bldg_Police", 6, 9, police), 0),     // AR/샷건/탄약 대량
            Rule(LootDatabase.BuildingKind.Mart, Table("Bldg_Mart", 10, 14, mart), 0),         // 음식/음료 대량
            Rule(LootDatabase.BuildingKind.Church, Table("Bldg_Church", 3, 5, church), 0),     // 의료
            Rule(LootDatabase.BuildingKind.Warehouse, Table("Bldg_Warehouse", 4, 6, metal), 4), // 랜덤 + 좀비 多
        };

        // ── 좀비 드랍 (1개씩) ──
        var smallAmmo = Table("Zombie_Ammo", 1, 1, new List<E> { PAmmo(1, 5, 10), ARAmmo(1, 5, 15), SGAmmo(1, 2, 4) });
        var ragT = Table("Zombie_Rag", 1, 1, new List<E> { new E(rag, 1, 1, 2) });
        var policePistol = Table("Zombie_PolicePistol", 1, 1, new List<E> { new E(W + "G17.asset", 5), new E(W + "P9.asset", 4), new E(W + "Five-seveN.asset", 1) });
        var policeAmmo = Table("Zombie_PoliceAmmo", 1, 1, new List<E> { PAmmo(1, 8, 15) });
        var runnerFood = Table("Zombie_RunnerFood", 1, 1, new List<E> { new E(C + "Apple.asset", 3), new E(C + "Bread.asset", 2), new E(C + "CannedFood.asset", 1), new E(C + "Cola.asset", 2) });
        var tankRare = new List<E> { new E(C + "MedKit.asset", 4), new E(C + "Painkiller.asset", 3) }; AddGuns(tankRare, RareGuns, 0.5f);
        var tankT = Table("Zombie_TankRare", 1, 1, tankRare);
        db.zombieDrops = new List<LootDatabase.ZombieDropRule>
        {
            Drop("Normal", 0.10f, smallAmmo), Drop("Normal", 0.20f, ragT),           // 일반: 탄약 10%, 넝마 20%
            Drop("Police", 0.30f, policePistol), Drop("Police", 0.50f, policeAmmo), // 경찰: 권총 30%, 탄약 50%
            Drop("Girl", 0.25f, runnerFood),                                        // 러너(빠른 좀비): 음식 가끔
            Drop("Fat", 0.60f, tankT),                                              // 탱커(체력 400): 희귀 아이템
        };
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        Debug.Log("[LootTableBuilder] 완료 - 테이블 " + System.IO.Directory.GetFiles(LootDir, "*.asset").Length + "개 + Resources/LootDatabase");
    }

    static void AddGuns(List<E> list, string[] names, float w) { foreach (var n in names) list.Add(new E(W + n + ".asset", w)); }

    static LootTable Table(string name, int minRolls, int maxRolls, List<E> entries)
    {
        var t = LoadOrCreate<LootTable>($"{LootDir}/{name}.asset");
        t.minRolls = minRolls; t.maxRolls = maxRolls;
        t.entries = new List<LootEntry>();
        foreach (var e in entries)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(e.path);
            if (item == null) { Debug.LogWarning("[LootTableBuilder] 아이템 없음: " + e.path); continue; }
            t.entries.Add(new LootEntry { item = item, weight = e.w, minAmount = e.min, maxAmount = e.max });
        }
        EditorUtility.SetDirty(t);
        return t;
    }

    static LootDatabase.BuildingRule Rule(LootDatabase.BuildingKind k, LootTable t, int zombies)
        => new LootDatabase.BuildingRule { kind = k, table = t, extraZombies = zombies };

    static LootDatabase.ZombieDropRule Drop(string name, float chance, LootTable t)
        => new LootDatabase.ZombieDropRule { nameContains = name, chance = chance, table = t };

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    // 넝마 (재료 아이템, 이모지 아이콘)
    static string BuildRag()
    {
        const string icon = "Assets/Items/Materials/Icons/icon_Rag.png", path = "Assets/Items/Materials/Rag.asset";
        AssetDatabase.ImportAsset(icon);
        var imp = (TextureImporter)AssetImporter.GetAtPath(icon);
        if (imp != null && (imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single))
        {
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.SaveAndReimport();
        }
        var m = LoadOrCreate<MaterialData>(path);
        m.itemName = "넝마"; m.itemType = ItemType.Material; m.rarity = Rarity.Common;
        m.isStackable = true; m.maxStackSize = 20; m.description = "낡은 천 조각";
        m.icon = AssetDatabase.LoadAssetAtPath<Sprite>(icon);
        EditorUtility.SetDirty(m);
        return path;
    }
}

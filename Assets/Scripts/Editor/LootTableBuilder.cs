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
    const string B = "Assets/Resources/Backpacks/"; // 가방 (Tools/Items/Build Backpacks)

    struct E
    {
        public string path; public float w; public int min, max;
        public E(string path, float w, int min = 1, int max = 1) { this.path = path; this.w = w; this.min = min; this.max = max; }
    }

    // ── 2026-11-12 밸런스 (AR·샷건 탄약이 너무 흔함) ──
    // 권총 탄약은 초반 무기라 그대로. AR·샷건(·저격) 탄약은 드랍 가중치 50% 감소 + 수량 하향
    //  - 수량 비율은 스펙 기준: AR 5→3 / 10→6 / 20→12 (×0.6), 샷건 3→2 / 6→4 / 12→8 (×0.67)
    //  - 실제 테이블 수량(예: 나무 상자 AR 15~30)에 같은 비율을 곱함 (스펙 숫자는 기존 값과 달라서 비율로 적용)
    // const float RifleAmmoWeightMul = 0.5f, ShotgunAmmoWeightMul = 0.5f;   // (이전 11-12 오전) 50% 감소
    // const float RifleAmmoAmountMul = 0.6f, ShotgunAmmoAmountMul = 0.67f;
    // ── 2026-11-12 오후 피드백: "상자·좀비에서 총알이 너무 안 나옴, 더 많이" → 위 감소를 되돌리고 오히려 크게 늘림 ──
    // 모든 탄약(권총 포함) 드랍 가중치 ×3 (나무 상자 기준 롤당 탄약 확률 약 22% → 55%, 상자 1개에 탄약 평균 약 2묶음)
    // 수량 ×1.5 (예: 나무 상자 AR 15~30 → 23~45발)
    const float AmmoWeightMul = 3f;
    const float AmmoAmountMul = 1.5f;
    const float RifleAmmoWeightMul = AmmoWeightMul, ShotgunAmmoWeightMul = AmmoWeightMul;
    const float RifleAmmoAmountMul = AmmoAmountMul, ShotgunAmmoAmountMul = AmmoAmountMul;
    // 음식·음료 드랍 가중치 40% 감소 (소비 속도는 그대로 → 수급보다 소비가 빠르게)
    const float FoodWeightMul = 0.6f;
    // 가방 드랍 가중치 2배
    const float BackpackWeightMul = 2f;

    static int Scale(int n, float mul) => Mathf.Max(1, Mathf.RoundToInt(n * mul));

    // static E PAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/Pistol Ammo.asset", w, min, max); // (이전) 권총 탄약은 배율 없음
    static E PAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/Pistol Ammo.asset", w * AmmoWeightMul, Scale(min, AmmoAmountMul), Scale(max, AmmoAmountMul));
    static E ARAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/AR Ammo.asset", w * RifleAmmoWeightMul, Scale(min, RifleAmmoAmountMul), Scale(max, RifleAmmoAmountMul));
    static E SGAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/Shotgun Ammo.asset", w * ShotgunAmmoWeightMul, Scale(min, ShotgunAmmoAmountMul), Scale(max, ShotgunAmmoAmountMul));
    // 음식 (가중치 ×0.6)
    static E Food(string file, float w) => new E(C + file, w * FoodWeightMul);
    // 가방 (가중치 ×2)
    static E Bag(float w) => new E(B + "Backpack_Medium.asset", w * BackpackWeightMul);

    // ── 2026-11-16: 스나이퍼 2종 + MedKit 드롭 비율 ──
    const string SW = "Assets/Items/Weapons/Sniper/";  // Tools/Weapons/Build Sniper + Special Weapons
    const string MedKitPath = C + "MedKit.asset";
    // 2026-11-16 투척물 (Tools/Items/Build Throwables)
    const string GrenadePath = "Assets/Items/Throwables/Grenade.asset";
    const string MolotovPath = "Assets/Items/Throwables/Molotov.asset";
    // 저격 탄약 (이전엔 어느 테이블에도 없었음 → 스나이퍼가 5발/8발 쏘면 끝이라 금속·군용 상자·경찰서에 조금 추가)
    static E SRAmmo(float w, int min, int max) => new E("Assets/Items/Ammo/Sniper Ammo.asset", w, min, max);

    /// <summary>
    /// 테이블 안에서 이 아이템이 뽑힐 비율(share, 0~1)이 되도록 가중치를 계산해서 추가.
    /// 예: 병원 MedKit 15% → 나머지 가중치 합 × 0.15/0.85. 이미 같은 아이템이 있으면 빼고 다시 넣음.
    /// 반드시 그 테이블의 다른 항목을 다 넣은 뒤에 호출할 것
    /// </summary>
    static void AddShare(List<E> list, string path, float share) => AddShares(list, (path, share));

    /// <summary>여러 아이템의 비율을 한 번에 (따로 넣으면 뒤에 넣은 게 앞의 비율을 깎음). 예: 금속 상자 MedKit 5% + Mosin 10%</summary>
    static void AddShares(List<E> list, params (string path, float share)[] items)
    {
        float total = 0f;
        foreach (var it in items) { list.RemoveAll(e => e.path == it.path); total += it.share; }
        float others = 0f; foreach (var e in list) if (!IsRetired(e.path)) others += e.w; // 드롭 0인 권총은 빼고 계산
        foreach (var it in items) list.Add(new E(it.path, others * it.share / (1f - total)));
    }

    static readonly string[] CommonGuns = { "G17", "P9", "S12P8", "AK-556", "AR-15", "G36K", "FAMAS", "Skorpion", "UMP45", "MP7" };
    static readonly string[] RareGuns = { "Five-seveN", "RARAN_1130", "XMA-10", "SAIGA-12", "L85A2", "AUG", "Galil", "Groza", "Vector", "P90" };
    static readonly string[] EpicGuns = { "Deagle", "BandJ_357", "AA-12", "SCAR-L", "SCAR-H", "G3", "AS-762", "Honey_Badger", "SR-3M" };

    // 7일 플레이 피드백: 권총/AR만 보이고 샷건·SMG는 거의 못 봄 (나무 상자 롤당 1.1%, 경찰서엔 SMG 없음, 집은 권총만)
    // → 흔한 곳(나무 상자/집/마트/경찰서/경찰 좀비)에도 샷건·SMG를 섞음
    static readonly string[] CommonShotguns = { "S12P8", "XMA-10" };
    static readonly string[] CommonSMGs = { "Skorpion", "UMP45", "MP7" };
    static readonly string[] RareShotguns = { "SAIGA-12", "AA-12" };
    static readonly string[] RareSMGs = { "Vector", "P90", "SR-3M" };

    [MenuItem("Tools/Items/Build Loot Tables")]
    public static void BuildAll()
    {
        System.IO.Directory.CreateDirectory(LootDir);
        System.IO.Directory.CreateDirectory("Assets/Resources");
        string rag = BuildRag();

        var foods = new List<E>
        {
            // (이전) Apple 10 / Bread 8 / CannedFood 6 / Ramen 5 / Water 10 / Cola 6 / Coffee 5 → Food()가 ×0.6
            Food("Apple.asset", 10), Food("Bread.asset", 8), Food("CannedFood.asset", 6), Food("Ramen.asset", 5),
            Food("Water.asset", 10), Food("Cola.asset", 6), Food("Coffee.asset", 5),
        };

        // ── 상자 ──
        var wood = new List<E>(foods) { new E(C + "Bandage.asset", 10), PAmmo(10, 10, 20), ARAmmo(8, 15, 30), SGAmmo(6, 4, 8) };
        AddGuns(wood, new[] { "G17", "P9" }, 3f); AddGuns(wood, new[] { "Skorpion", "S12P8" }, 1f);
        AddGuns(wood, CommonShotguns, 1.5f); AddGuns(wood, CommonSMGs, 1.5f); // 샷건/SMG 롤당 약 1% → 약 6%
        wood.Add(Bag(2)); // 가방 (1종, Bag()이 가중치 ×2)
        AddShare(wood, MedKitPath, 0.02f); // 나무 상자 MedKit 2% (2026-11-16)
        var crateWood = Table("Crate_Wood", 2, 5, wood);

        var metal = new List<E>
        {
            new E(C + "Bandage.asset", 8), new E(C + "MedKit.asset", 6), new E(C + "Painkiller.asset", 4),
            Food("CannedFood.asset", 6), Food("Ramen.asset", 6), Food("Water.asset", 6),
            PAmmo(8, 15, 30), ARAmmo(10, 30, 60), SGAmmo(6, 6, 12),
        };
        AddGuns(metal, new[] { "G17", "S12P8", "AK-556", "AR-15", "UMP45" }, 2f); AddGuns(metal, RareGuns, 1.5f);
        AddGuns(metal, CommonSMGs, 1.5f); AddGuns(metal, CommonShotguns, 1.5f);
        metal.Add(Bag(3));
        var warehouse = new List<E>(metal); // 창고는 예전처럼 금속 상자 목록 복사 (스나이퍼·저격탄은 금속 상자에만)
        metal.Add(SRAmmo(3, 5, 10));
        // 금속 상자 MedKit 5% (이전 가중치 6 ≈ 6%) + 희귀 상자(금속) 가중치의 10% = Mosin
        AddShares(metal, (MedKitPath, 0.05f), (SW + "Mosin_Sniper.asset", 0.10f));
        var crateMetal = Table("Crate_Metal", 2, 5, metal);

        var military = new List<E> { new E(C + "MedKit.asset", 6), new E(C + "Painkiller.asset", 4), PAmmo(8, 20, 30), ARAmmo(12, 30, 60), SGAmmo(8, 8, 16) };
        AddGuns(military, RareGuns, 2f);
        military.Add(Bag(3));
        military.Add(SRAmmo(4, 5, 10));
        // AddShare(military, MedKitPath, 0.15f); // 군용 상자 MedKit 15%
        AddShares(military, (MedKitPath, 0.15f), (GrenadePath, 0.15f)); // 군용 상자 MedKit 15% + 수류탄 15%
        var crateMilitary = Table("Crate_Military", 1, 4, military);   // + 보장 1개 = 2~5개
        var epic = new List<E>(); AddGuns(epic, EpicGuns, 1f);
        AddShare(epic, SW + "L118a1.asset", 0.10f); // 영웅 상자(군용 보장 Epic) 가중치의 10% = L118a1
        var crateMilitaryG = Table("Crate_Military_Guaranteed", 1, 1, epic);

        // ── 건물 ──
        var house = new List<E>(foods) { new E(C + "Bandage.asset", 6), PAmmo(4, 8, 15) };
        AddGuns(house, new[] { "G17", "P9" }, 1.5f); // 권총 가끔 (~3%)
        AddGuns(house, new[] { "S12P8", "Skorpion" }, 0.75f); // 집에 있던 샷건/SMG 드물게 (~1.5%)
        house.Add(Bag(1));
        // AddShare(house, MedKitPath, 0.04f); // 민가 MedKit 4%
        AddShares(house, (MedKitPath, 0.04f), (GrenadePath, 0.02f), (MolotovPath, 0.05f)); // 민가 MedKit 4% + 수류탄 2% + 화염병 5%
        var police = new List<E> { ARAmmo(20, 30, 60), PAmmo(15, 20, 40), SGAmmo(12, 8, 16), new E(C + "Bandage.asset", 5), new E(C + "MedKit.asset", 3) };
        AddGuns(police, new[] { "AK-556", "AR-15", "G36K", "FAMAS", "S12P8" }, 4f); AddGuns(police, new[] { "L85A2", "AUG", "SAIGA-12", "XMA-10" }, 1.5f); AddGuns(police, new[] { "G17" }, 3f);
        // 경찰서: 진압용 샷건 + 기동대 SMG 추가 (총 중 샷건/SMG 비중 약 50%)
        AddGuns(police, CommonShotguns, 3f); AddGuns(police, CommonSMGs, 3f); AddGuns(police, RareShotguns, 1f); AddGuns(police, RareSMGs, 1f);
        police.Add(SRAmmo(3, 5, 10));
        // AddShare(police, MedKitPath, 0.08f); // 경찰서 MedKit 8% (이전 가중치 3 ≈ 2%)
        AddShares(police, (MedKitPath, 0.08f), (GrenadePath, 0.08f)); // 경찰서 MedKit 8% + 수류탄 8%
        var mart = new List<E>(foods) { Bag(1) }; // 상점에서도 가방 가끔 (2026-11-12)
        // 마트: 음식 대량 + 계산대/창고에 숨겨둔 샷건·SMG 가끔 (마트 1곳당 총 1자루 나올 확률 약 40%)
        AddGuns(mart, new[] { "S12P8", "Skorpion", "UMP45" }, 0.7f); mart.Add(SGAmmo(1.5f, 4, 8));
        // AddShare(mart, MedKitPath, 0.04f); // 마트 MedKit 4%
        AddShares(mart, (MedKitPath, 0.04f), (MolotovPath, 0.03f)); // 마트 MedKit 4% + 화염병 3%
        var church = new List<E> { new E(C + "Bandage.asset", 10), new E(C + "MedKit.asset", 6), new E(C + "Painkiller.asset", 4), Food("Water.asset", 3) };
        AddShare(church, MedKitPath, 0.02f); // 교회 MedKit 2% (이전 가중치 6 ≈ 27%)
        // 병원: 의료 아이템만 (붕대/구급상자/진통제), 교회보다 많이
        var hospital = new List<E> { new E(C + "Bandage.asset", 10), new E(C + "MedKit.asset", 7), new E(C + "Painkiller.asset", 6) };
        AddShare(hospital, MedKitPath, 0.15f); // 병원 MedKit 15% (이전 가중치 7 ≈ 30%)
        // AddShare(warehouse, MedKitPath, 0.05f); // 창고 MedKit 5%
        AddShares(warehouse, (MedKitPath, 0.05f), (MolotovPath, 0.04f)); // 창고 MedKit 5% + 화염병 4%

        var db = LoadOrCreate<LootDatabase>("Assets/Resources/LootDatabase.asset");
        db.crateWood = crateWood; db.crateMetal = crateMetal; db.crateMilitary = crateMilitary; db.crateMilitaryGuaranteed = crateMilitaryG;
        db.woodCrateCount = 100; db.metalCrateCount = 20; db.militaryCrateCount = 4; // 길거리 상자 2배 (이전 50/10/2, 등급 비율 유지)
        db.crateZombiesMin = 2; db.crateZombiesMax = 3;
        db.buildingRules = new List<LootDatabase.BuildingRule>
        {
            Rule(LootDatabase.BuildingKind.House, Table("Bldg_House", 1, 3, house), 0),
            Rule(LootDatabase.BuildingKind.Police, Table("Bldg_Police", 6, 9, police), 0),     // AR/샷건/탄약 대량
            Rule(LootDatabase.BuildingKind.Mart, Table("Bldg_Mart", 6, 9, mart), 0),           // 음식/음료 (이전 10~14 → 40% 감소)
            Rule(LootDatabase.BuildingKind.Church, Table("Bldg_Church", 3, 5, church), 0),     // 의료
            // Rule(LootDatabase.BuildingKind.Warehouse, Table("Bldg_Warehouse", 4, 6, metal), 4), // (이전) 금속 상자 목록 그대로
            Rule(LootDatabase.BuildingKind.Warehouse, Table("Bldg_Warehouse", 4, 6, warehouse), 4), // 랜덤 + 좀비 多 (금속 상자 목록 + MedKit 5%)
            Rule(LootDatabase.BuildingKind.Hospital, Table("Bldg_Hospital", 6, 9, hospital), 0), // 의료 대량
        };

        // ── 좀비 드랍 (1개씩) ──
        var smallAmmo = Table("Zombie_Ammo", 1, 1, new List<E> { PAmmo(1, 5, 10), ARAmmo(1, 5, 15), SGAmmo(1, 2, 4) });
        var ragT = Table("Zombie_Rag", 1, 1, new List<E> { new E(rag, 1, 1, 2) });
        // 경찰 좀비: 권총 위주 + 샷건/SMG도 가끔
        var policePistol = Table("Zombie_PolicePistol", 1, 1, new List<E> { new E(W + "G17.asset", 5), new E(W + "P9.asset", 4), new E(W + "Five-seveN.asset", 1),
                                                                            new E(W + "S12P8.asset", 2), new E(W + "MP7.asset", 1.5f), new E(W + "UMP45.asset", 1.5f) });
        var policeAmmo = Table("Zombie_PoliceAmmo", 1, 1, new List<E> { PAmmo(1, 8, 15) });
        var runnerFood = Table("Zombie_RunnerFood", 1, 1, new List<E> { new E(C + "Apple.asset", 3), new E(C + "Bread.asset", 2), new E(C + "CannedFood.asset", 1), new E(C + "Cola.asset", 2) });
        var tankRare = new List<E> { new E(C + "MedKit.asset", 4), new E(C + "Painkiller.asset", 3) }; AddGuns(tankRare, RareGuns, 0.5f);
        AddGuns(tankRare, RareShotguns, 0.5f); AddGuns(tankRare, RareSMGs, 0.5f);
        var tankT = Table("Zombie_TankRare", 1, 1, tankRare);
        var medkitT = Table("Zombie_MedKit", 1, 1, new List<E> { new E(MedKitPath, 1) });
        db.zombieDrops = new List<LootDatabase.ZombieDropRule>
        {
            // 2026-11-12 오후: 총알 더 많이 → 일반 탄약 20% → 50%, 러너·탱커도 탄약 드랍 (수량은 AmmoAmountMul로 ×1.5)
            Drop("Normal", 0.50f, smallAmmo), Drop("Normal", 0.20f, ragT),           // 일반: 탄약 50%(이전 20%), 넝마 20%
            Drop("Police", 0.30f, policePistol), Drop("Police", 1.00f, policeAmmo), // 경찰: 권총 30%, 탄약 100%
            Drop("Girl", 0.15f, runnerFood), Drop("Girl", 0.35f, smallAmmo),        // 러너: 음식 15%, 탄약 35%(신규)
            Drop("Fat", 0.60f, tankT), Drop("Fat", 1.00f, smallAmmo),               // 탱커(체력 400): 희귀 아이템 60%, 탄약 100%(신규)
            // 2026-11-16 MedKit: 모든 좀비 1.5%, 사냥꾼은 +3.5% (사냥꾼 이름 "<원래 이름> (사냥꾼)"이라 일반 규칙도 같이 걸림 → 합계 약 5%)
            Drop("Normal", 0.015f, medkitT), Drop("Girl", 0.015f, medkitT), Drop("Fat", 0.015f, medkitT), Drop("Police", 0.015f, medkitT),
            Drop("사냥꾼", 0.035f, medkitT),
        };
        EditorUtility.SetDirty(db);
        LiberatorWeaponBuilder.ApplyPistolBalance(); // 남은 권총 2자루 스탯 (2026-11-16)
        AssetDatabase.SaveAssets();
        Debug.Log("[LootTableBuilder] 완료 - 테이블 " + System.IO.Directory.GetFiles(LootDir, "*.asset").Length + "개 + Resources/LootDatabase");
    }

    static void AddGuns(List<E> list, string[] names, float w) { foreach (var n in names) list.Add(new E(W + n + ".asset", w)); }

    // 2026-11-16 권총 정리: 희귀 Five-seveN + 영웅 Deagle 두 자루만 드롭. 나머지 권총(일반 G17·P9, 희귀 RARAN, 영웅 B&J)은 가중치 0
    //  → 모든 테이블에서 빠짐 (이 목록만 고치면 다시 살릴 수 있음)
    static readonly HashSet<string> RetiredPistols = new HashSet<string> { "G17", "P9", "RARAN_1130", "BandJ_357" };

    static bool IsRetired(string path) => path.StartsWith(W) && RetiredPistols.Contains(System.IO.Path.GetFileNameWithoutExtension(path));

    static LootTable Table(string name, int minRolls, int maxRolls, List<E> entries)
    {
        var t = LoadOrCreate<LootTable>($"{LootDir}/{name}.asset");
        t.minRolls = minRolls; t.maxRolls = maxRolls;
        t.entries = new List<LootEntry>();
        foreach (var e in entries)
        {
            if (IsRetired(e.path)) continue; // 드롭 0
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 시작 시 월드 루팅 배치 (LootDatabase 설정 사용)
///  1) 상자: 나무 50 / 금속 10 / 군용 2 를 야외 NavMesh 위 랜덤 위치에 (건물 안, 서로 10m 이내, 시작 지점 15m 이내 제외)
///  2) 건물별 루팅: 집/경찰서/마트/교회/창고 실내 바닥에 아이템 배치
///  3) 창고: 좀비 추가 스폰
///  4) 실내 상자: mustHaveCrate 건물마다 1개 (2026-11-12)
///  5) 가방 보장: 민가·상점·창고 종류별 1개 + 시작 지점 가까운 집 1개 (2026-11-12)
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이 열리면 자동 생성 (Resources/LootDatabase 필요).
/// </summary>
public class LootWorldSpawner : MonoBehaviour
{
    const float CrateMinSpacing = 10f;
    const float StartSafeRadius = 15f;

    // 씬이 로드될 때마다 자동 생성 (재시작으로 씬을 다시 로드해도 상자/루팅이 다시 깔리게)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => AutoCreate();

    static void AutoCreate()
    {
        // 게임 씬(DayNightCycle 있음)에서만. 강화학습 학습장에도 Player 태그(더미)가 있어서
        // 여기서 막지 않으면 학습장에 상자 수십 개가 깔려 장애물이 된다.
        if (!WorldDecor.IsGameScene) return;
        if (LootDatabase.Instance == null || GameObject.FindWithTag("Player") == null) return;
        if (FindFirstObjectByType<LootWorldSpawner>() != null) return;
        new GameObject("LootWorldSpawner").AddComponent<LootWorldSpawner>();
    }

    readonly List<Bounds> buildingTriggers = new List<Bounds>();
    readonly List<Vector3> cratePositions = new List<Vector3>();

    void Start()
    {
        var db = LootDatabase.Instance;
        var buildings = FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None);
        foreach (var b in buildings)
        {
            var t = FindTrigger(b);
            if (t != null) buildingTriggers.Add(t.bounds);
        }

        int crates = SpawnCrates(db);
        int items = 0, zombies = 0;
        foreach (var b in buildings) SpawnBuildingLoot(db, b, ref items, ref zombies);
        int indoorCrates = SpawnInteriorCrates(db, buildings);   // 2026-11-12: 건물마다 상자 1개 보장
        int bags = EnsureBackpacks(buildings);                    // 2026-11-12: 민가·상점·창고 + 시작 근처 가방 보장
        Debug.Log($"[LootWorldSpawner] 상자 {crates}개(실내 {indoorCrates}개 별도), 건물 루팅 아이템 {items}개, 창고 좀비 {zombies}마리, 보장 가방 {bags}개 배치");
    }

    // ── 상자 ──
    int SpawnCrates(LootDatabase db)
    {
        Bounds area = MapArea();
        Vector3 start = GameObject.FindWithTag("Player").transform.position;
        int placed = 0;
        placed += PlaceCrates(db.militaryCrateCount, Rarity.Epic, db.crateMilitary, db.crateMilitaryGuaranteed, area, start);
        placed += PlaceCrates(db.metalCrateCount, Rarity.Rare, db.crateMetal, null, area, start);
        placed += PlaceCrates(db.woodCrateCount, Rarity.Common, db.crateWood, null, area, start);
        return placed;
    }

    int PlaceCrates(int count, Rarity tier, LootTable table, LootTable guaranteed, Bounds area, Vector3 start)
    {
        if (table == null) return 0;
        int placed = 0;
        for (int attempt = 0; attempt < count * 40 && placed < count; attempt++)
        {
            Vector3 p = new Vector3(Random.Range(area.min.x, area.max.x), area.max.y, Random.Range(area.min.z, area.max.z));
            if (!NavMesh.SamplePosition(new Vector3(p.x, GroundY(p), p.z), out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            Vector3 pos = hit.position;
            if ((pos - start).sqrMagnitude < StartSafeRadius * StartSafeRadius) continue;
            if (InsideBuilding(pos)) continue;
            if (cratePositions.Exists(c => (c - pos).sqrMagnitude < CrateMinSpacing * CrateMinSpacing)) continue;
            // 상자 놓을 자리(1m)에 다른 물체가 없어야 함
            if (Physics.CheckBox(pos + Vector3.up * 0.4f, new Vector3(0.6f, 0.3f, 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;

            var crate = LootCrate.CreateCrate(pos, Random.Range(0f, 360f), tier, table, guaranteed);
            crate.transform.SetParent(transform, true);
            cratePositions.Add(pos);
            placed++;
        }
        return placed;
    }

    // NavMesh가 깔린 영역 = 맵 (지형 있으면 지형 범위)
    static Bounds MapArea()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            Vector3 size = terrain.terrainData.size, origin = terrain.transform.position;
            return new Bounds(origin + new Vector3(size.x / 2f, 200f, size.z / 2f), new Vector3(size.x * 0.95f, 0f, size.z * 0.95f));
        }
        var tri = NavMesh.CalculateTriangulation();
        var b = new Bounds(tri.vertices.Length > 0 ? tri.vertices[0] : Vector3.zero, Vector3.zero);
        foreach (var v in tri.vertices) b.Encapsulate(v);
        b.center = new Vector3(b.center.x, b.max.y + 50f, b.center.z);
        return b;
    }

    static float GroundY(Vector3 top)
    {
        return Physics.Raycast(new Vector3(top.x, 400f, top.z), Vector3.down, out RaycastHit h, 800f, ~0, QueryTriggerInteraction.Ignore)
            ? h.point.y : 0f;
    }

    bool InsideBuilding(Vector3 p)
    {
        foreach (var b in buildingTriggers)
        {
            var e = b; e.Expand(new Vector3(3f, 20f, 3f)); // 벽 바로 옆도 피함
            if (e.Contains(p)) return true;
        }
        return false;
    }

    // ── 건물 루팅 ──
    void SpawnBuildingLoot(LootDatabase db, BuildingInterior b, ref int items, ref int zombies)
    {
        var rule = db.GetRule(LootDatabase.Classify(b));
        var trig = FindTrigger(b);
        if (rule == null || trig == null) return;

        if (rule.table != null)
        {
            foreach (var (item, amount) in rule.table.Roll())
            {
                if (TryInteriorPoint(trig.bounds, out Vector3 p))
                {
                    var drop = WorldItemDrop.Spawn(p, item, amount);
                    if (drop != null)
                    {
                        drop.transform.SetParent(transform, true); items++;
                        if (item is BackpackData) bagKinds.Add(LootDatabase.Classify(b)); // 가방 보장 체크용
                    }
                }
            }
        }

        if (rule.extraZombies > 0)
        {
            var spawner = FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include);
            for (int i = 0; spawner != null && i < rule.extraZombies; i++)
                if (TryInteriorPoint(trig.bounds, out Vector3 p) && spawner.SpawnExtraNear(p, 0.5f, 2f) != null) zombies++;
        }
    }

    // ── 실내 상자 보장 (2026-11-12) ──
    // 파밍 루프 강화: mustHaveCrate가 켜진 건물마다 실내 바닥에 상자 1개. 등급은 일반 70% / 희귀 25% / 전설(군용) 5%
    int SpawnInteriorCrates(LootDatabase db, BuildingInterior[] buildings)
    {
        int placed = 0;
        foreach (var b in buildings)
        {
            if (!b.mustHaveCrate) continue;
            var trig = FindTrigger(b);
            if (trig == null) continue;

            float r = Random.value;
            Rarity tier = r < 0.05f ? Rarity.Epic : r < 0.30f ? Rarity.Rare : Rarity.Common;
            LootTable table = tier == Rarity.Epic ? db.crateMilitary : tier == Rarity.Rare ? db.crateMetal : db.crateWood;
            LootTable guaranteed = tier == Rarity.Epic ? db.crateMilitaryGuaranteed : null;
            if (table == null) continue;

            // 상자 자리(1m)가 비어 있는 실내 바닥점을 몇 번 찾아봄
            for (int i = 0; i < 8; i++)
            {
                if (!TryInteriorPoint(trig.bounds, out Vector3 p)) continue;
                if (Physics.CheckBox(p + Vector3.up * 0.4f, new Vector3(0.6f, 0.3f, 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                var crate = LootCrate.CreateCrate(p, Random.Range(0f, 360f), tier, table, guaranteed);
                crate.transform.SetParent(transform, true);
                placed++;
                break;
            }
        }
        return placed;
    }

    // ── 가방 보장 (2026-11-12) ──
    // 가방이 드물어 인벤토리 확장이 어려움 → 민가·상점·창고 종류마다 가방이 하나도 안 나왔으면 그 종류 건물 1곳에 1개,
    // 그리고 시작 위치에서 가장 가까운 집에 1개 (Day 1~2에도 얻을 수 있게)
    readonly HashSet<LootDatabase.BuildingKind> bagKinds = new HashSet<LootDatabase.BuildingKind>();

    int EnsureBackpacks(BuildingInterior[] buildings)
    {
        var bag = Resources.Load<BackpackData>("Backpacks/Backpack_Medium");
        if (bag == null) return 0;
        int placed = 0;

        foreach (var kind in new[] { LootDatabase.BuildingKind.House, LootDatabase.BuildingKind.Mart, LootDatabase.BuildingKind.Warehouse })
        {
            if (bagKinds.Contains(kind)) continue;
            var list = System.Array.FindAll(buildings, x => LootDatabase.Classify(x) == kind && FindTrigger(x) != null);
            if (list.Length == 0) continue;
            if (PlaceBag(bag, list[Random.Range(0, list.Length)])) placed++;
        }

        // 시작 지점에서 가장 가까운 집 (실내 바닥을 못 찾으면 그다음 집)
        Vector3 start = GameObject.FindWithTag("Player").transform.position;
        var houses = System.Array.FindAll(buildings, x => LootDatabase.Classify(x) == LootDatabase.BuildingKind.House && FindTrigger(x) != null);
        System.Array.Sort(houses, (a, c) => (a.transform.position - start).sqrMagnitude.CompareTo((c.transform.position - start).sqrMagnitude));
        for (int i = 0; i < houses.Length && i < 5; i++)
            if (PlaceBag(bag, houses[i])) { placed++; break; }
        return placed;
    }

    bool PlaceBag(BackpackData bag, BuildingInterior b)
    {
        if (!TryInteriorPoint(FindTrigger(b).bounds, out Vector3 p)) return false;
        var drop = WorldItemDrop.Spawn(p, bag, 1);
        if (drop == null) return false;
        drop.transform.SetParent(transform, true);
        return true;
    }

    // 실내 바닥 위 걸을 수 있는 점 (트리거 범위 안쪽, 아래로 레이 → NavMesh 0.6m 이내, NavMesh 없으면 빈 공간 체크)
    public static bool TryInteriorPoint(Bounds tb, out Vector3 point) // MissionManager(열쇠 카드 배치)도 사용
    {
        for (int i = 0; i < 12; i++)
        {
            float x = Random.Range(tb.min.x + 0.8f, tb.max.x - 0.8f), z = Random.Range(tb.min.z + 0.8f, tb.max.z - 0.8f);
            // 트리거 바닥이 지면보다 낮은 건물(교회/옛날 집: 지면 -2.6m)은 지면 기준 (안 그러면 땅속에서 쏴서 항상 실패)
            var terrain = Terrain.activeTerrain;
            float floor = terrain != null ? Mathf.Max(tb.min.y, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y) : tb.min.y;
            Vector3 from = new Vector3(x, floor + 2.2f, z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.7f) continue;
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit nh, 0.6f, NavMesh.AllAreas)) { point = nh.position; return true; }
            // 나중에 진입 가능해진 건물(경찰서/교회 등)은 실내에 NavMesh가 없음 → 평평하고 위가 비어 있으면 OK
            if (!Physics.CheckSphere(hit.point + Vector3.up * 0.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) { point = hit.point; return true; }
        }
        point = Vector3.zero;
        return false;
    }

    public static Collider FindTrigger(BuildingInterior b)
    {
        foreach (var c in b.GetComponents<Collider>()) if (c.isTrigger) return c;
        return null;
    }
}

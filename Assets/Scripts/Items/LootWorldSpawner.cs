using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 시작 시 월드 루팅 배치 (LootDatabase 설정 사용)
///  1) 상자: 나무 50 / 금속 10 / 군용 2 를 야외 NavMesh 위 랜덤 위치에 (건물 안, 서로 10m 이내, 시작 지점 15m 이내 제외)
///  2) 건물별 루팅: 집/경찰서/마트/교회/창고 실내 바닥에 아이템 배치
///  3) 창고: 좀비 추가 스폰
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
        Debug.Log($"[LootWorldSpawner] 상자 {crates}개, 건물 루팅 아이템 {items}개, 창고 좀비 {zombies}마리 배치");
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
                    if (drop != null) { drop.transform.SetParent(transform, true); items++; }
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

    // 실내 바닥 위 걸을 수 있는 점 (트리거 범위 안쪽, 아래로 레이 → NavMesh 0.6m 이내, NavMesh 없으면 빈 공간 체크)
    static bool TryInteriorPoint(Bounds tb, out Vector3 point)
    {
        for (int i = 0; i < 12; i++)
        {
            float x = Random.Range(tb.min.x + 0.8f, tb.max.x - 0.8f), z = Random.Range(tb.min.z + 0.8f, tb.max.z - 0.8f);
            Vector3 from = new Vector3(x, tb.min.y + 2.2f, z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.7f) continue;
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit nh, 0.6f, NavMesh.AllAreas)) { point = nh.position; return true; }
            // 나중에 진입 가능해진 건물(경찰서/교회 등)은 실내에 NavMesh가 없음 → 평평하고 위가 비어 있으면 OK
            if (!Physics.CheckSphere(hit.point + Vector3.up * 0.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) { point = hit.point; return true; }
        }
        point = Vector3.zero;
        return false;
    }

    static Collider FindTrigger(BuildingInterior b)
    {
        foreach (var c in b.GetComponents<Collider>()) if (c.isTrigger) return c;
        return null;
    }
}

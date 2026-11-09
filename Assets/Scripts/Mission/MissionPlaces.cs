using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 미션 장소 (맵에 원래 없는 곳을 매 판 지정/생성). 처음 Ensure() 때 한 번 정함:
///  - 주유소 / 카페 / 서점 = 민가 중 랜덤 (서로 60m 이상), 발전소 = 창고 중 랜덤 (없으면 민가)
///    → EntranceMarkers가 이 건물 입구 라벨/색을 장소 이름으로 바꿈
///  - 라디오 타워 / 공원 = 맵 안 트인 빈 땅에 모형을 세움 (MissionProps)
/// EntranceMarkers.Start와 MissionManager.Start 중 먼저 부르는 쪽에서 만들어짐. 씬을 다시 불러오면 새로 정함.
/// </summary>
public static class MissionPlaces
{
    public enum Kind { GasStation, Cafe, Bookstore, PowerPlant, RadioTower, Park }

    public class Place
    {
        public Kind kind;
        public string label;
        public Color color;
        public BuildingInterior building; // 건물 장소만 (라디오 타워/공원은 null)
        public Vector3 position;          // 건물: 입구 바깥, 그 외: 중심 (지면)
    }

    static readonly Dictionary<Kind, Place> places = new Dictionary<Kind, Place>();
    static Transform root; // 생성한 모형 부모 (씬이 바뀌면 파괴됨 → null이면 새로 정함)

    public static string LabelOf(Kind k) => k switch
    {
        Kind.GasStation => "주유소",
        Kind.Cafe => "카페",
        Kind.Bookstore => "서점",
        Kind.PowerPlant => "발전소",
        Kind.RadioTower => "라디오 타워",
        _ => "공원",
    };

    static Color ColorOf(Kind k) => k switch
    {
        Kind.GasStation => new Color(1f, 0.45f, 0.1f),
        Kind.Cafe => new Color(0.75f, 0.5f, 0.3f),
        Kind.Bookstore => new Color(0.65f, 0.4f, 1f),
        Kind.PowerPlant => new Color(1f, 0.9f, 0.2f),
        _ => Color.white,
    };

    public static bool TryGet(Kind k, out Place p) { Ensure(); return places.TryGetValue(k, out p); }

    /// <summary>이 건물이 미션 장소로 지정됐으면 그 장소 (EntranceMarkers 라벨용)</summary>
    public static bool TryGetByBuilding(BuildingInterior b, out Place p)
    {
        Ensure();
        foreach (var kv in places) if (kv.Value.building == b) { p = kv.Value; return true; }
        p = null;
        return false;
    }

    public static void Ensure()
    {
        if (root != null) return;
        places.Clear();
        root = new GameObject("MissionPlaces").transform;

        var player = GameObject.FindWithTag("Player");
        float refY = player != null ? player.transform.position.y : 15f;
        var buildings = new List<BuildingInterior>(Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None));
        var houses = buildings.FindAll(b => LootDatabase.Classify(b) == LootDatabase.BuildingKind.House && HasDoor(b));
        var warehouses = buildings.FindAll(b => LootDatabase.Classify(b) == LootDatabase.BuildingKind.Warehouse && HasDoor(b));

        PickBuilding(Kind.PowerPlant, warehouses.Count > 0 ? warehouses : houses);
        PickBuilding(Kind.GasStation, houses);
        PickBuilding(Kind.Cafe, houses);
        PickBuilding(Kind.Bookstore, houses);

        var trigs = new List<Bounds>();
        foreach (var b in buildings) { var t = LootWorldSpawner.FindTrigger(b); if (t != null) { var e = t.bounds; e.Expand(new Vector3(14f, 40f, 14f)); trigs.Add(e); } }
        if (TryOpenGround(refY, 4f, trigs, out Vector3 tower))
        {
            MissionProps.RadioTower(tower).SetParent(root, true);
            Add(Kind.RadioTower, null, tower);
        }
        else Debug.LogWarning("[MissionPlaces] 라디오 타워 자리를 못 찾음");
        if (TryOpenGround(refY, 8.5f, trigs, out Vector3 park))
        {
            MissionProps.Park(park).SetParent(root, true);
            Add(Kind.Park, null, park);
        }
        else Debug.LogWarning("[MissionPlaces] 공원 자리를 못 찾음");

        var log = new List<string>();
        foreach (var kv in places) log.Add($"{kv.Value.label}{(kv.Value.building != null ? "(" + kv.Value.building.name + ")" : "")} {kv.Value.position:F0}");
        Debug.Log("[MissionPlaces] " + string.Join(" / ", log));
    }

    static bool HasDoor(BuildingInterior b) => LootWorldSpawner.FindTrigger(b) != null && b.TryGetDoor(out _, out _);

    static void Add(Kind k, BuildingInterior b, Vector3 pos)
        => places[k] = new Place { kind = k, label = LabelOf(k), color = ColorOf(k), building = b, position = pos };

    // 아직 안 쓴 건물 중 랜덤 (이미 지정한 장소들과 60m 이상, 안 되면 거리 무시)
    static void PickBuilding(Kind k, List<BuildingInterior> list)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var left = new List<BuildingInterior>(list);
            while (left.Count > 0)
            {
                int i = Random.Range(0, left.Count);
                var b = left[i];
                left.RemoveAt(i);
                if (IsUsed(b)) continue;
                b.TryGetDoor(out Vector3 door, out Vector3 outDir);
                Vector3 pos = door + outDir * 2f;
                if (pass == 0 && TooClose(pos, 60f)) continue;
                Add(k, b, pos);
                return;
            }
        }
        Debug.LogWarning($"[MissionPlaces] {LabelOf(k)}로 쓸 건물이 없음");
    }

    static bool IsUsed(BuildingInterior b) { foreach (var kv in places) if (kv.Value.building == b) return true; return false; }
    static bool TooClose(Vector3 p, float d) { foreach (var kv in places) if ((kv.Value.position - p).sqrMagnitude < d * d) return true; return false; }

    // 맵 안쪽(지형 85%) 트인 땅: 지면 점 + 건물 주변 아님 + 반경 r 안 0.6~3.6m 높이에 걸리는 것 없음 + 맵과 이어짐 + 다른 장소와 60m 이상
    static bool TryOpenGround(float refY, float r, List<Bounds> trigs, out Vector3 result)
    {
        var terrain = Terrain.activeTerrain;
        Bounds area = terrain != null
            ? new Bounds(terrain.transform.position + terrain.terrainData.size / 2f, new Vector3(terrain.terrainData.size.x * 0.8f, 200f, terrain.terrainData.size.z * 0.8f))
            : new Bounds(Vector3.zero, new Vector3(500f, 200f, 500f));
        for (int i = 0; i < 600; i++)
        {
            if (!MissionManager.TryGroundPoint(Random.Range(area.min.x, area.max.x), Random.Range(area.min.z, area.max.z), refY, out Vector3 c)) continue;
            if (trigs.Exists(t => t.Contains(c))) continue;
            if (Physics.CheckBox(c + Vector3.up * 2.1f, new Vector3(r, 1.5f, r), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (TooClose(c, 60f)) continue;
            if (!MissionManager.IsConnectedToMap(c)) continue;
            result = c;
            return true;
        }
        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 건물 안 걸어갈 수 있는 바닥 점 (문에서 이어지는 곳). 실내 NavMesh가 없는 건물(경찰서 등)은 평평한 빈 바닥,
    /// 그것도 없으면 문 바로 안쪽.
    /// </summary>
    public static bool TryBuildingPoint(BuildingInterior b, out Vector3 point)
    {
        point = Vector3.zero;
        var trig = LootWorldSpawner.FindTrigger(b);
        if (trig == null || !b.TryGetDoor(out Vector3 door, out Vector3 outDir)) return false;
        var path = new NavMeshPath();
        for (int i = 0; i < 6; i++)
        {
            if (!LootWorldSpawner.TryInteriorPoint(trig.bounds, out Vector3 p)) continue;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas)) continue;
            Vector3 outside = door + outDir * 3f; outside.y = hit.position.y;
            if (!NavMesh.SamplePosition(outside, out NavMeshHit from, 4f, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(from.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            point = hit.position;
            return true;
        }
        if (LootWorldSpawner.TryInteriorPoint(trig.bounds, out Vector3 any)) { point = any; return true; }
        point = door - outDir * 1.5f;
        if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit h, 5f, ~0, QueryTriggerInteraction.Ignore)) point = h.point;
        return true;
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 열쇠 카드 후보 지점 (게임 시작 시 맵에서 자동 수집, 씬 파일에 Transform을 저장하지 않음).
///  - 실내 IndoorCount곳: 경찰서/병원/마트/교회 + 나머지 건물 랜덤. 문에서 걸어갈 수 있는 곳만
///  - 실외 OutdoorCount곳: 벤치/쓰레기통/차/가로등 옆. 걸어갈 수 있는 곳만
/// MissionManager가 이 중 4곳을 랜덤으로 고름.
/// </summary>
public static class KeyCardSpawnPoints
{
    const int IndoorCount = 14, OutdoorCount = 12;

    // 실외 후보로 쓰는 소품 (이름 앞부분, 라벨)
    static readonly (string prefix, string label)[] OutdoorProps =
    {
        ("bench", "벤치 옆"), ("bin-", "쓰레기통 옆"), ("car ", "차 옆"), ("lampione", "가로등 아래"),
    };

    public static List<(Vector3 pos, string label)> Build(List<BuildingInterior> buildings)
    {
        var result = new List<(Vector3, string)>();
        AddIndoor(buildings, result);
        AddOutdoor(buildings, result);
        return result;
    }

    static void AddIndoor(List<BuildingInterior> buildings, List<(Vector3, string)> result)
    {
        // 미션 건물을 앞에 두고 나머지는 섞음
        var order = new List<BuildingInterior>(buildings);
        Shuffle(order);
        order.Sort((a, b) => IsLandmark(b).CompareTo(IsLandmark(a)));

        int added = 0;
        var path = new NavMeshPath();
        foreach (var b in order)
        {
            if (added >= IndoorCount) break;
            var trig = LootWorldSpawner.FindTrigger(b);
            if (trig == null || !b.TryGetDoor(out Vector3 door, out Vector3 outDir)) continue;
            if (!LootWorldSpawner.TryInteriorPoint(trig.bounds, out Vector3 p)) continue;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas)) continue;
            // 문 바깥에서 실내 그 지점까지 실제로 이어지는지 (막힌 방 제외 - 못 줍는 카드가 나오면 클리어 불가)
            Vector3 outside = door + outDir * 3f;
            outside.y = hit.position.y;
            if (!NavMesh.SamplePosition(outside, out NavMeshHit from, 4f, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(from.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            result.Add((hit.position, MissionManager.BuildingLabel(LootDatabase.Classify(b))));
            added++;
        }
    }

    static bool IsLandmark(BuildingInterior b)
    {
        var k = LootDatabase.Classify(b);
        return k == LootDatabase.BuildingKind.Police || k == LootDatabase.BuildingKind.Hospital || k == LootDatabase.BuildingKind.Mart || k == LootDatabase.BuildingKind.Church;
    }

    static void AddOutdoor(List<BuildingInterior> buildings, List<(Vector3, string)> result)
    {
        var trigs = new List<Bounds>();
        foreach (var b in buildings) { var t = LootWorldSpawner.FindTrigger(b); if (t != null) { var e = t.bounds; e.Expand(new Vector3(2f, 40f, 2f)); trigs.Add(e); } }

        var byProp = new List<Vector3>[OutdoorProps.Length];
        for (int i = 0; i < byProp.Length; i++) byProp[i] = new List<Vector3>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string n = r.name.ToLowerInvariant();
            for (int i = 0; i < OutdoorProps.Length; i++)
                if (n.StartsWith(OutdoorProps[i].prefix)) { byProp[i].Add(r.bounds.center); break; }
        }

        // 종류별로 돌아가며 하나씩 (같은 종류에 몰리지 않게)
        foreach (var list in byProp) Shuffle(list);
        int added = 0;
        for (int round = 0; added < OutdoorCount && round < 40; round++)
        {
            int i = round % OutdoorProps.Length;
            if (byProp[i].Count == 0) continue;
            Vector3 prop = byProp[i][byProp[i].Count - 1];
            byProp[i].RemoveAt(byProp[i].Count - 1);
            if (TryBeside(prop, trigs, out Vector3 p)) { result.Add((p, OutdoorProps[i].label)); added++; }
        }
    }

    // 소품 옆 1.5~3m 지면 NavMesh 점 (건물 안/지붕 제외, 맵 안쪽으로 걸어갈 수 있는 곳만)
    static bool TryBeside(Vector3 prop, List<Bounds> trigs, out Vector3 point)
    {
        var terrain = Terrain.activeTerrain;
        for (int i = 0; i < 6; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(1.5f, 3f);
            Vector3 p = prop + new Vector3(d.x, 0f, d.y);
            float ground = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : prop.y;
            p.y = ground + 0.5f;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(hit.position.y - ground) > 1.5f) continue;
            if (trigs.Exists(t => t.Contains(hit.position))) continue;
            if (!MissionManager.IsConnectedToMap(hit.position)) continue;
            point = hit.position;
            return true;
        }
        point = Vector3.zero;
        return false;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

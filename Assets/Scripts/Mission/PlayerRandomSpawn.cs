using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 랜덤 시작 위치: 씬이 로드될 때(다른 스크립트 Start 전) 맵 전역 후보 중 한 곳으로 씬의 Player를 옮김.
/// 새로 Instantiate하지 않음 - 카메라/HUD/인벤토리가 씬의 Player를 참조하고 있어서 옮기는 쪽이 안전.
/// 후보 조건: NavMesh 위 / 지면 높이(지붕 X) / 건물 밖 / 주변이 트임 / 좀비 스폰 포인트 근처 아님 / 맵과 연결됨 / 15m 안 좀비 2마리 이하.
/// 건물 이벤트 좀비는 씬 로드 뒤에 생기므로, 첫 프레임에 다시 확인해서 붐비면 다음 후보로 옮김 (SpawnProtection이 호출).
/// 열쇠 카드는 이 위치에서 50m 이상 떨어진 곳에만 놓임 (MissionManager).
/// </summary>
public static class PlayerRandomSpawn
{
    const int CandidateCount = 15;
    const float MinCandidateSpacing = 80f; // 후보끼리 최소 거리 (맵 전역에 분산)
    const float ZombieSpawnClearance = 25f; // 좀비 스폰 포인트에서 최소 거리 (좀비 밀집 지역 제외)
    const float MinFromLastSpawn = 80f;     // 직전 판 시작 위치에서 최소 거리
    const float CrowdRadius = 15f;          // 이 반경 안 좀비가
    const int MaxNearbyZombies = 2;         // 이 수를 넘으면 다음 후보로
    const float SpawnHeightOffset = 0.6f;

    static Vector3? lastSpawn;
    static readonly List<Vector3> candidates = new List<Vector3>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null || Terrain.activeTerrain == null) return;

        candidates.Clear();
        candidates.AddRange(BuildCandidates(player.transform.position.y));
        if (lastSpawn.HasValue && candidates.Exists(c => (c - lastSpawn.Value).sqrMagnitude >= MinFromLastSpawn * MinFromLastSpawn))
            candidates.RemoveAll(c => (c - lastSpawn.Value).sqrMagnitude < MinFromLastSpawn * MinFromLastSpawn);
        if (candidates.Count == 0) { Debug.Log("[PlayerRandomSpawn] 후보 없음 - 씬 위치에서 시작"); return; }

        Shuffle(candidates);
        int idx = candidates.FindIndex(c => ZombiesNear(c) <= MaxNearbyZombies);
        Vector3 pos = candidates[Mathf.Max(idx, 0)];
        MovePlayer(player, pos);
        Debug.Log($"[PlayerRandomSpawn] 후보 {candidates.Count}곳 중 시작 위치 {pos}");
    }

    /// <summary>시작 지점 15m 안 좀비가 2마리를 넘으면 다음 후보로 옮김 (열쇠 카드 50m 규칙 유지). 맞는 곳이 없으면 가장 한산한 후보로</summary>
    public static void RelocateIfCrowded()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        int here = ZombiesNear(player.transform.position);
        if (here <= MaxNearbyZombies) return;

        var mm = MissionManager.Instance;
        bool FarFromCards(Vector3 c) => mm == null || !mm.keyCardsInWorld.Exists(k => k != null && (k.transform.position - c).sqrMagnitude < mm.keyCardMinPlayerDistance * mm.keyCardMinPlayerDistance);

        Vector3? best = null;
        int bestCount = here;
        foreach (var c in candidates)
        {
            if (!FarFromCards(c)) continue;
            int n = ZombiesNear(c);
            if (n >= bestCount) continue;
            bestCount = n;
            best = c;
            if (n <= MaxNearbyZombies) break;
        }
        if (!best.HasValue) { Debug.Log($"[PlayerRandomSpawn] 15m 안 좀비 {here}마리 - 더 한산한 후보 없음"); return; }
        MovePlayer(player, best.Value);
        Debug.Log($"[PlayerRandomSpawn] 15m 안 좀비 {here}마리 → {best.Value}로 이동 ({bestCount}마리)");
    }

    static int ZombiesNear(Vector3 pos)
    {
        int n = 0;
        foreach (var z in ZombieController.All)
        {
            if (z == null) continue;
            Vector3 d = z.transform.position - pos; d.y = 0f;
            if (d.sqrMagnitude < CrowdRadius * CrowdRadius) n++;
        }
        return n;
    }

    static void MovePlayer(GameObject player, Vector3 ground)
    {
        Vector3 pos = ground + Vector3.up * SpawnHeightOffset;
        lastSpawn = pos;
        player.transform.position = pos;
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }

    static void Shuffle(List<Vector3> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static List<Vector3> BuildCandidates(float groundY)
    {
        var terrain = Terrain.activeTerrain;
        Vector3 size = terrain.terrainData.size, origin = terrain.transform.position;
        var area = new Bounds(origin + size / 2f, new Vector3(size.x * 0.85f, size.y, size.z * 0.85f));

        var trigs = new List<Bounds>();
        foreach (var b in Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            var t = LootWorldSpawner.FindTrigger(b);
            if (t == null) continue;
            var e = t.bounds;
            e.Expand(new Vector3(8f, 40f, 8f));
            trigs.Add(e);
        }

        var zombiePoints = new List<Vector3>();
        var spawner = Object.FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include);
        if (spawner != null)
        {
            if (spawner.indoorSpawnPoints != null) foreach (var p in spawner.indoorSpawnPoints) if (p != null) zombiePoints.Add(p.position);
            if (spawner.outdoorSpawnPoints != null) foreach (var p in spawner.outdoorSpawnPoints) if (p != null) zombiePoints.Add(p.position);
        }

        var result = new List<Vector3>();
        for (int i = 0; i < 600 && result.Count < CandidateCount; i++)
        {
            if (!MissionManager.TryGroundPoint(Random.Range(area.min.x, area.max.x), Random.Range(area.min.z, area.max.z), groundY, out Vector3 c)) continue; // 지붕/위층/맵 아래 제외
            if (trigs.Exists(t => t.Contains(c))) continue;                                                   // 건물 안/바로 옆 제외
            if (Physics.CheckSphere(c + Vector3.up * 2.6f, 2f, ~0, QueryTriggerInteraction.Ignore)) continue; // 주변이 트여 있어야
            if (result.Exists(q => (q - c).sqrMagnitude < MinCandidateSpacing * MinCandidateSpacing)) continue;
            if (zombiePoints.Exists(z => { float dx = z.x - c.x, dz = z.z - c.z; return dx * dx + dz * dz < ZombieSpawnClearance * ZombieSpawnClearance; })) continue;
            if (!MissionManager.IsConnectedToMap(c)) continue;
            result.Add(c);
        }
        return result;
    }
}

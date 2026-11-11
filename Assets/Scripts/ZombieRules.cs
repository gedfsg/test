using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 규칙에서 오는 좀비 제약 모음 (AI 구현과 무관). 좀비 컨트롤러는 행동을 정하기 전에 여기에 물어봄.
/// FSM/ML 컨트롤러(Zombie/ZombieController)로 통합할 때도 같은 호출만 넣으면 됨:
///  - IsHeld: 스폰 보호 - 추격/공격하지 않고 HeldSpeedFactor로 느리게
///  - CanAttack: 스폰 보호 / 건물에서 막 나온 안전 창
///  - (이전) DoorAvoidApplies + TryPickDoorAvoidDestination: 플레이어 실내 시 입구 5m 좀비를 밖으로 밀어냄
///    → 밀려난 좀비가 다시 플레이어를 쫓아 입구로 돌아와서 입구 앞을 맴도는 원인이었음. 아래 입구 규칙으로 교체 (git 기록 참고)
///  - SpeedMultiplier / DamageMultiplier: 7일 이후 난이도
///
/// ━ 건물 입구 규칙 (2026-11) ━ (NavMesh는 문을 통해 실내까지 이어져 있음 - 실측 94/94)
///  - BuildingAt(pos): pos가 들어 있는 건물 (트리거 범위 캐시)
///  - MayChasePlayer: 플레이어가 실내면, 같은 건물 안에 있는 좀비(+ 최종 공격 좀비)만 추격. 바깥 좀비는 배회로
///  - AdjustDestination: 바깥 좀비의 목적지가 입구 반경(BuildingInterior.exclusionRadius, 3m) 안이면 반경 밖 가장 가까운 점으로
///  - TryGetDoorExit: 바깥 좀비가 입구 반경 안에서 서성이면 입구에서 DoorWaitDistance(5m) 이상 떨어진 곳으로
///  - 실내 좀비(스폰 시 건물 안): 플레이어가 그 건물에 들어오기 전엔 감지 OFF (ZombieController가 처리)
/// </summary>
public static class ZombieRules
{
    public const float HeldSpeedFactor = 0.25f; // 스폰 보호 중 이동 속도 배수
    // public const float DoorAvoidRadius = 5f;  // (이전) 플레이어가 실내에 있으면 입구 이 반경(m) 안 좀비는 물러남
    public const float DoorWaitDistance = 5f;   // 입구 반경 안에서 서성이던 바깥 좀비가 물러나는 거리 (m)
    public const float IndoorAlertDelay = 1f;   // 플레이어가 들어온 뒤 실내 좀비가 알아차리기까지 (초)

    public static float SpeedMultiplier => ZombieSpawner.DifficultyMultiplier;
    public static float DamageMultiplier => ZombieSpawner.DifficultyMultiplier;
    /// <summary>7일 이후 좀비 체력 배수 (Day 8 +10%, 하루마다 +10%) - 2026-11-12 추가 (스폰 시 ZombieController.SetupHealth)</summary>
    public static float HealthMultiplier => ZombieSpawner.DifficultyMultiplier;

    public static bool IsHeld(Vector3 zombiePos) => SpawnProtection.Holds(zombiePos);

    public static bool CanAttack(Vector3 zombiePos) => !SpawnProtection.Holds(zombiePos) && !BuildingInterior.InSafeExit(zombiePos);

    // ── 건물 / 입구 캐시 ─────────────────────────────
    struct Door { public Vector3 pos, outDir; public float radius; }
    struct Area { public BuildingInterior building; public Bounds bounds; }
    static readonly List<Door> doors = new List<Door>();
    static readonly List<Area> areas = new List<Area>();
    static int cacheScene = -1;

    // 씬이 바뀌면(재시작 포함) 다시 모음. 트리거는 씬 로드 때 벽 안쪽으로 줄어든 뒤라(BuildingInterior) 첫 사용 시점 값이 최종
    static void EnsureCache()
    {
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (cacheScene == scene && (areas.Count == 0 || areas[0].building != null)) return;
        cacheScene = scene;
        doors.Clear();
        areas.Clear();
        foreach (var b in Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            var trig = LootWorldSpawner.FindTrigger(b);
            if (trig != null) areas.Add(new Area { building = b, bounds = trig.bounds });
            if (!b.TryGetDoor(out Vector3 center, out Vector3 outDir)) continue;
            // 문이 여러 벽에 걸친 건물(병원 등)은 TryGetDoor가 평균을 내므로, 실제 문 구멍마다 따로 등록
            var holes = EntranceMarkers.DoorCenters(b.GetComponentsInChildren<BuildingWallCollider>(true));
            if (holes.Count == 0 && b.transform.parent != null) holes = EntranceMarkers.DoorCenters(b.transform.parent.GetComponentsInChildren<BuildingWallCollider>(true));
            if (holes.Count == 0) holes.Add(center);
            foreach (var h in holes) doors.Add(new Door { pos = h, outDir = outDir, radius = b.exclusionRadius });
        }
    }

    static float FlatSq(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

    /// <summary>pos가 들어 있는 건물 (실내 트리거 범위). 없으면 null</summary>
    public static BuildingInterior BuildingAt(Vector3 pos)
    {
        EnsureCache();
        Vector3 p = pos + Vector3.up * 0.5f; // NavMesh 높이는 바닥 면이라 트리거 아래 경계에 걸릴 수 있음
        foreach (var a in areas) if (a.building != null && a.bounds.Contains(p)) return a.building;
        return null;
    }

    /// <summary>
    /// 이 좀비가 지금 플레이어를 쫓아도 되는지 (건물 규칙만; 감지 거리는 컨트롤러가 따로 판단).
    ///  플레이어가 밖 → 가능 / 플레이어가 실내 → 같은 건물 안 좀비만 (바깥 좀비는 추격 중단 → 배회).
    ///  hunting(최종 공격 좀비)은 예외: 문으로 실내까지 걸어 들어감 (실내도 안전지대가 아니어야 함 - 기존 결정)
    /// </summary>
    public static bool MayChasePlayer(Vector3 zombiePos, bool hunting)
    {
        BuildingInterior playerIn = BuildingInterior.Current;
        if (playerIn == null || hunting) return true;
        return BuildingAt(zombiePos) == playerIn;
    }

    /// <summary>바깥 좀비 목적지 보정: 어떤 입구 반경 안이면 반경 바깥의 가장 가까운 점(NavMesh 위)으로. 실내 목적지는 그대로</summary>
    public static Vector3 AdjustDestination(Vector3 dest)
    {
        EnsureCache();
        foreach (var d in doors)
        {
            if (FlatSq(dest, d.pos) >= d.radius * d.radius) continue;
            if (BuildingAt(dest) != null) return dest; // 실내 쪽 목적지 (실내에서 플레이어를 쫓는 중)
            Vector3 away = dest - d.pos; away.y = 0f;
            Vector3 dir = away.sqrMagnitude > 0.04f ? away.normalized : d.outDir;
            // 반경 바로 바깥부터: 정면이 막혀 있으면 좌우로 30°씩 돌려 가며 찾음
            for (int i = 0; i < 7; i++)
            {
                float ang = (i + 1) / 2 * 30f * (i % 2 == 0 ? 1f : -1f);
                Vector3 c = d.pos + Quaternion.Euler(0f, ang, 0f) * dir * (d.radius + 0.4f);
                c.y = dest.y;
                if (NavMesh.SamplePosition(c, out NavMeshHit hit, 1.2f, NavMesh.AllAreas) && FlatSq(hit.position, d.pos) >= d.radius * d.radius && BuildingAt(hit.position) == null)
                    return hit.position;
            }
            // 반경 밖 점을 못 찾으면 입구 바깥 방향 DoorWaitDistance 지점에서 대기
            Vector3 w = d.pos + d.outDir * DoorWaitDistance; w.y = dest.y;
            if (NavMesh.SamplePosition(w, out NavMeshHit wait, 2f, NavMesh.AllAreas)) return wait.position;
            return dest;
        }
        return dest;
    }

    /// <summary>zombiePos가 (바깥에서) 입구 반경 안이면, 입구에서 DoorWaitDistance~+2m 떨어진 바깥 지점을 고름. 아니면 false</summary>
    public static bool TryGetDoorExit(Vector3 zombiePos, out Vector3 destination)
    {
        EnsureCache();
        destination = zombiePos;
        foreach (var d in doors)
        {
            if (FlatSq(zombiePos, d.pos) >= d.radius * d.radius) continue;
            if (BuildingAt(zombiePos) != null) return false; // 실내 쪽 (실내 좀비/실내 추격)
            Vector3 away = zombiePos - d.pos; away.y = 0f;
            Vector3 dir = away.sqrMagnitude > 0.04f ? (away.normalized + d.outDir).normalized : d.outDir;
            for (int i = 0; i < 6; i++)
            {
                Vector3 c = d.pos + Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * dir * Random.Range(DoorWaitDistance, DoorWaitDistance + 2f);
                c.y = zombiePos.y;
                if (NavMesh.SamplePosition(c, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) && FlatSq(hit.position, d.pos) >= d.radius * d.radius && BuildingAt(hit.position) == null)
                { destination = hit.position; return true; }
            }
            return false;
        }
        return false;
    }
}

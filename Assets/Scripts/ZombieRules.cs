using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 게임 규칙에서 오는 좀비 제약 모음 (AI 구현과 무관). 좀비 컨트롤러는 행동을 정하기 전에 여기에 물어봄.
/// FSM/ML 컨트롤러(Zombie/ZombieController)로 통합할 때도 같은 호출만 넣으면 됨:
///  - IsHeld: 스폰 보호 - 추격/공격하지 않고 HeldSpeedFactor로 느리게
///  - CanAttack: 스폰 보호 / 건물에서 막 나온 안전 창
///  - DoorAvoidApplies + TryPickDoorAvoidDestination: 플레이어 실내 시 입구에서 물러남
///  - SpeedMultiplier / DamageMultiplier: 7일 이후 난이도
/// </summary>
public static class ZombieRules
{
    public const float HeldSpeedFactor = 0.25f; // 스폰 보호 중 이동 속도 배수
    public const float DoorAvoidRadius = 5f;    // 플레이어가 실내에 있으면 입구 이 반경(m) 안 좀비는 물러남

    public static float SpeedMultiplier => ZombieSpawner.DifficultyMultiplier;
    public static float DamageMultiplier => ZombieSpawner.DifficultyMultiplier;

    public static bool IsHeld(Vector3 zombiePos) => SpawnProtection.Holds(zombiePos);

    public static bool CanAttack(Vector3 zombiePos) => !SpawnProtection.Holds(zombiePos) && !BuildingInterior.InSafeExit(zombiePos);

    /// <summary>입구 회피 대상 상황인지. 최종 공격 좀비(hunting)는 제외 - 실내도 안전지대가 아니어야 함</summary>
    public static bool DoorAvoidApplies(bool hunting)
    {
        BuildingInterior b = BuildingInterior.Current;
        return b != null && !hunting && b.TryGetDoor(out _, out _);
    }

    /// <summary>zombiePos가 입구 반경 안의 바깥이면 입구 반대쪽 목적지를 고름. 물러날 필요가 없거나 못 찾으면 false</summary>
    public static bool TryPickDoorAvoidDestination(Vector3 zombiePos, out Vector3 destination)
    {
        destination = zombiePos;
        BuildingInterior b = BuildingInterior.Current;
        if (b == null || !b.TryGetDoor(out Vector3 door, out Vector3 outDir)) return false;

        Vector3 away = zombiePos - door; away.y = 0f;
        if (away.sqrMagnitude >= DoorAvoidRadius * DoorAvoidRadius || b.ContainsPoint(zombiePos)) return false;
        Vector3 dir = away.sqrMagnitude > 0.01f ? away.normalized : outDir;
        dir = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * dir;
        if (!NavMesh.SamplePosition(zombiePos + dir * Random.Range(8f, 14f), out NavMeshHit hit, 4f, NavMesh.AllAreas)) return false;
        destination = hit.position;
        return true;
    }
}

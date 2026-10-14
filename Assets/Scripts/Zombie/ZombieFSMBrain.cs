using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 규칙 기반(FSM) 판단 계층. 기존 ZombieController의 행동 로직을 상태별로 분리한 것.
///
///   Idle ─┬─> Wander ──> Chase ──> Attack
///         └─> Investigate (놓친 지점 수색) ──> Wander
///   (어느 상태에서든) Stunned
///
/// ML-Agents 도입 후에도 이 컴포넌트는 남겨둔다 —— 폴백 겸, FSM vs RL 비교 실험의 대조군.
/// </summary>
public class ZombieFSMBrain : ZombieBrain
{
    [Header("배회")]
    public bool enableWander = true;
    public float wanderRadius = 8f;
    public float wanderIntervalMin = 2f;
    public float wanderIntervalMax = 5f;
    public float wanderSpeedFactor = 0.5f;

    [Header("추적")]
    [Tooltip("시야에서 사라진 뒤 이 시간까지는 계속 추적 (모퉁이 돌자마자 포기하면 어색함)")]
    public float sightMemory = 1.2f;

    [Header("수색 (타겟 놓쳤을 때)")]
    [Tooltip("마지막 목격 지점을 몇 초까지 기억하고 쫓아갈지")]
    public float investigateMemory = 6f;
    [Tooltip("수색 지점에 이 거리까지 접근하면 도착으로 간주")]
    public float investigateArrive = 1.5f;
    public float investigateSpeedFactor = 0.8f;

    [Header("피격 반응")]
    [Tooltip("맞았을 때 이 시간 동안 감지 반경이 늘어난다")]
    public float alertDuration = 5f;
    public float alertRangeMultiplier = 2f;

    // ─────────────────────────────────────────────
    private ZombieState state = ZombieState.Idle;
    public override ZombieState State => state;

    private Vector3 wanderDestination;
    private bool hasWanderDestination;
    private float wanderTimer;

    private float alertTimer;

    public override ZombieAction Think(in ZombieObservation obs, float dt)
    {
        if (alertTimer > 0f) alertTimer -= dt;

        state = Decide(obs);

        switch (state)
        {
            case ZombieState.Stunned:
            case ZombieState.Idle:
                return ZombieAction.Stand();

            case ZombieState.Attack:
                return ZombieAction.Strike();

            case ZombieState.Chase:
                hasWanderDestination = false;
                return ZombieAction.MoveTo(obs.targetPosition, 1f);

            case ZombieState.Investigate:
                hasWanderDestination = false;
                return ZombieAction.MoveTo(obs.lastKnownPosition, investigateSpeedFactor);

            case ZombieState.Wander:
            default:
                return Wander(dt);
        }
    }

    // ── 상태 전이 ──────────────────────────────────
    private ZombieState Decide(in ZombieObservation obs)
    {
        if (obs.isStunned) return ZombieState.Stunned;
        if (!obs.hasTarget) return enableWander ? ZombieState.Wander : ZombieState.Idle;

        // 공격은 NavMesh 여부와 무관하게 성립 (기존 동작 유지)
        if (obs.distance <= obs.attackRange) return ZombieState.Attack;

        if (!obs.canMove) return ZombieState.Idle;

        float effectiveRange = obs.detectionRange * (alertTimer > 0f ? alertRangeMultiplier : 1f);

        // 지금 보이거나, 방금 전까지 보였으면 추적 유지
        bool tracking = obs.targetVisible || obs.timeSinceSeen <= sightMemory;
        if (tracking && obs.distance <= effectiveRange) return ZombieState.Chase;

        if (obs.hasLastKnown && obs.timeSinceSeen <= investigateMemory)
        {
            float d = Vector3.Distance(transform.position, obs.lastKnownPosition);
            if (d > investigateArrive) return ZombieState.Investigate;
        }

        return enableWander ? ZombieState.Wander : ZombieState.Idle;
    }

    // ── 배회 ───────────────────────────────────────
    private ZombieAction Wander(float dt)
    {
        wanderTimer -= dt;

        bool arrived = hasWanderDestination &&
                       Vector3.Distance(transform.position, wanderDestination) < 0.7f;

        if (!hasWanderDestination || wanderTimer <= 0f || arrived)
        {
            if (PickWanderDestination(out Vector3 dest))
            {
                wanderDestination = dest;
                hasWanderDestination = true;
            }
            wanderTimer = Random.Range(wanderIntervalMin, wanderIntervalMax);
        }

        if (!hasWanderDestination) return ZombieAction.Stand();
        return ZombieAction.MoveTo(wanderDestination, wanderSpeedFactor);
    }

    private bool PickWanderDestination(out Vector3 result)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = transform.position + new Vector3(rnd.x, 0f, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }
        result = transform.position;
        return false;
    }

    // ── 외부 이벤트 ────────────────────────────────
    public override void OnDamaged()
    {
        alertTimer = alertDuration;
    }

    public override void OnHeardNoise(Vector3 position)
    {
        // 소리를 들으면 경계 상태가 되고, 수색 목적지는 Perception이 이미 잡아뒀다.
        alertTimer = alertDuration;
        hasWanderDestination = false;   // 배회 목적지 폐기 → 바로 소리 쪽으로
    }

    public override void OnResetEpisode()
    {
        state = ZombieState.Idle;
        alertTimer = 0f;
        wanderTimer = 0f;
        hasWanderDestination = false;
    }
}
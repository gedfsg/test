using UnityEngine;

/// <summary>
/// 강화학습 판단 계층. ZombieAgent가 넘겨준 행동을 ZombieAction으로 변환한다.
///
/// 왜 ZombieAgent와 둘로 나눴나:
///   ML-Agents의 Agent도 MonoBehaviour고 ZombieBrain도 MonoBehaviour인데
///   C#은 다중 상속이 안 되므로 ZombieMLBrain : ZombieBrain, Agent 가 불가능하다.
///   → 관측/행동/보상은 ZombieAgent가, 행동 해석은 이 클래스가 맡는다.
///
/// 붙이는 법: 좀비 프리팹에 ZombieAgent와 함께 붙이고,
///            ZombieController의 Brain 슬롯에 이 컴포넌트를 꽂는다.
/// </summary>
public class ZombieMLBrain : ZombieBrain
{
    [Header("행동 해석")]
    [Tooltip("정책이 내놓은 방향으로 몇 m 앞을 목적지로 잡을지")]
    public float stepDistance = 2.5f;

    [Tooltip("공격 행동이 켜져도 사거리 밖이면 무시할지 (true 권장)")]
    public bool requireRangeForAttack = true;

    // ── Agent ↔ Brain 사이의 캐시 ────────────────────
    // Agent는 자기 주기(DecisionRequester)로 돌고 Brain은 매 프레임 호출되므로,
    // 둘을 직접 호출로 엮지 않고 최신 값만 주고받는다.
    private ZombieObservation lastObs;
    private bool    hasObs;
    private Vector2 moveAction;
    private bool    attackAction;

    public bool HasObservation => hasObs;
    public ZombieObservation LastObservation => lastObs;

    /// <summary>
    /// RL 정책에는 명시적 상태가 없다. 이 값은 디버그 HUD와
    /// FSM 비교 로깅을 위해 관측으로부터 사후적으로 붙인 라벨일 뿐,
    /// 에이전트의 의사결정에는 전혀 쓰이지 않는다.
    /// </summary>
    public override ZombieState State
    {
        get
        {
            if (!hasObs) return ZombieState.Idle;
            if (lastObs.isStunned) return ZombieState.Stunned;
            if (!lastObs.hasTarget) return ZombieState.Wander;
            if (lastObs.InAttackRange) return ZombieState.Attack;
            if (lastObs.targetVisible) return ZombieState.Chase;
            if (lastObs.hasLastKnown && lastObs.timeSinceSeen < 6f) return ZombieState.Investigate;
            return ZombieState.Wander;
        }
    }

    public override ZombieAction Think(in ZombieObservation obs, float dt)
    {
        lastObs = obs;
        hasObs  = true;

        if (obs.isStunned) return ZombieAction.Stand();

        if (attackAction && (!requireRangeForAttack || obs.InAttackRange))
            return ZombieAction.Strike();

        // 행동은 좀비 로컬 기준 (관측의 toTargetLocal과 같은 좌표계)
        Vector3 localDir = new Vector3(moveAction.x, 0f, moveAction.y);
        float magnitude  = Mathf.Clamp01(localDir.magnitude);
        if (magnitude < 0.05f) return ZombieAction.Stand();

        Vector3 worldDir = transform.TransformDirection(localDir.normalized);
        return ZombieAction.FromDirection(transform.position, worldDir, magnitude, stepDistance);
    }

    /// <summary>ZombieAgent.OnActionReceived에서 호출.</summary>
    public void SetActions(Vector2 move, bool attack)
    {
        moveAction   = move;
        attackAction = attack;
    }

    public override void OnResetEpisode()
    {
        hasObs       = false;
        moveAction   = Vector2.zero;
        attackAction = false;
    }
}

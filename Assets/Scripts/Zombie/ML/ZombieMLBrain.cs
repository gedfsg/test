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

    [Tooltip("타겟이 보이고 사거리 안이면 정책과 상관없이 공격한다.\n" +
             "학습장의 더미는 체력이 없어서 공격에는 보상이 없었다 → 정책의 공격 출력은 사실상 무작위.\n" +
             "게임에서는 이걸 켜서 FSM과 같은 규칙으로 때리게 한다. 이동 판단은 그대로 정책이 한다.")]
    public bool autoAttackInRange = true;

    /// <summary>조율기+RL 모델을 학습할 때 쓴 잔차 크기. 프리팹 도구가 _RL_Slot 프리팹에 이 값을 넣는다 (yaml slot_residual과 같아야 함).</summary>
    public const float DefaultSlotResidual = 0.5f;
    /// <summary>사격 대응 모델(shoot3부터, 회전식 옆 보정)을 학습할 때 쓴 잔차 크기 (yaml slot_residual과 같아야 함).</summary>
    public const float DefaultShootResidual = 0.8f;

    [Header("조율기 + 강화학습 (잔차)")]
    [Tooltip("0보다 크면 '잔차' 방식: 무리 조율기가 정한 방향으로 기본 이동하고, 정책의 행동은 그 위에 더하는 보정이 된다.\n" +
             "  최종 방향 = 조율기 방향 + 정책 행동 × 이 값   (속도 = 그 길이, 최대 1)\n" +
             "정책이 0을 내면 조율기 그대로, 반대로 밀면 느려지거나 멈춘다. 조율기에 속하지 않았거나 명령이 없으면 정책 그대로.\n" +
             "⚠ 학습 때 값(yaml slot_residual)과 같아야 학습한 대로 움직인다. 0 = 꺼짐 (기존 모델).")]
    public float residualScale = 0f;
    [Tooltip("켜면 보정을 '옆으로'만 허용한다: 조율기 방향으로는 항상 최고 속도, 정책은 좌우로 얼마나 비킬지만 정한다.\n" +
             "총 쏘는 더미 상대로 자유 보정을 줬더니 피하는 대신 속도를 줄여 머뭇거렸다 (3.1 → 2.6 m/s, 더 오래 맞음).\n" +
             "⚠ 학습 때 값(yaml slot_residual_lateral)과 같아야 한다.")]
    public bool residualLateralOnly = false;

    private ZombieController body;

    // ── Agent ↔ Brain 사이의 캐시 ────────────────────
    // Agent는 자기 주기(DecisionRequester)로 돌고 Brain은 매 프레임 호출되므로,
    // 둘을 직접 호출로 엮지 않고 최신 값만 주고받는다.
    private ZombieObservation lastObs;
    private bool    hasObs;
    private Vector2 moveAction;
    private bool    attackAction;

    public bool HasObservation => hasObs;

    // ── 회피 측정용 (평가 도구/학습 통계가 읽는다. 행동에는 영향 없음) ──
    /// <summary>이번 프레임에 '옆으로만 보정' 방식으로 진행 방향을 몇 도 틀었는지 (+/- 방향). HasSteer가 false면 의미 없음.</summary>
    public float LastSteerDeg { get; private set; }
    /// <summary>이번 프레임에 옆으로만 보정이 실제로 적용됐는가 (조율기 명령이 있고 아직 도착 전).</summary>
    public bool  HasSteer     { get; private set; }
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
            if (lastObs.targetVisible && lastObs.InAttackRange) return ZombieState.Attack;
            if (lastObs.targetVisible) return ZombieState.Chase;
            if (lastObs.hasBelief && lastObs.timeSinceSeen < 6f) return ZombieState.Investigate;
            return ZombieState.Wander;
        }
    }

    public override ZombieAction Think(in ZombieObservation obs, float dt)
    {
        lastObs = obs;
        hasObs  = true;
        HasSteer = false;

        if (obs.isStunned) return ZombieAction.Stand();

        bool attack = (attackAction && (!requireRangeForAttack || obs.InAttackRange))
                   || (autoAttackInRange && obs.targetVisible && obs.InAttackRange);

        // ⚠ 공격해도 이동은 멈추지 않는다.
        // 예전에는 공격 = Strike() = 제자리 정지였다. 공격 사거리(1.8m)가 포획 거리(1.6m)보다
        // 넓어서, 1.8m에 닿으면 멈춰 휘두름 → 더미가 멀어짐 → 다시 쫓음 → 또 멈춤이 반복됐다
        // (바로 뒤에서 움찔움찔하며 못 잡는 '제논의 역설' 현상). 휴리스틱은 사거리 안이면
        // 항상 공격하므로 매번 걸렸고, RL도 탐색(entropy) 때문에 수시로 걸렸다.
        // 멈추고 싶으면 정책이 이동 크기를 0으로 내면 된다 —— 정지는 이동 행동의 몫이다.

        // 행동은 좀비 로컬 기준 (관측의 toTargetLocal과 같은 좌표계)
        Vector3 localDir = new Vector3(moveAction.x, 0f, moveAction.y);
        float magnitude  = Mathf.Clamp01(localDir.magnitude);

        // 잔차 방식: 조율기 방향 + 정책 보정
        if (residualScale > 0f && TryResidual(localDir, attack, out ZombieAction residual))
            return residual;

        ZombieAction action;
        if (magnitude < 0.05f)
        {
            action = attack ? ZombieAction.Strike() : ZombieAction.Stand();
        }
        else
        {
            Vector3 worldDir = transform.TransformDirection(localDir.normalized);
            action = ZombieAction.FromDirection(transform.position, worldDir, magnitude, stepDistance);
            action.attack = attack;
        }
        return action;
    }

    /// <summary>
    /// 조율기가 이 좀비에게 명령 지점을 줬으면: 그쪽 단위 방향 + 정책 행동 × residualScale 로 움직인다.
    /// 명령이 없으면(무리가 플레이어를 모름 / 조율기 없음) false → 원래대로 정책 그대로.
    /// </summary>
    private bool TryResidual(Vector3 localAction, bool attack, out ZombieAction action)
    {
        action = default;
        if (body == null) body = GetComponent<ZombieController>();
        var pack = HunterPackTactics.PackOf(body);
        if (pack == null || !pack.TryGetOrder(body, out Vector3 order, out _)) return false;

        Vector3 baseDir = order - transform.position;
        baseDir.y = 0f;
        if (baseDir.sqrMagnitude < 0.04f) baseDir = Vector3.zero;   // 이미 도착 → 보정만
        else baseDir.Normalize();

        Vector3 worldAction = transform.TransformDirection(localAction);
        worldAction.y = 0f;

        Vector3 world;
        float speed;

        if (residualLateralOnly && baseDir.sqrMagnitude > 0f)
        {
            // [수정] 기존에는 baseDir(1.0) + side * (최대 0.5) 방식이라 최대 회피 각도가 26.5도에 불과해
            // 예측 사격(aimLead)과 장애물을 피하지 못했음.
            // 좌우 입력(lat)으로 진행 방향 자체를 최대 ±(residualScale * 75도)까지 회전시키고,
            // 전후방 입력(fwd)으로도 약간의 가감속/대기(0.6~1.0)를 허용해 총알 회피와 엄폐 기동이 가능하도록 개선.
            Vector3 side = new Vector3(-baseDir.z, 0f, baseDir.x);
            float lat = Mathf.Clamp(Vector3.Dot(worldAction, side), -1f, 1f);
            float fwd = Mathf.Clamp(Vector3.Dot(worldAction, baseDir), -1f, 1f);

            float maxSteerDeg = Mathf.Clamp(residualScale * 75f, 15f, 75f);
            float steerAngle  = lat * maxSteerDeg;
            LastSteerDeg = steerAngle;
            HasSteer     = true;
            world = Quaternion.Euler(0f, steerAngle, 0f) * baseDir;
            speed = Mathf.Clamp(1f + fwd * 0.25f, 0.7f, 1f);
        }
        else if (baseDir.sqrMagnitude > 0f)
        {
            Vector3 correction = worldAction * residualScale;
            world = baseDir + correction;
            world.y = 0f;
            // 보정 때문에 속도가 너무 죽어 표적이 되는 현상 방지 (최소 속도 0.6 보장)
            speed = Mathf.Clamp(world.magnitude, 0.6f, 1f);
        }
        else
        {
            // 슬롯 위치에 이미 도달한 상태에서도 제자리에 멈춰 총에 맞지 않고 자유롭게 회피 기동 허용
            world = worldAction;
            speed = Mathf.Clamp01(world.magnitude);
        }

        if (speed < 0.05f || world.sqrMagnitude < 0.0025f)
        {
            action = attack ? ZombieAction.Strike() : ZombieAction.Stand();
            return true;
        }
        action = ZombieAction.FromDirection(transform.position, world.normalized, speed, stepDistance);
        action.attack = attack;
        return true;
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
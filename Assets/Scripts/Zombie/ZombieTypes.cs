using UnityEngine;

/// <summary>
/// 좀비 FSM 상태. Brain이 이 값을 결정하고, Controller/디버그 툴이 읽어간다.
/// </summary>
public enum ZombieState
{
    Idle,        // 대기 (타겟 없음)
    Wander,      // 배회
    Investigate, // 마지막 목격 지점으로 이동 (타겟 놓침 / 피격 반응)
    Chase,       // 추적
    Attack,      // 공격 사거리 내
    Stunned,     // 기절
    Dead
}

/// <summary>
/// 한 프레임의 "인식 결과". Brain은 오직 이 구조체만 보고 판단한다.
/// = ML-Agents의 observation vector와 1:1 대응 (ToVector 참고).
/// </summary>
public struct ZombieObservation
{
    public bool    hasTarget;          // 타겟 존재
    public bool    targetVisible;      // 실제로 보이는가 (현재는 거리 판정만, 시야/차폐 붙일 자리)
    public float   distance;           // 타겟까지 거리
    public Vector3 toTargetLocal;      // 자기 기준 로컬 방향 (정규화)
    public Vector3 targetPosition;     // 타겟 월드 좌표
    public Vector3 lastKnownPosition;  // 마지막 목격 지점
    public bool    hasLastKnown;
    public float   timeSinceSeen;      // 마지막 목격 후 경과 시간

    public float   healthNormalized;   // 0~1
    public bool    canMove;            // NavMesh 위에 있는가
    public bool    isStunned;
    public bool    attackReady;        // 공격 쿨다운 끝났는가

    public float   detectionRange;
    public float   attackRange;

    public bool InAttackRange => hasTarget && distance <= attackRange;

    // ── ML-Agents 브릿지 ────────────────────────────────
    // ZombieMLBrain에서 CollectObservations(VectorSensor sensor) 구현 시:
    //   var buf = new float[ZombieObservation.VectorLength];
    //   obs.ToVector(buf);
    //   foreach (var v in buf) sensor.AddObservation(v);
    // 이렇게만 하면 FSM과 RL이 "똑같은 것을 본다"가 보장된다.
    // 관측 항목을 늘릴 땐 VectorLength도 같이 올릴 것 (안 그러면 학습된 모델과 shape 불일치).
    public const int VectorLength = 10;

    public void ToVector(float[] buffer)
    {
        if (buffer == null || buffer.Length < VectorLength) return;

        float range = Mathf.Max(detectionRange, 0.01f);

        buffer[0] = hasTarget      ? 1f : 0f;
        buffer[1] = targetVisible  ? 1f : 0f;
        buffer[2] = Mathf.Clamp01(distance / range);
        buffer[3] = toTargetLocal.x;
        buffer[4] = toTargetLocal.z;
        buffer[5] = Mathf.Clamp01(timeSinceSeen / 5f);
        buffer[6] = healthNormalized;
        buffer[7] = canMove        ? 1f : 0f;
        buffer[8] = attackReady    ? 1f : 0f;
        buffer[9] = InAttackRange  ? 1f : 0f;
    }
}

/// <summary>
/// Brain이 Controller에게 내리는 명령. "어떻게"가 아니라 "무엇을"만 담는다.
/// NavMeshAgent / Animator 같은 실행 수단은 전부 Controller 쪽 책임.
/// </summary>
public struct ZombieAction
{
    public bool    hasMoveTarget;
    public Vector3 moveTarget;
    public float   speedScale;   // moveSpeed 대비 비율 (0~1)
    public bool    faceTarget;   // 이동 없이 타겟 쪽을 바라볼 때
    public bool    attack;

    public static ZombieAction Stand()
    {
        return new ZombieAction { hasMoveTarget = false, speedScale = 0f };
    }

    public static ZombieAction MoveTo(Vector3 position, float speedScale = 1f)
    {
        return new ZombieAction
        {
            hasMoveTarget = true,
            moveTarget    = position,
            speedScale    = Mathf.Clamp01(speedScale)
        };
    }

    public static ZombieAction Strike()
    {
        return new ZombieAction { hasMoveTarget = false, speedScale = 0f, faceTarget = true, attack = true };
    }

    /// <summary>
    /// 연속 행동(방향 벡터)을 목적지로 변환. ML-Agents brain이 쓸 진입점.
    /// dir은 월드 기준 방향, stepDistance만큼 앞을 목적지로 잡아 NavMesh 경로를 태운다.
    /// </summary>
    public static ZombieAction FromDirection(Vector3 origin, Vector3 dir, float speedScale = 1f, float stepDistance = 2f)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Stand();
        return MoveTo(origin + dir.normalized * stepDistance, speedScale);
    }
}

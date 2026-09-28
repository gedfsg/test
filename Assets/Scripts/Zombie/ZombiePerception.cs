using UnityEngine;

/// <summary>
/// 시야 설정. ZombieController 인스펙터에서 좀비 종류별로 조절한다.
/// </summary>
[System.Serializable]
public class ZombieVision
{
    [Header("시야각")]
    [Range(30f, 360f)]
    [Tooltip("정면 기준 전체 시야각. 좀비는 목이 뻣뻣하다는 설정이면 좁게.")]
    public float viewAngle = 110f;

    [Header("근접 감각")]
    [Tooltip("이 거리 안이면 등 뒤라도 감지 (냄새/기척). 0이면 끔.")]
    public float nearSenseRadius = 4f;

    [Header("차폐 판정")]
    [Tooltip("시야를 막는 레이어. 건물이 Default에 있으므로 보통 Default + Obstacle.")]
    public LayerMask occlusionMask = ~0;
    [Tooltip("좀비 눈높이")]
    public float eyeHeight = 1.6f;
    [Tooltip("플레이어의 어느 높이를 노려보는가 (발밑을 보면 턱마다 막힘)")]
    public float targetHeight = 1.0f;

    [Header("청각")]
    [Tooltip("소음 반경 배율. 1=기본, 0=귀머거리. 탱커는 낮게, 달리기형은 높게.")]
    public float hearingSensitivity = 1f;

    [Header("성능")]
    [Tooltip("시야 판정 간격(초). 레이캐스트 비용이라 매 프레임 할 필요 없음.")]
    public float checkInterval = 0.2f;

    [Header("디버그")]
    [Tooltip("Scene 뷰에 시선 라인 표시 (초록=보임, 빨강=막힘)")]
    public bool debugDrawSight = false;
}

/// <summary>
/// 인식(Sense) 계층. 월드 상태를 ZombieObservation 하나로 압축한다.
/// Brain은 여기 통과한 정보만 보므로, FSM이든 RL이든 입력이 동일하다.
/// </summary>
public class ZombiePerception
{
    protected readonly Transform self;
    protected readonly ZombieController body;

    private Transform target;
    private float retargetTimer;

    private Vector3 lastKnownPosition;
    private bool hasLastKnown;
    private float timeSinceSeen = 999f;

    // 시야 판정 캐시 (매 프레임 레이캐스트 안 하려고)
    private bool visibleCached;
    private float visionCheckTimer;

    // 레이캐스트 결과 버퍼 (GC 방지)
    private readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    public Transform Target => target;

    public ZombiePerception(ZombieController body)
    {
        this.body = body;
        this.self = body.transform;

        // 좀비 30마리가 같은 프레임에 몰려서 레이캐스트하지 않도록 위상을 흩뿌린다
        visionCheckTimer = Random.Range(0f, Mathf.Max(body.vision.checkInterval, 0.01f));

        FindPlayer();
    }

    public void Tick(float dt)
    {
        // 타겟 재탐색 (씬 로드 직후 / 플레이어 리스폰 대비)
        retargetTimer -= dt;
        if (target == null && retargetTimer <= 0f)
        {
            FindPlayer();
            retargetTimer = body.retargetInterval;
        }

        timeSinceSeen += dt;

        // 시야 판정은 간격을 두고
        visionCheckTimer -= dt;
        if (visionCheckTimer <= 0f)
        {
            visionCheckTimer = Mathf.Max(body.vision.checkInterval, 0.01f);
            visibleCached = target != null && CanSee(target);
        }

        if (visibleCached && target != null)
        {
            timeSinceSeen = 0f;
            lastKnownPosition = target.position;
            hasLastKnown = true;
        }
    }

    public ZombieObservation Observe()
    {
        var obs = new ZombieObservation
        {
            detectionRange = body.detectionRange,
            attackRange = body.attackRange,
            healthNormalized = body.HealthNormalized,
            canMove = body.CanMove,
            isStunned = body.IsStunned,
            attackReady = body.AttackReady,
            lastKnownPosition = lastKnownPosition,
            hasLastKnown = hasLastKnown,
            timeSinceSeen = timeSinceSeen,
        };

        if (target == null)
        {
            obs.hasTarget = false;
            obs.distance = float.MaxValue;
            return obs;
        }

        Vector3 delta = target.position - self.position;
        delta.y = 0f;

        obs.hasTarget = true;
        obs.targetPosition = target.position;
        obs.distance = delta.magnitude;
        obs.targetVisible = visibleCached;
        obs.toTargetLocal = delta.sqrMagnitude > 0.0001f
            ? self.InverseTransformDirection(delta.normalized)
            : Vector3.zero;

        return obs;
    }

    // ── 시야 판정 ─────────────────────────────────────
    /// <summary>
    /// 거리 → 근접 감각 → 시야각 → 차폐 순서.
    /// 싼 검사부터 하는 게 중요 (레이캐스트는 맨 마지막).
    /// </summary>
    protected virtual bool CanSee(Transform t)
    {
        var v = body.vision;

        Vector3 d = t.position - self.position;
        d.y = 0f;
        float sqDist = d.sqrMagnitude;

        // 1. 감지 반경 밖
        if (sqDist > body.detectionRange * body.detectionRange) return false;

        // 2. 바로 옆이면 각도 무시 (등 뒤에 바짝 붙어도 알아챈다)
        if (v.nearSenseRadius > 0f && sqDist <= v.nearSenseRadius * v.nearSenseRadius)
            return true;

        // 3. 시야각
        if (Vector3.Angle(self.forward, d) > v.viewAngle * 0.5f) return false;

        // 4. 차폐
        return !IsBlocked(t);
    }

    /// <summary>
    /// 눈 → 타겟 가슴으로 레이캐스트.
    /// 좀비끼리, 그리고 타겟 자신은 시야를 막지 않는 것으로 친다
    /// (건물이 Default 레이어라 마스크만으로는 구분이 안 되기 때문).
    /// </summary>
    protected bool IsBlocked(Transform t)
    {
        var v = body.vision;

        Vector3 eye = self.position + Vector3.up * v.eyeHeight;
        Vector3 aim = t.position + Vector3.up * v.targetHeight;
        Vector3 dir = aim - eye;
        float len = dir.magnitude;
        if (len < 0.01f) return false;
        dir /= len;

        int count = Physics.RaycastNonAlloc(
            eye, dir, hitBuffer, len, v.occlusionMask, QueryTriggerInteraction.Ignore);

        bool blocked = false;
        for (int i = 0; i < count; i++)
        {
            Transform h = hitBuffer[i].transform;

            if (h == self || h.IsChildOf(self)) continue;                      // 자기 자신
            if (h == t || h.IsChildOf(t)) continue;                      // 타겟 본인
            if (h.GetComponentInParent<ZombieController>() != null) continue;  // 다른 좀비

            blocked = true;
            break;
        }

        if (v.debugDrawSight)
            Debug.DrawLine(eye, aim, blocked ? Color.red : Color.green, v.checkInterval);

        return blocked;
    }

    // ── 외부 이벤트 ────────────────────────────────────
    /// <summary>피격/소음 등으로 위치 힌트를 강제 주입.</summary>
    public void NotifyDisturbance(Vector3 position)
    {
        lastKnownPosition = position;
        hasLastKnown = true;
        timeSinceSeen = 0.5f;   // 목격은 아니지만 "방금 뭔가 있었다" 수준
    }

    /// <summary>
    /// 총성처럼 발생원을 특정 못 하는 자극. 타겟의 현재 위치를 대략적 힌트로 쓴다.
    /// (벽 뒤에서 쏴도 좀비가 그 방향으로 몰려오게 하는 용도)
    /// </summary>
    public void NotifyDisturbanceFromTarget()
    {
        if (target != null) NotifyDisturbance(target.position);
    }

    public void ForgetTarget()
    {
        target = null;
        retargetTimer = 0f;
        visibleCached = false;
    }

    private void FindPlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) target = p.transform;
    }
}
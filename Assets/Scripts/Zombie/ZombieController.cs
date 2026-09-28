using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비 "몸통" (Act 계층).
/// 역할: 컴포넌트 자가복구, NavMesh 복귀, 애니메이션 구동, 데미지/사망 처리, 그리고
///       Sense → Think → Act 루프를 돌리는 것.
///
/// 판단은 전부 ZombieBrain이 한다. 이 클래스에는 if(거리 &lt; X) 같은 전술 판단이 없어야 한다.
///   Sense : ZombiePerception  → ZombieObservation
///   Think : ZombieBrain       → ZombieAction
///   Act   : 이 클래스          → NavMeshAgent / Animator
///
/// 외부 API(TakeDamage/Stun)는 기존과 동일하므로 Bullet.cs / MeleeWeapon.cs는 수정 불필요.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ZombieController : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth      = 50f;
    public float moveSpeed      = 2f;
    public float attackRange    = 1.8f;
    public float attackDamage   = 10f;
    public float attackCooldown = 1.5f;

    [Header("AI")]
    public float detectionRange   = 30f;
    public float retargetInterval = 1f;   // 플레이어 다시 찾기 간격

    [Header("시야")]
    public ZombieVision vision = new ZombieVision();

    [Header("Brain")]
    [Tooltip("비워두면 ZombieFSMBrain을 자동 장착. ML-Agents 도입 시 여기에 ZombieMLBrain을 넣는다.")]
    public ZombieBrain brain;

    [Header("성능")]
    [Tooltip("NavMesh 경로 재계산 간격(초). 0이면 매 프레임. 좀비가 많을수록 올릴 것.")]
    public float repathInterval = 0.15f;

    [Header("디버그")]
    public bool drawGizmos = true;

    // ─────────────────────────────────────────────
    private float attackTimer;
    private bool  isDead;
    private bool  isStunned;
    private float stunTimer;

    private float repathTimer;
    private Vector3 lastDestination;
    private bool    hasDestination;

    private NavMeshAgent agent;
    private Animator     anim;
    private Health       health;

    private ZombiePerception perception;
    private ZombieFSMBrain   fallbackBrain;
    private bool             brainFailed;

    private static readonly int HashIsWalking = Animator.StringToHash("IsWalking");
    private static readonly int HashSpeed     = Animator.StringToHash("Speed");
    private static readonly int HashAttack    = Animator.StringToHash("Attack");
    private static readonly int HashDie       = Animator.StringToHash("Die");

    // ── Brain이 읽어가는 몸 상태 ──────────────────────
    public bool  CanMove           => agent != null && agent.enabled && agent.isOnNavMesh;
    public bool  IsStunned         => isStunned;
    public bool  AttackReady       => attackTimer <= 0f;
    public bool  IsDead            => isDead;
    public float HealthNormalized  => health != null && health.maxHealth > 0f
                                      ? Mathf.Clamp01(health.GetCurrentHealth() / health.maxHealth)
                                      : 1f;

    /// <summary>현재 FSM 상태 (디버그 HUD / 실험 로깅용).</summary>
    public ZombieState State => isDead ? ZombieState.Dead
                              : (ActiveBrain != null ? ActiveBrain.State : ZombieState.Idle);

    /// <summary>주 brain이 꺼져 있거나 예외를 던졌으면 FSM으로 폴백.</summary>
    private ZombieBrain ActiveBrain
    {
        get
        {
            if (!brainFailed && brain != null && brain.isActiveAndEnabled) return brain;
            return fallbackBrain;
        }
    }

    // ─────────────────────────────────────────────
    void Awake()
    {
        SetupAgent();
        SetupCollider();
        SetupRigidbody();
        SetupAnimator();
        SetupHealth();
        SetupBrain();

        perception = new ZombiePerception(this);

        // 스폰 직후 약간 떠 있을 수 있음
        TryWarpToNavMesh();
    }

    void Start()
    {
        EnsureAnimatorClips();
    }

    void OnEnable()  { NoiseSystem.Register(this); }
    void OnDisable() { NoiseSystem.Unregister(this); }

    // ── 자가복구 셋업 ─────────────────────────────────
    void SetupAgent()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        agent.speed            = moveSpeed;
        agent.angularSpeed     = 720f;
        agent.acceleration     = 12f;
        agent.stoppingDistance = attackRange * 0.7f;
        agent.radius           = 0.4f;
        agent.height           = 1.8f;
        agent.autoBraking      = true;
    }

    void SetupCollider()
    {
        // 총알 맞으려면 필수
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
        col.isTrigger = false;
        col.height    = 1.8f;
        col.radius    = 0.5f;   // 약간 크게 → 총알 맞기 쉽게
        col.center    = new Vector3(0, 0.9f, 0);
    }

    void SetupRigidbody()
    {
        // Trigger 이벤트 발동에 필수
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic            = true;   // NavMeshAgent가 이동 담당이므로 물리 안 씀
        rb.useGravity             = false;
        rb.interpolation          = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    void SetupAnimator()
    {
        anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim == null) return;

        anim.applyRootMotion = false;

        if (anim.avatar == null)
            Debug.LogError($"[Zombie] {name}: ❌ Animator의 Avatar가 null! FBX를 Humanoid로 설정해야 함");
        else if (!anim.avatar.isHuman)
            Debug.LogError($"[Zombie] {name}: ❌ Avatar가 Humanoid가 아님 (Generic)");
        else if (!anim.avatar.isValid)
            Debug.LogError($"[Zombie] {name}: ❌ Avatar 유효하지 않음 - 본 매핑 확인 필요");
    }

    void SetupHealth()
    {
        health = GetComponent<Health>();
        if (health == null) health = gameObject.AddComponent<Health>();
        health.maxHealth = maxHealth;
        // Health.Awake가 currentHealth=100으로 미리 설정함 → Heal(0)로 maxHealth로 클램프
        health.Heal(0f);

        // UnityEvent가 런타임 추가 시 null일 수 있음
        if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
        if (health.onHurt  == null) health.onHurt  = new UnityEngine.Events.UnityEvent();

        health.onDeath.RemoveListener(Die);
        health.onDeath.AddListener(Die);

        // ⭐ Bullet.cs는 Health.TakeDamage를 직접 호출하므로, 여기를 안 걸면
        //    좀비는 총에 맞은 사실 자체를 모른다. Brain에 피격을 전달하는 유일한 경로.
        health.onHurt.RemoveListener(NotifyBrainDamaged);
        health.onHurt.AddListener(NotifyBrainDamaged);
    }

    void SetupBrain()
    {
        fallbackBrain = GetComponent<ZombieFSMBrain>();
        if (fallbackBrain == null) fallbackBrain = gameObject.AddComponent<ZombieFSMBrain>();

        if (brain == null)
        {
            // 인스펙터에서 안 꽂았으면, 자신 외의 brain이 붙어있는지 확인
            foreach (var b in GetComponents<ZombieBrain>())
            {
                if (b != fallbackBrain) { brain = b; break; }
            }
        }
    }

    // ── 메인 루프: Sense → Think → Act ─────────────────
    void Update()
    {
        if (isDead) return;

        float dt = Time.deltaTime;

        // 기절 타이머는 몸의 책임 (Brain은 결과만 본다)
        if (isStunned)
        {
            stunTimer -= dt;
            if (stunTimer <= 0f) isStunned = false;
        }

        if (attackTimer > 0f) attackTimer -= dt;

        // NavMesh 위에 없으면 복구 시도
        if (!CanMove) TryWarpToNavMesh();

        // 1. Sense
        perception.Tick(dt);
        ZombieObservation obs = perception.Observe();

        // 2. Think
        ZombieAction action = ThinkSafely(in obs, dt);

        // 3. Act
        Execute(in action, in obs);
    }

    private ZombieAction ThinkSafely(in ZombieObservation obs, float dt)
    {
        ZombieBrain b = ActiveBrain;
        if (b == null) return ZombieAction.Stand();

        try
        {
            return b.Think(in obs, dt);
        }
        catch (System.Exception e)
        {
            // RL brain이 터져도 게임이 멈추면 안 됨 → 영구 폴백
            if (!brainFailed)
            {
                brainFailed = true;
                Debug.LogError($"[Zombie] {name}: brain({b.GetType().Name}) 예외 → FSM으로 폴백\n{e}");
            }
            return fallbackBrain != null ? fallbackBrain.Think(in obs, dt) : ZombieAction.Stand();
        }
    }

    private void Execute(in ZombieAction action, in ZombieObservation obs)
    {
        if (action.faceTarget && obs.hasTarget) FaceTowards(obs.targetPosition);

        if (action.attack && AttackReady && obs.hasTarget)
        {
            DoAttack();
            attackTimer = attackCooldown;
        }

        float moveSpeedNow = 0f;

        if (CanMove)
        {
            if (action.hasMoveTarget)
            {
                agent.speed = moveSpeed * Mathf.Max(action.speedScale, 0.01f);
                SetDestinationThrottled(action.moveTarget);
                moveSpeedNow = agent.velocity.magnitude;
            }
            else if (hasDestination)
            {
                agent.ResetPath();
                hasDestination = false;
            }
        }

        SetAnimSpeed(moveSpeedNow);
    }

    /// <summary>
    /// SetDestination은 NavMesh 경로 재계산이라 가장 비싼 호출.
    /// 좀비 30마리가 매 프레임 부르면 체감이 크므로 간격을 두고, 목적지가 거의 안 움직였으면 생략.
    /// </summary>
    private void SetDestinationThrottled(Vector3 destination)
    {
        repathTimer -= Time.deltaTime;

        // 목적지가 크게 튀었으면(상태 전환 등) 타이머 무시하고 즉시 갱신
        bool bigJump = !hasDestination ||
                       (destination - lastDestination).sqrMagnitude > 9f;   // 3m

        if (repathTimer > 0f && !bigJump) return;

        agent.SetDestination(destination);
        lastDestination = destination;
        hasDestination  = true;
        repathTimer     = repathInterval;
    }

    void FaceTowards(Vector3 position)
    {
        Vector3 dir = position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(dir),
            Time.deltaTime * 10f);
    }

    void TryWarpToNavMesh()
    {
        if (agent == null || !agent.enabled) return;
        if (agent.isOnNavMesh) return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    // ── 애니메이션 ────────────────────────────────────
    void EnsureAnimatorClips()
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;

        var src = anim.runtimeAnimatorController.animationClips;
        AnimationClip walkClip = null, idleClip = null, attackClip = null;
        foreach (var c in src)
        {
            if (c == null) continue;
            string n = c.name.ToLower();
            if (walkClip   == null && (n.Contains("walk") || n.Contains("run"))) walkClip   = c;
            if (idleClip   == null && n.Contains("idle"))                        idleClip   = c;
            if (attackClip == null && n.Contains("attack"))                      attackClip = c;
        }

        if (walkClip == null && idleClip == null) return;

        var over = new AnimatorOverrideController(anim.runtimeAnimatorController);
        var overrides = new System.Collections.Generic.List<
            System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>(over.overridesCount);
        over.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            var key = overrides[i].Key;
            if (key == null) continue;
            string kn = key.name.ToLower();
            AnimationClip newClip = null;
            if      (kn.Contains("walk")   && walkClip   != null) newClip = walkClip;
            else if (kn.Contains("idle")   && idleClip   != null) newClip = idleClip;
            else if (kn.Contains("attack") && attackClip != null) newClip = attackClip;
            else if (kn.Contains("run")    && walkClip   != null) newClip = walkClip;

            if (newClip != null)
                overrides[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(key, newClip);
        }
        over.ApplyOverrides(overrides);
        anim.runtimeAnimatorController = over;
    }

    void SetAnimSpeed(float speed)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;

        bool walking = speed > 0.1f;
        anim.SetBool(HashIsWalking, walking);

        foreach (var p in anim.parameters)
        {
            if (p.nameHash == HashSpeed)
            {
                anim.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime);
                break;
            }
        }

        // 트랜지션이 안 먹는 컨트롤러 대비 강제 전환
        try
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Attack") || info.IsTag("Attack")) return;
            if (info.IsName("Die")    || info.IsTag("Die"))    return;

            if (walking)
            {
                if (!IsInState(info, "Walk", "Walking", "Run", "walk", "Move"))
                    PlayFirstAvailable("Walk", "Walking", "Run", "walk", "Move");
            }
            else
            {
                if (!IsInState(info, "Idle", "idle", "Stand"))
                    PlayFirstAvailable("Idle", "idle", "Stand");
            }
        }
        catch { }
    }

    bool IsInState(AnimatorStateInfo info, params string[] names)
    {
        foreach (var n in names)
            if (info.IsName(n)) return true;
        return false;
    }

    void PlayFirstAvailable(params string[] names)
    {
        foreach (var n in names)
        {
            if (anim.HasState(0, Animator.StringToHash(n)))
            {
                anim.CrossFade(n, 0.15f);
                return;
            }
        }
    }

    // ── 공격 ─────────────────────────────────────────
    void DoAttack()
    {
        if (anim != null && anim.runtimeAnimatorController != null)
            anim.SetTrigger(HashAttack);

        Transform t = perception != null ? perception.Target : null;
        if (t == null) return;

        Health playerHealth = t.GetComponent<Health>();
        if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
    }

    // ── 외부 API (Bullet.cs / MeleeWeapon.cs가 호출) ────
    public void TakeDamage(float damage)
    {
        if (isDead) return;

        if (health != null)
        {
            health.TakeDamage(damage);   // onHurt를 통해 NotifyBrainDamaged가 호출됨
        }
        else
        {
            maxHealth -= damage;
            NotifyBrainDamaged();
            if (maxHealth <= 0f) Die();
        }
    }

    /// <summary>피격 위치를 아는 경우 (근접 무기 등). 수색 지점 힌트로 쓴다.</summary>
    public void TakeDamage(float damage, Vector3 sourcePosition)
    {
        perception?.NotifyDisturbance(sourcePosition);
        TakeDamage(damage);
    }

    private void NotifyBrainDamaged()
    {
        if (isDead) return;

        // 위치 힌트는 주지 않는다 — 어디서 날아온 총알인지 알 방법이 없으므로.
        // 총소리 방향은 NoiseSystem이 따로 알려준다.
        ActiveBrain?.OnDamaged();
    }

    /// <summary>NoiseSystem이 호출. 소음 지점을 수색 목표로 삼는다.</summary>
    public void HearNoise(Vector3 position)
    {
        if (isDead) return;
        perception?.NotifyDisturbance(position);
        ActiveBrain?.OnHeardNoise(position);
    }

    public void Stun(float duration)
    {
        if (isDead) return;
        isStunned = true;
        stunTimer = duration;
        if (CanMove) { agent.ResetPath(); hasDestination = false; }
    }

    // ── 사망 ─────────────────────────────────────────
    void Die()
    {
        if (isDead) return;
        isDead = true;

        if (agent != null) agent.enabled = false;

        // 가짜 죽음 모션과 충돌 방지
        // (Mixamo 죽음 애니메이션을 넣게 되면 이 줄을 지우고 anim.SetTrigger(HashDie)로 교체)
        if (anim != null) anim.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        foreach (var b in GetComponents<ZombieBrain>()) b.enabled = false;

        StartCoroutine(FakeDeathRoutine());
    }

    System.Collections.IEnumerator FakeDeathRoutine()
    {
        // 1. 뒤로 쓰러짐 (0.6초)
        float duration = 0.6f;
        float elapsed  = 0f;
        Quaternion startRot = transform.rotation;
        Quaternion endRot   = startRot * Quaternion.Euler(-90f, 0f, 0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            transform.rotation = Quaternion.Slerp(startRot, endRot, t * t);
            transform.position += Vector3.down * (Time.deltaTime * 0.3f);
            yield return null;
        }

        // 2. 누워있기
        yield return new WaitForSeconds(1.5f);

        // 3. 땅 밑으로 가라앉으며 소멸
        float sinkDuration = 1f;
        float sinkElapsed  = 0f;
        Vector3 sinkStart = transform.position;
        Vector3 sinkEnd   = sinkStart + Vector3.down * 1.5f;
        while (sinkElapsed < sinkDuration)
        {
            sinkElapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(sinkStart, sinkEnd, sinkElapsed / sinkDuration);
            yield return null;
        }

        Destroy(gameObject);
    }

    // ── 디버그 ───────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // 시야 콘
        if (vision != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Vector3 eye = transform.position + Vector3.up * vision.eyeHeight;
            Quaternion l = Quaternion.Euler(0f, -vision.viewAngle * 0.5f, 0f);
            Quaternion r = Quaternion.Euler(0f,  vision.viewAngle * 0.5f, 0f);
            Gizmos.DrawRay(eye, l * transform.forward * detectionRange);
            Gizmos.DrawRay(eye, r * transform.forward * detectionRange);

            if (vision.nearSenseRadius > 0f)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
                Gizmos.DrawWireSphere(transform.position, vision.nearSenseRadius);
            }
        }

        if (!Application.isPlaying) return;

        switch (State)
        {
            case ZombieState.Chase:       Gizmos.color = Color.red;     break;
            case ZombieState.Attack:      Gizmos.color = Color.magenta; break;
            case ZombieState.Investigate: Gizmos.color = new Color(1f, 0.5f, 0f); break;
            case ZombieState.Wander:      Gizmos.color = Color.green;   break;
            default:                      Gizmos.color = Color.gray;    break;
        }
        Gizmos.DrawSphere(transform.position + Vector3.up * 2.2f, 0.25f);
    }
}

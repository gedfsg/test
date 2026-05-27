using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비 AI - 추적/공격/배회 + 자가복구(NavMesh 복귀, 컴포넌트 자동추가).
/// </summary>
public class ZombieController : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth      = 50f;
    public float moveSpeed      = 2f;
    public float attackRange    = 1.8f;
    public float attackDamage   = 10f;
    public float attackCooldown = 1.5f;

    [Header("AI")]
    public float detectionRange = 30f;
    public float retargetInterval = 1f;   // 플레이어 다시 찾기 간격

    [Header("랜덤 배회")]
    public bool  enableWander       = true;
    public float wanderRadius       = 8f;
    public float wanderIntervalMin  = 2f;
    public float wanderIntervalMax  = 5f;
    public float wanderSpeedFactor  = 0.5f;

    // ─────────────────────────────────────────────
    private float attackTimer;
    private bool  isDead;
    private bool  isStunned;
    private float stunTimer;
    private float wanderTimer;
    private bool  isWandering;
    private float retargetTimer;

    private Transform    target;
    private NavMeshAgent agent;
    private Animator     anim;
    private Health       health;

    private static readonly int HashIsWalking = Animator.StringToHash("IsWalking");
    private static readonly int HashSpeed     = Animator.StringToHash("Speed");
    private static readonly int HashAttack    = Animator.StringToHash("Attack");
    private static readonly int HashDie       = Animator.StringToHash("Die");

    // ─────────────────────────────────────────────
    void Awake()
    {
        // 1. NavMeshAgent 자동 추가/설정
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        agent.speed            = moveSpeed;
        agent.angularSpeed     = 720f;
        agent.acceleration     = 12f;
        agent.stoppingDistance = attackRange * 0.7f;
        agent.radius           = 0.4f;
        agent.height           = 1.8f;
        agent.autoBraking      = true;

        // 2. 콜라이더 자동 추가 (총알 맞으려면 필수)
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
        col.isTrigger = false;
        col.height = 1.8f;
        col.radius = 0.5f;     // 약간 크게 → 총알 맞기 쉽게
        col.center = new Vector3(0, 0.9f, 0);

        // 2-1. Rigidbody 자동 추가 (Trigger 이벤트 발동에 필수!)
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;    // NavMeshAgent가 이동 담당이므로 물리 안 씀
        rb.useGravity  = false;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        // 3. Animator 찾기
        anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;

            // Avatar 진단
            if (anim.avatar == null)
                Debug.LogError($"[Zombie] {name}: ❌ Animator의 Avatar가 null! FBX를 Humanoid로 설정해야 함");
            else if (!anim.avatar.isHuman)
                Debug.LogError($"[Zombie] {name}: ❌ Avatar가 Humanoid가 아님 (Generic)");
            else if (!anim.avatar.isValid)
                Debug.LogError($"[Zombie] {name}: ❌ Avatar 유효하지 않음 - 본 매핑 확인 필요");
            else
                Debug.Log($"[Zombie] {name}: ✅ Avatar OK (Humanoid)");
        }

        // 4. Health 추가 (총알 데미지 받기)
        health = GetComponent<Health>();
        if (health == null) health = gameObject.AddComponent<Health>();
        health.maxHealth = maxHealth;
        // Health.Awake가 currentHealth=100으로 미리 설정함 → Heal(0)로 maxHealth(50)로 클램프
        health.Heal(0f);
        // UnityEvent가 런타임 추가 시 null일 수 있음
        if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
        if (health.onHurt  == null) health.onHurt  = new UnityEngine.Events.UnityEvent();
        health.onDeath.RemoveListener(Die);
        health.onDeath.AddListener(Die);
        Debug.Log($"[Zombie] {name} Health 세팅: max={health.maxHealth}, current={health.GetCurrentHealth()}");

        // 5. NavMesh 위로 워프 (스폰 직후 약간 떠 있을 수 있음)
        TryWarpToNavMesh();
    }

    void Start()
    {
        FindPlayer();
        EnsureAnimatorClips();
    }

    // 컨트롤러의 빈 Motion 슬롯을 자동으로 채움
    void EnsureAnimatorClips()
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;

        var src = anim.runtimeAnimatorController.animationClips;
        AnimationClip walkClip = null, idleClip = null, attackClip = null;
        foreach (var c in src)
        {
            if (c == null) continue;
            string n = c.name.ToLower();
            if (walkClip   == null && (n.Contains("walk") || n.Contains("run"))) walkClip = c;
            if (idleClip   == null && n.Contains("idle"))                         idleClip = c;
            if (attackClip == null && n.Contains("attack"))                       attackClip = c;
        }

        Debug.Log($"[Zombie] {name}: 찾은 클립 - walk={walkClip?.name}, idle={idleClip?.name}, attack={attackClip?.name}");

        if (walkClip == null && idleClip == null) return;

        // AnimatorOverrideController로 슬롯 재매핑
        var over = new AnimatorOverrideController(anim.runtimeAnimatorController);
        var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>(over.overridesCount);
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

    void FindPlayer()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) target = p.transform;
    }

    void TryWarpToNavMesh()
    {
        if (agent == null || !agent.enabled) return;
        if (agent.isOnNavMesh) return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
    }

    // ─────────────────────────────────────────────
    void Update()
    {
        if (isDead) return;

        // 타겟 재탐색 (씬 로드 직후/플레이어 리스폰 대비)
        retargetTimer -= Time.deltaTime;
        if (target == null && retargetTimer <= 0f)
        {
            FindPlayer();
            retargetTimer = retargetInterval;
        }
        if (target == null) return;

        // 기절 처리
        if (isStunned)
        {
            stunTimer -= Time.deltaTime;
            if (stunTimer <= 0) isStunned = false;
            SetAnimSpeed(0f);
            return;
        }

        // NavMesh 위에 없으면 복구 시도
        bool canMove = agent != null && agent.enabled && agent.isOnNavMesh;
        if (!canMove) TryWarpToNavMesh();
        canMove = agent != null && agent.enabled && agent.isOnNavMesh;

        float dist = Vector3.Distance(transform.position, target.position);

        // 공격 범위 내: NavMesh와 상관없이 공격
        if (dist <= attackRange)
        {
            if (canMove) agent.ResetPath();
            FacePlayer();
            SetAnimSpeed(0f);

            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                DoAttack();
                attackTimer = attackCooldown;
            }
            return;
        }

        // NavMesh 안 되면 이동 불가, 가만히
        if (!canMove)
        {
            SetAnimSpeed(0f);
            return;
        }

        // 플레이어 감지 범위 내: 추적
        if (dist <= detectionRange)
        {
            if (isWandering)
            {
                isWandering = false;
                agent.speed = moveSpeed;
            }
            agent.SetDestination(target.position);
            SetAnimSpeed(agent.velocity.magnitude);
        }
        // 범위 밖: 배회
        else
        {
            DoWander();
        }
    }

    void FacePlayer()
    {
        Vector3 dir = target.position - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(dir),
            Time.deltaTime * 10f);
    }

    // ── 배회 ─────────────────────────────────────────
    void DoWander()
    {
        if (!enableWander)
        {
            agent.ResetPath();
            SetAnimSpeed(0f);
            return;
        }

        if (!isWandering)
        {
            isWandering = true;
            agent.speed = moveSpeed * wanderSpeedFactor;
            wanderTimer = 0f;
        }

        wanderTimer -= Time.deltaTime;
        bool reached = !agent.pathPending && agent.remainingDistance < 0.7f;
        if (wanderTimer <= 0f || reached)
        {
            PickNewWanderDestination();
            wanderTimer = Random.Range(wanderIntervalMin, wanderIntervalMax);
        }

        SetAnimSpeed(agent.velocity.magnitude);
    }

    void PickNewWanderDestination()
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = transform.position + new Vector3(rnd.x, 0, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }
    }

    // ── 애니메이션 ────────────────────────────────────
    void SetAnimSpeed(float speed)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return;

        bool walking = speed > 0.1f;
        anim.SetBool(HashIsWalking, walking);

        // Speed 파라미터가 있으면 추가 전달
        foreach (var p in anim.parameters)
        {
            if (p.nameHash == HashSpeed)
            {
                anim.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime);
                break;
            }
        }

        // 트랜지션 안 먹어도 강제로 상태 전환 (다양한 이름 시도)
        try
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            bool inAttack = info.IsName("Attack") || info.IsTag("Attack");
            bool inDie    = info.IsName("Die")    || info.IsTag("Die");

            if (inAttack || inDie) return;

            if (walking)
            {
                if (!IsInState(info, "Walk", "Walking", "Run", "walk", "Move"))
                {
                    PlayFirstAvailable("Walk", "Walking", "Run", "walk", "Move");
                }
            }
            else
            {
                if (!IsInState(info, "Idle", "idle", "Stand"))
                {
                    PlayFirstAvailable("Idle", "idle", "Stand");
                }
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

    // 트리거 디버그 (총알 맞는지 확인용)
    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<Bullet>() != null)
            Debug.Log($"[Zombie] 🎯 {name} 총알 트리거됨: {other.name}");
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.GetComponent<Bullet>() != null)
            Debug.Log($"[Zombie] 💥 {name} 총알 충돌됨: {collision.collider.name}");
    }

    void DoAttack()
    {
        if (anim != null && anim.runtimeAnimatorController != null)
            anim.SetTrigger(HashAttack);

        if (target == null) return;
        Health playerHealth = target.GetComponent<Health>();
        if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
    }

    // ── 외부 데미지 API ──────────────────────────────
    public void TakeDamage(float damage)
    {
        if (isDead) return;
        Debug.Log($"[Zombie] {name} 데미지 {damage} 받음");

        if (health != null)
        {
            health.TakeDamage(damage);
        }
        else
        {
            // Health 없으면 자체 카운트로라도 죽이기
            maxHealth -= damage;
            if (maxHealth <= 0f) Die();
        }
    }

    public void Stun(float duration)
    {
        if (isDead) return;
        isStunned = true;
        stunTimer = duration;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
    }

    // ── 사망 ─────────────────────────────────────────
    void Die()
    {
        if (isDead) return;
        isDead = true;

        if (agent != null) agent.enabled = false;

        // Animator 끄기 (가짜 죽음 애니메이션과 충돌 방지)
        if (anim != null) anim.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        // 죽는 클립이 컨트롤러에 있으면 사용, 없으면 가짜 죽음 코루틴
        StartCoroutine(FakeDeathRoutine());
    }

    System.Collections.IEnumerator FakeDeathRoutine()
    {
        // 1. 뒤로 쓰러지면서 (0.6초 동안 90도 회전)
        float duration = 0.6f;
        float elapsed = 0f;
        Quaternion startRot = transform.rotation;
        Quaternion endRot   = startRot * Quaternion.Euler(-90f, 0f, 0f); // 뒤로 넘어짐

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // 살짝 가속 곡선 (마지막에 빨라짐)
            transform.rotation = Quaternion.Slerp(startRot, endRot, t * t);
            // 약간 내려앉기
            transform.position += Vector3.down * (Time.deltaTime * 0.3f);
            yield return null;
        }

        // 2. 1.5초 동안 누워있음
        yield return new WaitForSeconds(1.5f);

        // 3. 1초에 걸쳐 땅 밑으로 가라앉으며 사라짐
        float sinkDuration = 1f;
        float sinkElapsed = 0f;
        Vector3 sinkStart = transform.position;
        Vector3 sinkEnd   = sinkStart + Vector3.down * 1.5f;
        while (sinkElapsed < sinkDuration)
        {
            sinkElapsed += Time.deltaTime;
            float t = sinkElapsed / sinkDuration;
            transform.position = Vector3.Lerp(sinkStart, sinkEnd, t);
            yield return null;
        }

        Destroy(gameObject);
    }
}

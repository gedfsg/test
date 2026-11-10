using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비 AI - 추적/공격/배회 + 자가복구(NavMesh 복귀, 컴포넌트 자동추가).
///
/// [건물 규칙 - 2026-11] (판정은 ZombieRules)
///  - 실내 좀비: 스폰 때 건물 안이면 그 건물이 homeBuilding. 플레이어가 그 건물에 들어오기 전엔 건물 안에서만 배회
///    (감지/소음/공격 OFF). 들어오면 1초 뒤 알아차리고(그르렁) 전원 추격. 건물 밖으로 나가면 바깥 좀비가 됨.
///  - 바깥 좀비: 플레이어가 실내면 추격 중단 → 배회 (같은 건물 안에 있는 좀비, 최종 공격 좀비는 예외).
///    목적지가 입구 반경(3m) 안이면 반경 밖으로 보정하고, 반경 안에서 서성이면 5m 밖으로 물러남.
/// </summary>
public class ZombieController : MonoBehaviour
{
    /// <summary>좀비가 죽을 때마다 (미션: 좀비 처치 수 집계용)</summary>
    public static event System.Action<ZombieController> AnyDied;

    /// <summary>살아있는(활성) 좀비 목록 (NoiseSystem이 반경 안 좀비를 찾을 때 사용 - 매번 FindObjects 안 하게)</summary>
    public static readonly System.Collections.Generic.List<ZombieController> All = new System.Collections.Generic.List<ZombieController>();

    [Header("Stats")]
    public float maxHealth      = 50f;
    public float moveSpeed      = 2f;
    public float attackRange    = 1.8f;
    public float attackDamage   = 10f;
    public float attackCooldown = 1.5f;

    [Header("AI")]
    public float detectionRange = 30f;
    public float retargetInterval = 1f;   // 플레이어 다시 찾기 간격

    [Header("밤 / 최종 공격")]
    [Tooltip("밤에 감지 범위 배수 (30m → 60m). 플레이어가 가만히 있어도 주변 좀비가 몰려옴")]
    public float nightDetectionMultiplier = 2f;
    [Tooltip("밤에 쫓아올 때 이동 속도 배수")]
    public float nightSpeedMultiplier = 1.2f;
    [Tooltip("켜지면 거리 상관없이 플레이어를 추적 (ZombieSpawner의 최종 공격 좀비 떼가 켬)")]
    public bool huntPlayer = false;

    [Header("소음 반응 (NoiseSystem)")]
    [Tooltip("소음을 들으면 이 시간(초) 동안 소리 난 곳으로 감 (도착하면 다시 배회)")]
    public float investigateTime = 8f;

    [Header("랜덤 배회")]
    public bool  enableWander       = true;
    public float wanderRadius       = 8f;
    public float wanderIntervalMin  = 2f;
    public float wanderIntervalMax  = 5f;
    public float wanderSpeedFactor  = 0.5f;

    [Header("애니메이션")]
    [Tooltip("러너: 쫓아올 때 달리기 애니메이션 (IsRunning)")]
    public bool isRunner = false;

    [Header("UI")]
    public GameObject damageTextPrefab;
    public bool showHealthBar = true;

    [Header("탄약 드랍 (사망 시) - 프리팹별로 확률/종류/수량 다르게 설정")]
    public AmmoData dropAmmoData;
    [Range(0f, 1f)] public float dropChance = 0.5f;
    public int dropAmmoMin = 5;
    public int dropAmmoMax = 10;

    // ─────────────────────────────────────────────
    private float attackTimer;
    private bool  inAttackRange;     // 공격 범위 판정 (들어올 땐 attackRange, 나갈 땐 조금 더 멀어야 → 경계에서 떨림 방지)
    private float attackLockUntil;   // 공격 동작 재생 중엔 쫓아가지 않고 제자리에서 바라보기만
    [Tooltip("공격 애니메이션 재생 시간(초). 이 동안은 제자리에서 플레이어만 바라봄 (Animator의 공격 재생 시간과 맞출 것)")]
    public float attackAnimDuration = 1.3f;
    private bool  isDead;
    private bool  isStunned;
    private float stunTimer;
    private float wanderTimer;
    private bool  isWandering;
    private float retargetTimer;
    private Vector3 noisePos;         // 마지막으로 들은 소음 위치
    private float investigateUntil;   // 이 시각까지 소음 위치로 이동
    // private float doorAvoidUntil;  // (이전) 플레이어 실내 시 입구 5m 밖으로 밀어내기 - 다시 돌아와 입구를 맴도는 원인이라 교체
    private float doorExitUntil;      // 이 시각까지 입구 반경 밖으로 물러나는 중 (바깥 좀비가 입구 앞에서 서성일 때)
    private float nextDoorExitCheck;  // 입구 반경 검사 간격 (0.5초 - 좀비 150마리 × 입구 100여 개를 매 프레임 돌지 않게)

    // 건물 규칙 (ZombieRules)
    private BuildingInterior homeBuilding;    // 실내 좀비: 스폰 때 들어 있던 건물 (밖으로 나가면 null = 바깥 좀비)
    private BuildingInterior currentBuilding; // 지금 서 있는 건물 (0.5초마다 갱신)
    private bool  indoorAlerted;              // 실내 좀비가 플레이어를 알아차림
    private float indoorAlertAt = -1f;        // 이 시각에 알아차림 (플레이어가 들어오고 1초 뒤)
    private float nextBuildingCheck;
    /// <summary>실내 전용 좀비인지 (아직 자기 건물 안)</summary>
    public bool IsIndoorZombie => homeBuilding != null;

    private Transform    target;
    private NavMeshAgent agent;
    private Animator     anim;
    private Health       health;

    private static readonly int HashIsWalking = Animator.StringToHash("IsWalking");
    private static readonly int HashSpeed     = Animator.StringToHash("Speed");
    private static readonly int HashAttack    = Animator.StringToHash("Attack");
    private static readonly int HashDie       = Animator.StringToHash("Die");
    private static readonly int HashHit       = Animator.StringToHash("Hit");
    private static readonly int HashIsRunning = Animator.StringToHash("IsRunning");
    private static readonly int HashAttackIdx = Animator.StringToHash("AttackIndex");

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
        health.SetMaxHealth(maxHealth);
        // UnityEvent가 런타임 추가 시 null일 수 있음
        if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
        if (health.onHurt  == null) health.onHurt  = new UnityEngine.Events.UnityEvent();
        health.onDeath.RemoveListener(Die);
        health.onDeath.AddListener(Die);
        health.onHurt.RemoveListener(OnHurtAnim);
        health.onHurt.AddListener(OnHurtAnim); // 피격 비틀거림
        if (damageTextPrefab != null) health.damageTextPrefab = damageTextPrefab;
        Debug.Log($"[Zombie] {name} Health 세팅: max={health.maxHealth}, current={health.GetCurrentHealth()}");

        // 5. 머리 위 체력바
        if (showHealthBar && GetComponent<ZombieHealthBar>() == null)
            gameObject.AddComponent<ZombieHealthBar>();

        // 6. NavMesh 위로 워프 (스폰 직후 약간 떠 있을 수 있음)
        TryWarpToNavMesh();
    }

    void Start()
    {
        FindPlayer();
        EnsureAnimatorClips();
        // 스폰 위치가 건물 안이면 실내 좀비. 플레이어가 이미 그 건물 안이면 1초 뒤 알아차림
        homeBuilding = currentBuilding = ZombieRules.BuildingAt(transform.position);
        if (homeBuilding != null && BuildingInterior.Current == homeBuilding) indoorAlertAt = Time.time + ZombieRules.IndoorAlertDelay;
    }

    /// <summary>플레이어가 건물에 들어옴 → 내 건물이면 1초 뒤 알아차림</summary>
    void OnPlayerEnteredBuilding(BuildingInterior b)
    {
        if (homeBuilding == null || b != homeBuilding || indoorAlerted || indoorAlertAt > 0f) return;
        indoorAlertAt = Time.time + ZombieRules.IndoorAlertDelay;
    }

    /// <summary>지금 서 있는 건물 갱신 + 실내 좀비 알아차림 타이머</summary>
    void UpdateBuildingState()
    {
        if (Time.time >= nextBuildingCheck)
        {
            nextBuildingCheck = Time.time + 0.5f;
            currentBuilding = ZombieRules.BuildingAt(transform.position);
            if (homeBuilding != null && currentBuilding != homeBuilding) { homeBuilding = null; indoorAlertAt = -1f; } // 밖으로 나감 → 바깥 좀비
        }
        if (indoorAlertAt > 0f && Time.time >= indoorAlertAt)
        {
            indoorAlertAt = -1f;
            if (homeBuilding != null && BuildingInterior.Current == homeBuilding) // 1초 사이에 나갔으면 취소
            {
                indoorAlerted = true;
                ZombieVoiceManager.Play(this, ZombieVoiceManager.State.Chase); // "그르렁" (목소리 풀 상한 안에서)
            }
        }
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

        // 스폰 보호: 추격/공격 없이 느리게 어슬렁거림 (플레이어를 바라보는 "경계" 상태)
        if (ZombieRules.IsHeld(transform.position))
        {
            inAttackRange = false;
            if (canMove)
            {
                DoWander();
                agent.speed = moveSpeed * ZombieRules.HeldSpeedFactor;
            }
            else SetAnimSpeed(0f);
            ZombieVoiceManager.Tick(this, ZombieVoiceManager.State.Idle);
            return;
        }

        UpdateBuildingState();

        // 실내 좀비 (아직 못 알아차림): 자기 건물 안에서 배회만 - 감지/공격 OFF
        if (homeBuilding != null && !indoorAlerted)
        {
            inAttackRange = false;
            if (canMove) DoWander(); else SetAnimSpeed(0f);
            ZombieVoiceManager.Tick(this, ZombieVoiceManager.State.Idle);
            return;
        }

        float dist = Vector3.Distance(transform.position, target.position);

        // 공격 범위 내 (또는 공격 동작 재생 중): NavMesh와 상관없이 제자리에서 공격
        // 들어올 땐 attackRange, 나갈 땐 1.25배 → 경계에서 매 프레임 추격/정지를 오가며 Idle↔Walk로 떨던 문제 방지
        inAttackRange = inAttackRange ? dist <= attackRange * 1.25f : dist <= attackRange;
        if (inAttackRange || Time.time < attackLockUntil)
        {
            if (canMove) { agent.ResetPath(); agent.velocity = Vector3.zero; } // 미끄러지며 멈추지 않게 즉시 정지
            FacePlayer();
            SetAnimSpeed(0f);
            if (!inAttackRange) return; // 동작 끝날 때까지 기다리기만

            if (!ZombieRules.CanAttack(transform.position)) return; // 건물에서 막 나온 플레이어: 입구 근처 좀비 공격 금지 (안전 창)

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

        // if (UpdateDoorAvoid()) return; // (이전) 플레이어가 실내면 입구 5m 좀비를 밀어냄 → 아래 MayChasePlayer + 입구 보정으로 교체

        // 플레이어 감지 범위 내: 추적 (안개면 20% 감소 - WeatherManager, 밤엔 nightDetectionMultiplier배, 최종 공격 좀비는 무조건)
        bool isNight = DayNightCycle.Instance != null && DayNightCycle.Instance.CurrentPhase == DayNightCycle.Phase.Night;
        float detect = detectionRange * WeatherManager.DetectionMultiplier * (isNight ? nightDetectionMultiplier : 1f);
        bool playerInMyBuilding = homeBuilding != null && BuildingInterior.Current == homeBuilding; // 알아차린 실내 좀비: 자기 건물 안 플레이어는 거리 상관없이
        if (homeBuilding != null && !playerInMyBuilding && dist > detect) indoorAlerted = false; // 플레이어가 나가서 멀어짐 → 다시 배회
        if ((huntPlayer || playerInMyBuilding || dist <= detect) && ZombieRules.MayChasePlayer(transform.position, huntPlayer))
        {
            isWandering = false;
            doorExitUntil = 0f;
            agent.speed = moveSpeed * (isNight ? nightSpeedMultiplier : 1f) * ZombieRules.SpeedMultiplier; // 밤엔 더 빠르게, 7일 넘기면 하루마다 +10%
            // 바깥 좀비는 입구 반경 안을 목적지로 삼지 않음 (입구 앞에 선 플레이어 → 반경 바로 바깥까지만)
            agent.SetDestination(currentBuilding == null ? ZombieRules.AdjustDestination(target.position) : target.position);
            SetAnimSpeed(agent.velocity.magnitude);
            ZombieVoiceManager.Tick(this, ZombieVoiceManager.State.Chase); // 2~5초마다 신음/비명
        }
        // 입구 반경 안에서 서성이는 바깥 좀비 → 5m 밖으로 (추격 중이 아닐 때만)
        else if (UpdateDoorExit()) { }
        // 범위 밖: 소음을 들었으면 그곳으로 확인하러 감, 아니면 배회
        else if (Time.time < investigateUntil)
        {
            isWandering = false;
            agent.speed = moveSpeed * ZombieRules.SpeedMultiplier;
            agent.SetDestination(currentBuilding == null ? ZombieRules.AdjustDestination(noisePos) : noisePos);
            if (!agent.pathPending && agent.remainingDistance < 1.5f) investigateUntil = 0f; // 도착 → 배회로
            SetAnimSpeed(agent.velocity.magnitude);
            ZombieVoiceManager.Tick(this, ZombieVoiceManager.State.Idle);
        }
        else
        {
            DoWander();
            ZombieVoiceManager.Tick(this, ZombieVoiceManager.State.Idle); // 5~15초마다 신음
        }
    }

    void OnEnable()
    {
        All.Add(this);
        BuildingInterior.PlayerEntered += OnPlayerEnteredBuilding;
    }
    void OnDisable()
    {
        All.Remove(this);
        BuildingInterior.PlayerEntered -= OnPlayerEnteredBuilding;
        ZombieVoiceManager.Forget(this); // 목소리 쿨타임 기록 정리
    }

    /// <summary>NoiseSystem: 소음을 들음 → investigateTime 동안 그 위치로 이동. 죽었거나 이미 쫓는 중(최종 공격)이면 false</summary>
    public bool HearNoise(Vector3 pos)
    {
        if (isDead || huntPlayer) return false;
        if (homeBuilding != null && !indoorAlerted) return false; // 실내 좀비는 플레이어가 들어오기 전까지 감지 OFF
        // 건물 안에서 난 소리는 그 건물 안 좀비만 들음 (바깥 좀비가 실내 총소리를 듣고 입구로 몰려드는 것 방지)
        BuildingInterior noiseIn = ZombieRules.BuildingAt(pos);
        if (noiseIn != null && noiseIn != ZombieRules.BuildingAt(transform.position)) return false;
        noisePos = pos;
        investigateUntil = Time.time + investigateTime;
        return true;
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

    // ── 입구 대기 금지 ───────────────────────────────
    // (이전) UpdateDoorAvoid: 플레이어가 실내면 그 건물 입구 5m 안 바깥 좀비를 8~14m 밖으로 밀어냄.
    //  밀려난 뒤 다시 감지 범위 안 플레이어를 쫓아 입구로 돌아와서 입구 앞을 맴돌았음 → 아래 방식으로 교체 (git 기록 참고)
    // 이제: 플레이어가 실내면 바깥 좀비는 아예 추격 안 함(ZombieRules.MayChasePlayer) + 모든 입구 반경 3m 안에서 서성이면 5m 밖으로.
    // 물러나는 중이면 true
    bool UpdateDoorExit()
    {
        if (Time.time < doorExitUntil)
        {
            if (!agent.pathPending && agent.remainingDistance < 0.7f) { doorExitUntil = 0f; return false; }
            SetAnimSpeed(agent.velocity.magnitude);
            ZombieVoiceManager.Tick(this, ZombieVoiceManager.State.Idle);
            return true;
        }
        if (Time.time < nextDoorExitCheck) return false;
        nextDoorExitCheck = Time.time + 0.5f;
        if (currentBuilding != null || !ZombieRules.TryGetDoorExit(transform.position, out Vector3 destination)) return false;
        isWandering = false;
        investigateUntil = 0f; // 입구 쪽 소음 확인은 그만둠
        agent.speed = moveSpeed * Mathf.Max(wanderSpeedFactor, 0.75f);
        agent.SetDestination(destination);
        doorExitUntil = Time.time + 4f;
        SetAnimSpeed(agent.velocity.magnitude);
        return true;
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
                // 실내 좀비(못 알아차림): 자기 건물 안에서만 (문 밖으로 나와 입구에 서 있지 않게)
                if (homeBuilding != null && !indoorAlerted && ZombieRules.BuildingAt(hit.position) != homeBuilding) continue;
                // 바깥 좀비: 입구 반경 안은 피함
                agent.SetDestination(currentBuilding == null ? ZombieRules.AdjustDestination(hit.position) : hit.position);
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
        if (HasParam(HashIsRunning)) anim.SetBool(HashIsRunning, isRunner && speed > 2f); // 러너가 쫓아올 때만 달리기

        // Speed 파라미터가 있으면 추가 전달
        foreach (var p in anim.parameters)
        {
            if (p.nameHash == HashSpeed)
            {
                anim.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime);
                break;
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
        {
            if (HasParam(HashAttackIdx)) anim.SetInteger(HashAttackIdx, Random.Range(0, 2)); // 공격 / 목 물기 랜덤
            anim.SetTrigger(HashAttack);
            attackLockUntil = Time.time + attackAnimDuration;
        }
        ZombieVoiceManager.Play(this, ZombieVoiceManager.State.Attack); // 공격: 즉시 비명

        if (target == null) return;
        Health playerHealth = target.GetComponent<Health>();
        if (playerHealth != null) playerHealth.TakeDamage(attackDamage * ZombieRules.DamageMultiplier);
    }

    bool HasParam(int hash)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        foreach (var p in anim.parameters) if (p.nameHash == hash) return true;
        return false;
    }

    void OnHurtAnim()
    {
        SpawnProtection.Cancel(); // 던진 무기 등 어떤 공격이든 좀비가 맞으면 스폰 보호 해제
        if (!isDead && HasParam(HashHit)) anim.SetTrigger(HashHit);
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
        AnyDied?.Invoke(this);

        // 사망 사운드: 단말마(목소리 풀) + 피 튀는 소리 + 쓰러지는 소리
        ZombieVoiceManager.Play(this, ZombieVoiceManager.State.Death);
        StartCoroutine(DeathSounds());

        // 좀비 종류별 드랍 (LootDatabase: 일반 탄약10%/넝마20%, 경찰 권총30%/탄약50%, 러너 음식, 탱커 희귀).
        // 데이터베이스가 없을 때만 예전 방식(프리팹의 dropAmmoData)으로 탄약 드랍
        if (LootDatabase.Instance != null) LootDatabase.Instance.DropForZombie(gameObject);
        else TryDropAmmo();

        if (agent != null) agent.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        // 사망 애니메이션(Die)이 컨트롤러에 있으면 그걸 재생, 없으면 예전 코드 쓰러뜨리기
        if (HasParam(HashDie))
        {
            if (HasParam(HashHit)) anim.ResetTrigger(HashHit); // 같은 프레임 피격 트리거가 남아 있으면 사망 후 다시 일어남
            anim.SetTrigger(HashDie);
            StartCoroutine(AnimDeathRoutine());
        }
        else
        {
            if (anim != null) anim.enabled = false; // 가짜 죽음 회전과 충돌 방지
            StartCoroutine(FakeDeathRoutine());
        }
    }

    void TryDropAmmo()
    {
        if (dropAmmoData == null) return;
        if (Random.value > dropChance) return;

        int amount = Random.Range(dropAmmoMin, dropAmmoMax + 1);
        AmmoDropSpawner.Spawn(transform.position, dropAmmoData, amount);
    }

    System.Collections.IEnumerator DeathSounds()
    {
        var lib = AudioManager.Library;
        if (lib == null) yield break;
        Vector3 pos = transform.position + Vector3.up;
        AudioManager.PlaySFX(SoundLibrary.Pick(lib.bloodSplatter), pos, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 25f);
        yield return new WaitForSeconds(0.7f); // 몸이 바닥에 닿는 쯤
        AudioManager.PlaySFX(SoundLibrary.Pick(lib.bodyFall), transform.position, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 25f);
    }

    // 사망 애니메이션 재생 → 누워있다가 → 땅 밑으로 가라앉으며 사라짐
    System.Collections.IEnumerator AnimDeathRoutine()
    {
        yield return new WaitForSeconds(3f);
        float sinkDuration = 1f, sinkElapsed = 0f;
        Vector3 sinkStart = transform.position, sinkEnd = sinkStart + Vector3.down * 1.5f;
        while (sinkElapsed < sinkDuration)
        {
            sinkElapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(sinkStart, sinkEnd, sinkElapsed / sinkDuration);
            yield return null;
        }
        Destroy(gameObject);
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

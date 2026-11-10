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

    [Header("UI")]
    public GameObject damageTextPrefab;
    public bool showHealthBar = true;

    [Header("탄약 드랍 (사망 시) - 프리팹별로 확률/종류/수량 다르게 설정")]
    public AmmoData dropAmmoData;
    [Range(0f, 1f)] public float dropChance = 0.5f;
    public int dropAmmoMin = 5;
    public int dropAmmoMax = 10;

    [Header("애니메이션 (kimozero/dev에서 합침)")]
    [Tooltip("러너: 쫓아올 때 달리기 애니메이션 (IsRunning)")]
    public bool isRunner = false;
    [Tooltip("공격 애니메이션 재생 시간(초). 이 동안은 제자리에서 플레이어만 바라봄 (Animator의 공격 재생 시간과 맞출 것)")]
    public float attackAnimDuration = 1.3f;
    [Tooltip("공격 동작 중에는 이동을 멈추고 대상을 바라본다 (게임 연출). 학습장은 학습 조건을 바꾸지 않도록 끈다")]
    public bool attackMoveLock = true;

    [Header("종류")]
    [Tooltip("일반/달리기/탱커 프로필. 비워두면 아래 인스펙터 값을 그대로 쓴다.")]
    public ZombieProfile profile;

    [Header("시야")]
    public ZombieVision vision = new ZombieVision();

    [Header("학습용")]
    [Tooltip("지정하면 Player 태그 대신 이 대상을 쫓는다. 학습 구역을 여러 개 복제할 때 필수 —\n" +
             "비워두면 모든 구역의 좀비가 씬에서 처음 찾은 플레이어 하나를 공유하게 된다.")]
    public Transform targetOverride;

    [Header("밤 / 최종 공격 (팀원 kimozero/dev에서 옮겨 옴)")]
    [Tooltip("밤에 감지 범위 배수 (30m → 60m). 플레이어가 가만히 있어도 주변 좀비가 몰려옴")]
    public float nightDetectionMultiplier = 2f;
    [Tooltip("밤에 쫓아올 때 이동 속도 배수")]
    public float nightSpeedMultiplier = 1.2f;
    [Tooltip("켜지면 거리 상관없이 플레이어를 추적 (ZombieSpawner의 최종 공격 좀비 떼가 켬)")]
    public bool huntPlayer = false;

    /// <summary>좀비가 죽을 때마다 (미션: 좀비 처치 수 집계용)</summary>
    public static event System.Action<ZombieController> AnyDied;
    /// <summary>살아있는(활성) 좀비 목록</summary>
    public static readonly System.Collections.Generic.List<ZombieController> All =
        new System.Collections.Generic.List<ZombieController>();

    [Header("Brain")]
    [Tooltip("비워두면 ZombieFSMBrain을 자동 장착. ML-Agents 도입 시 여기에 ZombieMLBrain을 넣는다.")]
    public ZombieBrain brain;

    [Header("좀비다움")]
    [Tooltip("켜면 ZombieQuirks를 붙여 좀비답게 움직인다: 늦은 반응, 멍한 개체, 총소리 무시, 가끔 멈춤, 점점 가속.\n" +
             "학습장(ZombieTrainingArena 안)에서는 자동으로 꺼진다. 값 조절은 프리팹에 ZombieQuirks를 미리 붙여서.")]
    public bool zombieQuirks = true;

    [Header("성능")]
    [Tooltip("NavMesh 경로 재계산 간격(초). 0이면 매 프레임. 좀비가 많을수록 올릴 것.")]
    public float repathInterval = 0.15f;

    [Header("디버그")]
    public bool drawGizmos = true;

    // ─────────────────────────────────────────────
    private float attackTimer;
    private float attackLockUntil;   // 공격 동작 재생 중엔 쫓아가지 않고 제자리에서 바라보기만
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
    private ZombieQuirks     quirks;

    private static readonly int HashIsWalking = Animator.StringToHash("IsWalking");
    private static readonly int HashSpeed     = Animator.StringToHash("Speed");
    private static readonly int HashAttack    = Animator.StringToHash("Attack");
    private static readonly int HashDie       = Animator.StringToHash("Die");
    private static readonly int HashHit       = Animator.StringToHash("Hit");
    private static readonly int HashIsRunning = Animator.StringToHash("IsRunning");
    private static readonly int HashAttackIdx = Animator.StringToHash("AttackIndex");

    /// <summary>날씨·밤을 반영한 감지 거리 (안개면 20% 감소, 밤엔 nightDetectionMultiplier배 —— 없으면 그대로).</summary>
    public float DetectionRangeNow => detectionRange * WeatherManager.DetectionMultiplier
                                      * (IsNightNow ? nightDetectionMultiplier : 1f);

    /// <summary>게임의 밤인가 (학습장 좀비는 낮밤과 무관)</summary>
    private bool IsNightNow => targetOverride == null && DayNightCycle.Instance != null
                               && DayNightCycle.Instance.CurrentPhase == DayNightCycle.Phase.Night;

    // ── Brain이 읽어가는 몸 상태 ──────────────────────
    public bool  CanMove           => agent != null && agent.enabled && agent.isOnNavMesh;
    public bool  IsStunned         => isStunned;
    public bool  AttackReady       => attackTimer <= 0f;
    public bool  IsDead            => isDead;
    /// <summary>공격 동작 재생 중인가 (좀비다움이 이 동안은 애니메이션 속도를 건드리지 않는다)</summary>
    public bool  AttackAnimating   => Time.time < attackLockUntil;
    /// <summary>러너가 아니어도 달리기 애니메이션을 쓴다 (좀비다움의 돌진 중)</summary>
    [System.NonSerialized] public bool ForceRunAnim;
    /// <summary>지금 타겟(플레이어/더미)을 직접 보고 있는가. 뇌 종류와 상관없이 같은 시야 판정을 쓴다.</summary>
    public bool  SeesTarget        => perception != null && perception.TargetVisible;
    /// <summary>지금 쫓는 대상 (플레이어/더미). 없으면 null. 위협 센서가 상대의 조준 방향을 읽는 데 쓴다.</summary>
    public Transform CurrentTarget => perception != null ? perception.Target : null;
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
        // 프로필이 꽂혀 있으면 인스펙터 값을 덮어쓴다.
        // SetupAgent/SetupHealth가 moveSpeed, maxHealth를 읽으므로 반드시 먼저.
        if (profile != null) profile.ApplyTo(this);

        SetupAgent();
        SetupCollider();
        SetupRigidbody();
        SetupAnimator();
        SetupHealth();
        SetupBrain();

        if (profile != null && fallbackBrain != null) profile.ApplyTo(fallbackBrain);

        // 좀비다움 (학습장에서는 붙이지 않는다 —— 붙어 있어도 스스로 꺼진다)
        quirks = GetComponent<ZombieQuirks>();
        if (quirks == null && zombieQuirks && GetComponentInParent<ZombieTrainingArena>() == null)
            quirks = gameObject.AddComponent<ZombieQuirks>();

        perception = new ZombiePerception(this);

        // 스폰 직후 약간 떠 있을 수 있음
        TryWarpToNavMesh();
    }

    void Start()
    {
        EnsureAnimatorClips();

        // 건물 규칙: 스폰 위치가 건물 안이면 실내 좀비. 플레이어가 이미 그 건물 안이면 1초 뒤 알아차림 (학습장 좀비는 제외)
        if (targetOverride == null)
        {
            homeBuilding = currentBuilding = ZombieRules.BuildingAt(transform.position);
            if (homeBuilding != null && BuildingInterior.Current == homeBuilding) indoorAlertAt = Time.time + ZombieRules.IndoorAlertDelay;
        }
    }

    void OnEnable()
    {
        NoiseSystem.Register(this);
        if (!All.Contains(this)) All.Add(this);
        BuildingInterior.PlayerEntered += OnPlayerEnteredBuilding;
    }
    void OnDisable()
    {
        NoiseSystem.Unregister(this);
        BuildingInterior.PlayerEntered -= OnPlayerEnteredBuilding;
        All.Remove(this);
        ZombieVoiceManager.Forget(this);   // 목소리 쿨타임 기록 정리
    }

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
        // main에서 추가된 API. Heal(0)은 currentHealth가 새 maxHealth까지 안 올라가서 교체됨.
        health.SetMaxHealth(maxHealth);

        // UnityEvent가 런타임 추가 시 null일 수 있음
        if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
        if (health.onHurt  == null) health.onHurt  = new UnityEngine.Events.UnityEvent();

        health.onDeath.RemoveListener(Die);
        health.onDeath.AddListener(Die);

        // ⭐ Bullet.cs는 Health.TakeDamage를 직접 호출하므로, 여기를 안 걸면
        //    좀비는 총에 맞은 사실 자체를 모른다. Brain에 피격을 전달하는 유일한 경로.
        health.onHurt.RemoveListener(NotifyBrainDamaged);
        health.onHurt.AddListener(NotifyBrainDamaged);
        health.onHurt.RemoveListener(OnHurtAnim);
        health.onHurt.AddListener(OnHurtAnim);   // 피격 비틀거림

        if (damageTextPrefab != null) health.damageTextPrefab = damageTextPrefab;

        // 머리 위 체력바
        if (showHealthBar && GetComponent<ZombieHealthBar>() == null)
            gameObject.AddComponent<ZombieHealthBar>();
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

        // 최종 공격 좀비 떼: 거리 상관없이 플레이어 쪽으로 (1초마다 플레이어 위치를 '들은' 것으로)
        if (huntPlayer && targetOverride == null)
        {
            huntTimer -= dt;
            if (huntTimer <= 0f)
            {
                huntTimer = 1f;
                Transform p = CurrentTarget != null ? CurrentTarget : FindPlayer();
                if (p != null) HearNoiseNow(p.position);
            }
        }

        // 1. Sense
        perception.Tick(dt);
        ZombieObservation obs = perception.Observe();

        // 게임 규칙 (팀원 ZombieRules): 스폰 보호 중이면 판단·좀비다움을 건너뛰고 느리게 어슬렁 (학습장 좀비는 제외)
        heldNow = targetOverride == null && ZombieRules.IsHeld(transform.position);

        // 건물 규칙 (kimozero 2026-11): 지금 서 있는 건물 갱신 + 실내 좀비 알아차림 타이머
        if (targetOverride == null) UpdateBuildingState();
        // 실내 좀비(아직 못 알아차림): 판단·좀비다움을 건너뛰고 자기 건물 안에서 배회만 (감지/소음/공격 OFF)
        bool dormant = !heldNow && targetOverride == null && IndoorDormant;

        // 2. Think
        ZombieAction action = heldNow ? HeldAction(in obs, dt)
                            : dormant ? DormantAction()
                            : ThinkSafely(in obs, dt);

        // 2.5 좀비다움: 늦은 반응 / 가끔 멈춤 / 점점 가속 (판단은 그대로, 실행만 비튼다)
        if (!heldNow && !dormant && quirks != null && zombieQuirks) action = quirks.Filter(in action, in obs, State, dt);

        // 2.7 게임 규칙: 건물에서 막 나온 플레이어 공격 금지 / 플레이어가 실내면 바깥 좀비 추격 중단 / 입구 3m 제외 영역
        if (!heldNow && !dormant && targetOverride == null) action = ApplyGameRules(in action, in obs);

        // 3. Act
        Execute(in action, in obs);

        // 4. 목소리 (팀원 사운드 시스템): 추적 5~10초 / 배회 10~25초마다 신음·비명. 학습장에서는 안 냄
        if (targetOverride == null)
        {
            var st = State;
            // bool chasing = !heldNow && !doorAvoiding && (st == ZombieState.Chase || st == ZombieState.Attack);  // (이전)
            bool chasing = !heldNow && !dormant && !ruleWandering && !doorExiting && (st == ZombieState.Chase || st == ZombieState.Attack);
            ZombieVoiceManager.Tick(this, chasing ? ZombieVoiceManager.State.Chase : ZombieVoiceManager.State.Idle);
        }
    }

    // ── 게임 규칙 (팀원 kimozero/dev의 ZombieRules —— AI와 분리된 규칙을 같은 호출로 적용) ──
    private bool    heldNow;
    private float   heldRepickAt;
    private Vector3 heldPoint;
    // private bool    doorAvoiding;     // (이전) 플레이어 실내 시 입구 반경 좀비를 밀어냄 → 다시 입구로 돌아와 맴도는 원인이라 아래 입구 규칙으로 교체
    // private float   doorAvoidUntil;
    // private Vector3 doorAvoidPoint;

    // ── 건물 입구 규칙 (kimozero 2026-11, 판정은 ZombieRules) ──
    //  - 실내 좀비: 스폰 때 건물 안이면 그 건물이 homeBuilding. 플레이어가 그 건물에 들어오기 전엔 건물 안에서만 배회
    //    (감지/소음/공격 OFF). 들어오면 1초 뒤 알아차리고(그르렁) 추격. 건물 밖으로 나가면 바깥 좀비가 됨.
    //  - 바깥 좀비: 플레이어가 실내면 추격·수색 중단 → 배회 (같은 건물 안 좀비, 최종 공격 좀비는 예외).
    //    목적지가 입구 반경(3m) 안이면 반경 밖으로 보정하고, 반경 안에서 서성이면 5m 밖으로 물러남.
    private BuildingInterior homeBuilding;    // 실내 좀비: 스폰 때 들어 있던 건물 (밖으로 나가면 null = 바깥 좀비)
    private BuildingInterior currentBuilding; // 지금 서 있는 건물 (0.5초마다 갱신)
    private bool    indoorAlerted;            // 실내 좀비가 플레이어를 알아차림
    private float   indoorAlertAt = -1f;      // 이 시각에 알아차림 (플레이어가 들어오고 1초 뒤)
    private float   nextBuildingCheck, indoorPingAt;
    private bool    ruleWandering, doorExiting;   // 규칙 때문에 배회 중 / 입구 반경 밖으로 물러나는 중
    private float   ruleWanderRepickAt, doorExitUntil, nextDoorExitCheck;
    private Vector3 ruleWanderPoint, doorExitPoint;
    /// <summary>실내 전용 좀비인지 (아직 자기 건물 안)</summary>
    public bool IsIndoorZombie => homeBuilding != null;
    private bool IndoorDormant => homeBuilding != null && !indoorAlerted;

    /// <summary>스폰 보호: 추격·공격 없이 주변을 느리게 어슬렁거리며 플레이어 쪽을 본다 ('경계')</summary>
    private ZombieAction HeldAction(in ZombieObservation obs, float dt)
    {
        // doorAvoiding = false;  // (이전)
        doorExiting = false;
        ruleWandering = false;
        float now = Time.time;
        Vector3 flat = heldPoint - transform.position; flat.y = 0f;
        if (now >= heldRepickAt || flat.sqrMagnitude < 1f)
        {
            Vector2 r = Random.insideUnitCircle * 6f;
            Vector3 p = transform.position + new Vector3(r.x, 0f, r.y);
            heldPoint = NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : transform.position;
            heldRepickAt = now + Random.Range(3f, 5f);
        }
        var a = ZombieAction.Stand();
        a.hasMoveTarget = true;
        a.moveTarget    = heldPoint;
        a.speedScale    = ZombieRules.HeldSpeedFactor;
        a.faceTarget    = obs.hasTarget;
        a.attack        = false;
        return a;
    }

    private ZombieAction ApplyGameRules(in ZombieAction action, in ZombieObservation obs)
    {
        ZombieAction a = action;

        // 건물에서 막 나온 플레이어: 입구 근처 좀비는 잠깐 공격 금지 (안전 창)
        if (a.attack && !ZombieRules.CanAttack(transform.position)) a.attack = false;

        // (이전) 플레이어가 실내: 입구 반경 안의 바깥 좀비는 입구 반대쪽으로 물러난다 (최종 공격 좀비 떼는 제외)
        //  → 밀려난 좀비가 4초 뒤 다시 플레이어를 쫓아 입구로 돌아와 맴돌아서 아래 규칙으로 교체 (git 기록 참고)
        // if (inReach || !ZombieRules.DoorAvoidApplies(huntPlayer)) { doorAvoiding = false; doorAvoidUntil = 0f; return a; }
        // ... TryPickDoorAvoidDestination으로 4초간 물러남

        bool inReach = obs.hasTarget && obs.distance <= obs.attackRange;
        var  st = State;
        bool pursuing = st == ZombieState.Chase || st == ZombieState.Attack || st == ZombieState.Investigate;

        // 1. 플레이어가 실내인데 이 좀비는 그 건물 밖 → 추격·수색 그만두고 배회 (최종 공격 좀비는 예외)
        if (pursuing && !ZombieRules.MayChasePlayer(transform.position, huntPlayer))
        {
            // 플레이어를 바로 앞까지 쫓아왔다 멈춘 경우 입구 반경 안일 수 있음 → 먼저 5m 밖으로
            if (UpdateDoorExit()) { ruleWandering = false; return ZombieAction.MoveTo(doorExitPoint, 0.75f); } // 다 물러나면 새 배회 지점부터
            return RuleWanderAction();
        }
        ruleWandering = false;

        // 2. 실내 쪽(실내 좀비 / 실내까지 쫓아 들어온 좀비)은 그대로
        if (currentBuilding != null) { doorExiting = false; return a; }

        // 3. 바깥 좀비가 쫓는 중: 입구 반경 안을 목적지로 삼지 않음 (실내 목적지는 AdjustDestination이 그대로 둠).
        //    바로 앞까지 붙었으면(공격 거리) 보정 안 함 → 입구에서 나온 플레이어는 정상적으로 공격
        if (pursuing)
        {
            doorExiting = false;
            if (a.hasMoveTarget && !inReach) a.moveTarget = ZombieRules.AdjustDestination(a.moveTarget);
            return a;
        }

        // 4. 배회 중: 입구 반경 안에서 서성이면 5m 밖으로, 아니면 배회 목적지만 보정
        if (UpdateDoorExit()) return ZombieAction.MoveTo(doorExitPoint, 0.75f);
        if (a.hasMoveTarget) a.moveTarget = ZombieRules.AdjustDestination(a.moveTarget);
        return a;
    }

    // ── 건물 입구 규칙 (kimozero 2026-11) ───────────────────
    /// <summary>플레이어가 건물에 들어옴 → 내 건물이면 1초 뒤 알아차림</summary>
    void OnPlayerEnteredBuilding(BuildingInterior b)
    {
        if (homeBuilding == null || b != homeBuilding || indoorAlerted || indoorAlertAt > 0f) return;
        indoorAlertAt = Time.time + ZombieRules.IndoorAlertDelay;
    }

    /// <summary>지금 서 있는 건물 갱신 + 실내 좀비 알아차림 / 다시 잠잠해짐</summary>
    void UpdateBuildingState()
    {
        float now = Time.time;
        if (now >= nextBuildingCheck)
        {
            nextBuildingCheck = now + 0.5f;
            currentBuilding = ZombieRules.BuildingAt(transform.position);
            if (homeBuilding != null && currentBuilding != homeBuilding) { homeBuilding = null; indoorAlertAt = -1f; } // 밖으로 나감 → 바깥 좀비
        }
        if (homeBuilding == null) return;

        bool playerInMyBuilding = BuildingInterior.Current == homeBuilding;
        if (indoorAlertAt > 0f && now >= indoorAlertAt)
        {
            indoorAlertAt = -1f;
            if (playerInMyBuilding) // 1초 사이에 나갔으면 취소
            {
                indoorAlerted = true;
                indoorPingAt = 0f;
                ZombieVoiceManager.Play(this, ZombieVoiceManager.State.Chase); // "그르렁" (목소리 풀 상한 안에서)
            }
        }
        if (!indoorAlerted) return;

        Transform p = FindPlayer();
        if (playerInMyBuilding)
        {
            // 실내 벽에 가려 시야로 못 봐도 같은 건물 안 플레이어는 쫓음: 1초마다 플레이어 위치를 '들은' 것으로 (최종 공격 좀비와 같은 방식)
            if (p != null && now >= indoorPingAt) { indoorPingAt = now + 1f; HearNoiseNow(p.position); }
        }
        else if (p == null || (p.position - transform.position).magnitude > DetectionRangeNow)
        {
            indoorAlerted = false; // 플레이어가 나가서 멀어짐 → 다시 건물 안 배회
        }
    }

    /// <summary>실내 좀비(못 알아차림): 자기 건물 안에서만 느리게 배회 (문 밖으로 나와 입구에 서 있지 않게)</summary>
    private ZombieAction DormantAction()
    {
        doorExiting = false;
        ruleWandering = false;
        PickRuleWanderPoint(5f, true);
        return ZombieAction.MoveTo(ruleWanderPoint, 0.5f);
    }

    /// <summary>플레이어가 실내라 쫓지 못하는 바깥 좀비: 주변 배회 (입구 반경은 피함)</summary>
    private ZombieAction RuleWanderAction()
    {
        if (!ruleWandering) ruleWanderRepickAt = 0f; // 막 추격을 멈춤 → 바로 새 배회 지점
        ruleWandering = true;
        PickRuleWanderPoint(8f, false);
        return ZombieAction.MoveTo(ruleWanderPoint, 0.5f);
    }

    /// <summary>배회 지점 고르기. insideHome이면 자기 건물 안에서만, 아니면 건물 밖 + 입구 반경 밖</summary>
    private void PickRuleWanderPoint(float radius, bool insideHome)
    {
        float now = Time.time;
        Vector3 flat = ruleWanderPoint - transform.position; flat.y = 0f;
        if (now < ruleWanderRepickAt && flat.sqrMagnitude > 0.5f) return;
        ruleWanderRepickAt = now + Random.Range(3f, 6f);
        ruleWanderPoint = transform.position;
        for (int i = 0; i < 6; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 c = transform.position + new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            BuildingInterior at = ZombieRules.BuildingAt(hit.position);
            if (insideHome ? at != homeBuilding : at != null) continue;
            ruleWanderPoint = insideHome ? hit.position : ZombieRules.AdjustDestination(hit.position);
            return;
        }
    }

    /// <summary>바깥 좀비가 입구 반경(3m) 안에서 서성이면 입구에서 5m 이상 떨어진 곳으로 (0.5초마다 검사). 물러나는 중이면 true</summary>
    private bool UpdateDoorExit()
    {
        float now = Time.time;
        if (doorExiting)
        {
            Vector3 left = doorExitPoint - transform.position; left.y = 0f;
            if (now < doorExitUntil && left.sqrMagnitude > 0.5f) return true;
            doorExiting = false; // 도착 (또는 4초 지남) → 원래 배회로
        }
        if (now < nextDoorExitCheck) return false;
        nextDoorExitCheck = now + 0.5f; // 좀비 150마리 × 입구 100여 개를 매 프레임 돌지 않게
        if (!ZombieRules.TryGetDoorExit(transform.position, out doorExitPoint)) return false;
        doorExiting = true;
        doorExitUntil = now + 4f;
        return true;
    }

    private float huntTimer;
    private static Transform cachedPlayer;
    private static Transform FindPlayer()
    {
        if (cachedPlayer == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) cachedPlayer = go.transform;
        }
        return cachedPlayer;
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

        // 공격 동작 재생 중: 제자리에서 대상만 바라본다 (미끄러지며 멈추지 않게 즉시 정지)
        if (attackMoveLock && Time.time < attackLockUntil)
        {
            if (CanMove && hasDestination) { agent.ResetPath(); hasDestination = false; }
            if (CanMove) agent.velocity = Vector3.zero;
            if (obs.hasTarget) FaceTowards(obs.targetPosition);
            SetAnimSpeed(0f);
            return;
        }

        float moveSpeedNow = 0f;

        if (CanMove)
        {
            if (action.hasMoveTarget)
            {
                // 밤엔 쫓을 때 더 빠르게 (팀원 밸런스: ×1.2)
                var stNow = State;
                float night = IsNightNow && (stNow == ZombieState.Chase || stNow == ZombieState.Attack) ? nightSpeedMultiplier : 1f;
                // 7일 넘기면 하루마다 +10% (ZombieRules) —— 학습장 좀비는 그대로
                float difficulty = targetOverride == null ? ZombieRules.SpeedMultiplier : 1f;
                agent.speed = moveSpeed * Mathf.Max(action.speedScale, 0.01f) * night * difficulty;
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
        if (HasParam(HashIsRunning)) anim.SetBool(HashIsRunning, (isRunner || ForceRunAnim) && speed > 2f);   // 러너가 쫓아올 때 / 돌진할 때만 달리기

        foreach (var p in anim.parameters)
        {
            if (p.nameHash == HashSpeed)
            {
                anim.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime);
                break;
            }
        }

    }

    // ── 공격 ─────────────────────────────────────────
    /// <summary>
    /// 공격을 휘둘렀을 때 (대상, 데미지). 학습장이 더미의 가상 체력을 깎는 데 쓴다 ——
    /// 더미에는 Health를 붙일 수 없어서(죽으면 Time.timeScale = 0) 이 이벤트로 받는다.
    /// 게임에는 영향 없다 (구독자가 없으면 아무 일도 안 일어남).
    /// </summary>
    public event System.Action<ZombieController, Transform> AttackLanded;

    void DoAttack()
    {
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            if (HasParam(HashAttackIdx)) anim.SetInteger(HashAttackIdx, Random.Range(0, 2));   // 공격 / 목 물기 랜덤
            anim.SetTrigger(HashAttack);
            attackLockUntil = Time.time + attackAnimDuration;
        }

        Transform t = perception != null ? perception.Target : null;
        if (t == null) return;

        AttackLanded?.Invoke(this, t);
        if (targetOverride == null) ZombieVoiceManager.Play(this, ZombieVoiceManager.State.Attack);   // 공격: 즉시 비명

        Health playerHealth = t.GetComponent<Health>();
        if (playerHealth != null) playerHealth.TakeDamage(attackDamage * ZombieRules.DamageMultiplier);   // 7일 이후 난이도
    }

    bool HasParam(int hash)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        foreach (var p in anim.parameters) if (p.nameHash == hash) return true;
        return false;
    }

    void OnHurtAnim()
    {
        if (targetOverride == null) SpawnProtection.Cancel(); // 던진 무기 등 어떤 공격이든 좀비가 맞으면 스폰 보호 해제
        if (!isDead && HasParam(HashHit)) anim.SetTrigger(HashHit);
    }

    // ── 외부 API (Bullet.cs / MeleeWeapon.cs가 호출) ────
    /// <summary>마지막으로 받은 피해량 (좀비다움의 피격 경직 세기용).
    /// 총알·도끼는 Health.TakeDamage를 직접 부르므로 체력이 줄어든 양으로 잰다.</summary>
    public float LastDamage { get; private set; }
    private float lastHealthSeen = -1f;

    public void TakeDamage(float damage)
    {
        if (isDead) return;
        LastDamage = damage;

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
        if (health != null)
        {
            float cur = health.GetCurrentHealth();
            float before = lastHealthSeen >= 0f ? lastHealthSeen : health.maxHealth;
            if (before > cur) LastDamage = before - cur;
            lastHealthSeen = cur;
        }

        // 위치 힌트는 주지 않는다 — 어디서 날아온 총알인지 알 방법이 없으므로.
        // 총소리 방향은 NoiseSystem이 따로 알려준다.
        if (quirks != null && zombieQuirks) quirks.NotifyDamaged();   // 맞으면 멍하던 좀비도 깬다
        ActiveBrain?.OnDamaged();
    }

    /// <summary>NoiseSystem이 호출. 소음 지점을 수색 목표로 삼는다. 반응했으면(늦게라도) true</summary>
    public bool HearNoise(Vector3 position)
    {
        if (isDead) return false;
        if (targetOverride == null)
        {
            if (IndoorDormant) return false; // 실내 좀비는 플레이어가 들어오기 전까지 감지 OFF
            // 건물 안에서 난 소리는 그 건물 안 좀비만 들음 (바깥 좀비가 실내 총소리를 듣고 입구로 몰려드는 것 방지)
            BuildingInterior noiseIn = ZombieRules.BuildingAt(position);
            if (noiseIn != null && noiseIn != ZombieRules.BuildingAt(transform.position)) return false;
        }
        // 좀비다움: 못 들은 척하거나 늦게 반응 (늦게 반응하면 ZombieQuirks가 나중에 HearNoiseNow를 부른다)
        if (quirks != null && zombieQuirks && quirks.InterceptNoise(position)) return true;
        HearNoiseNow(position);
        return true;
    }

    /// <summary>소리에 지금 바로 반응한다 (ZombieQuirks가 미뤄 둔 소리를 전달할 때도 쓴다).</summary>
    public void HearNoiseNow(Vector3 position)
    {
        if (isDead) return;
        perception?.NotifyDisturbance(position);
        ActiveBrain?.OnHeardNoise(position);
    }

    /// <summary>
    /// ML-Agents 에피소드 리셋용. 좀비를 지정 위치로 옮기고 내부 상태를 초기화한다.
    /// 학습 씬에서만 호출되며, 본 게임 흐름에는 영향 없다.
    /// </summary>
    public void ResetForEpisode(Vector3 position, Quaternion rotation)
    {
        isDead      = false;
        isStunned   = false;
        stunTimer   = 0f;
        attackTimer = 0f;
        attackLockUntil = 0f;

        hasDestination = false;
        repathTimer    = 0f;

        transform.SetPositionAndRotation(position, rotation);

        if (agent != null)
        {
            agent.enabled = true;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            if (agent.isOnNavMesh) agent.ResetPath();
        }

        if (health != null) health.SetMaxHealth(maxHealth);

        // 이전 에피소드의 목격 기록이 남으면 새 에피소드가 오염된다
        perception = new ZombiePerception(this);
        if (quirks != null) quirks.ResetState();

        foreach (var b in GetComponents<ZombieBrain>())
        {
            b.enabled = true;
            b.OnResetEpisode();
        }
    }

    public void Stun(float duration)
    {
        if (isDead) return;
        isStunned = true;
        stunTimer = duration;
        if (CanMove) { agent.ResetPath(); hasDestination = false; }
    }

    // ── 사망 ─────────────────────────────────────────
    System.Collections.IEnumerator DeathSounds()
    {
        var lib = AudioManager.Library;
        if (lib == null) yield break;
        Vector3 pos = transform.position + Vector3.up;
        AudioManager.PlaySFX(SoundLibrary.Pick(lib.bloodSplatter), pos, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 25f);
        yield return new WaitForSeconds(0.7f);   // 몸이 바닥에 닿는 쯤
        AudioManager.PlaySFX(SoundLibrary.Pick(lib.bodyFall), transform.position, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 25f);
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;
        AnyDied?.Invoke(this);

        // 사망 사운드 (팀원 사운드 시스템): 단말마 + 피 튀는 소리 + 쓰러지는 소리. 학습장에서는 안 냄
        if (targetOverride == null)
        {
            ZombieVoiceManager.Play(this, ZombieVoiceManager.State.Death);
            StartCoroutine(DeathSounds());
        }

        // 좀비 종류별 드랍 (LootDatabase). 데이터베이스가 없을 때만 예전 방식(프리팹의 dropAmmoData)으로 탄약 드랍
        if (LootDatabase.Instance != null) LootDatabase.Instance.DropForZombie(gameObject);
        else TryDropAmmo();

        if (agent != null) agent.enabled = false;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        foreach (var b in GetComponents<ZombieBrain>()) b.enabled = false;

        // 사망 애니메이션(Die)이 컨트롤러에 있으면 그걸 재생, 없으면 예전 코드 쓰러뜨리기
        if (HasParam(HashDie))
        {
            if (HasParam(HashHit)) anim.ResetTrigger(HashHit);   // 같은 프레임 피격 트리거가 남아 있으면 사망 후 다시 일어남
            anim.SetTrigger(HashDie);
            StartCoroutine(AnimDeathRoutine());
        }
        else
        {
            if (anim != null) anim.enabled = false;   // 가짜 죽음 회전과 충돌 방지
            StartCoroutine(FakeDeathRoutine());
        }
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

    void TryDropAmmo()
    {
        if (dropAmmoData == null) return;
        if (Random.value > dropChance) return;

        int amount = Random.Range(dropAmmoMin, dropAmmoMax + 1);
        AmmoDropSpawner.Spawn(transform.position, dropAmmoData, amount);
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

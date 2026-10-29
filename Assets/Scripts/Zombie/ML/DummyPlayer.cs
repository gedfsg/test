using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 학습용 더미 플레이어. 사람이 조작할 수 없으므로 스크립트로 움직인다.
///
/// 중요: 이 오브젝트는 Tag가 "Player"여야 한다 (ZombiePerception이 태그로 찾는다).
///       Health는 붙이지 않는다 —— 죽으면 Time.timeScale = 0 으로 학습이 멈춘다.
///
/// ── 왜 도주 로직이 똑똑해야 하는가 ───────────────────────────
/// 추격자의 수준은 도망자의 수준에 묶인다. 도망자가 알아서 구석에 몰리면
/// 좀비는 "직진하면 알아서 잡힌다"를 배우고, 포위를 배울 이유가 사라진다.
/// 그러면 다중 에이전트 협동이라는 주제 자체가 성립하지 않는다.
///
/// 그래서 단순히 "좀비 반대편으로" 가지 않고, 여러 방향을 후보로 놓고
/// (1) 그쪽으로 얼마나 멀리 갈 수 있는가 (막다른 길 회피)
/// (2) 그 끝 지점이 모든 좀비에게서 얼마나 먼가 (포위망 회피)
/// 를 점수로 매겨 고른다. 이러면 벽을 등져도 벽을 따라 빠져나가고,
/// 좀비가 길목을 막아야만 잡히게 된다.
///
/// ── 시야 제한 (limitedSight) ───────────────────────────────
/// 끄면(기존) 더미는 벽 너머의 좀비까지 항상 전부 안다. 사람은 그렇지 않다 ——
/// 건물 뒤로 돌아 들어오는 좀비는 보이기 전까지 모른다. 그래서 사람 상대로는 통하는
/// 매복과 몰이가, 전지적인 더미 상대로는 한 번도 통하지 않아 학습되지 않았다.
/// 켜면 더미는 시야(벽에 안 가려진 sightRange 안)와 청각(hearRadius 안, 벽 무관)으로
/// 좀비를 알아채고, 놓친 좀비는 memorySeconds 동안 마지막 위치를 기억한다.
/// yaml의 environment_parameters/evader_limited_sight(1/0)로 빌드 없이 켜고 끌 수 있다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class DummyPlayer : MonoBehaviour
{
    [Header("이동")]
    public float minSpeed = 2.5f;
    public float maxSpeed = 4.5f;
    [Tooltip("배회할 때 다음 목적지를 고르는 반경")]
    public float roamRadius = 15f;

    [Header("도주 성향 (에피소드마다 무작위)")]
    [Tooltip("좀비가 이 거리 안에 들어오면 도망친다. 범위 안에서 매번 추첨")]
    public float minFleeDistance = 12f;
    public float maxFleeDistance = 22f;

    [Header("기동성")]
    [Tooltip("초당 회전 각도. NavMeshAgent 기본값(120)은 반 바퀴 트는 데 1.5초나 걸려서,\n" +
             "벽 사이로 도망칠 때 최고 속도에 도달하지 못한다. 그러면 커리큘럼으로 속도를\n" +
             "올려도 실제 움직임은 거의 그대로라, 좀비가 포위를 배울 압력이 생기지 않는다.\n" +
             "좀비(720)와 맞춘다.")]
    public float turnSpeed = 720f;
    [Tooltip("가속도. 사람이 조작하는 캐릭터처럼 즉시 최고 속도에 가깝게")]
    public float acceleration = 30f;

    [Header("도주 판단")]
    [Tooltip("도주 방향 후보 수. 많을수록 똑똑하지만 연산이 는다")]
    [Range(6, 24)] public int fleeSamples = 12;
    [Tooltip("한 방향으로 내다보는 거리")]
    public float fleeLookAhead = 14f;
    [Tooltip("'멀리 갈 수 있음'에 주는 가중치. 높을수록 막다른 길을 강하게 피한다")]
    public float opennessWeight = 1.2f;

    [Header("방향 고수 (우왕좌왕 방지)")]
    [Tooltip("한 번 정한 도주 방향을 최소 이 시간(초) 동안 유지한다.\n" +
             "매번 새로 계산하면 점수가 비슷한 방향끼리 엎치락뒤치락하며 제자리에서 떨게 되고,\n" +
             "좀비는 '가까이만 가면 알아서 버벅거린다'를 배워버린다.")]
    public float minCommitTime = 0.7f;
    [Tooltip("새 방향이 지금 방향보다 이 비율 이상 좋아야 갈아탄다 (0.2 = 20%)")]
    public float switchMargin = 0.2f;
    [Tooltip("좀비가 이 거리 안이면 고수 시간을 무시하고 즉시 다시 판단한다 (긴급 회피)")]
    public float panicDistance = 4f;

    [Header("난이도 (실험 변수)")]
    [Tooltip("0 = 늘 단순히 반대 방향으로 도망 (잘 몰린다)\n" +
             "1 = 늘 열린 쪽을 계산해서 도망 (포위해야 잡힌다)\n" +
             "도망자 수준을 바꿔가며 학습하면 '얼마나 똑똑한 상대까지 잡는가'를 실험할 수 있다.")]
    [Range(0f, 1f)] public float evasionQuality = 1f;

    [Header("시야 제한 (사람처럼)")]
    [Tooltip("켜면 보이거나 들리는 좀비만 안다. yaml evader_limited_sight(1/0)가 있으면 그 값")]
    public bool  limitedSight   = false;
    [Tooltip("벽에 안 가려졌을 때 볼 수 있는 거리(m). 탑뷰라 방향 제한은 두지 않는다")]
    public float sightRange     = 30f;
    [Tooltip("이 거리 안의 좀비는 벽 너머라도 안다 (발소리)")]
    public float hearRadius     = 5f;
    [Tooltip("놓친 좀비의 마지막 위치를 기억하는 시간(초)")]
    public float memorySeconds  = 2f;

    // ── 사격 (반격하는 플레이어) ─────────────────────────
    // 실제 플레이어는 도망만 치지 않고 쏜다. 탑뷰 슈터처럼 '바라보는 방향 = 총구 방향'이고,
    // 이동과 시선은 따로 논다 —— 좀비를 바라본 채 뒷걸음질 치며 쏠 수 있다.
    // 조준은 회전 속도에 한계가 있고(마우스를 끌어야 하므로), 새 표적을 잡으면 반응 시간이 걸리고,
    // 총알은 날아가는 시간이 있다. 그래서 옆으로 빠르게 움직이거나, 벽 뒤로 돌거나,
    // 동료가 조준당하는 틈에 다가오는 좀비는 덜 맞는다 —— 규칙으로 짜기 어렵고 학습이 잘하는 영역.
    // 모든 값은 yaml(environment_parameters)로 덮어쓸 수 있다 (괄호 안 이름).
    // 기본값은 2D 시뮬레이션으로 맞췄다: 조율기만 쓴 무리(일반 3.5×2 / 빠른 4.0×2 / 탱커 2.5, 체력 50/35/150)가
    // 더미 2.6m/s 상대로 60판 중 34판 잡고 26판 전멸 —— 피하는 법에 따라 결과가 갈리는 세기.
    // (0.35초 · 25 데미지 · ±3° · 360°/s면 60판 중 59판 전멸이라 배울 신호가 없었다)
    [Header("사격 (반격하는 플레이어)")]
    [Tooltip("켜면 바라보는 방향으로 쏜다 (dummy_shoot 1/0). 맞은 좀비의 체력 처리는 학습장(Arena)이 한다.")]
    public bool  shootingEnabled = false;
    [Tooltip("발사 간격(초) (dummy_fire_interval)")]
    public float fireInterval    = 0.7f;
    [Tooltip("한 발 데미지 (dummy_damage)")]
    public float bulletDamage    = 20f;
    [Tooltip("탄창 크기 (dummy_mag)")]
    public int   magazineSize    = 12;
    [Tooltip("재장전 시간(초) (dummy_reload)")]
    public float reloadTime      = 1.8f;
    [Tooltip("총알 속도(m/s) (dummy_bullet_speed). 날아가는 동안 옆으로 비키면 빗나간다")]
    public float bulletSpeed     = 40f;
    [Tooltip("0보다 크면 매 판 연사 간격을 [fireInterval, 이 값] 사이에서 무작위로 고른다 (dummy_fire_interval_max).\n" +
             "게임 총이 여러 종류라 한 가지 총에만 맞춰 배우지 않게 하려는 것. 0 = 끔")]
    public float fireIntervalMax = 0f;
    [Tooltip("0보다 크면 매 판 총알 속도를 [bulletSpeed, 이 값] 사이에서 무작위로 고른다 (dummy_bullet_speed_max). 0 = 끔")]
    public float bulletSpeedMax  = 0f;
    // 무작위화 기준값 (인스펙터/yaml 값). 뽑은 값이 fireInterval에 들어가므로 기준을 따로 둬야 판마다 밀려가지 않는다.
    private float fireIntervalSetting = -1f, bulletSpeedSetting = -1f;
    [Tooltip("사거리(m) (dummy_range)")]
    public float fireRange       = 20f;
    [Tooltip("탄 퍼짐(±도) (dummy_spread)")]
    public float spreadDeg       = 6f;
    [Tooltip("조준 회전 속도(도/초) (dummy_turn_speed). 마우스로 시선을 돌리는 속도의 한계")]
    public float aimTurnSpeed    = 180f;
    [Tooltip("새 표적을 잡고 첫 발을 쏘기까지(초) (dummy_reaction)")]
    public float reactionTime    = 0.5f;
    [Tooltip("움직이는 표적의 앞을 얼마나 내다보고 쏘는가 (0 = 지금 위치, 1 = 정확한 예측) (dummy_aim_lead)")]
    [Range(0f, 1f)] public float aimLead = 0.3f;
    [Tooltip("사람의 반응 지연(초) (dummy_perception_delay). 0 = 꺼짐(지금 위치를 바로 안다).\n" +
             "켜면 더미는 이 시간 전에 본 좀비의 위치·속도로 '지금쯤 여기 있겠지' 하고 겨눈다.\n" +
             "똑바로 오는 좀비는 그대로 맞히지만, 방향을 바꾼 좀비는 조준이 늦게 따라온다 → 비켜 가는 게 실제로 통한다.\n" +
             "사람의 시각 반응 시간은 대략 0.2~0.3초.")]
    public float perceptionDelay = 0f;
    [Tooltip("조준이 표적에서 이 각도(도) 안일 때만 쏜다")]
    public float fireCone        = 6f;
    [Tooltip("뒷걸음질 속도 배율 (dummy_backpedal). 바라보는 반대쪽으로 움직일 때 이만큼 느려진다 (1 = 안 느려짐)")]
    [Range(0.3f, 1f)] public float backpedalFactor = 0.8f;
    [Tooltip("사격 중에는 이 배율만큼 가까이 와야 도망친다 (dummy_flee_scale). 쏘는 플레이어는 버티다가 물러선다")]
    public float shootingFleeScale = 0.6f;
    [Tooltip("좀비 몸 반지름(m). 총알이 이 안을 지나가면 맞는다")]
    public float zombieHitRadius = 0.5f;
    [Tooltip("Scene 뷰에 총알 궤적을 그린다")]
    public bool  drawBullets     = true;

    /// <summary>총알이 좀비에 맞았을 때 (맞은 좀비, 데미지). 학습장이 가상 체력을 깎는다.</summary>
    public event System.Action<Transform, float> ShotHit;

    /// <summary>사격이 켜져 있는가 (이번 판).</summary>
    public bool    ShootingActive => shootingNow;
    /// <summary>총구(시선) 방향 (수평).</summary>
    public Vector3 AimForward     => Dir(aimYaw);
    /// <summary>재장전 중인가.</summary>
    public bool    IsReloading    => shootingNow && Time.time < reloadUntil;
    /// <summary>탄창에 남은 비율 (0~1).</summary>
    public float   AmmoFraction   => magazineSizeNow > 0 ? ammo / (float)magazineSizeNow : 0f;
    /// <summary>지금 조준 중인 좀비 (없으면 null).</summary>
    public Transform AimTarget    => aimTarget;
    /// <summary>이번 판 발사 수 / 명중 수 (지표용).</summary>
    public int ShotsFired { get; private set; }
    public int ShotsHit   { get; private set; }

    [Header("참조")]
    [Tooltip("비우면 씬에서 자동 탐색. Arena가 SetThreats로 구역 좀비들을 넣어준다.")]
    public Transform threat;

    // ─────────────────────────────────────────────
    private readonly List<Transform> threats = new List<Transform>();
    private NavMeshAgent agent;
    private float fleeDistance;
    private float repathTimer;

    private bool    hasFleeTarget;
    private Vector3 fleeTarget;
    private float   fleeCommitUntil;

    // 더미가 '안다고 믿는' 좀비 위치 (시야 제한을 끄면 실제 위치 전부)
    private struct Known { public Vector3 pos; public float time; }
    private readonly Dictionary<Transform, Known> known = new Dictionary<Transform, Known>();
    private readonly List<Vector3> threatPositions = new List<Vector3>();
    private readonly List<Transform> forgetBuffer = new List<Transform>();
    private readonly RaycastHit[] losBuffer = new RaycastHit[8];

    /// <summary>지금 좀비를 피해 도망치는 중인지 (배회 중이면 false). 평가 지표용.</summary>
    public bool IsFleeing { get; private set; }

    // 사격 상태
    private bool  shootingNow;
    private float baseSpeed;
    private float aimYaw;
    private Transform aimTarget;
    private float aimAcquiredTime;
    private float targetPickTimer;
    private float nextFireTime;
    private float reloadUntil;
    private int   ammo;
    private int   magazineSizeNow;
    private float fleeScaleNow = 1f;

    private struct Bullet { public Vector3 pos; public Vector3 dir; public float traveled; }
    private readonly List<Bullet> bullets = new List<Bullet>();
    private readonly RaycastHit[] bulletBuffer = new RaycastHit[8];
    private readonly Dictionary<Transform, NavMeshAgent> threatNavs = new Dictionary<Transform, NavMeshAgent>();
    // 반응 지연용 기록: 좀비마다 최근 (시각, 위치, 속도)
    private struct Seen { public float t; public Vector3 p, v; }
    private readonly Dictionary<Transform, List<Seen>> seenHistory = new Dictionary<Transform, List<Seen>>();

    void Awake()
    {
        // ⚠ 더미는 죽으면 안 된다.
        // Health.Die()는 Player 태그가 죽으면 Time.timeScale = 0 으로 게임을 멈추는데,
        // 학습 씬에서 그게 일어나면 시뮬레이션 전체가 정지해 Academy가 스텝을 못 돌리고
        // Python 쪽이 "worker stuck in waiting state"로 타임아웃된다.
        // 게다가 에피소드가 바뀌어도 체력이 회복되지 않아 데미지가 누적되므로
        // 결국 반드시 죽는다. 학습용 더미에서는 Health를 아예 제거한다.
        // (ZombieController.DoAttack은 Health가 없으면 그냥 넘어간다)
        var hp = GetComponent<Health>();
        if (hp != null) Destroy(hp);

        agent = GetComponent<NavMeshAgent>();
        agent.angularSpeed = turnSpeed;
        agent.acceleration = acceleration;
        agent.autoBraking  = false;   // 목적지 근처에서 감속하지 않는다 (도망치는 중이므로)
        if (threat == null)
        {
            var z = FindAnyObjectByType<ZombieController>();
            if (z != null) threat = z.transform;
        }
        Randomize();
    }

    /// <summary>Arena가 구역의 좀비들을 알려준다.</summary>
    public void SetThreats(IEnumerable<Transform> list)
    {
        threats.Clear();
        foreach (var t in list) if (t != null) threats.Add(t);
        if (threats.Count > 0) threat = threats[0];
    }

    /// <summary>에피소드 리셋 시 Arena가 호출.</summary>
    public void Randomize()
    {
        hasFleeTarget = false;
        IsFleeing     = false;
        baseSpeed    = Random.Range(minSpeed, maxSpeed);
        agent.speed  = baseSpeed;
        fleeDistance = Random.Range(minFleeDistance, maxFleeDistance);
        repathTimer  = 0f;
        known.Clear();

        if (Academy.IsInitialized)
            limitedSight = Academy.Instance.EnvironmentParameters
                .GetWithDefault("evader_limited_sight", limitedSight ? 1f : 0f) > 0.5f;

        ReadShootingRules();
    }

    /// <summary>
    /// 총 설정을 코드로 바꾼다 (평가 도구의 '게임 총' 옵션). 다음 판부터 적용된다.
    /// 연사 간격·총알 속도는 [최소, 최대] 범위에서 매 판 무작위로 고른다.
    /// </summary>
    public void SetGun(float fireMin, float fireMax, float speedMin, float speedMax, int mag, float reload, float range, float spread)
    {
        fireInterval = fireIntervalSetting = fireMin;
        fireIntervalMax = fireMax;
        bulletSpeed  = bulletSpeedSetting  = speedMin;
        bulletSpeedMax  = speedMax;
        magazineSize = mag;
        reloadTime   = reload;
        fireRange    = range;
        spreadDeg    = spread;
    }

    /// <summary>사격 규칙을 yaml에서 다시 읽고 사격 상태를 초기화한다 (매 판).</summary>
    private void ReadShootingRules()
    {
        if (fireIntervalSetting < 0f) fireIntervalSetting = fireInterval;
        if (bulletSpeedSetting  < 0f) bulletSpeedSetting  = bulletSpeed;
        if (Academy.IsInitialized)
        {
            var ep = Academy.Instance.EnvironmentParameters;
            shootingEnabled   = ep.GetWithDefault("dummy_shoot", shootingEnabled ? 1f : 0f) > 0.5f;
            fireIntervalSetting = ep.GetWithDefault("dummy_fire_interval", fireIntervalSetting);
            bulletDamage      = ep.GetWithDefault("dummy_damage",        bulletDamage);
            magazineSize      = Mathf.RoundToInt(ep.GetWithDefault("dummy_mag", magazineSize));
            reloadTime        = ep.GetWithDefault("dummy_reload",        reloadTime);
            bulletSpeedSetting  = ep.GetWithDefault("dummy_bullet_speed",  bulletSpeedSetting);
            fireRange         = ep.GetWithDefault("dummy_range",         fireRange);
            spreadDeg         = ep.GetWithDefault("dummy_spread",        spreadDeg);
            aimTurnSpeed      = ep.GetWithDefault("dummy_turn_speed",    aimTurnSpeed);
            reactionTime      = ep.GetWithDefault("dummy_reaction",      reactionTime);
            aimLead           = Mathf.Clamp01(ep.GetWithDefault("dummy_aim_lead", aimLead));
            perceptionDelay   = Mathf.Max(0f, ep.GetWithDefault("dummy_perception_delay", perceptionDelay));
            backpedalFactor   = Mathf.Clamp(ep.GetWithDefault("dummy_backpedal", backpedalFactor), 0.3f, 1f);
            shootingFleeScale = ep.GetWithDefault("dummy_flee_scale",    shootingFleeScale);
            fireIntervalMax   = ep.GetWithDefault("dummy_fire_interval_max", fireIntervalMax);
            bulletSpeedMax    = ep.GetWithDefault("dummy_bullet_speed_max",  bulletSpeedMax);
        }

        // 총 종류 무작위화: 이번 판에 쓸 값을 범위 안에서 고른다.
        // (ML-Agents의 uniform 샘플러는 환경 리셋 때만 다시 뽑아서 판마다 바뀌지 않는다 → 여기서 직접 뽑는다)
        fireInterval = fireIntervalMax > fireIntervalSetting ? Random.Range(fireIntervalSetting, fireIntervalMax) : fireIntervalSetting;
        bulletSpeed  = bulletSpeedMax  > bulletSpeedSetting  ? Random.Range(bulletSpeedSetting,  bulletSpeedMax)  : bulletSpeedSetting;

        shootingNow     = shootingEnabled;
        magazineSizeNow = Mathf.Max(1, magazineSize);
        ammo            = magazineSizeNow;
        reloadUntil     = 0f;
        nextFireTime    = 0f;
        aimTarget       = null;
        targetPickTimer = 0f;
        bullets.Clear();
        foreach (var h in seenHistory.Values) h.Clear();
        ShotsFired = 0;
        ShotsHit   = 0;
        fleeScaleNow = shootingNow ? Mathf.Max(0.1f, shootingFleeScale) : 1f;

        // 사격 중에는 시선(=총구)을 직접 돌린다. NavMeshAgent가 이동 방향으로 몸을 돌리면 뒷걸음질이 안 된다.
        if (agent != null) agent.updateRotation = !shootingNow;
        aimYaw = transform.eulerAngles.y;
    }

    public void TeleportTo(Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            if (agent.isOnNavMesh) agent.ResetPath();
        }
    }

    void Update()
    {
        if (agent == null) return;

        // 장애물이 옮겨오며 발밑 NavMesh가 깎이면 isOnNavMesh가 false가 되어
        // 영영 못 움직인다. 가장 가까운 NavMesh 위로 스스로 복귀한다.
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit back, 5f, NavMesh.AllAreas))
                agent.Warp(back.position);
            return;
        }

        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f) return;
        repathTimer = 0.25f;

        UpdateThreatKnowledge();

        // 쏘는 플레이어는 좀비가 더 가까이 와야 물러선다 (멀리 있으면 버티고 쏜다)
        bool fleeing = TryNearestThreat(out Vector3 near) &&
                       Vector3.Distance(transform.position, near) < fleeDistance * fleeScaleNow;

        IsFleeing = fleeing;
        if (!fleeing) hasFleeTarget = false;

        if (fleeing)                                   agent.SetDestination(PickFleePoint(near));
        else if (shootingNow && aimTarget != null)     agent.SetDestination(transform.position);   // 제자리에서 쏜다
        else                                           agent.SetDestination(PickRoamPoint());
    }

    // ── 사격 ──────────────────────────────────────
    // 물리 스텝마다 돈다 —— 학습은 배속(20배)으로 돌아서 프레임 간격이 들쭉날쭉하기 때문.
    void FixedUpdate()
    {
        if (!shootingNow || agent == null) return;
        float dt = Time.fixedDeltaTime;
        float now = Time.time;

        // 0. 반응 지연: 좀비들의 지금 위치·속도를 기록해 둔다 (조준은 delay초 전 기록으로 한다)
        if (perceptionDelay > 0f) RecordSeen(now);

        // 1. 표적 고르기 (0.1초마다): 사거리 안, 벽에 안 가린 살아 있는 좀비 중 가장 가까운 것
        targetPickTimer -= dt;
        if (targetPickTimer <= 0f)
        {
            targetPickTimer = 0.1f;
            Transform best = null;
            float bestSq = fireRange * fireRange;
            Vector3 me = transform.position;
            foreach (var t in threats)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                float sq = (t.position - me).sqrMagnitude;
                if (sq > bestSq) continue;
                if (!CanSee(t)) continue;
                bestSq = sq;
                best = t;
            }
            if (best != aimTarget)
            {
                aimTarget = best;
                aimAcquiredTime = now;   // 새 표적 → 반응 시간
            }
        }
        if (aimTarget != null && !aimTarget.gameObject.activeInHierarchy) aimTarget = null;

        // 2. 시선 돌리기: 표적이 있으면 (앞을 내다본) 표적 쪽, 없으면 가는 쪽
        float desiredYaw;
        if (aimTarget != null)
        {
            Vector3 to = AimPoint(aimTarget) - transform.position;
            to.y = 0f;
            desiredYaw = to.sqrMagnitude > 0.0001f ? Yaw(to) : aimYaw;
        }
        else
        {
            Vector3 v = agent.velocity;
            v.y = 0f;
            desiredYaw = v.sqrMagnitude > 0.25f ? Yaw(v) : aimYaw;
        }
        aimYaw = Mathf.MoveTowardsAngle(aimYaw, desiredYaw, aimTurnSpeed * dt);
        transform.rotation = Quaternion.Euler(0f, aimYaw, 0f);

        // 3. 뒷걸음질: 바라보는 반대쪽으로 움직이면 느려진다
        Vector3 vel = agent.desiredVelocity;
        vel.y = 0f;
        float back = vel.sqrMagnitude > 0.01f ? Mathf.Clamp01(-Vector3.Dot(AimForward, vel.normalized)) : 0f;
        agent.speed = baseSpeed * Mathf.Lerp(1f, backpedalFactor, back);

        // 4. 재장전: 탄이 떨어졌거나, 쏠 표적이 없는데 반 이하로 남았으면
        if (now >= reloadUntil && ammo <= 0) { ammo = magazineSizeNow; }
        if (now >= reloadUntil && aimTarget == null && ammo < magazineSizeNow / 2)
        {
            reloadUntil = now + reloadTime;
            ammo = 0;   // 재장전이 끝나면 위에서 채운다
        }

        // 5. 발사: 반응 시간이 지났고, 조준이 표적 쪽이고, 탄이 있으면
        if (aimTarget != null && ammo > 0 && now >= reloadUntil && now >= nextFireTime &&
            now - aimAcquiredTime >= reactionTime &&
            Mathf.Abs(Mathf.DeltaAngle(aimYaw, desiredYaw)) <= fireCone)
        {
            float spread = Random.Range(-spreadDeg, spreadDeg);
            bullets.Add(new Bullet
            {
                pos = transform.position + Vector3.up * 1.2f + AimForward * 0.5f,
                dir = Dir(aimYaw + spread),
                traveled = 0f,
            });
            ShotsFired++;
            ammo--;
            nextFireTime = now + fireInterval;
            if (ammo <= 0) reloadUntil = now + reloadTime;
        }

        // 6. 총알 진행: 벽에 막히거나, 좀비 몸(반지름 zombieHitRadius)을 지나가면 맞는다
        StepBullets(dt);
    }

    /// <summary>앞을 내다본 조준점: 지금 위치 + 속도 × (총알 도착 시간) × aimLead.</summary>
    private Vector3 AimPoint(Transform t)
    {
        if (!threatNavs.TryGetValue(t, out NavMeshAgent nav))
        {
            nav = t.GetComponent<NavMeshAgent>();
            threatNavs[t] = nav;
        }
        Vector3 p = t.position;
        Vector3 v = nav != null ? nav.velocity : Vector3.zero;
        v.y = 0f;

        // 반응 지연: delay초 전에 본 위치·속도로 '지금 위치'를 짐작한다 (그 뒤로 똑바로 왔다고 가정)
        if (perceptionDelay > 0f && TryGetSeen(t, Time.time - perceptionDelay, out Seen s))
        {
            p = s.p + s.v * perceptionDelay;
            v = s.v;
        }

        if (aimLead > 0f && bulletSpeed > 0.1f)
        {
            float time = Vector3.Distance(transform.position, p) / bulletSpeed;
            p += v * time * aimLead;
        }
        return p;
    }

    private void RecordSeen(float now)
    {
        float keep = perceptionDelay + 0.2f;
        foreach (var t in threats)
        {
            if (t == null) continue;
            if (!seenHistory.TryGetValue(t, out var list)) seenHistory[t] = list = new List<Seen>();
            if (!threatNavs.TryGetValue(t, out NavMeshAgent nav)) { nav = t.GetComponent<NavMeshAgent>(); threatNavs[t] = nav; }
            Vector3 v = nav != null ? nav.velocity : Vector3.zero;
            v.y = 0f;
            list.Add(new Seen { t = now, p = t.position, v = v });
            int old = 0;
            while (old < list.Count && list[old].t < now - keep) old++;
            if (old > 0) list.RemoveRange(0, old);
        }
    }

    /// <summary>시각 when 이전의 가장 최근 기록. 기록이 그만큼 오래되지 않았으면(판 시작 직후) 가장 오래된 것.</summary>
    private bool TryGetSeen(Transform t, float when, out Seen s)
    {
        s = default;
        if (!seenHistory.TryGetValue(t, out var list) || list.Count == 0) return false;
        s = list[0];
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].t <= when) { s = list[i]; break; }
        return true;
    }

    private void StepBullets(float dt)
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            float step = bulletSpeed * dt;
            if (b.traveled + step > fireRange) step = fireRange - b.traveled;

            // 벽: 좀비와 더미 자신은 무시하고 가장 가까운 충돌
            float wall = step;
            int n = Physics.RaycastNonAlloc(b.pos, b.dir, bulletBuffer, step, ~0, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Transform h = bulletBuffer[k].transform;
                if (h == null || h.IsChildOf(transform)) continue;
                if (h.GetComponentInParent<ZombieController>() != null) continue;
                if (bulletBuffer[k].distance < wall) wall = bulletBuffer[k].distance;
            }

            // 좀비: 이번 구간(b.pos → b.pos + dir*wall)에 몸이 걸리는 가장 가까운 좀비
            Transform hit = null;
            float hitAlong = float.MaxValue;
            float r2 = zombieHitRadius * zombieHitRadius;
            foreach (var t in threats)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                Vector3 rel = t.position - b.pos;
                rel.y = 0f;
                Vector3 d = b.dir; d.y = 0f;
                float along = Vector3.Dot(rel, d);
                if (along < 0f || along > wall) continue;
                Vector3 perp = rel - d * along;
                if (perp.sqrMagnitude > r2) continue;
                if (along < hitAlong) { hitAlong = along; hit = t; }
            }

            if (drawBullets)
            {
                float len = hit != null ? hitAlong : wall;
                Debug.DrawLine(b.pos, b.pos + b.dir * len, hit != null ? Color.red : new Color(1f, 0.9f, 0.3f), 0.1f);
            }

            if (hit != null)
            {
                ShotsHit++;
                ShotHit?.Invoke(hit, bulletDamage);
                bullets.RemoveAt(i);
                continue;
            }

            b.pos += b.dir * wall;
            b.traveled += wall;
            if (wall < step - 0.001f || b.traveled >= fireRange - 0.001f) { bullets.RemoveAt(i); continue; }
            bullets[i] = b;
        }
    }

    private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    private static Vector3 Dir(float yawDeg)
    {
        float r = yawDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

    // ── 좀비 인식 ─────────────────────────────────
    /// <summary>
    /// 이번 판단에 쓸 좀비 위치 목록(threatPositions)을 만든다.
    /// 시야 제한이면 보이거나 들리는 좀비만 갱신하고, 놓친 좀비는 memorySeconds 동안 마지막 위치를 쓴다.
    /// </summary>
    private void UpdateThreatKnowledge()
    {
        threatPositions.Clear();

        // 죽어서 빠진 좀비(비활성)는 위협이 아니다
        if (!limitedSight)
        {
            if (threats.Count > 0) { foreach (var t in threats) if (t != null && t.gameObject.activeInHierarchy) threatPositions.Add(t.position); }
            else if (threat != null && threat.gameObject.activeInHierarchy) threatPositions.Add(threat.position);
            return;
        }

        float now = Time.time;
        Vector3 me = transform.position;
        foreach (var t in threats)
        {
            if (t == null) continue;
            if (!t.gameObject.activeInHierarchy) { known.Remove(t); continue; }
            float d = Vector3.Distance(me, t.position);
            bool senses = d <= hearRadius || (d <= sightRange && CanSee(t));
            if (senses) known[t] = new Known { pos = t.position, time = now };
        }

        forgetBuffer.Clear();
        foreach (var kv in known)
        {
            if (now - kv.Value.time > memorySeconds) forgetBuffer.Add(kv.Key);
            else threatPositions.Add(kv.Value.pos);
        }
        foreach (var t in forgetBuffer) known.Remove(t);
    }

    /// <summary>더미 눈높이에서 좀비까지 벽에 가리지 않는지 (좀비 몸과 더미 자신은 무시).</summary>
    private bool CanSee(Transform t)
    {
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 aim = t.position + Vector3.up * 1.0f;
        Vector3 dir = aim - eye;
        float len = dir.magnitude;
        if (len < 0.01f) return true;

        int n = Physics.RaycastNonAlloc(eye, dir / len, losBuffer, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform h = losBuffer[i].transform;
            if (h == null || h.IsChildOf(transform)) continue;
            if (h.GetComponentInParent<ZombieController>() != null) continue;
            return false;
        }
        return true;
    }

    private bool TryNearestThreat(out Vector3 nearest)
    {
        nearest = Vector3.zero;
        float bestSq = float.MaxValue;
        foreach (var p in threatPositions)
        {
            float sq = (p - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; nearest = p; }
        }
        return bestSq < float.MaxValue;
    }

    // ── 도주 ──────────────────────────────────────
    private Vector3 PickFleePoint(Vector3 nearest)
    {
        // 난이도에 따라 가끔은 단순하게 도망친다 (실험 변수)
        if (evasionQuality < 1f && Random.value > evasionQuality)
            return NaiveFleePoint(nearest);

        Vector3 origin = transform.position;
        float   bestScore = float.MinValue;
        Vector3 bestPoint = origin;
        bool    found = false;

        float angleOffset = Random.Range(0f, 360f / fleeSamples);   // 매번 살짝 틀어 다양성 확보

        for (int i = 0; i < fleeSamples; i++)
        {
            float angle = angleOffset + (360f / fleeSamples) * i;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

            // NavMesh.Raycast는 "이 방향으로 가다가 어디서 길이 끊기는가"를 알려준다.
            // 이게 막다른 길 판별의 핵심 —— 벽 쪽은 reach가 짧게 나온다.
            Vector3 wanted = origin + dir * fleeLookAhead;
            Vector3 reach  = NavMesh.Raycast(origin, wanted, out NavMeshHit block, NavMesh.AllAreas)
                ? block.position
                : wanted;

            float openness = Vector3.Distance(origin, reach);
            if (openness < 2f) continue;    // 사실상 벽

            float score = ScoreFleePoint(origin, reach);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = reach;
                found     = true;
            }
        }

        if (!found) return NaiveFleePoint(nearest);

        // ── 방향 고수 ──
        // 지금 가던 방향이 아직 쓸 만하면 웬만해선 바꾸지 않는다. 사람도 한 번 뛰기 시작한
        // 방향으로 어느 정도는 달린다. 단, 좀비가 코앞이면(panicDistance) 즉시 다시 판단한다.
        bool panic = Vector3.Distance(origin, nearest) < panicDistance;
        if (hasFleeTarget && !panic && Vector3.Distance(origin, fleeTarget) > 2f)
        {
            float current = ScoreFleePoint(origin, fleeTarget);
            bool committed  = Time.time < fleeCommitUntil;
            bool notBetter  = bestScore < current * (1f + switchMargin);
            if (committed || notBetter) return fleeTarget;
        }

        hasFleeTarget   = true;
        fleeTarget      = bestPoint;
        fleeCommitUntil = Time.time + minCommitTime;
        return bestPoint;
    }

    /// <summary>
    /// 도주 지점의 점수 = 아는 좀비 전부와의 최소 거리 + 그쪽으로 열린 정도.
    /// 한 마리만 보면 다른 좀비 품으로 뛰어들게 된다 —— 포위가 통하는 이유.
    /// (시야 제한이면 '아는' 좀비만 피한다 → 안 보이게 돌아온 좀비 쪽으로 뛰어들 수 있다)
    /// </summary>
    private float ScoreFleePoint(Vector3 origin, Vector3 point)
    {
        float minThreatDist = float.MaxValue;
        foreach (var p in threatPositions)
            minThreatDist = Mathf.Min(minThreatDist, Vector3.Distance(point, p));
        if (minThreatDist == float.MaxValue) minThreatDist = 0f;

        return minThreatDist + Vector3.Distance(origin, point) * opennessWeight;
    }

    /// <summary>단순히 반대 방향. 구석에 잘 몰린다 —— 난이도 낮춤용.</summary>
    private Vector3 NaiveFleePoint(Vector3 from)
    {
        Vector3 away = (transform.position - from).normalized;
        if (away.sqrMagnitude < 1e-4f) return PickRoamPoint();
        away = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * away;

        Vector3 candidate = transform.position + away * 10f;
        return NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas)
            ? hit.position
            : transform.position;
    }

    // ── 배회 ──────────────────────────────────────
    private Vector3 PickRoamPoint()
    {
        if (agent.hasPath && agent.remainingDistance > 1.5f) return agent.destination;

        for (int i = 0; i < 6; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * roamRadius;
            Vector3 candidate = transform.position + new Vector3(rnd.x, 0f, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                return hit.position;
        }
        return transform.position;
    }
}

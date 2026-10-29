using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 학습 구역 하나. 좀비 여러 마리 + 더미 플레이어 + 벽으로 구성한다.
///
/// 좀비들은 SimpleMultiAgentGroup으로 묶여 그룹 보상을 공유한다 (MA-POCA).
/// 누가 잡든 팀 전체가 보상을 받으므로, 혼자 최단거리로 달려드는 것보다
/// 역할을 나누는 쪽이 이득이 된다 —— 이게 경로탐색으로는 나올 수 없는 행동이다.
///
/// 에피소드 종료는 여기서 그룹 단위로 처리한다. 개별 Agent의 MaxStep은 0으로 둔다.
///
/// 배치 구조:
///   Arena (이 스크립트)
///    ├─ Ground / Walls / Blocks
///    ├─ Zombie_0 / Zombie_1 / Zombie_2
///    └─ DummyPlayer (Tag = Player)
/// </summary>
public class ZombieTrainingArena : MonoBehaviour
{
    [Header("구성원")]
    public List<ZombieController> zombies = new List<ZombieController>();
    public DummyPlayer            dummy;

    [Header("배치")]
    [Tooltip("구역 중심에서 스폰 위치를 뽑을 반경")]
    public float spawnRadius = 17f;
    [Tooltip("좀비와 플레이어의 최소 시작 거리")]
    public float minSeparation = 12f;
    [Tooltip("좀비끼리의 최소 시작 거리. 뭉쳐서 시작하면 포위를 배우기 어렵다")]
    public float zombieSeparation = 6f;

    [Header("에피소드")]
    [Tooltip("두 캡슐 중심 사이 거리가 이 안이면 포획. 몸 반지름(0.5+0.5)을 빼면 실제 틈은 0.2m.\n" +
             "NavMeshAgent끼리 서로 밀어내는 최소 거리(약 0.9)보다 작으면 영원히 못 잡는다.")]
    public float captureRadius = 1.2f;
    [Tooltip("켜면 포획 순간 실제 중심 거리를 Console에 찍는다 (디버그용, 학습 빌드에서는 끌 것)")]
    public bool logCaptures = false;
    [Tooltip("에피소드 최대 길이(결정 스텝). 이 안에 못 잡으면 무승부로 종료")]
    public int maxSteps = 3000;
    [Tooltip("에피소드 시작 직후 포획 판정을 건너뛸 스텝 수 (즉시 포획 폭주 방지)")]
    public int captureGraceSteps = 10;
    [Tooltip("포획 성공 시 팀 전체가 받는 보상")]
    public float captureReward = 1f;

    [Header("도망자 속도 (커리큘럼)")]
    [Tooltip("yaml의 environment_parameters/evader_speed가 없을 때 쓰는 기본값.\n" +
             "좀비 속도(3.5)와 같으면 직진 추격으로도 잡힌다 —— 포위가 필요 없는 환경.\n" +
             "도망자가 더 빨라야만 퇴로를 막는 협동이 유일한 해법이 된다.")]
    public float defaultEvaderSpeed = 3.5f;
    [Tooltip("에피소드마다 이 범위 안에서 속도를 흔든다 (중심 ± 이 값)")]
    public float evaderSpeedJitter = 0.3f;

    [Header("적응형 난이도")]
    [Tooltip("켜면 yaml의 evader_speed를 무시하고, 구역마다 최근 성적을 보고 더미 속도를 스스로 조절한다.\n" +
             "진행도(%)로 올리는 커리큘럼은 못 잡고 있어도 무조건 속도를 올려서, 학습이 무너질 수 있다.")]
    public bool  adaptiveSpeed = true;
    [Tooltip("시작 속도. 열린 구역에서는 좀비(실제 약 3.0m/s)와의 속도 차가 작으면\n" +
             "거리가 거의 안 좁혀져서, 2.6에서도 시간 안에 따라잡지 못했다.")]
    public float adaptiveStart = 2.4f;
    public float adaptiveMin   = 1.8f;
    public float adaptiveMax   = 4.5f;
    public float adaptiveStep  = 0.1f;
    [Tooltip("최근 몇 판의 성적을 볼지.\n" +
             "20판이면 에피소드가 긴 열린 구역에서 0.1 바꾸는 데 100만 스텝 넘게 걸렸다.")]
    public int   adaptiveWindow = 10;
    [Tooltip("최근 성공률이 이 이상이면 더미를 빠르게")]
    public float raiseAbove = 0.6f;
    [Tooltip("최근 성공률이 이 미만이면 더미를 느리게")]
    public float lowerBelow = 0.3f;
    [Tooltip("시간 압박. maxSteps에 걸쳐 총 -1이 되도록 분배된다")]
    public bool useTimePenalty = true;

    [Tooltip("팀 중 '가장 가까운 좀비'가 타겟에 가까워진 거리 1m당 그룹 보상.\n" +
             "개별이 아니라 팀 최소거리를 쓰는 게 핵심 —— 개별로 주면 셋 다 직진하는 게\n" +
             "최적해가 되어 포위를 배우지 않는다. 최소거리만 보면 한 마리가 압박하는 동안\n" +
             "나머지는 벌점 없이 길목을 돌 수 있다.")]
    public float approachGroupReward = 0.01f;

    [Header("포위 보상 (잠재함수 기반)")]
    [Tooltip("포위 점수가 0→1로 오를 때 팀이 받는 총 보상. 0이면 꺼짐.\n" +
             "yaml의 environment_parameters/encircle_reward가 있으면 그 값을 쓴다 (빌드 없이 조절).\n\n" +
             "잠재함수 기반(potential-based) 보상이라 매 스텝 '점수의 변화량'만 준다:\n" +
             "포위를 넓히면 +, 풀어지면 −. 에피소드 전체 합은 (마지막 점수 − 처음 점수)로\n" +
             "상쇄되므로, 이 보상만 노리고 시간을 끄는 꼼수가 생기지 않는다.\n" +
             "이론적으로 최적 정책을 바꾸지 않고 탐색만 안내한다 (Ng et al., 1999).")]
    public float encircleReward = 0f;
    [Tooltip("더미와 이 거리 안의 좀비만 포위에 참여한 것으로 친다.\n" +
             "멀리서 방향만 맞춘 좀비가 점수를 받지 않도록.")]
    public float encircleRadius = 10f;

    [Header("앞 막기 보상 (잠재함수 기반)")]
    [Tooltip("더미가 달리는 방향 앞쪽을 막는 정도가 0→1로 오를 때 팀이 받는 총 보상. 0이면 꺼짐.\n" +
             "yaml의 environment_parameters/cutoff_reward가 있으면 그 값.\n" +
             "뒤에서만 쫓는 습관을 깨려고 넣었다 —— 처치하려면 붙어서 여러 번 때려야 하는데,\n" +
             "방향을 홱 꺾는 상대는 뒤에서 쫓아서는 붙어 있을 수 없다.")]
    public float cutoffReward = 0f;
    [Tooltip("더미에서 이 거리 안의 좀비만 앞 막기에 참여한 것으로 친다")]
    public float cutoffRadius = 10f;

    [Header("지형 (지형 센서 실험)")]
    [Tooltip("켜면 포위 점수에서 벽도 퇴로를 막는 것으로 친다.\n" +
             "더미를 벽이나 막다른 골목에 몰아붙이면 그쪽은 좀비가 없어도 막힌 것으로 계산된다 →\n" +
             "지형을 이용한 몰이에 보상이 간다. 끄면 기존과 같이 좀비 배치만 본다.")]
    public bool  terrainAwareEncircle = false;
    [Tooltip("더미에서 이 거리(m) 안에 벽이 있는 방향은 막힌 것으로 본다")]
    public float wallBlockDistance = 3f;

    [Header("처치 모드")]
    [Tooltip("켜면 닿는 것만으로는 끝나지 않는다. 좀비의 실제 공격으로 더미 체력을 0으로 만들어야 성공.\n" +
             "게임 목표(플레이어 처치)와 같게 맞춘 것 —— 한 번 스치는 게 아니라 가둬두고 계속 때리는 법을 배운다.\n" +
             "끄면 기존처럼 captureRadius 안에 닿는 순간 성공.")]
    public bool  killMode = false;
    [Tooltip("더미의 가상 체력 (게임 플레이어 기본 체력 100). yaml의 environment_parameters/dummy_health가 있으면 그 값")]
    public float dummyMaxHealth = 100f;
    [Tooltip("체력을 전부 깎았을 때 팀이 받는 보상 합계 (처치 보상 captureReward와 별도)")]
    public float damageRewardTotal = 0.5f;
    [Tooltip("섞인 무리일 때 역할별 공격력 (게임 설계값: 일반 10 / 달리기 15 / 탱커 40).\n" +
             "섞인 무리가 아니면 각 좀비의 attackDamage를 그대로 쓴다.")]
    public float normalDamage = 10f;
    public float runnerDamage = 15f;
    public float tankDamage   = 40f;

    public enum PackRole { Normal, Runner, Tank }

    [Header("섞인 무리 (역할 분담 실험)")]
    [Tooltip("켜면 매 에피소드 좀비들에게 일반/달리기/탱커 역할(=최고 속도)을 무작위로 나눠준다.\n" +
             "역할 센서(ZombieRoleSensor)가 붙어 있어야 좀비가 서로의 속도를 안다.")]
    public bool  mixedPack = false;
    public float normalSpeed = 3.5f;
    public float runnerSpeed = 4.5f;
    public float tankSpeed   = 2.5f;
    [Tooltip("한 판에 달리기 좀비 최대 수 (0~이 값 중 무작위)")]
    public int   maxRunners = 2;
    [Tooltip("한 판에 탱커 최대 수 (0~이 값 중 무작위)")]
    public int   maxTanks   = 1;
    [Tooltip("역할별 색 (학습 화면에서 구분용, 비워도 됨)")]
    public Material normalMaterial, runnerMaterial, tankMaterial;

    [Header("학습 보조")]
    [Tooltip("좀비 한 마리는 더미가 보이는 자리에서 시작하게 한다.\n" +
             "셋 다 못 보고 시작하면 포획이 거의 안 일어나 그룹 보상이 발생하지 않고,\n" +
             "학습이 몇 시간 동안 제자리걸음을 한다. 수렴한 뒤 끄고 재학습하면\n" +
             "순수 수색 능력까지 평가할 수 있다.")]
    public bool guaranteeInitialSighting = true;

    [Header("무작위화 (정책 다양성 확보)")]
    public bool randomizeSpawns = true;
    [Tooltip("회전시킬 장애물들")]
    public Transform[] movableObstacles;
    public float obstacleJitter = 2.5f;

    public enum ObstacleMode
    {
        Jitter,   // 원래 자리에서 조금씩만 흔든다 (사각 구역)
        Scatter,  // 매 에피소드 개수와 위치를 새로 뽑는다 (열린 구역)
    }

    [Header("장애물 배치")]
    [Tooltip("Scatter: 매 에피소드 장애물 개수와 위치를 새로 뽑아 지형을 외우지 못하게 한다")]
    public ObstacleMode obstacleMode = ObstacleMode.Jitter;
    [Tooltip("Scatter: 장애물 중심을 놓을 최소 반경")]
    public float scatterMinRadius = 0f;
    [Tooltip("Scatter: 장애물의 어느 부분도 이 반경을 넘지 않는다.\n" +
             "외곽 벽과의 사이에 통로를 남겨, 벽에 붙어 생기는 막다른 곳을 없앤다.")]
    public float scatterMaxRadius = 29f;
    [Tooltip("Scatter: 장애물 가장자리끼리의 최소 간격(m).\n" +
             "이보다 좁으면 두 벽 사이에 더미를 몰아넣을 틈이 생긴다 → 포위 대신 벽 몰이를 배운다.")]
    public float scatterMinSpacing = 5f;
    [Tooltip("Scatter: 최소 몇 개는 켜둔다 (나머지는 에피소드마다 무작위로 켜고 끈다)")]
    public int scatterMinActive = 2;
    [Tooltip("Scatter: 배열 앞쪽 N개는 매 에피소드 반드시 켜고 가장 먼저 배치한다.\n" +
             "지형 씬의 'ㄷ'자 골목처럼 큰 장애물이 자리가 모자라 빠지지 않게 하려는 것.")]
    public int scatterPinnedFirst = 0;

    // ─────────────────────────────────────────────
    /// <summary>이 구역 좀비 팀이 공유하는 타겟 정보.</summary>
    public ZombieTeamIntel Intel { get; } = new ZombieTeamIntel();

    // ── 평가(ZombieEvaluator) 연결 고리 ─────────────────
    /// <summary>한 에피소드의 결과 요약. 평가 도구가 CSV로 기록한다.</summary>
    public struct EpisodeReport
    {
        public bool  captured;
        public int   steps;
        public float seconds;
        public float evaderSpeedSetting;  // 더미 최고 속도 설정값
        public float escapeGapDeg;        // 포획 순간 최대 빈틈 (못 잡았으면 NaN)
        public float encircleMean;        // 평균 포위 점수 (근처에 아무도 없었으면 NaN)
        public float evaderMeanSpeed;     // 더미 실제 평균 속도 (에피소드 전체)
        public float evaderFleeSpeed;     // 더미 실제 평균 속도 (도망치는 동안만, 없으면 NaN)
        public float zombieMeanSpeed;     // 좀비 실제 평균 속도
        public float predictionError;     // 1초 뒤 위치 예측 오차(m). 클수록 예측하기 어려운 움직임
        public int   shotsFired;          // 더미가 쏜 총알 수 (사격이 꺼져 있으면 -1)
        public int   shotsHit;            // 그중 좀비에 맞은 수 (사격이 꺼져 있으면 -1)
        // 회피 측정 (사격 중, 더미와 2.5m~사거리 사이에서 움직이는 좀비만):
        //   비스듬함 = 좀비 이동 방향과 '더미 쪽 직선' 사이 각도(도). 0 = 똑바로 돌진, 90 = 옆으로 감
        //   조준당함 = 더미가 지금 그 좀비를 표적으로 잡고 있음
        public float obliqueAimedDeg;     // 조준당할 때 평균 비스듬함 (표본 없으면 NaN)
        public float obliqueFreeDeg;      // 조준 안 당할 때 평균 비스듬함
        public float steerAimedDeg;       // RL 옆 보정으로 튼 각도의 절댓값 평균 —— 조준당할 때 (RL 아니면 NaN)
        public float steerFreeDeg;        //   〃 조준 안 당할 때
        public float aimedFraction;       // 측정 표본 중 조준당한 비율
    }

    /// <summary>에피소드가 끝날 때마다 호출된다 (중단된 에피소드는 제외).</summary>
    [System.NonSerialized] public System.Action<ZombieTrainingArena, EpisodeReport> EpisodeFinished;
    /// <summary>리셋 직전에 호출된다. 평가 도구가 여기서 다음 판의 속도와 난수 시드를 정한다.</summary>
    [System.NonSerialized] public System.Action<ZombieTrainingArena> BeforeReset;

    private bool abortRequested;
    /// <summary>진행 중인 에피소드를 기록 없이 끝내고 새로 시작하게 한다 (다음 에이전트 스텝에서).</summary>
    public void RequestAbort() => abortRequested = true;

    /// <summary>예측 오차를 잴 간격과 내다보는 거리 (물리 스텝 단위: 25 = 0.5초, 50 = 1초)</summary>
    private const int PredictEvery   = 25;
    private const int PredictHorizon = 50;
    private struct PendingPrediction { public int due; public Vector3 pos; }
    private readonly List<Queue<PendingPrediction>> predictions = new List<Queue<PendingPrediction>>();
    private float predErrSum;
    private int   predErrCount;

    private Vector3 prevDummyPos;
    private bool    hasPrevDummyPos;
    private float   dummyDistance;
    private float   dummyFleeDistance;
    private int     dummyFleeSteps;

    private SimpleMultiAgentGroup   group;
    private readonly List<ZombieAgent> agents = new List<ZombieAgent>();
    private Vector3[] obstacleHomes;
    private Collider[] obstacleColliders;
    private int       stepCount;
    private bool      episodeEnding;
    private float     prevMinDistance = -1f;
    private float     currentEvaderSpeed;

    // 진단 지표 누적 (에피소드 단위)
    private readonly List<NavMeshAgent> zombieNavs = new List<NavMeshAgent>();
    private int   sightSteps;      // 좀비 중 누구라도 더미를 보고 있던 스텝 수
    private float speedSum;        // 좀비 이동 속도 합
    private int   speedSamples;
    // 회피 측정 (BuildReport의 oblique*/steer* 참고)
    private float obqAimSum, obqFreeSum, steerAimSum, steerFreeSum;
    private int   obqAimN, obqFreeN, steerAimN, steerFreeN;
    private ZombieMLBrain[] zombieMl;
    private float encircleWeightNow;
    private float prevEncircle = -1f;
    private int   prevAliveForEncircle = -1;
    private float encircleSum;
    private int   encircleSamples;
    private float     adaptiveSpeedNow = -1f;
    private readonly Queue<bool> recentResults = new Queue<bool>();
    private float[]   bearingBuffer = new float[8];

    // 리셋마다 새로 할당하지 않도록 재사용
    private readonly RaycastHit[]  sightBuffer  = new RaycastHit[8];
    private readonly List<Vector3> placedBuffer = new List<Vector3>();

    void Awake()
    {
        // 구역 구성원 수집
        agents.Clear();
        foreach (var z in zombies)
        {
            if (z == null) continue;
            var a = z.GetComponent<ZombieAgent>();
            if (a != null) { agents.Add(a); a.arena = this; a.intel = Intel; }

            // 구역끼리 섞이지 않게 자기 더미만 보도록
            if (dummy != null) z.targetOverride = dummy.transform;
        }

        foreach (var a in agents) a.SetTeammates(agents);

        zombieNavs.Clear();
        foreach (var z in zombies)
            if (z != null) zombieNavs.Add(z.GetComponent<NavMeshAgent>());

        predictions.Clear();
        foreach (var _ in zombieNavs) predictions.Add(new Queue<PendingPrediction>());

        if (dummy != null)
        {
            var threats = new List<Transform>();
            foreach (var z in zombies) if (z != null) threats.Add(z.transform);
            dummy.SetThreats(threats);
        }

        // 그룹 등록 —— 이게 MA-POCA가 팀으로 묶어 학습하는 근거
        group = new SimpleMultiAgentGroup();
        foreach (var a in agents) group.RegisterAgent(a);

        // 처치 모드: 좀비의 실제 공격(사거리·쿨다운 그대로)으로 더미 체력을 깎는다
        foreach (var z in zombies)
            if (z != null) z.AttackLanded += OnZombieAttack;

        if (movableObstacles != null)
        {
            obstacleHomes     = new Vector3[movableObstacles.Length];
            obstacleColliders = new Collider[movableObstacles.Length];
            for (int i = 0; i < movableObstacles.Length; i++)
            {
                if (movableObstacles[i] == null) continue;
                obstacleHomes[i]     = movableObstacles[i].position;
                obstacleColliders[i] = movableObstacles[i].GetComponent<Collider>();
            }
        }
    }

    void OnEnable()
    {
        // 관측 수집 직전에 팀 정보를 갱신한다. 에이전트마다 따로 갱신하면
        // 먼저 관측한 좀비와 나중 좀비가 서로 다른 정보를 보게 된다.
        Academy.Instance.AgentPreStep += OnAgentPreStep;
    }

    void OnDisable()
    {
        if (Academy.IsInitialized) Academy.Instance.AgentPreStep -= OnAgentPreStep;
    }

    private void OnAgentPreStep(int academyStep)
    {
        if (dummy == null) return;

        bool anySees = false;
        foreach (var a in agents)
            if (a != null && a.SeesTarget) { anySees = true; break; }

        // 누군가 실제로 보고 있을 때만 위치가 들어간다 (벽 투시 아님)
        Vector3 target = dummy.transform.position;
        Intel.Tick(anySees, target);

        // 조율기 + 강화학습: 관측 직전에 자리를 새로 계산한다 (모든 좀비가 같은 계산 결과를 보게)
        if (slotTactics != null && slotTickEvery > 0 && academyStep % slotTickEvery == 0)
            slotTactics.TickNow();

        // 공유 범위 제한: 각자 '근처(intelRadiusNow 안)의 목격자'에게서만 정보를 받는다 (자기 자신 포함)
        if (intelRadiusNow > 0f && agentIntel != null)
        {
            float r2 = intelRadiusNow * intelRadiusNow;
            for (int i = 0; i < agents.Count; i++)
            {
                var me = agents[i];
                if (me == null) continue;
                bool heard = false;
                foreach (var s in agents)
                {
                    if (s == null || !s.SeesTarget) continue;
                    if ((s.transform.position - me.transform.position).sqrMagnitude <= r2) { heard = true; break; }
                }
                agentIntel[i].Tick(heard, target);
            }
        }
    }

    // ── 정보 공유 범위 ────────────────────────────────
    [Header("정보 공유 범위")]
    [Tooltip("0이면 한 마리가 보면 구역 전체가 안다 (기존). 0보다 크면 목격자에게서 이 거리(m) 안의 좀비만 안다.\n" +
             "yaml의 environment_parameters/intel_share_radius가 있으면 그 값. 게임(ZombieSquad)은 35m.\n" +
             "다 같이 알면 다 같이 최단 경로로 와서 한 줄이 되기 쉽다는 가설을 시험하려는 것.")]
    public float intelShareRadius = 0f;
    private float intelRadiusNow;
    private ZombieTeamIntel[] agentIntel;

    private void ApplyIntelSharingMode()
    {
        intelRadiusNow = Academy.Instance.EnvironmentParameters.GetWithDefault("intel_share_radius", intelShareRadius);

        if (agentIntel == null || agentIntel.Length != agents.Count)
        {
            agentIntel = new ZombieTeamIntel[agents.Count];
            for (int i = 0; i < agentIntel.Length; i++) agentIntel[i] = new ZombieTeamIntel();
        }
        for (int i = 0; i < agents.Count; i++)
        {
            agentIntel[i].Reset();
            if (agents[i] != null) agents[i].intel = intelRadiusNow > 0f ? agentIntel[i] : Intel;
        }
    }

    void OnDestroy()
    {
        foreach (var z in zombies)
            if (z != null) z.AttackLanded -= OnZombieAttack;
        if (dummy != null) dummy.ShotHit -= OnDummyShot;
    }

    // ── 반격하는 더미 (사격) ─────────────────────────
    [Header("반격하는 더미 (사격)")]
    [Tooltip("좀비 가상 체력. 더미의 총알(DummyPlayer.ShotHit)이 깎는다. 0이 되면 그 판에서 빠진다 (다음 판에 돌아옴).\n" +
             "yaml: zombie_hp_normal / zombie_hp_runner / zombie_hp_tank. 섞인 무리가 아니면 일반 체력을 쓴다.")]
    public float normalHealth = 50f;
    public float runnerHealth = 35f;
    public float tankHealth   = 150f;
    [Tooltip("좀비 한 마리가 죽을 때 팀이 받는 벌점 (yaml: zombie_death_penalty)")]
    public float deathPenalty = 0.2f;
    [Tooltip("총알에 맞은 좀비 '본인'이 받는 개별 벌점 (yaml: zombie_hit_penalty). 0이면 꺼짐.\n" +
             "팀 사망 벌점만으로는 누가 피했고 누가 못 피했는지가 신호에 안 드러나서 피하기를 배우지 못했다.\n" +
             "맞을 때만 생기는 벌점이라 '판을 오래 끌면 쌓이는' 꼼수는 생기지 않는다.")]
    public float hitPenalty = 0f;
    [Tooltip("총알에 맞은 좀비가 멈칫하는 시간(초) (yaml: zombie_hit_stun). 0이면 꺼짐.\n" +
             "게임에서도 맞을 때마다 멈칫한다. 벌점만 있으면 총을 무시하고 돌진해도 손해가 작은데,\n" +
             "멈칫하면 포위망에 늦게 도착해 그쪽 빈틈이 열린다 → 안 맞고 다가가는 게 실제로 이득이 된다.\n" +
             "체력을 아주 크게(사실상 무한) 주고 이것과 맞은 벌점으로만 학습하면, 판 내내 다섯 마리가 유지된다.")]
    public float hitStun = 0f;

    [Tooltip("총구 선 벌점 (초당) (yaml: zombie_aim_penalty). 0 = 꺼짐.\n" +
             "더미 총구에서 뻗은 직선에 가까이 있는 동안(aimLineWidth 안) 조금씩 깎는다. 선 한가운데일수록 크다.\n" +
             "맞은 벌점은 맞은 뒤에만 오지만 이건 '맞기 전에' 신호를 줘서, 정면을 비켜 가는 법을 배우게 한다.\n" +
             "더미에 반응 지연(dummy_perception_delay)을 같이 켜야 비키는 게 실제로 통한다.")]
    public float aimPenalty = 0f;
    [Tooltip("총구 선의 폭(m, 한쪽) (yaml: zombie_aim_width). 좀비 몸 반지름 0.5 + 여유")]
    public float aimLineWidth = 1.2f;

    private float[] zombieHp = new float[0];
    private float[] zombieHpMax = new float[0];
    private float   deathPenaltyNow;
    private float   hitPenaltyNow;
    private float   hitStunNow;
    private float   aimPenaltyNow, aimWidthNow;
    private int     inLineSamples, lineSamples;   // 통계: 총구 선 안에 있던 비율
    private ZombieAgent[] zombieAgents;
    private int     deathsThisEpisode;
    private bool    pendingWipe;

    /// <summary>살아서 이번 판에 남아 있는 좀비인가 (죽으면 비활성화된다).</summary>
    private static bool Alive(ZombieController z) => z != null && z.gameObject.activeInHierarchy;

    /// <summary>이 좀비의 가상 체력 비율 (0~1). 이 구역 좀비가 아니면 1. 위협 센서가 읽는다.</summary>
    public float HealthFraction(ZombieController z)
    {
        int i = zombies.IndexOf(z);
        if (i < 0 || i >= zombieHp.Length || zombieHpMax[i] <= 0f) return 1f;
        return Mathf.Clamp01(zombieHp[i] / zombieHpMax[i]);
    }

    /// <summary>
    /// 에피소드 진행(TickEpisode)을 맡는 에이전트: 살아 있는 에이전트 중 첫 번째.
    /// 고정된 첫 좀비가 맡으면 그 좀비가 죽는 순간 판이 영영 안 끝난다.
    /// </summary>
    public bool IsEpisodeLeader(ZombieAgent a)
    {
        foreach (var x in agents)
            if (x != null && x.isActiveAndEnabled) return x == a;
        return false;
    }

    /// <summary>더미의 총알이 좀비에 맞았을 때. 체력이 0이 되면 그 좀비를 이번 판에서 뺀다.</summary>
    private void OnDummyShot(Transform t, float damage)
    {
        if (episodeEnding || pendingWipe || group == null) return;
        int zi = -1;
        for (int i = 0; i < zombies.Count; i++)
            if (zombies[i] != null && zombies[i].transform == t) { zi = i; break; }
        if (zi < 0 || zi >= zombieHp.Length || !Alive(zombies[zi])) return;

        zombieHp[zi] -= damage;

        // 맞은 본인에게 바로 개별 벌점 —— 피하기의 직접 신호
        if (hitPenaltyNow > 0f)
        {
            var hitAgent = zombies[zi].GetComponent<ZombieAgent>();
            if (hitAgent != null && hitAgent.isActiveAndEnabled) hitAgent.AddReward(-hitPenaltyNow);
        }

        // 맞으면 멈칫한다 (ZombieController.Stun: 그동안 이동 정지, 뇌는 제자리 판단)
        if (hitStunNow > 0f) zombies[zi].Stun(hitStunNow);

        if (zombieHp[zi] > 0f) return;

        deathsThisEpisode++;
        if (deathPenaltyNow > 0f) group.AddGroupReward(-deathPenaltyNow);

        int alive = 0;
        foreach (var z in zombies) if (Alive(z)) alive++;
        if (alive <= 1)
        {
            // 마지막 한 마리: 비활성화하면 TickEpisode를 부를 에이전트가 없어진다.
            // 표시만 해두고, 다음 에이전트 스텝에서 판을 끝낸다 (에피소드 종료는 반드시 에이전트 스텝 안에서).
            pendingWipe = true;
            return;
        }

        // ML-Agents 예제(DungeonEscape)와 같은 방식: 죽은 에이전트는 꺼서 그룹에서 빠지게 하고, 리셋 때 다시 등록한다.
        // MA-POCA는 먼저 빠진 에이전트에게도 팀 결과를 나눠준다 —— 희생해서 길을 연 좀비도 공을 인정받는다.
        zombies[zi].gameObject.SetActive(false);
    }

    void Start()
    {
        if (dummy != null) dummy.ShotHit += OnDummyShot;
        if (useSlotTactics && slotTactics == null && dummy != null)
        {
            // 조언 모드: 좀비의 RL 뇌는 그대로 두고 자리만 계산한다 → 자리 센서(ZombieSlotSensor)가 읽는다
            slotTactics = HunterPackTactics.Create("SlotTactics", transform, zombies, dummy.transform,
                                                   slotSettings, takeControl: false);
            slotTactics.externalTick = true;   // 에이전트 스텝에 맞춰 OnAgentPreStep에서 계산한다
        }
        ResetArena();
    }

    // ── 조율기 + 강화학습 ─────────────────────────────
    [Header("조율기 + 강화학습")]
    [Tooltip("켜면 이 구역에 무리 조율기(HunterPackTactics)를 조언 모드로 붙인다.\n" +
             "조율기가 각 좀비의 자리(포위 위치 / 덮치기)를 정하고, 자리 센서(ZombieSlotSensor)가 그걸 관측으로 넣는다.\n" +
             "움직임은 강화학습 정책이 정한다 —— '누가 어디로'는 코드가, '어떻게 가서 언제 덮칠지'는 학습이 맡는다.")]
    public bool useSlotTactics = false;
    public HunterTacticsSettings slotSettings = new HunterTacticsSettings { showStatus = false };
    [Tooltip("각 좀비가 자기 자리(명령 지점)에 10m 가까워질 때 받는 개별 보상. 0이면 꺼짐.\n" +
             "yaml: slot_follow_reward. 잠재함수 방식(거리 변화량)이라 자리 근처에서 버티기만 해서는 쌓이지 않는다.\n" +
             "팀 보상(가둔 포획 성공)은 그대로라, 자리를 벗어나는 게 더 잘 잡히면 그쪽을 배울 수 있다.")]
    public float slotFollowReward = 0.2f;
    [Tooltip("조율기를 몇 물리 스텝마다 다시 계산할지 (결정 주기 5와 같게)")]
    public int slotTickEvery = 5;
    [Tooltip("잔차 크기 (ZombieMLBrain.residualScale). 0보다 크면 좀비는 조율기 방향으로 기본 이동하고,\n" +
             "정책 행동은 그 위에 더하는 보정이 된다 → 학습 첫 판부터 조율기만큼은 잡는다.\n" +
             "yaml: slot_residual. 게임 프리팹(_RL_Slot)에도 같은 값이 들어가야 한다 (기본 0.5).")]
    public float slotResidualScale = 0f;
    [Tooltip("켜면 보정을 옆으로만 허용 (ZombieMLBrain.residualLateralOnly). yaml: slot_residual_lateral (1/0)")]
    public bool  slotResidualLateral = false;

    private HunterPackTactics slotTactics;
    private float   slotFollowNow;
    private float[] prevOrderDist;
    private float   orderDistSum;
    private int     orderDistN;
    private int     slotClosingSteps;
    private int     slotKnownSteps;

    /// <summary>
    /// 각 좀비가 자기 명령 지점에 가까워진 만큼 개별 보상 (잠재함수 방식).
    /// 명령 지점이 1m 넘게 튀면(단계 전환, 자리 재배정) 그 스텝은 보상 없이 기준만 새로 잡는다.
    /// </summary>
    private void ApplySlotFollowReward()
    {
        if (slotTactics == null) return;
        if (prevOrderDist == null || prevOrderDist.Length != agents.Count)
        {
            prevOrderDist = new float[agents.Count];
            for (int i = 0; i < prevOrderDist.Length; i++) prevOrderDist[i] = -1f;
        }

        if (slotTactics.IsClosing) slotClosingSteps++;
        if (slotTactics.Knows)     slotKnownSteps++;

        for (int i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a == null || a.Body == null) continue;
            if (!slotTactics.TryGetOrder(a.Body, out Vector3 p, out _)) { prevOrderDist[i] = -1f; continue; }

            Vector3 d = p - a.transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            orderDistSum += dist;
            orderDistN++;

            float prev = prevOrderDist[i];
            prevOrderDist[i] = dist;
            if (slotFollowNow <= 0f || prev < 0f) continue;

            float delta = prev - dist;
            if (Mathf.Abs(delta) > 1f) continue;
            a.AddReward(delta * slotFollowNow * 0.1f);
        }
    }

    // ── 처치 모드 ────────────────────────────────────
    private float dummyHealth;
    private float dummyHealthMax;
    private bool  pendingKill;
    private int   hitCount;
    private readonly float[] roleDamage = new float[3];

    /// <summary>
    /// 좀비가 공격을 휘둘렀을 때 (ZombieController.AttackLanded). 공격 판단은 게임과 같은 규칙
    /// (보이고 사거리 1.8m 안이면 자동 공격, 쿨다운 1.5초)을 그대로 쓴다.
    /// 깎은 체력 비율만큼 팀 보상 → 마지막 한 방이 아니라 '계속 때릴 수 있는 배치'에 보상이 간다.
    /// </summary>
    private void OnZombieAttack(ZombieController z, Transform target)
    {
        if (!killMode || pendingKill || episodeEnding || dummy == null || group == null) return;
        if (target != dummy.transform) return;
        if (stepCount <= captureGraceSteps) return;

        // 안전장치: 실제로 사거리 안일 때만 (판단 시점과 휘두르는 시점이 어긋날 수 있다)
        Vector3 d = dummy.transform.position - z.transform.position;
        d.y = 0f;
        if (d.magnitude > z.attackRange + 0.2f) return;

        float dmg = Mathf.Min(Mathf.Max(0f, z.attackDamage), dummyHealth);
        if (dmg <= 0f) return;

        dummyHealth -= dmg;
        hitCount++;
        if (dummyHealthMax > 0f) group.AddGroupReward(dmg / dummyHealthMax * damageRewardTotal);

        int zi = zombies.IndexOf(z);
        if (mixedPack && zi >= 0 && zi < roles.Length) roleDamage[(int)roles[zi]] += dmg;

        if (dummyHealth <= 0f)
        {
            pendingKill = true;
            capturedBy  = zi;   // '누가 잡았나' 지표는 마지막 일격 기준
        }
    }

    /// <summary>
    /// 에피소드 진행 판정. 반드시 ZombieAgent.OnActionReceived 안에서 호출된다.
    ///
    /// FixedUpdate에서 부르면 안 된다 —— ML-Agents의 Academy도 자기 FixedUpdate에서
    /// 스텝을 돌리는데, 오브젝트 간 FixedUpdate 순서는 Unity가 보장하지 않는다.
    /// 에이전트 스텝 도중에 EndGroupEpisode가 끼어들면 trajectory가 꼬이고
    /// Unity가 응답을 못 보내는 상태로 굳는다 (worker stuck in waiting state).
    /// </summary>
    public void TickEpisode()
    {
        if (group == null || dummy == null || episodeEnding) return;

        // 평가 도구가 조건을 바꾸려 할 때: 기록 없이 끝내고 새 판으로
        if (abortRequested)
        {
            abortRequested = false;
            EndEpisode(false, report: false);
            return;
        }

        // 반격하는 더미: 무리가 전멸했다 (마지막 한 마리가 죽은 순간 표시해 둔 것)
        if (pendingWipe)
        {
            EndEpisode(false, report: true, terminal: true);
            return;
        }

        stepCount++;
        AccumulateDiagnostics();

        if (useTimePenalty && maxSteps > 0)
            group.AddGroupReward(-1f / maxSteps);

        ApplyApproachReward();
        ApplyEncircleReward();
        ApplyCutoffReward();
        ApplySlotFollowReward();

        // ── 처치 모드: 닿는 게 아니라 체력을 0으로 만들어야 끝난다 ──
        // 공격은 좀비의 Update(프레임)에서 일어나므로 거기서는 표시만 해두고,
        // 에피소드 종료는 반드시 여기(에이전트 스텝 안)서 한다.
        if (killMode)
        {
            if (pendingKill)
            {
                if (logCaptures)
                    Debug.Log($"[처치] {name} — {hitCount}타, {stepCount}스텝", this);
                group.AddGroupReward(captureReward);
                EndEpisode(true);
                return;
            }
            if (maxSteps > 0 && stepCount >= maxSteps)
                EndEpisode(false);
            return;
        }

        // 포획 판정 —— 누구든 한 마리만 닿으면 팀 성공.
        // 시작 직후 몇 스텝은 건너뛴다: 배치가 어쩌다 가깝게 잡혀도
        // 즉시 포획 → 리셋 → 즉시 포획이 폭주하지 않도록 하는 안전장치.
        if (stepCount <= captureGraceSteps) return;

        // 가둔 포획: 닿는 순간 더미의 퇴로(좀비 + 벽 기준 가장 큰 빈틈)가 pinnedMaxGap 이하여야 성공.
        // 빈틈은 누가 닿았든 같으므로 '누군가 닿았는가'만 먼저 본다.
        bool anyContact = false;
        for (int zi = 0; zi < zombies.Count && !anyContact; zi++)
        {
            var z = zombies[zi];
            if (Alive(z) && Vector3.Distance(z.transform.position, dummy.transform.position) <= captureRadius)
                anyContact = true;
        }
        if (pinnedNow && anyContact)
        {
            int aliveCount = 0;
            foreach (var z in zombies) if (Alive(z)) aliveCount++;

            lastContactGap = PinnedGapDeg(dummy.transform.position);
            // [수정] 살아남은 좀비가 1마리뿐일 때는 물리적으로 포위(빈틈 축소)가 불가능하므로 접촉 시 포획을 인정하고,
            // 2마리 이상일 때만 pinnedMaxGapNow 기준을 적용한다.
            if (aliveCount >= 2 && lastContactGap > pinnedMaxGapNow)
            {
                if (!wasInContact) unpinnedContacts++;   // 새로 닿을 때마다 한 번만 센다
                wasInContact = true;
                anyContact = false;                     // 뒤에서 혼자 닿은 것 → 성공 아님, 판 계속
            }
        }
        if (!anyContact) wasInContact = false;

        for (int zi = 0; zi < zombies.Count && anyContact; zi++)
        {
            var z = zombies[zi];
            if (!Alive(z)) continue;   // 죽어서 빠진 좀비 제외
            float dist = Vector3.Distance(z.transform.position, dummy.transform.position);
            if (dist <= captureRadius)
            {
                capturedBy = zi;
                if (logCaptures)
                    Debug.Log($"[포획] {name} / {z.name} — 중심 거리 {dist:F2}m (판정 {captureRadius:F2}m, " +
                              $"몸 사이 틈 약 {Mathf.Max(0f, dist - 1f):F2}m) / {stepCount}스텝", this);
                group.AddGroupReward(captureReward);
                EndEpisode(true);
                return;
            }
        }

        if (maxSteps > 0 && stepCount >= maxSteps)
            EndEpisode(false);
    }

    /// <summary>
    /// 팀 전체에서 타겟에 가장 가까운 좀비의 거리가 줄어들면 그룹 보상.
    /// 누가 줄였는지는 MA-POCA가 알아서 배분한다 —— 길목을 막아 상대를
    /// 다른 좀비 쪽으로 몬 좀비에게도 공이 돌아가는 건 이 구조 덕분이다.
    /// </summary>
    private void ApplyApproachReward()
    {
        if (approachGroupReward <= 0f) return;

        float minDist = float.MaxValue;
        foreach (var a in agents)
        {
            if (a == null) continue;
            float d = a.BelievedDistance;
            if (d >= 0f && d < minDist) minDist = d;
        }

        if (minDist == float.MaxValue)   // 아무도 타겟을 모름
        {
            prevMinDistance = -1f;
            return;
        }

        if (prevMinDistance >= 0f)
            group.AddGroupReward((prevMinDistance - minDist) * approachGroupReward);

        prevMinDistance = minDist;
    }

    /// <summary>
    /// 포위 점수(0~1)의 변화량만큼 그룹 보상. 점수 계산은 EncircleScore 참고.
    /// 점수가 꺼져 있어도(가중치 0) 진단 지표는 계속 쌓는다 —— 보상 없는 학습과 비교하기 위해.
    /// </summary>
    private void ApplyEncircleReward()
    {
        // 지형 반영 점수는 NavMesh 레이 72개를 쓰므로 결정 주기(5스텝)마다만 새로 잰다.
        // 그 사이에는 같은 점수를 쓴다 —— 잠재함수 보상이라 변화량 합은 그대로다.
        float s;
        if (terrainAwareEncircle && lastEncircleRaw > -1.5f && stepCount % TerrainScoreEvery != 0)
            s = lastEncircleRaw;
        else
            s = EncircleScore();
        lastEncircleRaw = s;

        // 진단: 누군가 근처에 있을 때만 평균에 넣는다 (멀리서 수색 중인 시간은 제외)
        if (s >= 0f)
        {
            encircleSum += s;
            encircleSamples++;
        }
        else
        {
            s = 0f;
        }

        int aliveNow = 0;
        foreach (var z in zombies) if (Alive(z)) aliveNow++;

        // [수정] 좀비가 총에 맞아 사망(비활성화)하면서 인원수 감소로 포위 점수가 급락한 경우,
        // 이미 사망 벌점(deathPenalty)을 받았으므로 잠재함수 음수 보상까지 이중으로 부과하지 않고 기준점만 갱신한다.
        bool memberDied = prevAliveForEncircle >= 0 && aliveNow < prevAliveForEncircle;
        if (encircleWeightNow > 0f && prevEncircle >= 0f && !(memberDied && s < prevEncircle))
            group.AddGroupReward((s - prevEncircle) * encircleWeightNow);

        prevEncircle = s;
        prevAliveForEncircle = aliveNow;
    }

    /// <summary>
    /// 포위 점수. 더미 반경 encircleRadius 안의 좀비들이 더미를 얼마나 고르게 둘러쌌는가.
    ///   1  = 가장 큰 빈틈이 120° 이하 (사방이 막힘)
    ///   0.75 = 둘이 정반대에서 협공 (빈틈 180°)
    ///   0  = 한 마리만 근처에 있거나, 전부 한쪽에 몰림
    /// 근처에 아무도 없으면 -1 (진단 평균에서 제외).
    ///
    /// 기준을 마리 수와 상관없이 120°로 고정했다 —— 3마리 구역과 5마리 구역의 점수를
    /// 같은 잣대로 비교하기 위해서. (EscapeGapDeg는 마리 수가 늘면 무작위 배치만으로도
    /// 작아지므로 구역 구성이 다르면 직접 비교할 수 없다.)
    /// </summary>
    private const float FullEncircleGap = 120f;

    private float EncircleScore()
    {
        if (dummy == null) return -1f;
        if (terrainAwareEncircle) return TerrainEncircleScore();

        float gap = LargestGap(dummy.transform.position, encircleRadius, out int n);
        if (n == 0) return -1f;
        if (n < 2)  return 0f;

        return Mathf.Clamp01((360f - gap) / (360f - FullEncircleGap));
    }

    // ── 가둔 포획 ────────────────────────────────────
    [Header("가둔 포획")]
    [Tooltip("켜면 닿기만 해서는 성공이 아니다. 닿는 순간 더미가 빠져나갈 가장 큰 빈틈이 Pinned Max Gap 이하여야 성공.\n" +
             "빈틈은 근처(Pinned Radius 안) 좀비의 몸과, 3m 안의 벽을 막힌 방향으로 쳐서 잰다.\n" +
             "뒤에서 혼자 따라붙어 닿는 건 성공이 아니므로, 반대쪽을 막는 협동(또는 벽 몰이)만 보상받는다.\n" +
             "yaml: pinned_capture(1/0), pinned_max_gap(도), pinned_radius(m)")]
    public bool  pinnedCapture = false;
    public float pinnedMaxGap  = 150f;
    [Tooltip("포위에 참여했다고 인정하는 반경(m). 조율기(HunterPackTactics)의 기본 ringRadius(7m)에 있는 동료도 포함되도록 8m 권장")]
    public float pinnedRadius  = 8f;
    [Tooltip("좀비 한 마리가 통제하는 반각(°). HunterPackTactics의 controlHalfAngle(25°)과 기준을 맞춘다")]
    public float pinnedControlHalfAngle = 25f;

    private bool  pinnedNow;
    private float pinnedMaxGapNow, pinnedRadiusNow, pinnedControlHalfAngleNow;
    private bool  wasInContact;
    private int   unpinnedContacts;
    private float lastContactGap = -1f;

    private void ReadPinnedRules()
    {
        var ep = Academy.Instance.EnvironmentParameters;
        pinnedNow                 = ep.GetWithDefault("pinned_capture", pinnedCapture ? 1f : 0f) > 0.5f;
        pinnedMaxGapNow           = ep.GetWithDefault("pinned_max_gap", pinnedMaxGap);
        pinnedRadiusNow           = ep.GetWithDefault("pinned_radius",  pinnedRadius);
        pinnedControlHalfAngleNow = ep.GetWithDefault("pinned_control_angle", pinnedControlHalfAngle);
        wasInContact     = false;
        unpinnedContacts = 0;
        lastContactGap   = -1f;
    }

    /// <summary>
    /// 더미가 빠져나갈 수 있는 가장 큰 빈틈(도). 막힌 방향 =
    ///   · pinnedRadius 안 좀비의 몸이 가리는 각도 (가까울수록 넓게: 반지름 0.5m 기준)
    ///   · 3m(wallBlockDistance) 안에 벽이 있는 방향 (NavMesh 레이)
    /// 막힌 방향이 하나도 없으면 360.
    /// </summary>
    private float PinnedGapDeg(Vector3 c)
    {
        System.Array.Clear(gapBlocked, 0, GapBins);

        float[] ring = TerrainProbe.Ring(c);
        if (ring != null)
        {
            float limit = wallBlockDistance / TerrainProbe.MaxDistance;
            for (int b = 0; b < GapBins; b++)
                if (TerrainProbe.Sample(ring, (b + 0.5f) * GapBinDeg) < limit) gapBlocked[b] = true;
        }

        float r2 = pinnedRadiusNow * pinnedRadiusNow;
        foreach (var z in zombies)
        {
            if (!Alive(z)) continue;   // 죽어서 빠진 좀비 제외
            Vector3 d = z.transform.position - c;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq > r2 || sq < 1e-4f) continue;
            float dist = Mathf.Sqrt(sq);
            float deg  = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            // [수정] 몸통 반지름(0.5m)만 쓰면 5~7m 거리에서 4~5도밖에 안 되어 조율기(25도)와 판정 괴리가 발생함.
            // 조율기(HunterPackTactics.ComputeGap)와 동일하게 전술 통제 반각(pinnedControlHalfAngleNow)을 반영한다.
            float half = Mathf.Max(pinnedControlHalfAngleNow, Mathf.Atan2(0.5f, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg);
            int from = Mathf.FloorToInt((deg - half) / GapBinDeg);
            int to   = Mathf.FloorToInt((deg + half) / GapBinDeg);
            for (int b = from; b <= to; b++) gapBlocked[((b % GapBins) + GapBins) % GapBins] = true;
        }

        int start = -1;
        for (int b = 0; b < GapBins; b++) if (gapBlocked[b]) { start = b; break; }
        if (start < 0) return 360f;

        int longest = 0, run = 0;
        for (int k = 1; k <= GapBins; k++)
        {
            if (gapBlocked[(start + k) % GapBins]) { longest = Mathf.Max(longest, run); run = 0; }
            else run++;
        }
        return Mathf.Min(360f, longest * GapBinDeg);
    }

    // ── 앞 막기 (잠재함수 기반) ──────────────────────
    private NavMeshAgent dummyNav;
    private Vector3 lastEvaderDir;
    private float   cutoffWeightNow;
    private float   prevCutoff = -1f;
    private float   cutoffSum;
    private int     cutoffSamples;

    /// <summary>
    /// 앞 막기 점수(0~1)의 변화량만큼 그룹 보상. 포위 보상과 같은 잠재함수 방식이라
    /// 한 판 합계는 (마지막 점수 − 처음 점수)로 상쇄되어, 앞에 서 있기만 하며 시간을 끄는 꼼수가 생기지 않는다.
    ///
    /// 점수: cutoffRadius 안의 좀비마다 b = (앞쪽 정도) × (가까운 정도),
    ///        앞쪽 정도 = 더미가 달리는 방향과 '더미→좀비' 방향의 내적 (뒤쪽이면 0),
    ///        가까운 정도 = 1 − 거리/cutoffRadius.
    ///        팀 점수 = 1 − Π(1 − b) —— 한 마리가 바로 앞에 붙으면 1에 가깝고, 여럿이면 더 단단해진다.
    /// 더미가 멈추면(몰려서 막힌 상태) 마지막으로 달리던 방향을 계속 쓴다 —— 막아서 세웠는데 벌받지 않도록.
    /// </summary>
    private void ApplyCutoffReward()
    {
        if (dummy == null) return;
        if (dummyNav == null) dummyNav = dummy.GetComponent<NavMeshAgent>();

        Vector3 v = dummyNav != null ? dummyNav.velocity : Vector3.zero;
        v.y = 0f;
        if (v.sqrMagnitude > 0.25f) lastEvaderDir = v.normalized;   // 0.5m/s 이상일 때만 방향 갱신
        if (lastEvaderDir == Vector3.zero) { prevCutoff = -1f; return; }

        Vector3 e = dummy.transform.position;
        float keep = 1f;
        bool  anyNear = false;
        foreach (var z in zombies)
        {
            if (!Alive(z)) continue;   // 죽어서 빠진 좀비 제외
            Vector3 d = z.transform.position - e;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > cutoffRadius || dist < 0.01f) continue;
            anyNear = true;

            float ahead = Vector3.Dot(lastEvaderDir, d / dist);
            if (ahead <= 0f) continue;
            float b = ahead * (1f - dist / cutoffRadius);
            keep *= 1f - Mathf.Clamp01(b);
        }
        float s = 1f - keep;

        if (anyNear) { cutoffSum += s; cutoffSamples++; }

        if (cutoffWeightNow > 0f && prevCutoff >= 0f)
            group.AddGroupReward((s - prevCutoff) * cutoffWeightNow);
        prevCutoff = s;
    }

    // ── 지형 반영 포위 점수 ──────────────────────────
    private const int   TerrainScoreEvery = 5;    // 결정 주기와 같게
    private const int   GapBins           = 72;   // 5° 간격
    private const float GapBinDeg         = 360f / GapBins;
    private readonly bool[] gapBlocked = new bool[GapBins];
    private float lastEncircleRaw = -2f;

    /// <summary>
    /// 벽도 퇴로를 막는 것으로 치는 포위 점수.
    ///
    /// 더미 주변을 5° 칸 72개로 나누고, 다음 칸을 '막힘'으로 표시한다.
    ///   - 그 방향으로 wallBlockDistance 안에 벽이 있다 (NavMesh 레이)
    ///   - 그 방향에 encircleRadius 안의 좀비가 있다
    /// 막히지 않은 칸이 가장 길게 이어진 구간이 '빈틈'이다. 나머지 계산은 기존 점수와 같다.
    ///
    /// 그래서 더미를 벽에 몰아붙이면 벽 쪽 반원이 공짜로 막히고, 남은 쪽만 좀비가 막으면 된다.
    /// 벽이 없는 곳에서는 기존 점수와 거의 같다. 근처에 좀비가 하나도 없으면 -1 (벽만으로는 점수 없음).
    /// </summary>
    private float TerrainEncircleScore()
    {
        Vector3 c = dummy.transform.position;
        System.Array.Clear(gapBlocked, 0, GapBins);

        float[] ring = TerrainProbe.Ring(c);
        if (ring != null)
        {
            float limit = wallBlockDistance / TerrainProbe.MaxDistance;
            for (int b = 0; b < GapBins; b++)
                if (TerrainProbe.Sample(ring, (b + 0.5f) * GapBinDeg) < limit) gapBlocked[b] = true;
        }

        float r2 = encircleRadius * encircleRadius;
        int n = 0;
        foreach (var z in zombies)
        {
            if (!Alive(z)) continue;   // 죽어서 빠진 좀비 제외
            Vector3 d = z.transform.position - c;
            d.y = 0f;
            if (d.sqrMagnitude > r2) continue;
            float deg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int bin = Mathf.FloorToInt(Mathf.Repeat(deg, 360f) / GapBinDeg) % GapBins;
            gapBlocked[bin] = true;
            n++;
        }
        if (n == 0) return -1f;

        // 막히지 않은 칸이 가장 길게 이어진 구간 (원형). 막힌 칸이 하나뿐이면 빈틈 360°.
        int start = -1;
        for (int b = 0; b < GapBins; b++) if (gapBlocked[b]) { start = b; break; }
        if (start < 0) return 0f;

        int longest = 0, run = 0;
        for (int k = 1; k <= GapBins; k++)
        {
            if (gapBlocked[(start + k) % GapBins])
            {
                longest = Mathf.Max(longest, run);
                run = 0;
            }
            else run++;
        }

        float gap = Mathf.Min(360f, (longest + 1) * GapBinDeg);
        return Mathf.Clamp01((360f - gap) / (360f - FullEncircleGap));
    }

    /// <param name="terminal">시간 초과가 아니라 진짜로 끝난 실패 (전멸). 가치함수에 '끝났다'를 알린다.</param>
    private void EndEpisode(bool captured, bool report = true, bool terminal = false)
    {
        episodeEnding = true;

        // 리셋 전에 —— 포획 순간의 배치를 재야 한다
        if (report)
        {
            RecordEpisodeStats(captured);
            EpisodeFinished?.Invoke(this, BuildReport(captured));
        }

        // 성공으로 끝난 에피소드는 EndGroupEpisode,
        // 시간 초과는 GroupEpisodeInterrupted —— 후자는 부트스트랩을 남겨서
        // "아직 끝나지 않았을 뿐"이라는 정보를 가치함수에 전달한다.
        if (captured || terminal) group.EndGroupEpisode();
        else                      group.GroupEpisodeInterrupted();

        BeforeReset?.Invoke(this);
        ResetArena();
        episodeEnding = false;
    }

    private EpisodeReport BuildReport(bool captured)
    {
        float dt      = Time.fixedDeltaTime;
        float seconds = stepCount * dt;
        return new EpisodeReport
        {
            captured           = captured,
            steps              = stepCount,
            seconds            = seconds,
            evaderSpeedSetting = currentEvaderSpeed,
            escapeGapDeg       = captured ? LargestEscapeGap() : float.NaN,
            encircleMean       = encircleSamples > 0 ? encircleSum / encircleSamples : float.NaN,
            evaderMeanSpeed    = seconds > 0f ? dummyDistance / seconds : 0f,
            evaderFleeSpeed    = dummyFleeSteps > 0 ? dummyFleeDistance / (dummyFleeSteps * dt) : float.NaN,
            zombieMeanSpeed    = speedSamples > 0 ? speedSum / speedSamples : 0f,
            predictionError    = predErrCount > 0 ? predErrSum / predErrCount : float.NaN,
            shotsFired         = dummy != null && dummy.ShootingActive ? dummy.ShotsFired : -1,
            shotsHit           = dummy != null && dummy.ShootingActive ? dummy.ShotsHit   : -1,
            obliqueAimedDeg    = obqAimN    > 0 ? obqAimSum    / obqAimN    : float.NaN,
            obliqueFreeDeg     = obqFreeN   > 0 ? obqFreeSum   / obqFreeN   : float.NaN,
            steerAimedDeg      = steerAimN  > 0 ? steerAimSum  / steerAimN  : float.NaN,
            steerFreeDeg       = steerFreeN > 0 ? steerFreeSum / steerFreeN : float.NaN,
            aimedFraction      = obqAimN + obqFreeN > 0 ? obqAimN / (float)(obqAimN + obqFreeN) : float.NaN,
        };
    }

    // ── 측정 지표 ─────────────────────────────────
    /// <summary>
    /// 에피소드 결과를 TensorBoard에 기록한다 (Pursuit/ 그룹으로 표시됨).
    ///
    /// EscapeGapDeg가 "포위했는가"를 숫자로 보여주는 핵심 지표다.
    /// 포획 순간 더미를 중심으로 각 좀비가 있는 방향(각도)을 구해 정렬하고,
    /// 이웃한 두 좀비 사이의 가장 큰 빈틈을 잰다.
    ///   - 셋이 한쪽에서 몰려왔다면 → 빈틈이 300° 안팎 (뒤가 뻥 뚫림)
    ///   - 셋이 둘러쌌다면        → 빈틈이 180° 이하 (퇴로 없음)
    /// 학습이 진행되며 이 값이 내려가면 포위를 배우고 있다는 직접 증거다.
    /// </summary>
    private void RecordEpisodeStats(bool captured)
    {
        var sr = Academy.Instance.StatsRecorder;

        sr.Add("Pursuit/CaptureRate", captured ? 1f : 0f);

        if (adaptiveSpeed) UpdateAdaptiveSpeed(captured);
        sr.Add("Pursuit/EvaderSpeed", currentEvaderSpeed);

        // ── 진단 ──
        // SightFraction: 에피소드 중 '좀비 누군가가 더미를 보고 있던' 시간 비율.
        //   낮으면 좀비들이 더미를 못 찾고 있다 → 협공 이전에 수색이 병목이다.
        // ZombieSpeed: 좀비들의 평균 실제 이동 속도(m/s). 최고 속도는 3.5.
        //   낮으면 정책이 행동 크기를 작게 내서 어슬렁거리고 있다.
        if (stepCount > 0)
            sr.Add("Pursuit/SightFraction", sightSteps / (float)stepCount);
        if (speedSamples > 0)
            sr.Add("Pursuit/ZombieSpeed", speedSum / speedSamples);
        // EncircleScore: 더미 근처에서 보낸 시간 동안의 평균 포위 점수(0~1).
        //   포획 순간만 보는 EscapeGapDeg와 달리 '쫓는 과정'의 대형을 본다. 보상을 꺼도 기록된다.
        if (encircleSamples > 0)
            sr.Add("Pursuit/EncircleScore", encircleSum / encircleSamples);
        // EvaderActualSpeed: 더미가 도망치는 동안의 실제 평균 속도. EvaderSpeed(설정값)는 최고 속도라
        //   장애물을 돌고 방향을 트는 손실이 빠져 있다. "좀비보다 빠른 상대"를 주장하려면 이 값을 봐야 한다.
        if (dummyFleeSteps > 0)
            sr.Add("Pursuit/EvaderActualSpeed", dummyFleeDistance / (dummyFleeSteps * Time.fixedDeltaTime));
        // PredictionError: 좀비 위치를 '지금 속도로 1초 직진'으로 예측했을 때의 평균 오차(m).
        //   클수록 플레이어가 좀비 움직임을 읽기 어렵다 —— "예측 불가능성"의 직접 지표.
        if (predErrCount > 0)
            sr.Add("Pursuit/PredictionError", predErrSum / predErrCount);

        RecordRoleStats(sr, captured);

        // 가둔 포획 지표
        //   UnpinnedContacts: 닿았지만 퇴로가 열려 있어 성공으로 안 친 횟수 (판당). 줄어들면 '가두고 닿기'를 배우는 중
        //   CaptureGapWithWalls: 성공 순간의 빈틈(좀비+벽). pinned_max_gap 이하여야 성공이다
        if (pinnedNow)
        {
            sr.Add("Pursuit/UnpinnedContacts", unpinnedContacts);
            if (captured && lastContactGap >= 0f) sr.Add("Pursuit/CaptureGapWithWalls", lastContactGap);
        }

        // CutoffScore: 더미가 달리는 방향 앞쪽을 좀비가 얼마나 막고 있었나 (0~1, 근처에 누가 있을 때만 평균)
        if (cutoffSamples > 0) sr.Add("Pursuit/CutoffScore", cutoffSum / cutoffSamples);

        // 조율기 + 강화학습 지표
        //   OrderDistance: 좀비가 자기 명령 지점에서 평균 몇 m 떨어져 있었나. 줄어들면 자리를 따라가는 법을 배우는 중
        //   ClosingFraction: 판 중 '덮치기' 단계였던 시간 비율 (포위가 완성되는 빈도)
        //   KnownFraction: 무리가 더미 위치를 알고 있던 시간 비율
        if (slotTactics != null && stepCount > 0)
        {
            if (orderDistN > 0) sr.Add("Slot/OrderDistance", orderDistSum / orderDistN);
            sr.Add("Slot/ClosingFraction", slotClosingSteps / (float)stepCount);
            sr.Add("Slot/KnownFraction",   slotKnownSteps   / (float)stepCount);
        }

        // 반격하는 더미 지표
        //   Deaths: 한 판에 죽은 좀비 수 / WipeRate: 전멸한 판 비율
        //   HitRate: 더미 총알 명중률 —— 내려가면 좀비가 피하는 법을 배우는 중
        //   SurvivorsOnCapture: 잡았을 때 살아 있던 좀비 수
        if (dummy != null && dummy.ShootingActive)
        {
            sr.Add("Shoot/Deaths",   deathsThisEpisode);
            sr.Add("Shoot/WipeRate", pendingWipe ? 1f : 0f);
            sr.Add("Shoot/ShotsFired", dummy.ShotsFired);
            // 회피: 조준당할 때와 아닐 때 비스듬함 / RL 옆 보정 각도
            if (obqAimN  > 0)   sr.Add("Dodge/ObliqueAimed", obqAimSum  / obqAimN);
            if (obqFreeN > 0)   sr.Add("Dodge/ObliqueFree",  obqFreeSum / obqFreeN);
            if (steerAimN  > 0) sr.Add("Dodge/SteerAimed",   steerAimSum  / steerAimN);
            if (steerFreeN > 0) sr.Add("Dodge/SteerFree",    steerFreeSum / steerFreeN);
            // 사거리 안 좀비 중 총구 선(aimLineWidth) 안에 있던 비율 —— 내려가면 정면을 비켜 가는 중
            if (lineSamples > 0) sr.Add("Dodge/InAimLine", inLineSamples / (float)lineSamples);
            if (dummy.ShotsFired > 0) sr.Add("Shoot/HitRate", dummy.ShotsHit / (float)dummy.ShotsFired);
            if (captured)
            {
                int alive = 0;
                foreach (var z in zombies) if (Alive(z)) alive++;
                sr.Add("Shoot/SurvivorsOnCapture", alive);
            }
        }

        // 처치 모드 지표
        //   DamageDealt: 더미 체력을 얼마나 깎았나 (0~1). 처치 못 한 판에서도 '거의 잡았는지'가 보인다.
        //   Hits: 한 판의 명중 수
        if (killMode && dummyHealthMax > 0f)
        {
            sr.Add("Kill/DamageDealt", 1f - Mathf.Clamp01(dummyHealth / dummyHealthMax));
            sr.Add("Kill/Hits", hitCount);

            // 역할별 데미지 비중: 그 역할이 이번 판에 있었을 때만 (탱커가 마무리를 맡는지 등)
            float total = 0f;
            foreach (float v in roleDamage) total += v;
            if (mixedPack && total > 0f)
                for (int r = 0; r < roleDamage.Length; r++)
                {
                    bool present = false;
                    foreach (var x in roles) if ((int)x == r) { present = true; break; }
                    if (present) sr.Add($"Role/DamageShare_{(PackRole)r}", roleDamage[r] / total);
                }
        }

        if (!captured) return;

        sr.Add("Pursuit/CaptureSteps", stepCount);
        sr.Add("Pursuit/EscapeGapDeg", LargestEscapeGap());

        // CaptureOpenness: 포획 순간 더미 주변이 얼마나 트여 있었나 (0~1, NavMesh 레이 36방향 평균).
        //   낮을수록 벽이나 골목에 몰아넣고 잡았다는 뜻 —— 지형을 쓰는지 보는 지표.
        //   학습이 진행되며 내려가면 '지형 몰이'를 배우는 중이다. 지형 센서가 없는 학습에서도 기록되므로
        //   같은 학습장에서 이전 모델과 비교할 수 있다.
        if (dummy != null)
        {
            float open = TerrainProbe.Openness(dummy.transform.position);
            if (open >= 0f) sr.Add("Pursuit/CaptureOpenness", open);
        }
    }

    /// <summary>
    /// 최근 adaptiveWindow판의 성공률로 다음 에피소드의 더미 속도를 정한다.
    /// 잘 잡으면 빠르게, 못 잡으면 느리게 —— 항상 '배울 만한' 난이도를 유지한다.
    ///
    /// 이 값 자체가 성능 지표가 된다: Pursuit/EvaderSpeed가 좀비 속도(3.5)를 넘어 올라가면
    /// 자기보다 빠른 상대를 잡는 법(협공)을 익혔다는 뜻이다.
    /// </summary>
    // yaml(environment_parameters)로 덮어쓸 수 있는 적응형 난이도 규칙. 매 리셋마다 다시 읽는다.
    //   adaptive_window / adaptive_step / adaptive_raise / adaptive_lower
    // 빌드를 다시 하지 않고 '얼마나 빨리 어려워지는가'를 조절하려는 것.
    private int   adaptiveWindowNow = -1;
    private float adaptiveStepNow, raiseAboveNow, lowerBelowNow;

    private void ReadAdaptiveRules()
    {
        var ep = Academy.Instance.EnvironmentParameters;
        adaptiveWindowNow = Mathf.Max(1, Mathf.RoundToInt(ep.GetWithDefault("adaptive_window", adaptiveWindow)));
        adaptiveStepNow   = Mathf.Max(0f, ep.GetWithDefault("adaptive_step",  adaptiveStep));
        raiseAboveNow     = ep.GetWithDefault("adaptive_raise", raiseAbove);
        lowerBelowNow     = ep.GetWithDefault("adaptive_lower", lowerBelow);
    }

    private void UpdateAdaptiveSpeed(bool captured)
    {
        if (adaptiveWindowNow < 0) ReadAdaptiveRules();

        recentResults.Enqueue(captured);
        while (recentResults.Count > adaptiveWindowNow) recentResults.Dequeue();
        if (recentResults.Count < adaptiveWindowNow) return;   // 표본이 찰 때까지는 그대로

        int wins = 0;
        foreach (bool r in recentResults) if (r) wins++;
        float rate = wins / (float)recentResults.Count;

        float before = adaptiveSpeedNow;
        if      (rate >= raiseAboveNow) adaptiveSpeedNow += adaptiveStepNow;
        else if (rate <  lowerBelowNow) adaptiveSpeedNow -= adaptiveStepNow;
        adaptiveSpeedNow = Mathf.Clamp(adaptiveSpeedNow, adaptiveMin, adaptiveMax);

        // 난이도가 바뀌면 기록을 비운다 —— 이전 난이도의 성적으로 또 판단하면 연속으로 튄다
        if (!Mathf.Approximately(before, adaptiveSpeedNow)) recentResults.Clear();
    }

    /// <summary>
    /// 회피 측정: 더미가 쏘는 동안, 조준당한 좀비와 아닌 좀비가 얼마나 비스듬히 다가가는지 잰다.
    /// 조준당할 때 더 비스듬하면 '총구를 보고 비켜 간다'는 뜻. 행동이나 보상에는 영향이 없다.
    /// </summary>
    private void AccumulateDodge()
    {
        if (dummy == null || !dummy.ShootingActive) return;
        if (zombieMl == null || zombieMl.Length != zombies.Count)
        {
            zombieMl = new ZombieMLBrain[zombies.Count];
            for (int i = 0; i < zombies.Count; i++)
                zombieMl[i] = zombies[i] != null ? zombies[i].GetComponent<ZombieMLBrain>() : null;
        }

        Vector3 dp = dummy.transform.position;
        Transform aimed = dummy.AimTarget;
        float range = Mathf.Max(dummy.fireRange, 3f);

        // 총구 선 벌점: 총구에서 뻗은 직선에 가까울수록 (사거리 안, 2m 밖, 멈칫 중 제외)
        if (zombieAgents == null || zombieAgents.Length != zombies.Count)
        {
            zombieAgents = new ZombieAgent[zombies.Count];
            for (int i = 0; i < zombies.Count; i++)
                zombieAgents[i] = zombies[i] != null ? zombies[i].GetComponent<ZombieAgent>() : null;
        }
        Vector3 aimDir = dummy.AimForward; aimDir.y = 0f;
        if (aimDir.sqrMagnitude > 1e-4f)
        {
            aimDir.Normalize();
            float dtLine = Time.fixedDeltaTime;
            for (int i = 0; i < zombies.Count; i++)
            {
                var z = zombies[i];
                if (!Alive(z) || z.IsStunned) continue;
                Vector3 rel = z.transform.position - dp; rel.y = 0f;
                float along = Vector3.Dot(rel, aimDir);
                if (along < 2f || along > range) continue;
                float perp = (rel - aimDir * along).magnitude;
                lineSamples++;
                if (perp >= aimWidthNow) continue;
                inLineSamples++;
                var ag = zombieAgents[i];
                if (aimPenaltyNow > 0f && ag != null && ag.isActiveAndEnabled)
                    ag.AddReward(-aimPenaltyNow * (1f - perp / aimWidthNow) * dtLine);
            }
        }
        for (int i = 0; i < zombies.Count; i++)
        {
            var z = zombies[i];
            if (!Alive(z)) continue;
            var nav = z.GetComponent<NavMeshAgent>();
            if (nav == null || !nav.isOnNavMesh) continue;

            Vector3 v = nav.velocity; v.y = 0f;
            if (v.sqrMagnitude < 0.25f) continue;            // 거의 멈춤(경직 포함)은 빼고 잰다
            Vector3 toD = dp - z.transform.position; toD.y = 0f;
            float dist = toD.magnitude;
            if (dist < 2.5f || dist > range) continue;       // 붙어 있을 때·사거리 밖은 뺀다

            float obq = Vector3.Angle(v, toD);
            bool isAimed = aimed == z.transform;
            if (isAimed) { obqAimSum += obq; obqAimN++; } else { obqFreeSum += obq; obqFreeN++; }

            var ml = zombieMl[i];
            if (ml != null && ml.enabled && z.brain == ml && ml.HasSteer)   // 지금 이 좀비를 움직이는 뇌일 때만
            {
                float s = Mathf.Abs(ml.LastSteerDeg);
                if (isAimed) { steerAimSum += s; steerAimN++; } else { steerFreeSum += s; steerFreeN++; }
            }
        }
    }

    private void AccumulateDiagnostics()
    {
        bool anySees = false;
        foreach (var a in agents)
            if (a != null && a.SeesTarget) { anySees = true; break; }
        if (anySees) sightSteps++;

        foreach (var nav in zombieNavs)
        {
            if (nav == null || !nav.isOnNavMesh) continue;
            speedSum += nav.velocity.magnitude;
            speedSamples++;
        }

        AccumulateDodge();

        // ── 더미 실제 이동 거리 ──
        if (dummy != null)
        {
            Vector3 p = dummy.transform.position;
            if (hasPrevDummyPos)
            {
                Vector3 d = p - prevDummyPos;
                d.y = 0f;
                float step = d.magnitude;
                // 순간이동(리셋, NavMesh 복귀)은 이동으로 치지 않는다
                if (step < 1f)
                {
                    dummyDistance += step;
                    if (dummy.IsFleeing) { dummyFleeDistance += step; dummyFleeSteps++; }
                    AccumulateRoleDiagnostics(p, d / Time.fixedDeltaTime);
                }
            }
            prevDummyPos    = p;
            hasPrevDummyPos = true;
        }

        // ── 예측 불가능성: 0.5초마다 '지금 속도로 1초 직진' 예측을 걸어두고, 1초 뒤 실제와 비교 ──
        float horizonSec = PredictHorizon * Time.fixedDeltaTime;
        for (int i = 0; i < zombieNavs.Count && i < predictions.Count; i++)
        {
            var nav = zombieNavs[i];
            var q   = predictions[i];
            if (nav == null) continue;

            Vector3 now = nav.transform.position;
            while (q.Count > 0 && q.Peek().due <= stepCount)
            {
                var pred = q.Dequeue();
                if (pred.due != stepCount) continue;
                Vector3 e = now - pred.pos;
                e.y = 0f;
                predErrSum += e.magnitude;
                predErrCount++;
            }

            if (stepCount % PredictEvery == 0 && nav.isOnNavMesh)
            {
                Vector3 v = nav.velocity;
                v.y = 0f;
                q.Enqueue(new PendingPrediction { due = stepCount + PredictHorizon, pos = now + v * horizonSec });
            }
        }
    }

    /// <summary>더미 기준으로 좀비들 사이의 가장 큰 각도 빈틈(도).</summary>
    private float LargestEscapeGap()
    {
        if (dummy == null) return 360f;
        return LargestGap(dummy.transform.position, float.PositiveInfinity, out _);
    }

    /// <summary>
    /// 중심 c에서 radius 안에 있는 좀비들 사이의 가장 큰 각도 빈틈(도).
    /// n = 반경 안의 좀비 수. 2마리 미만이면 360.
    /// </summary>
    private float LargestGap(Vector3 c, float radius, out int n)
    {
        if (bearingBuffer.Length < zombies.Count) bearingBuffer = new float[zombies.Count];

        float r2 = radius * radius;
        n = 0;
        foreach (var z in zombies)
        {
            if (!Alive(z)) continue;   // 죽어서 빠진 좀비 제외
            Vector3 d = z.transform.position - c;
            d.y = 0f;
            if (d.sqrMagnitude > r2) continue;
            float deg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            bearingBuffer[n++] = deg < 0f ? deg + 360f : deg;
        }
        if (n < 2) return 360f;

        System.Array.Sort(bearingBuffer, 0, n);

        float maxGap = bearingBuffer[0] + 360f - bearingBuffer[n - 1];   // 한 바퀴 돌아오는 틈
        for (int i = 1; i < n; i++)
            maxGap = Mathf.Max(maxGap, bearingBuffer[i] - bearingBuffer[i - 1]);
        return maxGap;
    }

    /// <summary>구역을 초기 상태로 되돌린다.</summary>
    public void ResetArena()
    {
        stepCount       = 0;
        sightSteps      = 0;
        speedSum        = 0f;
        speedSamples    = 0;
        obqAimSum = obqFreeSum = steerAimSum = steerFreeSum = 0f;
        obqAimN = obqFreeN = steerAimN = steerFreeN = 0;
        inLineSamples = lineSamples = 0;
        prevMinDistance = -1f;
        prevEncircle         = -1f;
        prevAliveForEncircle = -1;
        lastEncircleRaw      = -2f;
        encircleSum     = 0f;
        encircleSamples = 0;
        predErrSum      = 0f;
        predErrCount    = 0;
        foreach (var q in predictions) q.Clear();
        hasPrevDummyPos   = false;
        dummyDistance     = 0f;
        dummyFleeDistance = 0f;
        dummyFleeSteps    = 0;
        encircleWeightNow = Academy.Instance.EnvironmentParameters
            .GetWithDefault("encircle_reward", encircleReward);
        ReadAdaptiveRules();
        ApplyIntelSharingMode();
        ReadPinnedRules();
        // 처치 모드도 yaml(kill_mode 1/0)로 켤 수 있다 —— 예전에 만든 원형 씬을 다시 만들지 않고 쓰기 위해
        killMode = Academy.Instance.EnvironmentParameters.GetWithDefault("kill_mode", killMode ? 1f : 0f) > 0.5f;
        cutoffWeightNow = Academy.Instance.EnvironmentParameters.GetWithDefault("cutoff_reward", cutoffReward);
        prevCutoff      = -1f;
        lastEvaderDir   = Vector3.zero;
        cutoffSum       = 0f;
        cutoffSamples   = 0;
        slotFollowNow    = Academy.Instance.EnvironmentParameters.GetWithDefault("slot_follow_reward", slotFollowReward);
        orderDistSum     = 0f;
        orderDistN       = 0;
        slotClosingSteps = 0;
        slotKnownSteps   = 0;
        if (prevOrderDist != null) for (int i = 0; i < prevOrderDist.Length; i++) prevOrderDist[i] = -1f;
        if (slotTactics != null)
        {
            slotTactics.ResetState();
            float residual = Academy.Instance.EnvironmentParameters.GetWithDefault("slot_residual", slotResidualScale);
            bool  lateral  = Academy.Instance.EnvironmentParameters
                .GetWithDefault("slot_residual_lateral", slotResidualLateral ? 1f : 0f) > 0.5f;
            foreach (var z in zombies)
            {
                var ml = z != null ? z.GetComponent<ZombieMLBrain>() : null;
                if (ml == null) continue;
                ml.residualScale       = residual;
                ml.residualLateralOnly = lateral;
            }
        }
        Intel.Reset();
        if (obstacleMode == ObstacleMode.Scatter) ScatterObstacles();
        else                                      JitterObstacles();

        // 이 프로젝트는 Physics.autoSyncTransforms가 꺼져 있어서, 방금 옮긴 장애물의
        // 충돌체가 다음 물리 스텝까지 옛 위치에 남는다. 아래 스폰 판정이
        // 새 위치를 보도록 강제로 동기화한다.
        Physics.SyncTransforms();

        Vector3 dummyPos = randomizeSpawns
            ? RandomPoint(transform.position, spawnRadius)
            : transform.position;

        if (dummy != null)
        {
            dummy.TeleportTo(dummyPos);

            // 커리큘럼: Python 쪽 yaml이 학습 진행도에 따라 이 값을 올려준다.
            // 빌드를 다시 하지 않아도 yaml만 고치면 난이도를 바꿀 수 있다.
            if (adaptiveSpeed)
            {
                // 시작 속도도 yaml(adaptive_start)로 덮어쓸 수 있다.
                // 이어 학습할 때 2.4부터 다시 시작해 실력을 되찾느라 시간을 버리지 않도록.
                if (adaptiveSpeedNow < 0f)
                    adaptiveSpeedNow = Mathf.Clamp(
                        Academy.Instance.EnvironmentParameters.GetWithDefault("adaptive_start", adaptiveStart),
                        adaptiveMin, adaptiveMax);
                currentEvaderSpeed = adaptiveSpeedNow;
            }
            else
            {
                currentEvaderSpeed = Academy.Instance.EnvironmentParameters
                    .GetWithDefault("evader_speed", defaultEvaderSpeed);
            }
            dummy.minSpeed = currentEvaderSpeed - evaderSpeedJitter;
            dummy.maxSpeed = currentEvaderSpeed + evaderSpeedJitter;

            dummy.Randomize();   // 도주 성향도 매 에피소드 새로 뽑는다
        }

        // 반격하는 더미: 지난 판에 죽어서 꺼진 좀비를 되살리고 그룹에 다시 등록한다 (DungeonEscape 예제 방식)
        pendingWipe       = false;
        deathsThisEpisode = 0;
        foreach (var z in zombies)
            if (z != null && !z.gameObject.activeSelf) z.gameObject.SetActive(true);
        if (group != null)
            foreach (var a in agents) if (a != null) group.RegisterAgent(a);

        placedBuffer.Clear();
        var placed = placedBuffer;

        foreach (var z in zombies)
        {
            if (z == null) continue;

            Vector3 pos = randomizeSpawns
                ? FindZombieSpawn(dummyPos, placed, guaranteeInitialSighting && placed.Count == 0)
                : transform.position;

            placed.Add(pos);

            // 첫 좀비는 더미 쪽을 보게 돌려놓는다 (시야각 안에 들어오도록)
            Quaternion rot = (guaranteeInitialSighting && placed.Count == 1)
                ? LookAtFlat(pos, dummyPos)
                : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            z.ResetForEpisode(pos, rot);
        }

        if (mixedPack) AssignRoles();
        capturedBy = -1;

        // 좀비 가상 체력 (역할별). 더미가 쏘지 않으면 쓰이지 않는다.
        {
            var ep = Academy.Instance.EnvironmentParameters;
            float hpN = ep.GetWithDefault("zombie_hp_normal", normalHealth);
            float hpR = ep.GetWithDefault("zombie_hp_runner", runnerHealth);
            float hpT = ep.GetWithDefault("zombie_hp_tank",   tankHealth);
            deathPenaltyNow = ep.GetWithDefault("zombie_death_penalty", deathPenalty);
            hitPenaltyNow   = ep.GetWithDefault("zombie_hit_penalty",   hitPenalty);
            hitStunNow      = ep.GetWithDefault("zombie_hit_stun",      hitStun);
            aimPenaltyNow   = ep.GetWithDefault("zombie_aim_penalty",   aimPenalty);
            aimWidthNow     = Mathf.Max(0.3f, ep.GetWithDefault("zombie_aim_width", aimLineWidth));
            if (zombieHp.Length != zombies.Count) { zombieHp = new float[zombies.Count]; zombieHpMax = new float[zombies.Count]; }
            for (int i = 0; i < zombies.Count; i++)
            {
                float hp = hpN;
                if (mixedPack && i < roles.Length)
                    hp = roles[i] == PackRole.Tank ? hpT : roles[i] == PackRole.Runner ? hpR : hpN;
                zombieHp[i] = zombieHpMax[i] = Mathf.Max(1f, hp);
            }
        }

        // 처치 모드: 더미 체력 초기화 (yaml로 덮어쓸 수 있다 → 낮게 시작해 올리는 커리큘럼도 가능)
        dummyHealthMax = Mathf.Max(1f, Academy.Instance.EnvironmentParameters
            .GetWithDefault("dummy_health", dummyMaxHealth));
        dummyHealth = dummyHealthMax;
        pendingKill = false;
        hitCount    = 0;
        System.Array.Clear(roleDamage, 0, roleDamage.Length);
        System.Array.Clear(roleDistSum,  0, RoleCount);
        System.Array.Clear(roleDistN,    0, RoleCount);
        System.Array.Clear(roleAheadSum, 0, RoleCount);
        System.Array.Clear(roleAheadN,   0, RoleCount);
    }

    // ── 섞인 무리 ────────────────────────────────────
    private const int RoleCount = 3;
    private PackRole[] roles = new PackRole[0];
    private int capturedBy = -1;
    private readonly float[] roleDistSum  = new float[RoleCount];
    private readonly int[]   roleDistN    = new int[RoleCount];
    private readonly float[] roleAheadSum = new float[RoleCount];
    private readonly int[]   roleAheadN   = new int[RoleCount];

    /// <summary>이 판의 역할 구성을 뽑아 속도와 색을 입힌다. 탱커 0~maxTanks, 달리기 0~maxRunners, 나머지 일반.</summary>
    private void AssignRoles()
    {
        int n = zombies.Count;
        if (roles.Length != n) roles = new PackRole[n];

        int tanks   = Mathf.Min(Random.Range(0, maxTanks + 1), n);
        int runners = Mathf.Min(Random.Range(0, maxRunners + 1), n - tanks);

        for (int i = 0; i < n; i++)
            roles[i] = i < tanks ? PackRole.Tank : i < tanks + runners ? PackRole.Runner : PackRole.Normal;
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (roles[i], roles[j]) = (roles[j], roles[i]);
        }

        // 역할별 속도는 yaml(normal_speed / runner_speed / tank_speed)로 덮어쓸 수 있다
        var ep = Academy.Instance.EnvironmentParameters;
        float vNormal = ep.GetWithDefault("normal_speed", normalSpeed);
        float vRunner = ep.GetWithDefault("runner_speed", runnerSpeed);
        float vTank   = ep.GetWithDefault("tank_speed",   tankSpeed);

        for (int i = 0; i < n; i++)
        {
            var z = zombies[i];
            if (z == null) continue;

            z.moveSpeed = roles[i] == PackRole.Tank   ? vTank
                        : roles[i] == PackRole.Runner ? vRunner
                        : vNormal;
            z.attackDamage = roles[i] == PackRole.Tank   ? tankDamage
                           : roles[i] == PackRole.Runner ? runnerDamage
                           : normalDamage;

            Material m = roles[i] == PackRole.Tank   ? tankMaterial
                       : roles[i] == PackRole.Runner ? runnerMaterial
                       : normalMaterial;
            var r = z.GetComponent<Renderer>();
            if (r != null && m != null) r.sharedMaterial = m;
        }
    }

    /// <summary>
    /// 역할별 위치 지표를 쌓는다 (섞인 무리일 때만).
    ///   MeanDist: 더미까지 평균 거리 — 탱커가 멀찍이 출구를 지키는지, 달리기가 붙는지
    ///   Ahead   : 더미가 달리는 방향 기준 좀비 위치 (+1 = 바로 앞, −1 = 바로 뒤)
    ///             — 달리기가 앞질러 막고 일반이 뒤에서 미는지
    /// </summary>
    private void AccumulateRoleDiagnostics(Vector3 evaderPos, Vector3 evaderVel)
    {
        if (!mixedPack || roles.Length != zombies.Count) return;

        evaderVel.y = 0f;
        bool moving = evaderVel.sqrMagnitude > 0.25f;   // 0.5m/s 이상일 때만 앞/뒤를 따진다
        Vector3 fwd = moving ? evaderVel.normalized : Vector3.zero;

        for (int i = 0; i < zombies.Count; i++)
        {
            var z = zombies[i];
            if (!Alive(z)) continue;   // 죽어서 빠진 좀비 제외
            int r = (int)roles[i];

            Vector3 d = z.transform.position - evaderPos;
            d.y = 0f;
            float dist = d.magnitude;
            roleDistSum[r] += dist;
            roleDistN[r]++;

            if (moving && dist > 0.5f && dist < 20f)
            {
                roleAheadSum[r] += Vector3.Dot(fwd, d / dist);
                roleAheadN[r]++;
            }
        }
    }

    private void RecordRoleStats(StatsRecorder sr, bool captured)
    {
        if (!mixedPack) return;

        for (int r = 0; r < RoleCount; r++)
        {
            string role = ((PackRole)r).ToString();
            if (roleDistN[r]  > 0) sr.Add($"Role/MeanDist_{role}", roleDistSum[r]  / roleDistN[r]);
            if (roleAheadN[r] > 0) sr.Add($"Role/Ahead_{role}",    roleAheadSum[r] / roleAheadN[r]);
        }

        // 누가 잡았나: 그 역할이 이번 판에 있었을 때만 기록 (1 = 그 역할이 잡음).
        // 무리 안 비율(예: 5마리 중 1마리 = 0.2)보다 높으면 그 역할이 마무리를 맡는다는 뜻.
        if (!captured || capturedBy < 0 || capturedBy >= roles.Length) return;
        var who = roles[capturedBy];
        for (int r = 0; r < RoleCount; r++)
        {
            bool present = false;
            foreach (var x in roles) if ((int)x == r) { present = true; break; }
            if (present) sr.Add($"Role/CaptureBy_{(PackRole)r}", (int)who == r ? 1f : 0f);
        }
    }

    /// <summary>
    /// 좀비 스폰 위치. 반드시 더미와 충분히 떨어진 곳을 돌려준다.
    ///
    /// 예전 구현은 24번 시도가 모두 실패하면 '마지막 시도값'이 그대로 남았는데,
    /// 그게 더미 바로 옆이면 즉시 포획 → 리셋 → 즉시 포획이 폭주했다.
    /// 조건을 단계적으로 완화하며 반드시 쓸 만한 자리를 찾는다.
    /// </summary>
    private Vector3 FindZombieSpawn(Vector3 dummyPos, List<Vector3> placed, bool wantSight)
    {
        Vector3 relaxed = Vector3.zero;   // 거리 조건은 만족하되 시야만 못 맞춘 후보
        bool hasRelaxed = false;

        for (int i = 0; i < 24; i++)
        {
            Vector3 pos = RandomPoint(transform.position, spawnRadius);

            if (Vector3.Distance(pos, dummyPos) < minSeparation) continue;

            bool tooClose = false;
            foreach (var p in placed)
                if (Vector3.Distance(p, pos) < zombieSeparation) { tooClose = true; break; }
            if (tooClose) continue;

            if (!hasRelaxed) { relaxed = pos; hasRelaxed = true; }

            if (wantSight && i < 12 && !HasLineOfSight(pos, dummyPos)) continue;
            return pos;
        }

        if (hasRelaxed) return relaxed;

        // 전부 실패: 더미에서 일정 거리 떨어진 곳에 강제 배치
        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 cand = dummyPos + dir * (minSeparation * 1.2f);
            if (NavMesh.SamplePosition(cand, out NavMeshHit hit, 6f, NavMesh.AllAreas) &&
                Vector3.Distance(hit.position, dummyPos) >= minSeparation * 0.8f)
                return hit.position;
        }

        return transform.position;
    }

    private static Quaternion LookAtFlat(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(d)
            : Quaternion.identity;
    }

    /// <summary>
    /// 장애물 개수와 위치를 매 에피소드 새로 뽑는다.
    /// 장애물에는 NavMeshObstacle(carving)이 붙어 있어서 옮기면 NavMesh 구멍도 같이 따라간다.
    /// </summary>
    /// <summary>벽 하나를 평면 위의 굵은 선분으로 본 것.</summary>
    private struct WallSeg { public Vector2 a, b; public float halfThick; }
    private readonly List<WallSeg> placedWalls = new List<WallSeg>();

    private void ScatterObstacles()
    {
        if (movableObstacles == null || movableObstacles.Length == 0) return;

        int total  = movableObstacles.Length;
        int pinned = Mathf.Clamp(scatterPinnedFirst, 0, total);
        int active = Random.Range(Mathf.Min(scatterMinActive, total), total + 1);
        active = Mathf.Max(active, pinned);

        // 어느 장애물을 켤지 섞는다 (앞쪽 pinned개는 고정으로 먼저 배치)
        var order = new int[total];
        for (int i = 0; i < total; i++) order[i] = i;
        for (int i = total - 1; i > pinned; i--)
        {
            int j = Random.Range(pinned, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        Vector2 c0 = new Vector2(transform.position.x, transform.position.z);
        placedWalls.Clear();

        for (int k = 0; k < total; k++)
        {
            Transform t = movableObstacles[order[k]];
            if (t == null) continue;

            // 장애물은 긴 변이 로컬 x축, 두께가 로컬 z축이 되도록 만들어져 있다
            float halfLen   = t.localScale.x * 0.5f;
            float halfThick = t.localScale.z * 0.5f;

            bool placed = false;
            if (k < active)
            {
                // 끝점까지 포함해 scatterMaxRadius 안에 들어오도록 중심 반경을 제한
                float maxCenter = Mathf.Max(0f, scatterMaxRadius - halfLen - halfThick);
                float minCenter = Mathf.Min(scatterMinRadius, maxCenter);

                for (int tries = 0; tries < 60; tries++)
                {
                    float r   = Mathf.Sqrt(Random.Range(minCenter * minCenter, maxCenter * maxCenter));
                    float ang = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 c = c0 + new Vector2(Mathf.Sin(ang) * r, Mathf.Cos(ang) * r);

                    float yaw = Random.Range(0f, 180f);
                    float yr  = yaw * Mathf.Deg2Rad;
                    // Euler(0, yaw, 0) 이 로컬 x축을 (cos, -sin) 방향으로 돌린다
                    Vector2 axis = new Vector2(Mathf.Cos(yr), -Mathf.Sin(yr));
                    var seg = new WallSeg { a = c - axis * halfLen, b = c + axis * halfLen, halfThick = halfThick };

                    // 이미 놓인 벽과 가장자리 간격이 scatterMinSpacing 이상이어야 한다.
                    // 중심 거리만 보면 긴 벽 둘이 V자로 붙어 막다른 곳이 생긴다.
                    bool ok = true;
                    foreach (var w in placedWalls)
                    {
                        float gap = SegmentDistance(seg.a, seg.b, w.a, w.b) - seg.halfThick - w.halfThick;
                        if (gap < scatterMinSpacing) { ok = false; break; }
                    }
                    if (!ok) continue;

                    t.position = new Vector3(c.x, t.position.y, c.y);
                    t.rotation = Quaternion.Euler(0f, yaw, 0f);
                    placedWalls.Add(seg);
                    placed = true;
                    break;
                }
            }

            t.gameObject.SetActive(placed);
        }
    }

    /// <summary>평면 위 두 선분 사이의 최단 거리 (교차하면 0).</summary>
    private static float SegmentDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        if (SegmentsIntersect(p1, p2, q1, q2)) return 0f;
        return Mathf.Min(
            Mathf.Min(PointSegmentDistance(p1, q1, q2), PointSegmentDistance(p2, q1, q2)),
            Mathf.Min(PointSegmentDistance(q1, p1, p2), PointSegmentDistance(q2, p1, p2)));
    }

    private static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + ab * t);
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        float d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2);
        float d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>스폰 후보가 장애물 안이나 바로 옆인지.</summary>
    private bool NearObstacle(Vector3 pos, float margin = 1.5f)
    {
        if (obstacleColliders == null) return false;
        float sq = margin * margin;
        foreach (var c in obstacleColliders)
        {
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            // AABB(bounds)를 쓰면 대각선으로 놓인 긴 벽의 상자가 너무 커져서
            // 스폰 후보를 대부분 버리게 된다. 실제 표면까지의 거리로 판정한다.
            if ((c.ClosestPoint(pos) - pos).sqrMagnitude < sq) return true;
        }
        return false;
    }

    private void JitterObstacles()
    {
        if (movableObstacles == null || obstacleHomes == null) return;

        for (int i = 0; i < movableObstacles.Length; i++)
        {
            if (movableObstacles[i] == null) continue;
            Vector2 rnd = Random.insideUnitCircle * obstacleJitter;
            movableObstacles[i].position = obstacleHomes[i] + new Vector3(rnd.x, 0f, rnd.y);
        }
    }

    /// <summary>두 지점 사이에 시야를 막는 것이 없는지 (좀비/더미 자신은 제외).</summary>
    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 eye = from + Vector3.up * 1.6f;
        Vector3 aim = to   + Vector3.up * 1.0f;
        Vector3 dir = aim - eye;
        float   len = dir.magnitude;
        if (len < 0.01f) return true;

        int count = Physics.RaycastNonAlloc(eye, dir / len, sightBuffer, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform h = sightBuffer[i].transform;
            if (h.GetComponentInParent<ZombieController>() != null) continue;
            if (dummy != null && h.IsChildOf(dummy.transform)) continue;
            return false;
        }
        return true;
    }

    private Vector3 RandomPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 32; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(rnd.x, 0f, rnd.y);
            // NavMesh 구멍(carving)은 다음 프레임에야 반영되므로,
            // 방금 옮긴 장애물 안에 스폰되지 않도록 충돌체로 직접 확인한다.
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas) &&
                !NearObstacle(hit.position))
                return hit.position;
        }
        return center;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
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
    private float encircleWeightNow;
    private float prevEncircle = -1f;
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
        Intel.Tick(anySees, dummy.transform.position);
    }

    void Start()
    {
        ResetArena();
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

        stepCount++;
        AccumulateDiagnostics();

        if (useTimePenalty && maxSteps > 0)
            group.AddGroupReward(-1f / maxSteps);

        ApplyApproachReward();
        ApplyEncircleReward();

        // 포획 판정 —— 누구든 한 마리만 닿으면 팀 성공.
        // 시작 직후 몇 스텝은 건너뛴다: 배치가 어쩌다 가깝게 잡혀도
        // 즉시 포획 → 리셋 → 즉시 포획이 폭주하지 않도록 하는 안전장치.
        if (stepCount <= captureGraceSteps) return;

        foreach (var z in zombies)
        {
            if (z == null) continue;
            float dist = Vector3.Distance(z.transform.position, dummy.transform.position);
            if (dist <= captureRadius)
            {
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
        float s = EncircleScore();

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

        if (encircleWeightNow > 0f && prevEncircle >= 0f)
            group.AddGroupReward((s - prevEncircle) * encircleWeightNow);

        prevEncircle = s;
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

        float gap = LargestGap(dummy.transform.position, encircleRadius, out int n);
        if (n == 0) return -1f;
        if (n < 2)  return 0f;

        return Mathf.Clamp01((360f - gap) / (360f - FullEncircleGap));
    }

    private void EndEpisode(bool captured, bool report = true)
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
        if (captured) group.EndGroupEpisode();
        else          group.GroupEpisodeInterrupted();

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

        if (!captured) return;

        sr.Add("Pursuit/CaptureSteps", stepCount);
        sr.Add("Pursuit/EscapeGapDeg", LargestEscapeGap());
    }

    /// <summary>
    /// 최근 adaptiveWindow판의 성공률로 다음 에피소드의 더미 속도를 정한다.
    /// 잘 잡으면 빠르게, 못 잡으면 느리게 —— 항상 '배울 만한' 난이도를 유지한다.
    ///
    /// 이 값 자체가 성능 지표가 된다: Pursuit/EvaderSpeed가 좀비 속도(3.5)를 넘어 올라가면
    /// 자기보다 빠른 상대를 잡는 법(협공)을 익혔다는 뜻이다.
    /// </summary>
    private void UpdateAdaptiveSpeed(bool captured)
    {
        recentResults.Enqueue(captured);
        while (recentResults.Count > adaptiveWindow) recentResults.Dequeue();
        if (recentResults.Count < adaptiveWindow) return;   // 표본이 찰 때까지는 그대로

        int wins = 0;
        foreach (bool r in recentResults) if (r) wins++;
        float rate = wins / (float)recentResults.Count;

        float before = adaptiveSpeedNow;
        if      (rate >= raiseAbove) adaptiveSpeedNow += adaptiveStep;
        else if (rate <  lowerBelow) adaptiveSpeedNow -= adaptiveStep;
        adaptiveSpeedNow = Mathf.Clamp(adaptiveSpeedNow, adaptiveMin, adaptiveMax);

        // 난이도가 바뀌면 기록을 비운다 —— 이전 난이도의 성적으로 또 판단하면 연속으로 튄다
        if (!Mathf.Approximately(before, adaptiveSpeedNow)) recentResults.Clear();
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
            if (z == null) continue;
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
        prevMinDistance = -1f;
        prevEncircle    = -1f;
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
        int active = Random.Range(Mathf.Min(scatterMinActive, total), total + 1);

        // 어느 장애물을 켤지 섞는다
        var order = new int[total];
        for (int i = 0; i < total; i++) order[i] = i;
        for (int i = total - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
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

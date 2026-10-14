using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ML-Agents 쪽 창구. 관측을 모으고, 행동을 받아 ZombieMLBrain에 넘기고, 보상을 준다.
///
/// 다중 에이전트(MA-POCA) 구성:
///   한 구역의 좀비 3마리가 하나의 그룹으로 묶여 그룹 보상을 공유한다.
///   누가 잡든 팀 전체가 보상을 받으므로 "내가 직접 잡는다"가 아니라
///   "팀이 잡게 만든다"를 학습한다 —— 측면 차단과 길목 지키기가 여기서 나온다.
///
/// 에피소드 종료는 개별 Agent가 아니라 Arena가 그룹 단위로 처리한다.
/// 따라서 MaxStep은 0으로 두고 Arena의 타임아웃을 쓴다.
///
/// 인스펙터 설정 (Behavior Parameters):
///   Behavior Name                 : Zombie
///   Vector Observation Space Size : ObservationSize (= 10 + 7 + 4 x 7 = 45)
///   Continuous Actions            : 2
///   Discrete Branches             : 1, Size 2
///
/// 관측 구성 (v3):
///   [자기 10]  자기 눈으로 본 정보 (ZombieObservation.ToVector)
///   [팀 7]     팀 공유 정보: 정보 있음 / 마지막 목격 지점 방향 x,z / 거리 / 경과시간 /
///              타겟 이동 속도 x,z (내 기준 좌표)  ← 어디로 도망가는지. 앞을 막으려면 필수
///   [동료 7x4] 가까운 동료 4명 (가까운 순). 유효 / 동료 방향 x,z / 거리 / 동료가 보는가 /
///              타겟 기준 동료 위치 x,z  ← "동료가 타겟의 어느 쪽에 있나". 포위 기하 그 자체
///
/// 동료 수가 몇이든 '가까운 4명'만 보므로, 무리 크기가 달라도 같은 모델을 쓸 수 있다.
/// 동료가 4명보다 적으면 남는 칸은 유효=0으로 채운다.
/// </summary>
[RequireComponent(typeof(ZombieController))]
[RequireComponent(typeof(ZombieMLBrain))]
public class ZombieAgent : Agent
{
    /// <summary>관측에 포함할 동료 수 (가까운 순).</summary>
    public const int TeammateSlots = 4;
    /// <summary>동료 1명당 관측 길이.</summary>
    public const int PerTeammate = 7;
    /// <summary>팀 공유 정보 관측 길이.</summary>
    public const int TeamIntelLength = 7;
    /// <summary>Behavior Parameters의 Vector Observation Size에 넣을 값.</summary>
    public const int ObservationSize =
        ZombieObservation.SelfVectorLength + TeamIntelLength + TeammateSlots * PerTeammate;   // 10 + 7 + 28 = 45

    /// <summary>속도 관측 정규화 기준(m/s). 더미 최고 속도 근처.</summary>
    private const float VelocityScale = 5f;
    /// <summary>이보다 오래된 목격 정보의 속도는 믿지 않는다(초).</summary>
    private const float VelocityTrustAge = 1.0f;

    [Header("개별 보상")]
    [Tooltip("믿는 타겟 지점에 가까워진 거리 1m당 개별 보상.\n" +
             "⚠ 0을 권장. 이 값이 크면 '혼자 직진'이 최적해가 되어 포위를 못 배운다 ——\n" +
             "측면을 막으러 가는 좀비는 당장 타겟에서 멀어지므로 벌점을 받기 때문.\n" +
             "접근 보상은 Arena가 팀 최소거리 기준으로 그룹에 준다.")]
    public float approachReward = 0f;
    [Tooltip("타겟을 실제로 보고 있는 동안 매 결정마다")]
    public float visibleReward = 0.002f;

    [Header("무리 짓기 억제")]
    [Tooltip("동료와 이 거리보다 가까우면 벌점. 셋이 한 줄로 뭉쳐 다니는 걸 막는다.")]
    public float crowdDistance = 4f;
    public float crowdPenalty  = 0.004f;

    [Header("참조")]
    public ZombieTrainingArena arena;

    /// <summary>팀 공유 정보. Arena가 넣어준다. 없으면 팀 관측은 0으로 채운다.</summary>
    [System.NonSerialized] public ZombieTeamIntel intel;

    private ZombieController body;
    private ZombieMLBrain     mlBrain;
    private float             prevDistance = -1f;
    private bool              warnedMissingBrain;

    private readonly List<ZombieAgent> teammates = new List<ZombieAgent>();
    private readonly List<ZombieAgent> nearestBuffer = new List<ZombieAgent>();

    // Heuristic이 아무 정보도 없을 때 쓰는 배회 상태
    private Vector3 wanderDirWorld = Vector3.forward;
    private float   wanderUntil;

    // 재사용 버퍼
    private readonly float[] selfBuffer = new float[ZombieObservation.SelfVectorLength];

    // 진단용
    [System.NonSerialized] public int     actionCallCount;
    [System.NonSerialized] public int     heuristicCallCount;
    [System.NonSerialized] public int     episodeCount;
    [System.NonSerialized] public Vector2 lastMoveAction;

    public ZombieController Body => body;

    /// <summary>타겟을 지금 직접 보고 있는지. 동료 관측에서 서로 참조한다.</summary>
    public bool SeesTarget =>
        mlBrain != null && mlBrain.HasObservation && mlBrain.LastObservation.targetVisible;

    /// <summary>믿는 타겟까지의 거리. 모르면 음수.</summary>
    public float BelievedDistance =>
        mlBrain != null && mlBrain.HasObservation && mlBrain.LastObservation.hasBelief
            ? mlBrain.LastObservation.believedDistance
            : -1f;

    // ─────────────────────────────────────────────
    public override void Initialize()
    {
        ResolveRefs();
        if (arena == null) arena = GetComponentInParent<ZombieTrainingArena>();
        if (body != null && body.brain == null) body.brain = mlBrain;
    }

    // ── 게임 모드: 학습장(Arena) 밖이면 ZombieSquad에 등록해 정보를 공유한다 ──
    protected override void OnEnable()
    {
        base.OnEnable();   // 여기서 Initialize가 돌며 arena를 찾는다
        if (Application.isPlaying && arena == null)
            ZombieSquad.Instance.Register(this);
    }

    protected override void OnDisable()
    {
        if (ZombieSquad.HasInstance) ZombieSquad.Instance.Unregister(this);
        base.OnDisable();
    }

    private void ResolveRefs()
    {
        if (body    == null) body    = GetComponent<ZombieController>();
        if (mlBrain == null) mlBrain = GetComponent<ZombieMLBrain>();

        if (mlBrain == null && !warnedMissingBrain)
        {
            warnedMissingBrain = true;
            Debug.LogError($"[ZombieAgent] {name}: ZombieMLBrain이 없습니다.", this);
        }
    }

    /// <summary>
    /// 구역의 에피소드 진행을 책임지는 에이전트인지.
    /// 한 명만 TickEpisode를 불러야 스텝이 중복 계산되지 않는다.
    /// </summary>
    private bool isEpisodeLeader;

    /// <summary>Arena가 구역 구성원을 알려준다.</summary>
    public void SetTeammates(IReadOnlyList<ZombieAgent> all)
    {
        teammates.Clear();
        foreach (var a in all)
            if (a != null && a != this) teammates.Add(a);

        isEpisodeLeader = all.Count > 0 && all[0] == this;
    }

    /// <summary>
    /// 배회 방향. 일정 시간마다, 그리고 벽에 막혀 멈췄을 때 새로 뽑는다.
    /// </summary>
    private Vector3 NextWanderDirLocal()
    {
        bool stuck = false;
        var nav = body != null ? body.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
        if (nav != null && nav.isOnNavMesh && nav.velocity.sqrMagnitude < 0.04f)
            stuck = true;   // 벽에 붙어 제자리면 바로 방향 전환

        if (Time.time >= wanderUntil || stuck)
        {
            Vector2 r = Random.insideUnitCircle.normalized;
            wanderDirWorld = new Vector3(r.x, 0f, r.y);
            wanderUntil    = Time.time + Random.Range(1.5f, 3.5f);
        }

        return transform.InverseTransformDirection(wanderDirWorld);
    }

    public override void OnEpisodeBegin()
    {
        wanderUntil = 0f;
        // 구역 리셋은 Arena가 그룹 단위로 한 번만 수행한다.
        // 여기서 arena.ResetArena()를 부르면 좀비 수만큼 중복 리셋된다.
        episodeCount++;
        prevDistance = -1f;
        if (mlBrain != null) mlBrain.OnResetEpisode();
    }

    // ── 관측 ──────────────────────────────────────
    public override void CollectObservations(VectorSensor sensor)
    {
        // 여기서 예외가 나면 관측이 중간에 끊기고 ML-Agents는 경고만 낸다.
        // 무슨 일이 있어도 정확히 ObservationSize개를 쓴다.
        System.Array.Clear(selfBuffer, 0, selfBuffer.Length);

        try
        {
            ResolveRefs();
            if (mlBrain != null && mlBrain.HasObservation)
                mlBrain.LastObservation.ToVector(selfBuffer);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ZombieAgent] {name}: 관측 수집 실패 → 0으로 채움\n{e}", this);
        }

        for (int i = 0; i < selfBuffer.Length; i++) sensor.AddObservation(selfBuffer[i]);

        float range = ZombieObservation.ObservationRange;   // 프로필과 무관한 고정 기준 (학습 때와 동일)
        bool  hasIntel = intel != null && intel.HasInfo;
        Vector3 intelPos = hasIntel ? intel.LastPosition : Vector3.zero;

        // ── 팀 공유 정보 ──
        // 자기가 못 봐도 동료가 봤으면 안다. 돌아가서 길목을 막는 좀비가
        // 타겟을 시야에서 놓쳐도 정보가 끊기지 않게 하는 게 핵심.
        if (hasIntel)
        {
            Vector3 d = intelPos - transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            Vector3 dirLocal = dist > 0.0001f ? transform.InverseTransformDirection(d / dist) : Vector3.zero;

            // 속도: 최근에 본 경우만. 오래된 속도로 앞을 막으러 가면 엉뚱한 데로 간다.
            Vector3 velLocal = intel.Age <= VelocityTrustAge
                ? transform.InverseTransformDirection(intel.Velocity) / VelocityScale
                : Vector3.zero;

            sensor.AddObservation(1f);
            sensor.AddObservation(dirLocal.x);
            sensor.AddObservation(dirLocal.z);
            sensor.AddObservation(Mathf.Clamp01(dist / range));
            sensor.AddObservation(Mathf.Clamp01(intel.Age / 5f));
            sensor.AddObservation(Mathf.Clamp(velLocal.x, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(velLocal.z, -1f, 1f));
        }
        else
        {
            for (int k = 0; k < TeamIntelLength; k++) sensor.AddObservation(0f);
        }

        // ── 동료 관측 ──
        // 동료가 어디서 무엇을 보고 있는지 알아야 "쟤가 정면에서 쫓으니 나는 돌아가자"가 성립한다.
        // 가까운 순으로 정렬해 넣는다 —— 칸 순서에 일관된 의미가 생겨 학습이 쉬워진다.
        // 학습장: Arena가 알려준 구역 동료 / 게임: ZombieSquad에 등록된 맵 전체 좀비 중 가까운 순
        IReadOnlyList<ZombieAgent> pool = teammates;
        if (arena == null && ZombieSquad.HasInstance) pool = ZombieSquad.Instance.Members;

        nearestBuffer.Clear();
        foreach (var m in pool)
        {
            if (m == null || m == this || !m.isActiveAndEnabled) continue;
            if (m.Body != null && m.Body.IsDead) continue;
            nearestBuffer.Add(m);
        }
        Vector3 me = transform.position;
        nearestBuffer.Sort((a, b) =>
            (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));

        for (int i = 0; i < TeammateSlots; i++)
        {
            ZombieAgent mate = i < nearestBuffer.Count ? nearestBuffer[i] : null;

            if (mate == null || body == null)
            {
                for (int k = 0; k < PerTeammate; k++) sensor.AddObservation(0f);
                continue;
            }

            Vector3 d = mate.transform.position - transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            Vector3 local = dist > 0.0001f
                ? transform.InverseTransformDirection(d / dist)
                : Vector3.zero;

            sensor.AddObservation(1f);                            // 이 슬롯이 유효함
            sensor.AddObservation(local.x);                       // 동료 방향
            sensor.AddObservation(local.z);
            sensor.AddObservation(Mathf.Clamp01(dist / range));   // 동료까지 거리
            sensor.AddObservation(mate.SeesTarget ? 1f : 0f);     // 동료가 타겟을 보는가

            // 타겟 기준 동료 위치 (내 좌표계). 나와 동료가 타겟의 같은 쪽인지 반대쪽인지가
            // 여기서 바로 보인다 —— 포위 여부를 네트워크가 계산해낼 필요가 없어진다.
            if (hasIntel)
            {
                Vector3 rel = mate.transform.position - intelPos;
                rel.y = 0f;
                Vector3 relLocal = transform.InverseTransformDirection(rel) / range;
                sensor.AddObservation(Mathf.Clamp(relLocal.x, -1f, 1f));
                sensor.AddObservation(Mathf.Clamp(relLocal.z, -1f, 1f));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }
    }

    // ── 행동 ──────────────────────────────────────
    public override void OnActionReceived(ActionBuffers actions)
    {
        var move = new Vector2(
            Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f),
            Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f));

        bool attack = actions.DiscreteActions[0] == 1;

        actionCallCount++;
        lastMoveAction = move;

        ResolveRefs();
        if (mlBrain == null) return;
        mlBrain.SetActions(move, attack);

        ApplyRewards();

        // 에피소드 진행/종료는 반드시 에이전트 스텝 안에서 처리한다.
        // Arena의 FixedUpdate에서 부르면 Academy 스텝과 순서가 엇갈려
        // Unity가 응답을 못 보내고 굳는다.
        if (isEpisodeLeader && arena != null) arena.TickEpisode();
    }

    private void ApplyRewards()
    {
        if (mlBrain == null || !mlBrain.HasObservation) return;
        var obs = mlBrain.LastObservation;

        // 믿는 지점에 가까워지면 보상. 안 보일 땐 마지막 목격 지점이 기준이므로
        // "놓친 쪽으로 가보기"도 자연스럽게 보상받는다.
        if (obs.hasBelief)
        {
            if (prevDistance >= 0f)
                AddReward((prevDistance - obs.believedDistance) * approachReward);
            prevDistance = obs.believedDistance;
        }
        else
        {
            prevDistance = -1f;
        }

        if (obs.targetVisible) AddReward(visibleReward);

        // 동료와 겹치면 벌점 —— 셋이 한 덩어리로 다니면 포위가 안 된다
        if (crowdPenalty > 0f)
        {
            foreach (var mate in teammates)
            {
                if (mate == null) continue;
                if (Vector3.Distance(transform.position, mate.transform.position) < crowdDistance)
                    AddReward(-crowdPenalty);
            }
        }
    }

    /// <summary>
    /// 학습 없이 배선을 확인할 때 쓰는 수동 정책 (Behavior Type = Heuristic Only).
    /// 단순 직선 추격이라 협동은 하지 않는다 —— 그래서 이게
    /// "경로탐색만 쓰면 어디까지인가"의 기준선이기도 하다.
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        heuristicCallCount++;

        var continuous = actionsOut.ContinuousActions;
        var discrete   = actionsOut.DiscreteActions;

        continuous[0] = 0f;
        continuous[1] = 0f;
        discrete[0]   = 0;

        ResolveRefs();
        if (mlBrain == null || !mlBrain.HasObservation) return;
        var obs = mlBrain.LastObservation;

        // 자기는 모르지만 동료가 봤으면 그 지점으로 간다 (FSM의 경계 전파와 같은 수준).
        // 이걸 넣어야 '같은 정보를 받은 직선 추격'이 RL과 공정한 기준선이 된다.
        if (!obs.hasBelief && intel != null && intel.HasInfo)
        {
            Vector3 d = intel.LastPosition - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1f)
            {
                Vector3 local = transform.InverseTransformDirection(d.normalized);
                continuous[0] = local.x;
                continuous[1] = local.z;
                return;
            }
        }

        // 아무 정보도 없으면 배회하며 찾는다.
        // 여기서 0을 반환하면 좀비가 굳고, 움직이지 않으니 시야도 안 바뀌어
        // 영영 아무것도 발견하지 못하는 교착에 빠진다.
        if (!obs.hasBelief)
        {
            Vector3 wanderLocal = NextWanderDirLocal();
            continuous[0] = wanderLocal.x;
            continuous[1] = wanderLocal.z;
            return;
        }

        continuous[0] = obs.believedDirLocal.x;
        continuous[1] = obs.believedDirLocal.z;
        discrete[0]   = (obs.targetVisible && obs.InAttackRange) ? 1 : 0;
    }
}

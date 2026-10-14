using System.Collections.Generic;
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

    /// <summary>지금 좀비를 피해 도망치는 중인지 (배회 중이면 false). 평가 지표용.</summary>
    public bool IsFleeing { get; private set; }

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
        agent.speed  = Random.Range(minSpeed, maxSpeed);
        fleeDistance = Random.Range(minFleeDistance, maxFleeDistance);
        repathTimer  = 0f;
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

        Transform near = NearestThreat();
        bool fleeing = near != null &&
                       Vector3.Distance(transform.position, near.position) < fleeDistance;

        IsFleeing = fleeing;
        if (!fleeing) hasFleeTarget = false;

        agent.SetDestination(fleeing ? PickFleePoint(near) : PickRoamPoint());
    }

    private Transform NearestThreat()
    {
        if (threats.Count == 0) return threat;

        Transform best = null;
        float bestSq = float.MaxValue;
        foreach (var t in threats)
        {
            if (t == null) continue;
            float sq = (t.position - transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = t; }
        }
        return best;
    }

    // ── 도주 ──────────────────────────────────────
    private Vector3 PickFleePoint(Transform nearest)
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

            float score = ScoreFleePoint(origin, reach, nearest);
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
        bool panic = nearest != null && Vector3.Distance(origin, nearest.position) < panicDistance;
        if (hasFleeTarget && !panic && Vector3.Distance(origin, fleeTarget) > 2f)
        {
            float current = ScoreFleePoint(origin, fleeTarget, nearest);
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
    /// 도주 지점의 점수 = 모든 좀비와의 최소 거리 + 그쪽으로 열린 정도.
    /// 한 마리만 보면 다른 좀비 품으로 뛰어들게 된다 —— 포위가 통하는 이유.
    /// </summary>
    private float ScoreFleePoint(Vector3 origin, Vector3 point, Transform nearest)
    {
        float minThreatDist = float.MaxValue;
        foreach (var t in threats)
        {
            if (t == null) continue;
            minThreatDist = Mathf.Min(minThreatDist, Vector3.Distance(point, t.position));
        }
        if (threats.Count == 0 && nearest != null)
            minThreatDist = Vector3.Distance(point, nearest.position);
        if (minThreatDist == float.MaxValue) minThreatDist = 0f;

        return minThreatDist + Vector3.Distance(origin, point) * opennessWeight;
    }

    /// <summary>단순히 반대 방향. 구석에 잘 몰린다 —— 난이도 낮춤용.</summary>
    private Vector3 NaiveFleePoint(Transform from)
    {
        if (from == null) return PickRoamPoint();

        Vector3 away = (transform.position - from.position).normalized;
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

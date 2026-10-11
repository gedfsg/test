using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 학습용 더미 플레이어. 사람이 조작할 수 없으므로 스크립트로 움직인다.
///
/// 중요: 이 오브젝트는 Tag가 "Player"여야 한다 (ZombiePerception이 태그로 찾는다).
///       Health 컴포넌트도 있어야 좀비의 공격이 정상 처리된다.
///
/// 행동이 한 가지로 고정되면 좀비가 그 패턴에만 최적화되므로,
/// 에피소드마다 성향(겁많음/태평함/이동속도)을 무작위로 바꾼다.
/// 이게 정책 다양성 확보의 핵심 장치 중 하나다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class DummyPlayer : MonoBehaviour
{
    [Header("이동")]
    public float minSpeed = 3f;
    public float maxSpeed = 5f;
    [Tooltip("다음 목적지를 고르는 반경")]
    public float roamRadius = 15f;

    [Header("도주 성향 (에피소드마다 무작위)")]
    [Tooltip("좀비가 이 거리 안에 들어오면 도망친다. 범위 안에서 매번 추첨")]
    public float minFleeDistance = 4f;
    public float maxFleeDistance = 14f;

    [Header("참조")]
    [Tooltip("비우면 씬에서 자동 탐색")]
    public Transform threat;

    private NavMeshAgent agent;
    private float fleeDistance;
    private float repathTimer;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (threat == null)
        {
            var z = FindAnyObjectByType<ZombieController>();
            if (z != null) threat = z.transform;
        }
        Randomize();
    }

    /// <summary>에피소드 리셋 시 Arena가 호출.</summary>
    public void Randomize()
    {
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
        if (agent == null || !agent.isOnNavMesh) return;

        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f) return;
        repathTimer = 0.3f;

        bool fleeing = threat != null &&
                       Vector3.Distance(transform.position, threat.position) < fleeDistance;

        Vector3 target = fleeing ? PickFleePoint() : PickRoamPoint();
        agent.SetDestination(target);
    }

    private Vector3 PickFleePoint()
    {
        Vector3 away = (transform.position - threat.position).normalized;
        // 정확히 반대로만 가면 벽에 박히므로 각도를 흩뿌린다
        away = Quaternion.Euler(0f, Random.Range(-50f, 50f), 0f) * away;

        Vector3 candidate = transform.position + away * 10f;
        return NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas)
            ? hit.position
            : transform.position;
    }

    private Vector3 PickRoamPoint()
    {
        // 이미 목적지로 가는 중이면 유지
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

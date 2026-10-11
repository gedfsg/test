using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 학습 구역 하나. 좀비 1 + 더미 플레이어 1 + 벽 몇 개로 구성한다.
///
/// 이 프리팹을 8~16개 복제해 두면 한 번의 Play로 그만큼 병렬로 경험이 쌓여
/// 학습 속도가 거의 비례해서 빨라진다. 구역끼리는 서로 간섭하지 않도록
/// 충분히 떨어뜨려 배치할 것 (NavMesh도 구역별로 끊어져 있어야 한다).
///
/// 배치 구조:
///   Arena (이 스크립트)
///    ├─ Ground / Walls
///    ├─ Zombie       (ZombieController + ZombieMLBrain + ZombieAgent + DecisionRequester)
///    └─ DummyPlayer  (Tag = Player, Health, NavMeshAgent, DummyPlayer)
/// </summary>
public class ZombieTrainingArena : MonoBehaviour
{
    [Header("구성원")]
    public ZombieController zombie;
    public DummyPlayer      dummy;

    [Header("배치")]
    [Tooltip("구역 중심에서 스폰 위치를 뽑을 반경")]
    public float spawnRadius = 18f;
    [Tooltip("좀비와 플레이어의 최소 시작 거리. 너무 가까우면 학습이 시시해진다")]
    public float minSeparation = 12f;

    [Header("포획 판정")]
    [Tooltip("이 거리 안에 들어오면 포획으로 간주하고 에피소드 종료")]
    public float captureRadius = 1.6f;

    [Header("무작위화 (정책 다양성 확보)")]
    [Tooltip("에피소드마다 위치를 바꿀지. 끄면 좀비가 한 경로만 외운다")]
    public bool randomizeSpawns = true;
    [Tooltip("회전시킬 장애물들. 비워도 무방하다")]
    public Transform[] movableObstacles;
    public float obstacleJitter = 3f;

    private ZombieAgent agentRef;
    private Vector3[]   obstacleHomes;

    void Awake()
    {
        if (zombie != null) agentRef = zombie.GetComponent<ZombieAgent>();

        if (movableObstacles != null)
        {
            obstacleHomes = new Vector3[movableObstacles.Length];
            for (int i = 0; i < movableObstacles.Length; i++)
                if (movableObstacles[i] != null)
                    obstacleHomes[i] = movableObstacles[i].position;
        }
    }

    void Update()
    {
        if (zombie == null || dummy == null || agentRef == null) return;

        float d = Vector3.Distance(zombie.transform.position, dummy.transform.position);
        if (d <= captureRadius) agentRef.OnCaptured();
    }

    /// <summary>ZombieAgent.OnEpisodeBegin에서 호출.</summary>
    public void ResetArena()
    {
        JitterObstacles();

        Vector3 zombiePos = transform.position;
        Vector3 dummyPos  = transform.position;

        if (randomizeSpawns)
        {
            zombiePos = RandomPoint(transform.position, spawnRadius);

            // 최소 거리를 만족하는 플레이어 위치를 몇 번 찾아본다
            for (int i = 0; i < 12; i++)
            {
                dummyPos = RandomPoint(transform.position, spawnRadius);
                if (Vector3.Distance(dummyPos, zombiePos) >= minSeparation) break;
            }
        }

        if (dummy != null)
        {
            dummy.TeleportTo(dummyPos);
            dummy.Randomize();   // 도주 성향도 매 에피소드 새로 뽑는다
        }

        if (zombie != null)
            zombie.ResetForEpisode(zombiePos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
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

    private Vector3 RandomPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 16; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(rnd.x, 0f, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
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

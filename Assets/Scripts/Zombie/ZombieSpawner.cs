using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비 스포너 - 건물 내부(실내)와 나무/야외(실외) 구역을 분리 스폰
/// 낮: 실내 안 나옴 / 야외 조금 | 밤: 실내 많이 + 야외 낮보다 많이
/// </summary>
public class ZombieSpawner : MonoBehaviour
{
    [Header("좀비 프리팹")]
    public GameObject[] zombiePrefabs;

    [Header("━━ 실내 스폰 (건물 안) ━━")]
    [Tooltip("건물 내부에 배치한 빈 오브젝트들")]
    public Transform[] indoorSpawnPoints;
    [Tooltip("밤: 실내 스폰 간격 (초)")]
    public float nightIndoorInterval = 2f;
    [Tooltip("낮: 실내는 사실상 안 나옴")]
    public float dayIndoorInterval = 9999f;
    public int maxIndoorZombies = 15;

    [Tooltip("밤: 실내 한 번 스폰할 때 마리 수")]
    public int nightIndoorBurst = 2;
    [Tooltip("낮: 실내 한 번 스폰할 때 마리 수")]
    public int dayIndoorBurst = 1;

    [Header("━━ 실외 스폰 (나무/야외) ━━")]
    [Tooltip("나무 근처에 배치한 빈 오브젝트들")]
    public Transform[] outdoorSpawnPoints;
    [Tooltip("밤: 야외 스폰 간격 (초)")]
    public float nightOutdoorInterval = 5f;
    [Tooltip("낮: 야외 스폰 간격 (초)")]
    public float dayOutdoorInterval = 18f;
    public int maxOutdoorZombies = 8;
    [Tooltip("밤: 야외 한 번 스폰할 때 마리 수")]
    public int nightOutdoorBurst = 3;
    [Tooltip("낮: 야외 한 번 스폰할 때 마리 수 ⭐ 낮에 여러마리 원하면 여기 올리기")]
    public int dayOutdoorBurst = 3;

    [Header("스폰 포인트 없을 때 플레이어 기준 랜덤 스폰")]
    public bool useRandomFallback = true;
    public float spawnRadius = 25f;
    public float minSpawnDistance = 15f;

    [Header("스폰 포인트 주변 랜덤 반경")]
    [Tooltip("스폰 포인트 주변 N미터 안에서 랜덤 위치로 스폰 (겹침 방지)")]
    public float spawnPointJitter = 4f;

    [Header("디버그")]
    public bool debugLog = true;

    // ─────────────────────────────────────────────
    private float indoorTimer;
    private float outdoorTimer;
    private readonly List<GameObject> indoorZombies  = new List<GameObject>();
    private readonly List<GameObject> outdoorZombies = new List<GameObject>();
    private Transform player;

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        // 타이머 약간 엇갈리게 시작
        indoorTimer  = Random.Range(1f, 3f);
        outdoorTimer = Random.Range(3f, 6f);
    }

    void Update()
    {
        indoorZombies .RemoveAll(z => z == null);
        outdoorZombies.RemoveAll(z => z == null);

        bool isNight = DayNightCycle.Instance != null &&
                       DayNightCycle.Instance.CurrentPhase == DayNightCycle.Phase.Night;

        // ── 실내 스폰 ──
        if (indoorZombies.Count < maxIndoorZombies)
        {
            indoorTimer -= Time.deltaTime;
            if (indoorTimer <= 0f)
            {
                float interval = isNight ? nightIndoorInterval : dayIndoorInterval;
                int burst = isNight ? nightIndoorBurst : dayIndoorBurst;

                if (indoorSpawnPoints != null && indoorSpawnPoints.Length > 0)
                {
                    for (int b = 0; b < burst; b++)
                    {
                        if (indoorZombies.Count >= maxIndoorZombies) break;
                        SpawnAt(indoorSpawnPoints, indoorZombies);
                    }
                }
                indoorTimer = interval + Random.Range(-0.5f, 0.5f);
            }
        }

        // ── 실외 스폰 ──
        if (outdoorZombies.Count < maxOutdoorZombies)
        {
            outdoorTimer -= Time.deltaTime;
            if (outdoorTimer <= 0f)
            {
                float interval = isNight ? nightOutdoorInterval : dayOutdoorInterval;
                int burst = isNight ? nightOutdoorBurst : dayOutdoorBurst;

                for (int b = 0; b < burst; b++)
                {
                    if (outdoorZombies.Count >= maxOutdoorZombies) break;
                    if (outdoorSpawnPoints != null && outdoorSpawnPoints.Length > 0)
                        SpawnAt(outdoorSpawnPoints, outdoorZombies);
                    else if (useRandomFallback && player != null)
                        SpawnRandom(outdoorZombies);
                }
                outdoorTimer = interval + Random.Range(-1f, 1f);
            }
        }
    }

    // ─────────────────────────────────────────────

    void SpawnAt(Transform[] points, List<GameObject> list)
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0)
        {
            if (debugLog) Debug.LogWarning("[ZombieSpawner] 좀비 프리팹이 비어있음!");
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            Transform pt = points[Random.Range(0, points.Length)];
            if (pt == null) continue;

            // 스폰 포인트 주변에 랜덤 오프셋 추가 (겹침 방지)
            Vector2 rnd2D = Random.insideUnitCircle * spawnPointJitter;
            Vector3 searchPos = pt.position + new Vector3(rnd2D.x, 0, rnd2D.y);

            // 1차: 그 자리에서 NavMesh 찾기 (jitter 반경만큼 넉넉히)
            if (NavMesh.SamplePosition(searchPos, out NavMeshHit hit, spawnPointJitter + 2f, NavMesh.AllAreas))
            {
                Spawn(hit.position, list);
                if (debugLog) Debug.Log($"[ZombieSpawner] ✅ 스폰 at {pt.name} → {hit.position}");
                return;
            }

            // 2차: 아래로 레이캐스트해서 지면 찾기 (공중에 떠 있는 스폰 포인트 대비)
            if (Physics.Raycast(searchPos + Vector3.up * 2f, Vector3.down, out RaycastHit groundHit, 100f))
            {
                if (NavMesh.SamplePosition(groundHit.point, out NavMeshHit hit2, 5f, NavMesh.AllAreas))
                {
                    Spawn(hit2.position, list);
                    if (debugLog) Debug.Log($"[ZombieSpawner] ✅ 스폰 at {pt.name} (지면 자동찾기) → {hit2.position}");
                    return;
                }
            }

            // 3차: 더 큰 반경으로 한 번 더 시도
            if (NavMesh.SamplePosition(searchPos, out NavMeshHit hit3, 30f, NavMesh.AllAreas))
            {
                Spawn(hit3.position, list);
                if (debugLog) Debug.Log($"[ZombieSpawner] ✅ 스폰 at {pt.name} (넓은 반경) → {hit3.position}");
                return;
            }

            if (debugLog)
                Debug.LogWarning($"[ZombieSpawner] ❌ {pt.name} 근처 NavMesh 못 찾음 (위치: {pt.position})");
        }
    }

    void SpawnRandom(List<GameObject> list)
    {
        for (int i = 0; i < 5; i++)
        {
            Vector2 rnd = Random.insideUnitCircle.normalized * Random.Range(minSpawnDistance, spawnRadius);
            Vector3 candidate = player.position + new Vector3(rnd.x, 0, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                Spawn(hit.position, list);
                return;
            }
        }
    }

    void Spawn(Vector3 pos, List<GameObject> list)
    {
        GameObject prefab = zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];
        GameObject zombie = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));

        // NavMeshAgent를 NavMesh 위로 강제 Warp (안 그러면 isOnNavMesh=false 가 됨)
        NavMeshAgent na = zombie.GetComponent<NavMeshAgent>();
        if (na != null && NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            na.Warp(hit.position);
        }

        list.Add(zombie);
    }

    void OnDrawGizmosSelected()
    {
        // 실내 - 빨간 구
        if (indoorSpawnPoints != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            foreach (var pt in indoorSpawnPoints)
                if (pt != null) Gizmos.DrawSphere(pt.position, 0.4f);
        }
        // 실외 - 노란 구
        if (outdoorSpawnPoints != null)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.8f);
            foreach (var pt in outdoorSpawnPoints)
                if (pt != null) Gizmos.DrawSphere(pt.position, 0.4f);
        }
    }
}

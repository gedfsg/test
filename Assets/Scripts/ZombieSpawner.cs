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
    [Tooltip("zombiePrefabs와 같은 순서의 스폰 가중치. 비워두거나 길이가 안 맞으면 균등 확률로 폴백.")]
    public float[] spawnWeights;

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

    [Header("━━ 스트리밍 (멀리 있는 좀비 정리 → 플레이어 근처에 새로 생성) ━━")]
    [Tooltip("켜면 플레이어에서 despawnDistance보다 멀고 화면 밖인 좀비를 지워서 최대 수 자리를 비움. " +
             "안 켜면 맵 곳곳에 생긴 좀비가 안 사라져서 후반엔 최대 수가 꽉 찬 채 플레이어 근처엔 새 좀비가 안 나옴 (7일차 실측)")]
    public bool enableStreaming = true;
    [Tooltip("이보다 먼 좀비는 정리 대상 (m)")]
    public float despawnDistance = 70f;
    [Tooltip("실내 스폰 포인트는 플레이어에서 이 거리 안의 것만 사용 (m)")]
    public float indoorSpawnMaxDistance = 55f;
    [Tooltip("실내 스폰 포인트 최소 거리 (눈앞에서 갑자기 생기지 않게, m)")]
    public float indoorSpawnMinDistance = 12f;
    [Tooltip("정리 검사 간격 (초)")]
    public float streamCheckInterval = 2f;
    [Tooltip("1일차 최대 수 비율. 스트리밍으로 최대 수 전부가 플레이어 근처에 모이므로 초반엔 줄여둠 (65/35 × 0.4 = 26/14)")]
    [Range(0.1f, 1f)] public float startCapRatio = 0.4f;
    [Tooltip("하루 지날 때마다 최대 수 비율 증가 (7일차 = 0.4 + 0.1×6 = 100%)")]
    public float capRatioPerDay = 0.1f;

    [Header("디버그")]
    public bool debugLog = true;

    // ─────────────────────────────────────────────
    private float indoorTimer;
    private float outdoorTimer;
    private readonly List<GameObject> indoorZombies  = new List<GameObject>();
    private readonly List<GameObject> outdoorZombies = new List<GameObject>();
    private Transform player;
    private bool  lastIsNight;
    private float countLogTimer;
    private float streamTimer;
    private readonly List<Transform> nearIndoorPoints = new List<Transform>(); // 스트리밍용: 플레이어 근처 실내 포인트

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

        // ── 스트리밍: 먼 좀비 정리 ──
        if (enableStreaming && player != null)
        {
            streamTimer -= Time.deltaTime;
            if (streamTimer <= 0f)
            {
                streamTimer = streamCheckInterval;
                // extraZombies(창고/상자 이벤트 좀비)는 최대 수에 안 들어가고 건물에 미리 배치된 것이라 정리 안 함
                int removed = DespawnFar(indoorZombies) + DespawnFar(outdoorZombies);
                if (debugLog && removed > 0) Debug.Log($"[ZombieSpawner] 스트리밍: 플레이어에서 {despawnDistance}m 넘게 먼 좀비 {removed}마리 정리");
            }
        }

        bool isNight = DayNightCycle.Instance != null &&
                       DayNightCycle.Instance.CurrentPhase == DayNightCycle.Phase.Night;

        if (debugLog)
        {
            if (isNight != lastIsNight)
            {
                lastIsNight = isNight;
                Debug.Log(isNight
                    ? $"[ZombieSpawner] 🌙 밤 시작 - 스폰 간격 실내{nightIndoorInterval}s/{nightIndoorBurst}마리, 실외{nightOutdoorInterval}s/{nightOutdoorBurst}마리로 전환"
                    : $"[ZombieSpawner] ☀ 낮 시작 - 스폰 간격 실내{dayIndoorInterval}s/{dayIndoorBurst}마리, 실외{dayOutdoorInterval}s/{dayOutdoorBurst}마리로 전환");
            }

            countLogTimer += Time.deltaTime;
            if (countLogTimer >= 5f)
            {
                countLogTimer = 0f;
                Debug.Log($"[ZombieSpawner] 현재 활성 좀비: 실내 {indoorZombies.Count}/{Mathf.RoundToInt(maxIndoorZombies * CapScale())}, 실외 {outdoorZombies.Count}/{Mathf.RoundToInt(maxOutdoorZombies * CapScale())}, 합계 {indoorZombies.Count + outdoorZombies.Count}");
            }
        }

        // 일차별 실제 최대 수 (스트리밍 끄면 예전처럼 최대 수 그대로)
        float capScale = CapScale();
        int maxIn  = Mathf.RoundToInt(maxIndoorZombies  * capScale);
        int maxOut = Mathf.RoundToInt(maxOutdoorZombies * capScale);

        // ── 실내 스폰 ──
        if (indoorZombies.Count < maxIn)
        {
            indoorTimer -= Time.deltaTime;
            if (indoorTimer <= 0f)
            {
                float interval = isNight ? nightIndoorInterval : dayIndoorInterval;
                int burst = isNight ? nightIndoorBurst : dayIndoorBurst;

                // 스트리밍 중엔 플레이어 근처(indoorSpawnMinDistance~indoorSpawnMaxDistance) 실내 포인트만 사용
                Transform[] pts = enableStreaming && player != null ? NearIndoorPoints() : indoorSpawnPoints;
                if (pts != null && pts.Length > 0)
                {
                    for (int b = 0; b < burst; b++)
                    {
                        if (indoorZombies.Count >= maxIn) break;
                        SpawnAt(pts, indoorZombies);
                    }
                }
                else if (enableStreaming && useRandomFallback && player != null)
                {
                    // 근처에 실내 포인트가 없으면(예: 경찰서는 가장 가까운 포인트가 61m) 플레이어 주변 랜덤 위치로 채움
                    // → 어디에 있든 플레이어 주변 좀비 밀도 유지
                    for (int b = 0; b < burst; b++)
                    {
                        if (indoorZombies.Count >= maxIn) break;
                        SpawnRandom(indoorZombies);
                    }
                }
                indoorTimer = interval + Random.Range(-0.5f, 0.5f);
            }
        }

        // ── 실외 스폰 ──
        if (outdoorZombies.Count < maxOut)
        {
            outdoorTimer -= Time.deltaTime;
            if (outdoorTimer <= 0f)
            {
                float interval = isNight ? nightOutdoorInterval : dayOutdoorInterval;
                int burst = isNight ? nightOutdoorBurst : dayOutdoorBurst;

                for (int b = 0; b < burst; b++)
                {
                    if (outdoorZombies.Count >= maxOut) break;
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

    /// <summary>일차별 최대 수 비율 (1일차 startCapRatio → 하루마다 +capRatioPerDay, 최대 1)</summary>
    float CapScale()
    {
        if (!enableStreaming) return 1f;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        return Mathf.Clamp(startCapRatio + capRatioPerDay * (day - 1), 0.1f, 1f);
    }

    /// <summary>플레이어에서 despawnDistance보다 멀고 카메라에 안 보이는 좀비 삭제. 지운 수 반환</summary>
    int DespawnFar(List<GameObject> list)
    {
        int removed = 0;
        Camera cam = Camera.main;
        float sqr = despawnDistance * despawnDistance;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            GameObject z = list[i];
            if (z == null) { list.RemoveAt(i); continue; }
            if ((z.transform.position - player.position).sqrMagnitude < sqr) continue;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(z.transform.position);
                if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f) continue; // 화면에 보이면 유지
            }
            list.RemoveAt(i);
            Destroy(z);
            removed++;
        }
        return removed;
    }

    /// <summary>플레이어 기준 indoorSpawnMinDistance~indoorSpawnMaxDistance 안의 실내 스폰 포인트</summary>
    Transform[] NearIndoorPoints()
    {
        nearIndoorPoints.Clear();
        if (indoorSpawnPoints == null) return null;
        float min = indoorSpawnMinDistance * indoorSpawnMinDistance, max = indoorSpawnMaxDistance * indoorSpawnMaxDistance;
        foreach (var pt in indoorSpawnPoints)
        {
            if (pt == null) continue;
            float d = (pt.position - player.position).sqrMagnitude;
            if (d >= min && d <= max) nearIndoorPoints.Add(pt);
        }
        return nearIndoorPoints.ToArray();
    }

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

    GameObject PickWeightedPrefab()
    {
        if (spawnWeights == null || spawnWeights.Length != zombiePrefabs.Length)
            return zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];

        float total = 0f;
        foreach (var w in spawnWeights) total += w;
        float r = Random.Range(0f, total);
        float acc = 0f;
        for (int i = 0; i < spawnWeights.Length; i++)
        {
            acc += spawnWeights[i];
            if (r <= acc) return zombiePrefabs[i];
        }
        return zombiePrefabs[zombiePrefabs.Length - 1];
    }

    // 상자 열기/창고 같은 이벤트용 추가 스폰 (실내/실외 최대 수 제한에 안 들어감)
    private readonly List<GameObject> extraZombies = new List<GameObject>();

    /// <summary>center 기준 minDist~maxDist 거리의 NavMesh 위에 좀비 1마리 스폰. 실패하면 null</summary>
    public GameObject SpawnExtraNear(Vector3 center, float minDist, float maxDist)
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0) return null;
        for (int i = 0; i < 20; i++)
        {
            Vector2 rnd = Random.insideUnitCircle.normalized * Random.Range(minDist, maxDist);
            Vector3 candidate = center + new Vector3(rnd.x, 0f, rnd.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                Spawn(hit.position, extraZombies);
                return extraZombies[extraZombies.Count - 1];
            }
        }
        return null;
    }

    void Spawn(Vector3 pos, List<GameObject> list)
    {
        GameObject prefab = PickWeightedPrefab();
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

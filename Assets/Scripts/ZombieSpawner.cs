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

    [Header("━━ 스폰 위치 제한 (1층 바닥만) ━━")]
    [Tooltip("지형 높이보다 이 이상(m) 높은 곳엔 스폰 안 함. 건물 벽 콜라이더로 NavMesh를 구우면 지붕 위에도 NavMesh가 생겨서 " +
             "좀비가 위층/지붕(지면 +16m)에 떠 있었음 (실내는 1층만 보이게 잘라서 위층 좀비가 검정 실루엣으로 보임)")]
    public float maxSpawnHeightAboveGround = 1.5f;
    [Tooltip("지형보다 이만큼(m) 낮은 곳까지는 허용 (지형보다 살짝 낮게 깔린 건물 바닥)")]
    public float maxSpawnDepthBelowGround = 3f;
    [Tooltip("켜면 플레이어까지 NavMesh 경로가 이어지는 곳에만 스폰 (문 없는 방/막힌 마당/지붕 같은 못 가는 공간 제외)")]
    public bool requireReachable = true;
    [Tooltip("플레이어와 이보다 멀면(m) 플레이어 대신 '플레이어 쪽으로 40m 지점'까지 이어지는지로 판정 " +
             "(수백 m 경로 계산은 탐색 한도에 걸려 이어져 있어도 PathPartial이 나옴)")]
    public float reachCheckFullPathDistance = 90f;

    [Header("━━ 스폰 위치 제한 (문 앞 / 화면 안 금지) ━━")]
    [Tooltip("건물 문(BuildingWallCollider의 doorSlot) 중심에서 이 반경(m, 수평 거리) 안에는 스폰 안 함. " +
             "플레이테스트: 건물에서 나오자마자 문 앞 좀비에게 잡힘. 0이면 끔")]
    public float doorNoSpawnRadius = 5f;
    [Tooltip("켜면 카메라 화면 밖에서만 스폰 (눈앞에서 좀비가 갑자기 생기지 않게)")]
    public bool spawnOnlyOffscreen = true;
    [Tooltip("화면 가장자리 바깥 여유 (뷰포트 비율). 0.1 = 화면 경계에서 10% 더 바깥까지도 '보이는 것'으로 취급 → 가장자리에서 생기자마자 보이는 것 방지")]
    public float offscreenMargin = 0.1f;

    [Header("━━ 낮/밤 밸런스 ━━")]
    [Tooltip("낮(Phase.Day)에는 실외 최대 수에 이 비율을 곱함 (0.25 = 75% 감소 → 낮 바깥은 비교적 안전). " +
             "실내는 그대로 (파밍 위험 유지). 노을/밤/새벽은 그대로")]
    [Range(0f, 1f)] public float dayOutdoorCapRatio = 0.25f;

    [Header("━━ 최종 공격 (마지막 날 좀비 떼) ━━")]
    [Tooltip("마지막 날엔 플레이어 주변에 주기적으로 좀비 떼가 생기고, 떼 좀비는 거리 상관없이 플레이어를 추적")]
    public bool enableFinalAssault = true;
    [Tooltip("이 날부터 최종 공격. 0이면 DayNightCycle.totalDays (7일차)")]
    public int finalAssaultDay = 0;
    [Tooltip("낮: 좀비 떼 간격(초) / 마리 수")]
    public float hordeInterval = 30f;
    public int hordeSize = 6;
    [Tooltip("밤: 좀비 떼 간격(초) / 마리 수")]
    public float nightHordeInterval = 15f;
    public int nightHordeSize = 10;
    [Tooltip("동시에 살아있는 떼 좀비 최대 수 (실내/실외 최대 수와 별개)")]
    public int maxHordeZombies = 40;
    [Tooltip("떼가 생기는 거리 (플레이어 기준, m) - 화면 밖에서 몰려오게")]
    public float hordeMinDistance = 25f;
    public float hordeMaxDistance = 35f;

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
    private float hordeTimer = 5f;            // 최종 공격 시작 후 첫 떼까지
    private bool  finalAssaultAnnounced;
    private readonly List<GameObject> hordeZombies = new List<GameObject>();
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
        bool streamTick = false; // 이번 프레임이 정리 검사 주기인지 (낮 실외 정리도 같은 주기로)
        if (enableStreaming && player != null)
        {
            streamTimer -= Time.deltaTime;
            if (streamTimer <= 0f)
            {
                streamTimer = streamCheckInterval;
                streamTick = true;
                // extraZombies(창고/상자 이벤트 좀비)는 최대 수에 안 들어가고 건물에 미리 배치된 것이라 정리 안 함
                int removed = DespawnFar(indoorZombies) + DespawnFar(outdoorZombies);
                // 이미 위층/지붕에 올라가 있는 좀비 제거 (수정 전 스폰분, NavMesh 복구 워프로 지붕에 올라간 경우)
                int floating = RemoveFloating(indoorZombies) + RemoveFloating(outdoorZombies) + RemoveFloating(extraZombies) + RemoveFloating(hordeZombies);
                if (debugLog && floating > 0) Debug.Log($"[ZombieSpawner] 위층/지붕에 뜬 좀비 {floating}마리 제거");
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

        // ── 최종 공격: 마지막 날 좀비 떼 ──
        UpdateFinalAssault(isNight);

        // 일차별 실제 최대 수 (스트리밍 끄면 예전처럼 최대 수 그대로)
        float capScale = CapScale();
        int maxIn  = Mathf.RoundToInt(maxIndoorZombies  * capScale);
        int maxOut = Mathf.RoundToInt(maxOutdoorZombies * capScale);
        bool isDayPhase = DayNightCycle.Instance != null && DayNightCycle.Instance.CurrentPhase == DayNightCycle.Phase.Day;
        if (isDayPhase) maxOut = Mathf.RoundToInt(maxOut * dayOutdoorCapRatio); // 낮엔 실외 좀비 대폭 감소

        // 밤에 늘어난 실외 좀비가 낮 최대 수보다 많으면, 화면 밖에 있는 먼 것부터 정리 (streamCheckInterval마다)
        if (isDayPhase && outdoorZombies.Count > maxOut && streamTick)
        {
            int trimmed = TrimOffscreen(outdoorZombies, maxOut);
            if (debugLog && trimmed > 0) Debug.Log($"[ZombieSpawner] ☀ 낮: 실외 좀비 {trimmed}마리 정리 → {outdoorZombies.Count}/{maxOut}");
        }

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

    /// <summary>마지막 날(finalAssaultDay)이면 hordeInterval마다 플레이어 25~35m 둘레에 좀비 떼 생성 (밤엔 더 자주, 더 많이)</summary>
    void UpdateFinalAssault(bool isNight)
    {
        var dnc = DayNightCycle.Instance;
        if (!enableFinalAssault || dnc == null || player == null) return;
        int day = finalAssaultDay > 0 ? finalAssaultDay : dnc.totalDays;
        if (dnc.CurrentDay < day) return;

        if (!finalAssaultAnnounced)
        {
            finalAssaultAnnounced = true;
            PickupFeedbackUI.Instance?.ShowWarning("최종 공격! 좀비 떼가 몰려온다");
            Debug.Log($"[ZombieSpawner] ☠ {dnc.CurrentDay}일차 최종 공격 시작");
        }

        hordeZombies.RemoveAll(z => z == null);
        hordeTimer -= Time.deltaTime;
        if (hordeTimer > 0f) return;
        hordeTimer = isNight ? nightHordeInterval : hordeInterval;

        int size = isNight ? nightHordeSize : hordeSize;
        int spawned = 0;
        // 떼는 한 방향에서 몰려오게: 기준 방향 ±60° 안에서 생성
        float baseAngle = Random.Range(0f, 360f);
        for (int i = 0; i < size * 4 && spawned < size && hordeZombies.Count < maxHordeZombies; i++)
        {
            float a = (baseAngle + Random.Range(-60f, 60f)) * Mathf.Deg2Rad;
            Vector3 candidate = player.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(hordeMinDistance, hordeMaxDistance);
            candidate.y = GroundY(candidate) + 0.5f; // 1층 바닥 높이에서 찾기
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            if (!Spawn(hit.position, hordeZombies)) continue;
            var zc = hordeZombies[hordeZombies.Count - 1].GetComponent<ZombieController>();
            if (zc != null) zc.huntPlayer = true; // 거리 상관없이 플레이어 추적
            spawned++;
        }
        if (debugLog) Debug.Log($"[ZombieSpawner] ☠ 좀비 떼 {spawned}마리 (떼 좀비 {hordeZombies.Count}/{maxHordeZombies})");
    }

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

    /// <summary>list가 cap마리가 될 때까지 화면 밖 + 플레이어 감지 범위(30m) 밖인 좀비를 먼 순서로 삭제. 지운 수 반환</summary>
    int TrimOffscreen(List<GameObject> list, int cap)
    {
        if (player == null) return 0;
        list.RemoveAll(z => z == null);
        Camera cam = Camera.main;
        list.Sort((a, b) => (b.transform.position - player.position).sqrMagnitude.CompareTo((a.transform.position - player.position).sqrMagnitude)); // 먼 순
        int removed = 0;
        for (int i = 0; i < list.Count && list.Count > cap; )
        {
            GameObject z = list[i];
            bool visible = false;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(z.transform.position);
                visible = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
            }
            if (visible || (z.transform.position - player.position).sqrMagnitude < 30f * 30f) { i++; continue; } // 보이거나 쫓아오는 중이면 유지
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
            searchPos.y = GroundY(searchPos) + 0.5f; // 스폰 포인트 높이 대신 1층 바닥 높이로 고정 (위층/지붕 NavMesh를 안 집게)

            // 1차: 그 자리에서 NavMesh 찾기 (jitter 반경만큼 넉넉히)
            if (NavMesh.SamplePosition(searchPos, out NavMeshHit hit, spawnPointJitter + 2f, NavMesh.AllAreas))
            {
                if (Spawn(hit.position, list))
                {
                    if (debugLog) Debug.Log($"[ZombieSpawner] ✅ 스폰 at {pt.name} → {hit.position}");
                    return;
                }
            }

            // 2차: 아래로 레이캐스트해서 지면 찾기 (공중에 떠 있는 스폰 포인트 대비)
            if (Physics.Raycast(searchPos + Vector3.up * 2f, Vector3.down, out RaycastHit groundHit, 100f))
            {
                if (NavMesh.SamplePosition(groundHit.point, out NavMeshHit hit2, 5f, NavMesh.AllAreas))
                {
                    if (Spawn(hit2.position, list))
                    {
                        if (debugLog) Debug.Log($"[ZombieSpawner] ✅ 스폰 at {pt.name} (지면 자동찾기) → {hit2.position}");
                        return;
                    }
                }
            }

            // 3차: 더 큰 반경으로 한 번 더 시도
            if (NavMesh.SamplePosition(searchPos, out NavMeshHit hit3, 30f, NavMesh.AllAreas))
            {
                if (Spawn(hit3.position, list))
                {
                    if (debugLog) Debug.Log($"[ZombieSpawner] ✅ 스폰 at {pt.name} (넓은 반경) → {hit3.position}");
                    return;
                }
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
            candidate.y = GroundY(candidate) + 0.5f;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas) && Spawn(hit.position, list))
                return;
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
            candidate.y = GroundY(candidate) + 0.5f;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas) && Spawn(hit.position, extraZombies))
                return extraZombies[extraZombies.Count - 1];
        }
        return null;
    }

    /// <summary>지형 높이 (지형이 없으면 플레이어 높이 → 없으면 0)</summary>
    float GroundY(Vector3 p)
    {
        Terrain t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(p) + t.transform.position.y;
        return player != null ? player.position.y : 0f;
    }

    /// <summary>1층 바닥 높이인지 (지형 -maxSpawnDepthBelowGround ~ +maxSpawnHeightAboveGround)</summary>
    bool IsGroundFloor(Vector3 p)
    {
        if (Terrain.activeTerrain == null) return true;
        float dy = p.y - GroundY(p);
        return dy <= maxSpawnHeightAboveGround && dy >= -maxSpawnDepthBelowGround;
    }

    /// <summary>위층/지붕에 올라가 있는 좀비 삭제 (화면에 보여도 삭제 - 원래 있으면 안 되는 위치). 지운 수 반환</summary>
    int RemoveFloating(List<GameObject> list)
    {
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            GameObject z = list[i];
            if (z == null) { list.RemoveAt(i); continue; }
            if (z.transform.position.y - GroundY(z.transform.position) <= maxSpawnHeightAboveGround + 1f) continue; // 계단/턱 여유 1m
            list.RemoveAt(i);
            Destroy(z);
            removed++;
        }
        return removed;
    }

    private NavMeshPath reachPath;
    private int reachRejected; // 디버그: 못 가는 곳이라 거부한 수

    /// <summary>플레이어가 걸어서 갈 수 있는 곳인지 (스폰 지점 → 플레이어 NavMesh 경로가 끝까지 이어지는지)</summary>
    bool IsReachableFromPlayer(Vector3 pos)
    {
        if (!requireReachable || player == null) return true;
        // 플레이어가 점프 중 등으로 NavMesh 위가 아니면 검사 생략 (스폰을 막지 않음)
        if (!NavMesh.SamplePosition(player.position, out NavMeshHit ph, 3f, NavMesh.AllAreas)) return true;
        reachPath ??= new NavMeshPath();

        Vector3 target = ph.position;
        Vector3 flat = ph.position - pos; flat.y = 0f;
        if (flat.magnitude > reachCheckFullPathDistance)
        {
            // 멀면 플레이어 방향 40m 지점까지만 (그 근처 NavMesh가 없으면 판정 불가 → 허용)
            Vector3 mid = pos + flat.normalized * 40f;
            mid.y = GroundY(mid) + 0.5f;
            if (!NavMesh.SamplePosition(mid, out NavMeshHit mh, 6f, NavMesh.AllAreas)) return true;
            target = mh.position;
        }
        return NavMesh.CalculatePath(pos, target, NavMesh.AllAreas, reachPath) && reachPath.status == NavMeshPathStatus.PathComplete;
    }

    private List<Vector3> doorPositions; // 문 중심 월드 좌표 (처음 쓸 때 한 번 모음)

    /// <summary>건물 문 반경(doorNoSpawnRadius) 안인지 (수평 거리)</summary>
    bool IsNearDoor(Vector3 pos)
    {
        if (doorNoSpawnRadius <= 0f) return false;
        if (doorPositions == null)
        {
            doorPositions = new List<Vector3>();
            foreach (var wall in FindObjectsByType<BuildingWallCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (wall.hasDoor) doorPositions.Add(wall.transform.TransformPoint(wall.doorSlotCenter));
            if (debugLog) Debug.Log($"[ZombieSpawner] 문 {doorPositions.Count}개 주변 {doorNoSpawnRadius}m 스폰 금지");
        }
        float sqr = doorNoSpawnRadius * doorNoSpawnRadius;
        foreach (var d in doorPositions)
        {
            float dx = d.x - pos.x, dz = d.z - pos.z;
            if (dx * dx + dz * dz < sqr) return true;
        }
        return false;
    }

    /// <summary>카메라 화면(+offscreenMargin) 안에 보이는 위치인지 - 발 위치와 머리 높이(2m) 둘 중 하나라도 보이면 true</summary>
    bool IsOnScreen(Vector3 pos)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        return InViewport(cam, pos) || InViewport(cam, pos + Vector3.up * 2f);
    }

    bool InViewport(Camera cam, Vector3 p)
    {
        Vector3 vp = cam.WorldToViewportPoint(p);
        float m = offscreenMargin;
        return vp.z > 0f && vp.x > -m && vp.x < 1f + m && vp.y > -m && vp.y < 1f + m;
    }

    private int doorRejected, screenRejected; // 디버그: 문 앞 / 화면 안이라 거부한 수

    /// <summary>pos에 좀비 생성. 1층 바닥이 아니거나(위층/지붕) 문 앞이거나 화면 안이거나 플레이어가 못 가는 곳이면 생성 안 하고 false</summary>
    bool Spawn(Vector3 pos, List<GameObject> list)
    {
        if (!IsGroundFloor(pos)) return false;
        if (IsNearDoor(pos))
        {
            doorRejected++;
            if (debugLog && doorRejected % 20 == 1) Debug.Log($"[ZombieSpawner] 문 {doorNoSpawnRadius}m 안이라 스폰 안 함 (누적 {doorRejected}회) {pos}");
            return false;
        }
        if (spawnOnlyOffscreen && IsOnScreen(pos))
        {
            screenRejected++;
            if (debugLog && screenRejected % 20 == 1) Debug.Log($"[ZombieSpawner] 화면 안이라 스폰 안 함 (누적 {screenRejected}회) {pos}");
            return false;
        }
        if (!IsReachableFromPlayer(pos))
        {
            reachRejected++;
            if (debugLog && reachRejected % 20 == 1) Debug.Log($"[ZombieSpawner] 플레이어가 못 가는 곳이라 스폰 안 함 (누적 {reachRejected}회) {pos}");
            return false;
        }

        GameObject prefab = PickWeightedPrefab();
        GameObject zombie = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));

        // NavMeshAgent를 NavMesh 위로 강제 Warp (안 그러면 isOnNavMesh=false 가 됨)
        NavMeshAgent na = zombie.GetComponent<NavMeshAgent>();
        if (na != null && NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            na.Warp(hit.position);
        }

        list.Add(zombie);
        return true;
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

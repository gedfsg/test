using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 미션 시스템.
///
/// ━ 메인 미션: 전체 진행률 100% = 탈출 가능 ━
///  가중치: 열쇠 카드 1개 +15% (최대 60) / 서브 미션 완료 1개 +5% (최대 40) / 생존 하루 +1.43% (7일 10)
///  열쇠 카드 4개: 판마다 맵 전체 후보 중 랜덤 4곳 (KeyCardSpawnPoints). 위치 힌트 없음
///  탈출 지점: 진행률 100% + 7일차 이상이어야 열림, 도달하면 클리어. 7일이 지나도 게임오버 없이 Day가 계속 늘고 좀비가 강해짐 (ZombieSpawner)
///
/// ━ 서브 미션 (실패 없음) ━
///  매일(새벽→낮) 후보 중 랜덤 개수를 새로 할당 (Day 1: 2~3개 ... Day 6+: 4~5개). 미완료는 다음날 사라짐 (패널티 없음)
///  완료할 때마다 진행률 +5% + 아이템 보상
///
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성 (DeathUI와 같은 방식).
/// 씬에 직접 두면 Inspector 값(탈출 지점 위치 등)을 쓸 수 있음.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("메인 미션")]
    public int keyCardsRequired = 4;
    [Tooltip("열쇠 카드가 플레이어 시작 지점에서 떨어져야 하는 최소 거리 (m)")]
    public float keyCardMinPlayerDistance = 50f;
    [Tooltip("열쇠 카드끼리 떨어져야 하는 최소 거리 (m). 후보가 모자라면 무시")]
    public float keyCardMinSeparation = 40f;

    // 진행률 가중치 (%)
    public const float KeyCardWeight = 15f, SubMissionWeight = 5f, SubMissionWeightMax = 40f, SurvivalWeightMax = 10f;
    [Tooltip("탈출 지점 위치. 비워두면 시작 지점에서 먼 야외 NavMesh 위로 자동 선택")]
    public Transform escapePointOverride;
    [Tooltip("자동 선택 시 시작 지점에서 최소 거리 (m)")]
    public float escapeMinDistance = 180f;

    [Header("디버그")]
    public bool debugLog = true;

    // ── 상태 ──
    public int KeyCards { get; private set; }
    public int CompletedSubMissions { get; private set; }
    /// <summary>전체 진행률 0~100 (100이면 탈출 지점 열림)</summary>
    public float TotalProgress { get; private set; }
    /// <summary>탈출 지점이 열렸을 때: 진행률 100% + 7일차 (EscapePoint 알림용)</summary>
    public event System.Action EscapeUnlocked;
    bool escapeUnlocked;
    bool earlyFullNotified; // 7일차 전에 100% 달성 알림을 했는지
    public bool Finished { get; private set; }
    public bool Escaped { get; private set; }
    public readonly List<SubMission> subMissions = new List<SubMission>(); // 오늘 할당된 서브 미션
    public bool TodayAllDone { get; private set; }                         // "오늘 서브 100%"
    public const int MaxDailySubMissions = 5;
    List<BuildingInterior> buildingList;
    float nextProgressRefresh;
    static List<string> lastTitles; // 직전 할당 (같은 조합 연속 피하기)
    Transform playerTf;
    Vector3 lastPlayerPos;
    float rainMeters;
    public readonly List<KeyCardPickup> keyCardsInWorld = new List<KeyCardPickup>();
    public EscapePoint EscapePointObj { get; private set; }

    public int SurvivalGoalDays => DayNightCycle.Instance != null ? DayNightCycle.Instance.totalDays : 7;
    public int CurrentDay => DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
    public int CompletedDays => Mathf.Min(CurrentDay - 1, SurvivalGoalDays);
    public bool SurvivedEnough => CompletedDays >= SurvivalGoalDays;
    public bool HasAllKeyCards => KeyCards >= keyCardsRequired;
    public float KeyCardProgress => KeyCards * KeyCardWeight;
    public float SubMissionProgress => Mathf.Min(CompletedSubMissions * SubMissionWeight, SubMissionWeightMax);
    public float SurvivalProgress => (float)CompletedDays / SurvivalGoalDays * SurvivalWeightMax; // 하루 끝날 때마다 누적, 7일차 완료 시 최대
    public bool CanEscape => !Finished && escapeUnlocked;
    public bool ProgressFull => TotalProgress >= 100f;
    float ComputeProgress() => Mathf.Clamp(KeyCardProgress + SubMissionProgress + SurvivalProgress, 0f, 100f);

    // ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (GameObject.FindWithTag("Player") == null) return;
        if (FindFirstObjectByType<MissionManager>() != null) return;
        new GameObject("MissionManager").AddComponent<MissionManager>();
    }

    void Awake() => Instance = this;

    void OnEnable()
    {
        ZombieController.AnyDied += OnZombieDied;
        LootCrate.AnyOpened += OnCrateOpened;
        BuildingInterior.PlayerEntered += OnBuildingEntered;
        PlayerBuffManager.AnyConsumableUsed += OnConsumableUsed;
    }

    void OnDisable()
    {
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.onNewDay.RemoveListener(OnNewDay);
        ZombieController.AnyDied -= OnZombieDied;
        LootCrate.AnyOpened -= OnCrateOpened;
        BuildingInterior.PlayerEntered -= OnBuildingEntered;
        PlayerBuffManager.AnyConsumableUsed -= OnConsumableUsed;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        var buildings = new List<BuildingInterior>(FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None));
        buildingList = buildings;
        playerTf = GameObject.FindWithTag("Player").transform;
        lastPlayerPos = playerTf.position;
        PlaceKeyCards(buildings);
        PlaceEscapePoint(buildings);
        gameObject.AddComponent<MissionHUD>();
        AssignSubMissions(CurrentDay);
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.onNewDay.AddListener(OnNewDay);
    }

    void Update()
    {
        if (Finished) return;
        TrackRainDistance();
        if (Time.time < nextProgressRefresh) return;
        nextProgressRefresh = Time.time + 0.25f;
        RefreshProgress(); // 생존 일수는 시간으로 늘어남
    }

    // ── 메인 미션 ─────────────────────────────────

    void PlaceKeyCards(List<BuildingInterior> buildings)
    {
        Vector3 start = GameObject.FindWithTag("Player").transform.position;
        var candidates = KeyCardSpawnPoints.Build(buildings);
        candidates.RemoveAll(c => (c.pos - start).sqrMagnitude < keyCardMinPlayerDistance * keyCardMinPlayerDistance);
        if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 후보 {candidates.Count}곳");

        var picked = new List<Vector3>();
        // 1차: 서로 keyCardMinSeparation 이상 떨어진 곳 / 2차: 모자라면 거리 무시
        for (int pass = 0; pass < 2 && picked.Count < keyCardsRequired; pass++)
        {
            var left = new List<(Vector3 pos, string label)>(candidates);
            while (left.Count > 0 && picked.Count < keyCardsRequired)
            {
                int idx = Random.Range(0, left.Count);
                var (pos, label) = left[idx];
                left.RemoveAt(idx);
                if (picked.Contains(pos)) continue;
                if (pass == 0 && picked.Exists(q => (q - pos).sqrMagnitude < keyCardMinSeparation * keyCardMinSeparation)) continue;
                picked.Add(pos);
                var card = KeyCardPickup.Create(pos, label);
                card.transform.SetParent(transform, true);
                keyCardsInWorld.Add(card);
                if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 {picked.Count} → {label} {pos}");
            }
        }
        if (picked.Count < keyCardsRequired) Debug.LogError($"[MissionManager] 열쇠 카드 후보 부족: {picked.Count}/{keyCardsRequired}");
    }

    public static string BuildingLabel(LootDatabase.BuildingKind k) => k switch
    {
        LootDatabase.BuildingKind.Police => "경찰서",
        LootDatabase.BuildingKind.Hospital => "병원",
        LootDatabase.BuildingKind.Mart => "마트",
        LootDatabase.BuildingKind.Church => "교회",
        LootDatabase.BuildingKind.Warehouse => "창고",
        _ => "민가",
    };

    void PlaceEscapePoint(List<BuildingInterior> buildings)
    {
        Vector3 pos;
        if (escapePointOverride != null) pos = escapePointOverride.position;
        else if (!FindEscapePosition(buildings, out pos)) { Debug.LogWarning("[MissionManager] 탈출 지점 위치를 못 찾음"); return; }
        EscapePointObj = EscapePoint.Create(pos);
        EscapePointObj.transform.SetParent(transform, true);
        if (debugLog) Debug.Log($"[MissionManager] 탈출 지점 {pos} (시작 지점에서 {Vector3.Distance(pos, GameObject.FindWithTag("Player").transform.position):F0}m)");
    }

    /// <summary>시작 지점에서 escapeMinDistance 이상 떨어진 야외 NavMesh 점 중, 시작 지점에서 걸어서 갈 수 있는 곳 (못 찾으면 가장 먼 곳)</summary>
    bool FindEscapePosition(List<BuildingInterior> buildings, out Vector3 result)
    {
        Vector3 start = GameObject.FindWithTag("Player").transform.position;
        var terrain = Terrain.activeTerrain;
        Bounds area;
        if (terrain != null)
        {
            Vector3 size = terrain.terrainData.size, o = terrain.transform.position;
            area = new Bounds(o + size / 2f, new Vector3(size.x * 0.85f, size.y, size.z * 0.85f));
        }
        else area = new Bounds(start, new Vector3(600f, 100f, 600f));

        var trigs = new List<Bounds>();
        foreach (var b in buildings) { var t = LootWorldSpawner.FindTrigger(b); if (t != null) { var e = t.bounds; e.Expand(new Vector3(12f, 40f, 12f)); trigs.Add(e); } }

        var candidates = new List<Vector3>();
        for (int i = 0; i < 400 && candidates.Count < 40; i++)
        {
            if (!TryGroundPoint(Random.Range(area.min.x, area.max.x), Random.Range(area.min.z, area.max.z), start.y, out Vector3 c)) continue; // 지붕/위층/맵 아래 제외
            if ((c - start).sqrMagnitude < escapeMinDistance * escapeMinDistance) continue;
            if (trigs.Exists(t => t.Contains(c))) continue;                   // 건물 안/바로 옆 제외
            if (Physics.CheckSphere(c + Vector3.up * 2.6f, 2f, ~0, QueryTriggerInteraction.Ignore)) continue; // 주변이 트여 있어야 (구 아래 끝이 지면 0.6m 위 - 지면에 닿으면 전부 막힘)
            candidates.Add(c);
        }
        if (candidates.Count == 0) { result = Vector3.zero; return false; }

        // 걸어서 갈 수 있는지: 먼 경로는 탐색 한도로 PathPartial이 나올 수 있어서, 목표 → 시작 쪽 60m 지점까지 이어지는지로 판정
        var path = new NavMeshPath();
        foreach (var c in candidates)
        {
            Vector3 mid = c + (start - c).normalized * 60f;
            if (!NavMesh.SamplePosition(mid, out NavMeshHit mh, 8f, NavMesh.AllAreas)) continue;
            if (NavMesh.CalculatePath(c, mh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
            { result = c; return true; }
        }
        result = candidates[0];
        return true;
    }

    /// <summary>(x, z)의 실제 걸을 수 있는 바닥 점. 지형 높이는 믿을 수 없음: 맵 바닥은 지형 위 Ground 콜라이더(약 15m)이고
    /// 지형 자체는 곳곳이 0~10m라서, 지형 높이로 NavMesh를 찾으면 맵 아래 점이 나옴 (탈출 지점이 y=3에 생겨 도달 불가였음).
    /// → 위에서 레이를 쏴 처음 닿는 면이 기준 높이 refY ±2.5m이고 평평하면 OK (지붕/나무 위/맵 아래 제외)</summary>
    public static bool TryGroundPoint(float x, float z, float refY, out Vector3 point)
    {
        point = Vector3.zero;
        if (!Physics.Raycast(new Vector3(x, refY + 150f, z), Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (Mathf.Abs(hit.point.y - refY) > 2.5f || hit.normal.y < 0.7f) return false;
        if (!NavMesh.SamplePosition(hit.point, out NavMeshHit nav, 1.5f, NavMesh.AllAreas) || Mathf.Abs(nav.position.y - hit.point.y) > 1f) return false;
        point = nav.position;
        return true;
    }

    /// <summary>외딴 고립 구역이 아닌지: 맵 중앙 쪽 60m 지점까지 NavMesh 경로가 이어지는지.
    /// 먼 경로는 탐색 한도로 PathPartial이 나오므로 가까운 지점으로 판정</summary>
    public static bool IsConnectedToMap(Vector3 pos)
    {
        var terrain = Terrain.activeTerrain;
        Vector3 center = terrain != null ? terrain.transform.position + terrain.terrainData.size / 2f : Vector3.zero;
        center.y = pos.y;
        Vector3 toCenter = center - pos;
        Vector3 mid = pos + (toCenter.sqrMagnitude > 3600f ? toCenter.normalized * 60f : toCenter);
        if (!NavMesh.SamplePosition(mid, out NavMeshHit mh, 8f, NavMesh.AllAreas)) return false;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(pos, mh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    public void CollectKeyCard(KeyCardPickup card)
    {
        if (Finished) return;
        keyCardsInWorld.Remove(card);
        float before = ComputeProgress();
        KeyCards++;
        ToastProgress("열쇠 카드 획득", before, $"{KeyCards}/{keyCardsRequired}");
        if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 {KeyCards}/{keyCardsRequired} ({card.buildingLabel})");
        RefreshProgress();
    }

    // 진행률이 오른 이벤트 알림: "열쇠 카드 획득 (+15%) → 총 45%". 서브 미션 상한(40%)을 넘으면 +0%
    void ToastProgress(string what, float before, string detail)
    {
        float now = ComputeProgress();
        GetComponent<MissionHUD>()?.ShowToast($"{what} (+{Mathf.RoundToInt(now - before)}%) → 총 {Mathf.FloorToInt(now)}%", detail);
    }

    /// <summary>진행률 다시 계산. 100% + 7일차가 되면 탈출 지점 열림 이벤트 + 알림</summary>
    void RefreshProgress()
    {
        TotalProgress = ComputeProgress();
        if (escapeUnlocked || !ProgressFull) return;

        // 조기 탈출 방지: 7일차 전엔 100%여도 안 열림
        if (CurrentDay < SurvivalGoalDays)
        {
            if (earlyFullNotified) return;
            earlyFullNotified = true;
            PlaySound(l => l.missionNotification);
            GetComponent<MissionHUD>()?.ShowToast("100% 달성!", $"탈출 지점은 {SurvivalGoalDays}일차부터 열립니다");
            return;
        }

        escapeUnlocked = true;
        if (debugLog) Debug.Log("[MissionManager] 진행률 100% + 7일차 → 탈출 지점 열림");
        PlaySound(l => l.missionNotification);
        GetComponent<MissionHUD>()?.ShowToast("100% 달성! 탈출 지점이 활성화되었습니다", "초록 빛기둥으로 이동");
        EscapeUnlocked?.Invoke();
    }

    public string EscapeLockedReason()
    {
        if (ProgressFull) return $"{SurvivalGoalDays}일차까지 생존 필요 (지금 {CurrentDay}일차)";
        return $"잠김: 총 진행률 {Mathf.FloorToInt(TotalProgress)}% (100% + {SurvivalGoalDays}일차 필요)";
    }

    /// <summary>탈출 지점 도달 → 클리어</summary>
    public void Escape()
    {
        if (Finished) return;
        Finished = true;
        Escaped = true;
        if (debugLog) Debug.Log("[MissionManager] 탈출 성공!");
        PlaySound(l => l.missionSuccess);
        FindFirstObjectByType<DeathUI>()?.ShowEnding("탈출 성공", new Color(0.3f, 1f, 0.45f), $"{CurrentDay}일 생존 · 열쇠 카드 {KeyCards}/{keyCardsRequired} · 서브 미션 {CompletedSubMissions}개");
    }

    // ── 서브 미션 ─────────────────────────────────

    // 일차별 할당 개수 (최소, 최대)
    static (int min, int max) DailyCount(int day)
    {
        if (day <= 1) return (2, 3);
        if (day == 2) return (2, 4);
        if (day == 3) return (3, 4);
        if (day <= 5) return (3, 5);
        return (4, 5);
    }

    /// <summary>새 날(새벽→낮) 시작: 어제 미완료 미션은 버리고 새로 랜덤 할당 (패널티 없음)</summary>
    void OnNewDay(int day)
    {
        if (Finished) return;
        AssignSubMissions(day);
        RefreshProgress();
    }

    void AssignSubMissions(int day)
    {
        bool Has(LootDatabase.BuildingKind k) => buildingList.Exists(b => LootDatabase.Classify(b) == k);

        // 후보 (보상은 아이템만)
        var pool = new List<SubMission>
        {
            new SubMission { kind = SubMission.Kind.KillZombies, title = "좀비 15마리 처치", target = 15, rewardItemName = "Pistol Ammo", rewardLabel = "권총탄 x20", rewardAmount = 20 },
            new SubMission { kind = SubMission.Kind.KillAtNight, title = "밤에 좀비 10마리 처치", target = 10, rewardItemName = "MedKit", rewardLabel = "구급상자 x1", rewardAmount = 1 },
            new SubMission { kind = SubMission.Kind.OpenCrates, title = "상자 5개 열기", target = 5, rewardItemName = "MedKit", rewardLabel = "구급상자 x1", rewardAmount = 1 },
            new SubMission { kind = SubMission.Kind.UseConsumables, title = "소비 아이템 4번 사용", target = 4, rewardItemName = "CannedFood", rewardLabel = "통조림 x2", rewardAmount = 2 },
        };
        if (Has(LootDatabase.BuildingKind.Hospital))
            pool.Add(new SubMission { kind = SubMission.Kind.VisitBuilding, building = LootDatabase.BuildingKind.Hospital, title = "병원 들어가기", rewardItemName = "Painkiller", rewardLabel = "진통제 x2", rewardAmount = 2 });
        if (Has(LootDatabase.BuildingKind.Police))
            pool.Add(new SubMission { kind = SubMission.Kind.VisitBuilding, building = LootDatabase.BuildingKind.Police, title = "경찰서 들어가기", rewardItemName = "Pistol Ammo", rewardLabel = "권총탄 x30", rewardAmount = 30 });
        if (Has(LootDatabase.BuildingKind.Church))
            pool.Add(new SubMission { kind = SubMission.Kind.VisitBuilding, building = LootDatabase.BuildingKind.Church, title = "교회 들어가기", rewardItemName = "CannedFood", rewardLabel = "통조림 x2", rewardAmount = 2 });
        // 날씨 미션은 그 날씨인 날에만 후보 (날씨는 하루 단위로 정해져서, 아니면 그날 절대 못 깸)
        if (WeatherManager.Current == WeatherManager.Weather.Fog)
            pool.Add(new SubMission { kind = SubMission.Kind.KillInFog, title = "안개 속 좀비 5마리 처치", target = 5, rewardItemName = "Painkiller", rewardLabel = "진통제 x2", rewardAmount = 2 });
        if (WeatherManager.Current == WeatherManager.Weather.Rain)
            pool.Add(new SubMission { kind = SubMission.Kind.MoveInRain, title = "빗속 500m 이동", target = 500, rewardItemName = "CannedFood", rewardLabel = "통조림 x2", rewardAmount = 2 });

        var (min, max) = DailyCount(day);
        int count = Random.Range(min, max + 1);

        // 랜덤 count개. 어제와 같은 조합이 나오지 않게 다시 뽑음 (후보가 모자라 불가능하면 그대로)
        var picked = new List<SubMission>();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var left = new List<SubMission>(pool);
            picked.Clear();
            for (int i = 0; i < count && left.Count > 0; i++)
            {
                int idx = Random.Range(0, left.Count);
                picked.Add(left[idx]);
                left.RemoveAt(idx);
            }
            if (!SameSet(picked, lastTitles)) break;
        }
        subMissions.Clear();
        subMissions.AddRange(picked);
        lastTitles = subMissions.ConvertAll(m => m.title);
        TodayAllDone = false;

        PlaySound(l => l.missionNotification); // notification-bell
        GetComponent<MissionHUD>()?.ShowToast($"Day {day} 서브 미션 {subMissions.Count}개", "완료할 때마다 진행률 +5%");
        if (debugLog) Debug.Log($"[MissionManager] Day {day} 서브 미션 {subMissions.Count}개: " + string.Join(" / ", subMissions.ConvertAll(m => $"{m.title}(보상 {m.RewardText})")));
    }

    static bool SameSet(List<SubMission> a, List<string> titles)
    {
        if (titles == null || a.Count != titles.Count) return false;
        foreach (var m in a) if (!titles.Contains(m.title)) return false;
        return true;
    }

    void Advance(SubMission.Kind kind, LootDatabase.BuildingKind building = default, int amount = 1)
    {
        if (Finished) return;
        foreach (var m in subMissions)
        {
            if (m.Done || m.kind != kind) continue;
            if (kind == SubMission.Kind.VisitBuilding && m.building != building) continue;
            m.progress += amount;
            if (!m.Done) continue;

            float before = ComputeProgress();
            CompletedSubMissions++;
            ToastProgress("서브 미션 완료", before, $"{m.title} · 보상 {m.RewardText}");
            GiveReward(m);
            if (subMissions.TrueForAll(x => x.Done)) // 오늘 할당량 전부 완료
            {
                TodayAllDone = true;
                PickupFeedbackUI.Instance?.Show("오늘 서브 미션 100%!");
            }
            RefreshProgress();
        }
    }

    void OnZombieDied(ZombieController z)
    {
        Advance(SubMission.Kind.KillZombies);
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) Advance(SubMission.Kind.KillAtNight);
        if (WeatherManager.Current == WeatherManager.Weather.Fog) Advance(SubMission.Kind.KillInFog);
    }

    void OnCrateOpened(LootCrate c) => Advance(SubMission.Kind.OpenCrates);
    void OnBuildingEntered(BuildingInterior b) => Advance(SubMission.Kind.VisitBuilding, LootDatabase.Classify(b));
    void OnConsumableUsed(ConsumableData d) => Advance(SubMission.Kind.UseConsumables);

    // 빗속 이동: 비 오는 날 실외에서 걸은 거리 (m). 프레임당 10m 넘는 이동(순간이동)은 제외
    void TrackRainDistance()
    {
        if (playerTf == null) return;
        Vector3 d = playerTf.position - lastPlayerPos; d.y = 0f;
        lastPlayerPos = playerTf.position;
        if (WeatherManager.Current != WeatherManager.Weather.Rain || BuildingInterior.IsAnyoneIndoors || d.sqrMagnitude > 100f) return;
        rainMeters += d.magnitude;
        if (rainMeters < 1f) return;
        int whole = Mathf.FloorToInt(rainMeters);
        rainMeters -= whole;
        Advance(SubMission.Kind.MoveInRain, default, whole);
    }

    void GiveReward(SubMission s)
    {
        PlaySound(l => l.missionComplete);
        var item = FindItem(s.rewardItemName);
        var player = GameObject.FindWithTag("Player");
        if (item is AmmoData ad && AmmoInventory.Instance != null)
            AmmoInventory.Instance.AddAmmo(ad.weaponType, s.rewardAmount); // 탄약은 탄 개수 그대로
        else if (item != null && player != null)
        {
            var inv = player.GetComponent<InventoryManager>();
            if (inv == null || !inv.AddItem(item, s.rewardAmount))
                WorldItemDrop.Spawn(player.transform.position + player.transform.forward, item, s.rewardAmount); // 가방이 꽉 차면 발밑에
        }
        else Debug.LogWarning("[MissionManager] 보상 아이템을 못 찾음: " + s.rewardItemName);
        if (debugLog) Debug.Log($"[MissionManager] 서브 미션 완료: {s.title} → {s.RewardText} (진행률 +{SubMissionWeight}%)");
    }

    // 미션 알림/결과음 (2D). 라이브러리가 없으면 무시
    static void PlaySound(System.Func<SoundLibrary, AudioClip[]> clips)
    {
        if (AudioManager.Library != null) AudioManager.PlayUI(SoundLibrary.Pick(clips(AudioManager.Library)), AudioManager.Vol.Notification);
    }

    /// <summary>아이템 에셋 이름으로 찾기 (Resources의 LootDatabase가 참조하는 루팅 테이블들에서 검색 - 따로 Resources 에셋 안 만들어도 됨)</summary>
    static ItemData FindItem(string assetName)
    {
        var db = LootDatabase.Instance;
        if (db == null) return null;
        var tables = new List<LootTable> { db.crateWood, db.crateMetal, db.crateMilitary, db.crateMilitaryGuaranteed };
        foreach (var r in db.buildingRules) tables.Add(r.table);
        foreach (var r in db.zombieDrops) tables.Add(r.table);
        foreach (var t in tables)
        {
            if (t == null) continue;
            foreach (var e in t.entries)
                if (e.item != null && e.item.name == assetName) return e.item;
        }
        return null;
    }
}

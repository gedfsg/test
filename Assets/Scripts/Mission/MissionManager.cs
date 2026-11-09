using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 미션 시스템 (2026-11 리워크: 하루 단위 메인 1개 + 서브 1~2개).
///
/// ━ 메인 미션 ━ (MainMissionData, Resources/Missions/Main)
///  게임 시작 시 풀(14개)에서 랜덤 6개 → Day 1~6에 하나씩, Day 7은 항상 "탈출 지점 도달".
///  새 날(새벽→낮, DayNightCycle.onNewDay)이 되면 그날 미션이 공개됨. 못 끝낸 미션은 사라짐 (패널티 없음).
///  Day 7 "탈출 지점 도달" 완료 = 게임 클리어 (7일 넘어가면 계속 탈출 미션).
///
/// ━ 서브 미션 ━ (SubMissionData, Resources/Missions/Sub)
///  매일 풀에서 1~2개 랜덤 할당 (그날 날씨에 맞는 것만). 발견 조건 충족 전엔 HUD에서 숨김,
///  충족하면 notification-bell + 토스트 "새 서브 미션 발견" + HUD "발견" 라벨. 완료 시 아이템 보상.
///
/// ━ 진행률 (하루치, 실시간) ━ 메인 완료 +60% / 서브 1개 완료 +20% (하루 최대 100%). 못 채워도 패널티 없음.
///
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성 (DeathUI와 같은 방식).
/// (이전 방식: 열쇠 카드 4개 + 서브 미션 완료 수 + 생존 일수로 총 진행률 100% → 탈출. git 기록 참고)
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("메인 미션")]
    [Tooltip("열쇠 카드가 플레이어 위치에서 떨어져야 하는 최소 거리 (m)")]
    public float keyCardMinPlayerDistance = 50f;
    [Tooltip("탈출 지점 위치. 비워두면 시작 지점에서 먼 야외 NavMesh 위로 자동 선택")]
    public Transform escapePointOverride;
    [Tooltip("자동 선택 시 시작 지점에서 최소 거리 (m)")]
    public float escapeMinDistance = 180f;
    [Tooltip("좀비 떼 섬멸: 이 반경 안에서 죽은 좀비만 셈 (m)")]
    public float hordeRadius = 45f;
    [Tooltip("좀비 떼 섬멸: 미션 시작 때 그 지역에 추가로 부르는 좀비 수")]
    public int hordeExtraZombies = 12;
    [Tooltip("무전 신호 수신: 라디오 타워에서 이 거리 안에 있어야 시간이 참 (m)")]
    public float radioSignalRadius = 12f;
    [Tooltip("정찰: 깃발에서 이 거리 안에 들어가면 방문 (m)")]
    public float reconVisitRadius = 6f;

    [Header("서브 미션")]
    public int minDailySubMissions = 1;
    public int maxDailySubMissions = 2;
    [Tooltip("공원 탐색: 공원 중심에서 이 거리 안에 들어가면 완료 (m)")]
    public float parkReachRadius = 8f;

    [Header("디버그")]
    public bool debugLog = true;

    // 진행률 가중치 (%)
    public const float MainWeight = 60f, SubWeight = 20f;
    public const int MaxDailySubMissions = 2;

    // ── 상태 ──
    public readonly List<MainMissionData> schedule = new List<MainMissionData>(); // [0] = Day 1
    public MainMissionData CurrentMain { get; private set; }
    public int MainProgress { get; private set; }
    public bool MainDone { get; private set; }
    public string MainDetail { get; private set; } = ""; // HUD 미션명 옆 보조 표시 (예: "7/20")
    public readonly List<SubMission> subMissions = new List<SubMission>(); // 오늘 할당 (발견 전 포함)
    public int CompletedMainMissions { get; private set; }
    public int CompletedSubMissions { get; private set; }
    public bool Finished { get; private set; }
    public bool Escaped { get; private set; }
    /// <summary>탈출 미션이 공개됐을 때 (EscapePoint 알림용)</summary>
    public event System.Action EscapeUnlocked;
    public readonly List<KeyCardPickup> keyCardsInWorld = new List<KeyCardPickup>(); // PlayerRandomSpawn도 참조
    public EscapePoint EscapePointObj { get; private set; }

    public int SurvivalGoalDays => DayNightCycle.Instance != null ? DayNightCycle.Instance.totalDays : 7;
    public int CurrentDay => DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
    public bool CanEscape => !Finished && CurrentMain != null && CurrentMain.type == MainMissionType.Escape;
    public int DoneSubCount { get { int n = 0; foreach (var s in subMissions) if (s.Rewarded) n++; return n; } }
    /// <summary>오늘 진행률 0~100 (메인 60 + 서브 20씩)</summary>
    public float DayProgress => Mathf.Clamp((MainDone ? MainWeight : 0f) + DoneSubCount * SubWeight, 0f, 100f);
    public string FooterText => CurrentDay < SurvivalGoalDays ? $"{CurrentDay + 1}일차 시작 시 새 미션 개방" : "탈출 지점에 도달하면 생존 성공";

    List<BuildingInterior> buildingList;
    Transform playerTf;
    Vector3 lastPlayerPos;
    float nextPoll;
    float dayMeters;                       // 오늘 이동 거리 (서브 "이동" 발견 조건)
    readonly List<GameObject> mainObjects = new List<GameObject>(); // 오늘 메인 미션 오브젝트 (다음 날 정리)
    Vector3 hordeCenter;
    readonly List<Transform> reconFlags = new List<Transform>();
    readonly List<bool> reconVisited = new List<bool>();
    float radioSeconds;
    Vector3? guideTarget;                  // 방향 화살표 대상
    List<LootCrate> crates = new List<LootCrate>();
    float nextCrateScan;
    static List<string> lastSchedule, lastSubTitles; // 직전 판/어제와 같은 조합 피하기

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
        buildingList = new List<BuildingInterior>(FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None));
        playerTf = GameObject.FindWithTag("Player").transform;
        lastPlayerPos = playerTf.position;
        MissionPlaces.Ensure();
        PlaceEscapePoint(buildingList);
        BuildSchedule();
        gameObject.AddComponent<MissionHUD>();
        StartDay(CurrentDay);
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.onNewDay.AddListener(OnNewDay);
    }

    void Update()
    {
        if (Finished || playerTf == null) return;
        TrackMovement();
        if (Time.time < nextPoll) return;
        nextPoll = Time.time + 0.25f;
        PollMain();
        PollSubTriggers();
    }

    // ── 일정 ─────────────────────────────────────

    /// <summary>풀에서 랜덤 (goalDays-1)개 + 마지막 날 탈출. 직전 판과 같은 순서면 다시 뽑음</summary>
    void BuildSchedule()
    {
        var pool = new List<MainMissionData>(Resources.LoadAll<MainMissionData>("Missions/Main"));
        if (pool.Count == 0) pool = MissionDefaults.Main(); // 에셋 없으면 기본값 (Tools/Mission/Build Mission Data)
        var last = pool.Find(m => m.fixedLastDay);
        pool.RemoveAll(m => m.fixedLastDay);
        int randomDays = Mathf.Max(0, SurvivalGoalDays - 1);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            var left = new List<MainMissionData>(pool);
            schedule.Clear();
            for (int i = 0; i < randomDays && left.Count > 0; i++)
            {
                int idx = Random.Range(0, left.Count);
                schedule.Add(left[idx]);
                left.RemoveAt(idx);
            }
            var titles = schedule.ConvertAll(m => m.title);
            if (lastSchedule == null || string.Join("|", titles) != string.Join("|", lastSchedule)) { lastSchedule = titles; break; }
        }
        if (last != null) schedule.Add(last);
        if (debugLog) Debug.Log("[MissionManager] 메인 미션 일정: " + string.Join(" → ", schedule.ConvertAll(m => m.title)));
    }

    MainMissionData MainForDay(int day) => schedule.Count == 0 ? null : schedule[Mathf.Clamp(day, 1, schedule.Count) - 1];

    /// <summary>새 날(새벽→낮): 어제 미완료 미션은 버리고 오늘 미션 공개 (패널티 없음)</summary>
    void OnNewDay(int day)
    {
        if (Finished) return;
        StartDay(day);
    }

    void StartDay(int day)
    {
        CleanupMainObjects();
        CurrentMain = MainForDay(day);
        MainProgress = 0;
        MainDone = false;
        MainDetail = "";
        dayMeters = 0f;
        SetupMain();
        AssignSubMissions(day);

        PlaySound(l => l.missionNotification); // notification-bell
        if (CurrentMain != null)
            Hud?.ShowToast(MissionHUD.ToastKind.NewDay, $"{day}일차 메인 미션", CurrentMain.title);
        if (debugLog) Debug.Log($"[MissionManager] Day {day} 메인: {CurrentMain?.title} / 서브 {subMissions.Count}개: " + string.Join(", ", subMissions.ConvertAll(s => s.title)));
        if (CanEscape) { EscapeUnlocked?.Invoke(); }
    }

    MissionHUD hud;
    MissionHUD Hud => hud != null ? hud : hud = GetComponent<MissionHUD>();

    // ── 메인 미션: 배치 ───────────────────────────

    void SetupMain()
    {
        guideTarget = null;
        MainDetail = "";
        if (CurrentMain == null) return;
        var m = CurrentMain;
        switch (m.type)
        {
            case MainMissionType.EmergencyRadio:
            {
                // 경찰서 실내 or 라디오 타워 아래 (반반)
                Vector3 pos;
                bool atTower = MissionPlaces.TryGet(MissionPlaces.Kind.RadioTower, out var tower) && (Random.value < 0.5f || FindBuilding(LootDatabase.BuildingKind.Police) == null);
                if (atTower) pos = NearGround(tower.position, 3.5f);
                else if (!TryPointIn(FindBuilding(LootDatabase.BuildingKind.Police), out pos)) pos = RandomOutdoor(60f, 200f);
                var t = MissionProps.Radio(pos, out var beacon);
                AddInteractable(t, "무전기 가동", m.holdSeconds, beacon);
                break;
            }
            case MainMissionType.KeyCardA:
            case MainMissionType.KeyCardB:
            case MainMissionType.KeyCardC:
                PlaceKeyCard(m.type == MainMissionType.KeyCardA ? "A" : m.type == MainMissionType.KeyCardB ? "B" : "C");
                break;
            case MainMissionType.PowerRestore:
            {
                Vector3 pos = MissionPlaces.TryGet(MissionPlaces.Kind.PowerPlant, out var plant) && TryPointIn(plant.building, out var p) ? p : RandomOutdoor(60f, 200f);
                var t = MissionProps.Generator(pos, out var beacon);
                AddInteractable(t, "전력 복구", m.holdSeconds, beacon);
                break;
            }
            case MainMissionType.ZombieHorde:
            {
                hordeCenter = RandomOutdoor(80f, 250f);
                Track(MissionProps.HordeArea(hordeCenter, hordeRadius));
                var spawner = FindFirstObjectByType<ZombieSpawner>();
                int spawned = 0;
                for (int i = 0; spawner != null && i < hordeExtraZombies; i++) if (spawner.SpawnExtraNear(hordeCenter, 3f, 20f) != null) spawned++;
                guideTarget = hordeCenter;
                if (debugLog) Debug.Log($"[MissionManager] 좀비 떼 지역 {hordeCenter:F0} (추가 좀비 {spawned})");
                break;
            }
            case MainMissionType.FuelCollect:
            {
                Vector3 pos = MissionPlaces.TryGet(MissionPlaces.Kind.GasStation, out var gas) && TryPointIn(gas.building, out var p) ? p : RandomOutdoor(60f, 200f);
                var t = MissionProps.FuelCan(pos, out var beacon);
                AddInteractable(t, "연료통 획득", m.holdSeconds, beacon, radius: 1.8f, promptHeight: 1.4f);
                break;
            }
            case MainMissionType.Armory:
            {
                if (!TryPointIn(FindBuilding(LootDatabase.BuildingKind.Police), out var pos) && !TryPointIn(FindBuilding(LootDatabase.BuildingKind.Warehouse), out pos))
                    pos = RandomOutdoor(60f, 200f);
                var t = MissionProps.ArmoryLocker(pos, out var beacon);
                AddInteractable(t, "무기고 열기", m.holdSeconds, beacon, promptHeight: 2.7f);
                break;
            }
            case MainMissionType.Barricade:
            {
                Vector3 pos = RandomOutdoor(60f, 200f);
                var t = MissionProps.BarricadeSpot(pos, out var beacon);
                float yaw = t.eulerAngles.y;
                AddInteractable(t, "바리케이드 설치", m.holdSeconds, beacon, onDone: () => MissionProps.BarricadeBuilt(pos, yaw)); // 완성품은 다음 날에도 남김
                break;
            }
            case MainMissionType.Recon:
                PlaceReconFlags();
                break;
            case MainMissionType.RadioSignal:
                radioSeconds = 0f;
                if (MissionPlaces.TryGet(MissionPlaces.Kind.RadioTower, out var rt)) guideTarget = rt.position;
                break;
            case MainMissionType.Escape:
                if (EscapePointObj != null) guideTarget = EscapePointObj.transform.position;
                break;
        }
        RefreshMainDetail();
    }

    void Track(Transform t) { if (t != null) mainObjects.Add(t.gameObject); }

    void AddInteractable(Transform t, string action, float hold, Light beacon, float radius = 2.4f, float promptHeight = 2.2f, System.Action onDone = null)
    {
        Track(t);
        var it = t.gameObject.AddComponent<MissionInteractable>();
        it.actionText = action;
        it.holdSeconds = hold;
        it.radius = radius;
        it.promptHeight = promptHeight;
        it.beacon = beacon;
        it.onDone = () => { onDone?.Invoke(); CompleteMain(); };
        guideTarget = t.position;
        if (debugLog) Debug.Log($"[MissionManager] {action} 지점 {t.position:F0}");
    }

    void CleanupMainObjects()
    {
        foreach (var go in mainObjects) if (go != null) Destroy(go);
        mainObjects.Clear();
        foreach (var k in keyCardsInWorld) if (k != null) Destroy(k.gameObject);
        keyCardsInWorld.Clear();
        reconFlags.Clear();
        reconVisited.Clear();
    }

    BuildingInterior FindBuilding(LootDatabase.BuildingKind kind) => buildingList.Find(b => b != null && LootDatabase.Classify(b) == kind);

    static bool TryPointIn(BuildingInterior b, out Vector3 p)
    {
        p = Vector3.zero;
        return b != null && MissionPlaces.TryBuildingPoint(b, out p);
    }

    // 중심 근처 지면 (장애물 없는 쪽)
    Vector3 NearGround(Vector3 center, float dist)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * dist;
            if (TryGroundPoint(center.x + d.x, center.z + d.y, center.y, out Vector3 p) &&
                !Physics.CheckSphere(p + Vector3.up * 1f, 0.5f, ~0, QueryTriggerInteraction.Ignore)) return p;
        }
        return center + Vector3.forward * dist;
    }

    /// <summary>플레이어에서 minD~maxD 떨어진 야외 빈 땅 (건물 밖, 맵과 이어짐). 못 찾으면 가장 가까운 후보/플레이어 근처</summary>
    Vector3 RandomOutdoor(float minD, float maxD)
    {
        Vector3 from = playerTf.position;
        var trigs = new List<Bounds>();
        foreach (var b in buildingList) { var t = LootWorldSpawner.FindTrigger(b); if (t != null) { var e = t.bounds; e.Expand(new Vector3(4f, 40f, 4f)); trigs.Add(e); } }
        var terrain = Terrain.activeTerrain;
        Bounds area = terrain != null
            ? new Bounds(terrain.transform.position + terrain.terrainData.size / 2f, new Vector3(terrain.terrainData.size.x * 0.85f, 200f, terrain.terrainData.size.z * 0.85f))
            : new Bounds(from, new Vector3(600f, 200f, 600f));
        for (int i = 0; i < 400; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(minD, maxD);
            float x = from.x + d.x, z = from.z + d.y;
            if (x < area.min.x || x > area.max.x || z < area.min.z || z > area.max.z) continue;
            if (!TryGroundPoint(x, z, from.y, out Vector3 c)) continue;
            if (trigs.Exists(t => t.Contains(c))) continue;
            if (Physics.CheckBox(c + Vector3.up * 1.5f, new Vector3(1.5f, 0.9f, 1.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!IsConnectedToMap(c)) continue;
            return c;
        }
        Debug.LogWarning("[MissionManager] 야외 지점을 못 찾아 플레이어 근처로");
        return from + playerTf.forward * 5f;
    }

    void PlaceKeyCard(string letter)
    {
        Vector3 me = playerTf.position;
        var candidates = KeyCardSpawnPoints.Build(buildingList);
        candidates.RemoveAll(c => (c.pos - me).sqrMagnitude < keyCardMinPlayerDistance * keyCardMinPlayerDistance);
        if (candidates.Count == 0) { Debug.LogWarning("[MissionManager] 열쇠 카드 후보 없음"); return; }
        var (pos, label) = candidates[Random.Range(0, candidates.Count)];
        var card = KeyCardPickup.Create(pos, label);
        card.name = $"KeyCard_{letter}_{label}";
        card.transform.SetParent(transform, true);
        keyCardsInWorld.Add(card);
        if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 {letter} → {label} {pos:F0}");
    }

    /// <summary>맵을 4등분(북서/북동/남서/남동)해서 구역마다 정찰 깃발 1개</summary>
    void PlaceReconFlags()
    {
        var terrain = Terrain.activeTerrain;
        Vector3 center = terrain != null ? terrain.transform.position + terrain.terrainData.size / 2f : playerTf.position;
        float half = terrain != null ? terrain.terrainData.size.x * 0.4f : 250f;
        for (int q = 0; q < 4; q++)
        {
            float sx = (q % 2 == 0) ? -1f : 1f, sz = (q < 2) ? 1f : -1f;
            Vector3 pos = Vector3.zero; bool found = false;
            for (int i = 0; i < 300 && !found; i++)
            {
                float x = center.x + sx * Random.Range(half * 0.25f, half), z = center.z + sz * Random.Range(half * 0.25f, half);
                if (!TryGroundPoint(x, z, playerTf.position.y, out pos)) continue;
                if (Physics.CheckBox(pos + Vector3.up * 1.5f, new Vector3(1f, 0.9f, 1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!IsConnectedToMap(pos)) continue;
                found = true;
            }
            if (!found) { Debug.LogWarning($"[MissionManager] 정찰 구역 {q + 1} 지점을 못 찾음"); continue; }
            var flag = MissionProps.ReconFlag(pos, reconFlags.Count);
            Track(flag);
            reconFlags.Add(flag);
            reconVisited.Add(false);
        }
    }

    // ── 메인 미션: 판정 ───────────────────────────

    void PollMain()
    {
        if (CurrentMain == null || MainDone) return;
        Vector3 me = playerTf.position;
        switch (CurrentMain.type)
        {
            case MainMissionType.MedicalSupplies:
            {
                int bandage = CountItems(i => NameIs(i, "Bandage")), pill = CountItems(i => NameIs(i, "Painkiller"));
                MainDetail = $"붕대 {Mathf.Min(bandage, CurrentMain.target)}/{CurrentMain.target} · 진통제 {Mathf.Min(pill, CurrentMain.altTarget)}/{CurrentMain.altTarget}";
                if (bandage >= CurrentMain.target || (CurrentMain.altTarget > 0 && pill >= CurrentMain.altTarget)) CompleteMain();
                break;
            }
            case MainMissionType.WaterSupply:
            {
                int water = CountItems(i => NameIs(i, "Water"));
                MainDetail = $"{Mathf.Min(water, CurrentMain.target)}/{CurrentMain.target}";
                if (water >= CurrentMain.target) CompleteMain();
                break;
            }
            case MainMissionType.FoodSupply:
            {
                int canned = CountItems(i => NameIs(i, "CannedFood")), food = CountItems(i => i is ConsumableData c && c.foodAmount >= 15f);
                MainDetail = $"통조림 {Mathf.Min(canned, CurrentMain.target)}/{CurrentMain.target} · 식량 {Mathf.Min(food, CurrentMain.altTarget)}/{CurrentMain.altTarget}";
                if (canned >= CurrentMain.target || (CurrentMain.altTarget > 0 && food >= CurrentMain.altTarget)) CompleteMain();
                break;
            }
            case MainMissionType.Recon:
            {
                int visited = 0, nearest = -1; float best = float.MaxValue;
                for (int i = 0; i < reconFlags.Count; i++)
                {
                    if (reconFlags[i] == null) continue;
                    Vector3 d = reconFlags[i].position - me; d.y = 0f;
                    if (!reconVisited[i] && d.sqrMagnitude <= reconVisitRadius * reconVisitRadius)
                    {
                        reconVisited[i] = true;
                        MissionProps.SetFlagVisited(reconFlags[i]);
                        PickupFeedbackUI.Instance?.Show($"정찰 지점 {i + 1} 확인");
                    }
                    if (reconVisited[i]) visited++;
                    else if (d.sqrMagnitude < best) { best = d.sqrMagnitude; nearest = i; }
                }
                MainProgress = visited;
                guideTarget = nearest >= 0 ? reconFlags[nearest].position : (Vector3?)null;
                if (reconFlags.Count > 0 && visited >= reconFlags.Count) CompleteMain();
                break;
            }
            case MainMissionType.RadioSignal:
            {
                if (guideTarget.HasValue)
                {
                    Vector3 d = guideTarget.Value - me; d.y = 0f;
                    if (d.sqrMagnitude <= radioSignalRadius * radioSignalRadius) radioSeconds += 0.25f;
                }
                MainProgress = Mathf.FloorToInt(radioSeconds);
                if (radioSeconds >= CurrentMain.target) CompleteMain();
                break;
            }
        }
        RefreshMainDetail();
    }

    void RefreshMainDetail()
    {
        if (CurrentMain == null) return;
        switch (CurrentMain.type)
        {
            case MainMissionType.ZombieHorde: MainDetail = $"{Mathf.Min(MainProgress, CurrentMain.target)}/{CurrentMain.target}"; break;
            case MainMissionType.Recon: MainDetail = $"{MainProgress}/{Mathf.Max(reconFlags.Count, 1)}"; break;
            case MainMissionType.RadioSignal: MainDetail = $"{Mathf.Min(MainProgress, CurrentMain.target)}/{CurrentMain.target}초"; break;
        }
    }

    void CompleteMain()
    {
        if (MainDone || Finished || CurrentMain == null) return;
        MainDone = true;
        MainProgress = CurrentMain.target;
        CompletedMainMissions++;
        guideTarget = null;
        if (CurrentMain.type == MainMissionType.Escape) return; // 탈출은 Escape()에서 엔딩
        PlaySound(l => l.missionComplete);
        Hud?.ShowToast(MissionHUD.ToastKind.MainComplete, "메인 미션 완료", CurrentMain.title);
        if (debugLog) Debug.Log($"[MissionManager] 메인 미션 완료: {CurrentMain.title} (오늘 진행률 {DayProgress}%)");
    }

    public void CollectKeyCard(KeyCardPickup card)
    {
        if (Finished) return;
        keyCardsInWorld.Remove(card);
        if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 획득 ({card.buildingLabel})");
        if (CurrentMain != null && (CurrentMain.type == MainMissionType.KeyCardA || CurrentMain.type == MainMissionType.KeyCardB || CurrentMain.type == MainMissionType.KeyCardC))
            CompleteMain();
    }

    /// <summary>인벤토리 + 핫바(소비 아이템 칸)의 개수 합</summary>
    int CountItems(System.Func<ItemData, bool> match)
    {
        int n = 0;
        var inv = playerTf.GetComponent<InventoryManager>();
        if (inv != null) foreach (var s in inv.inventory) if (s.item != null && match(s.item)) n += s.amount;
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar != null)
            for (int i = 0; i < 5; i++) { var it = hotbar.GetItem(i); if (it != null && !(it is WeaponData) && match(it)) n += Mathf.Max(1, hotbar.GetAmmo(i)); }
        return n;
    }

    // 아이템 에셋 이름 비교 (Consumables/Heal 폴더에 같은 이름 에셋이 둘 있음: Bandage, Painkiller/PainKiller)
    static bool NameIs(ItemData i, string assetName) => string.Equals(i.name, assetName, System.StringComparison.OrdinalIgnoreCase);

    // ── 탈출 ─────────────────────────────────────

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
            if (Physics.CheckSphere(c + Vector3.up * 2.6f, 2f, ~0, QueryTriggerInteraction.Ignore)) continue; // 주변이 트여 있어야
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

    public string EscapeLockedReason() => $"탈출 지점은 {SurvivalGoalDays}일차에 열립니다 (지금 {CurrentDay}일차)";

    /// <summary>탈출 지점 도달 → 클리어 (Day 7 메인 미션 완료)</summary>
    public void Escape()
    {
        if (Finished) return;
        CompleteMain();
        Finished = true;
        Escaped = true;
        if (debugLog) Debug.Log("[MissionManager] 탈출 성공!");
        PlaySound(l => l.missionSuccess);
        FindFirstObjectByType<DeathUI>()?.ShowEnding("탈출 성공", new Color(0.3f, 1f, 0.45f),
            $"{CurrentDay}일 생존 · 메인 미션 {CompletedMainMissions}개 · 서브 미션 {CompletedSubMissions}개");
    }

    // ── 공용 지형 판정 (KeyCardSpawnPoints / PlayerRandomSpawn / MissionPlaces도 사용) ──

    public static string BuildingLabel(LootDatabase.BuildingKind k) => k switch
    {
        LootDatabase.BuildingKind.Police => "경찰서",
        LootDatabase.BuildingKind.Hospital => "병원",
        LootDatabase.BuildingKind.Mart => "마트",
        LootDatabase.BuildingKind.Church => "교회",
        LootDatabase.BuildingKind.Warehouse => "창고",
        _ => "민가",
    };

    /// <summary>(x, z)의 실제 걸을 수 있는 바닥 점. 지형 높이는 믿을 수 없음: 맵 바닥은 지형 위 Ground 콜라이더(약 15m)이고
    /// 지형 자체는 곳곳이 0~10m라서, 지형 높이로 NavMesh를 찾으면 맵 아래 점이 나옴.
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

    /// <summary>HUD 방향 화살표 대상 (열쇠 카드/아이템 수집은 직접 찾아야 해서 없음)</summary>
    public bool TryGetGuideTarget(out Vector3 pos)
    {
        pos = guideTarget ?? Vector3.zero;
        return guideTarget.HasValue && !MainDone && !Finished;
    }

    // ── 서브 미션 ─────────────────────────────────

    void AssignSubMissions(int day)
    {
        foreach (var s in subMissions) s.OnDiscovered -= OnSubDiscovered;
        subMissions.Clear();

        var pool = new List<SubMissionData>(Resources.LoadAll<SubMissionData>("Missions/Sub"));
        if (pool.Count == 0) pool = MissionDefaults.Sub();
        // 날씨 미션은 그 날씨인 날에만 (날씨는 하루 단위 - 아니면 그날 절대 발견 못 함), 장소 미션은 장소가 있을 때만
        pool.RemoveAll(d => (d.trigger == SubMissionTrigger.FogWeather && WeatherManager.Current != WeatherManager.Weather.Fog)
                         || (d.trigger == SubMissionTrigger.RainWeather && WeatherManager.Current != WeatherManager.Weather.Rain)
                         || (d.trigger == SubMissionTrigger.NearPlace && !MissionPlaces.TryGet(PlaceOf(d.type), out _)));

        int count = Mathf.Clamp(Random.Range(minDailySubMissions, maxDailySubMissions + 1), 0, MaxDailySubMissions);
        var picked = new List<SubMissionData>();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var left = new List<SubMissionData>(pool);
            picked.Clear();
            for (int i = 0; i < count && left.Count > 0; i++)
            {
                int idx = Random.Range(0, left.Count);
                picked.Add(left[idx]);
                left.RemoveAt(idx);
            }
            if (!SameSet(picked, lastSubTitles)) break;
        }
        foreach (var d in picked)
        {
            var s = new SubMission(d);
            s.OnDiscovered += OnSubDiscovered;
            subMissions.Add(s);
        }
        lastSubTitles = picked.ConvertAll(d => d.title);
    }

    static bool SameSet(List<SubMissionData> a, List<string> titles)
    {
        if (titles == null || a.Count != titles.Count) return false;
        foreach (var m in a) if (!titles.Contains(m.title)) return false;
        return true;
    }

    static MissionPlaces.Kind PlaceOf(SubMissionType t) => t switch
    {
        SubMissionType.SearchGasStation => MissionPlaces.Kind.GasStation,
        SubMissionType.SearchCafe => MissionPlaces.Kind.Cafe,
        SubMissionType.SearchBookstore => MissionPlaces.Kind.Bookstore,
        _ => MissionPlaces.Kind.Park,
    };

    void OnSubDiscovered(SubMission s)
    {
        PlaySound(l => l.missionNotification); // notification-bell
        Hud?.ShowToast(MissionHUD.ToastKind.Discovered, "새 서브 미션 발견", s.title);
        if (debugLog) Debug.Log($"[MissionManager] 서브 미션 발견: {s.title}");
    }

    /// <summary>발견 조건 확인 (0.25초마다)</summary>
    void PollSubTriggers()
    {
        Vector3 me = playerTf.position;
        foreach (var s in subMissions)
        {
            if (s.Discovered) { PollSubProgress(s, me); continue; }
            var d = s.data;
            switch (d.trigger)
            {
                case SubMissionTrigger.NearCrate:
                    if (NearUnopenedCrate(me, d.triggerRadius)) s.Discover();
                    break;
                case SubMissionTrigger.NearPlace:
                    if (MissionPlaces.TryGet(PlaceOf(d.type), out var p) && FlatDistSq(p.position, me) <= d.triggerRadius * d.triggerRadius) s.Discover();
                    break;
                case SubMissionTrigger.Moved:
                    if (dayMeters >= d.triggerRadius) s.Discover();
                    break;
                case SubMissionTrigger.NightStart:
                    if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) s.Discover();
                    break;
                case SubMissionTrigger.FogWeather:
                    if (WeatherManager.Current == WeatherManager.Weather.Fog) s.Discover();
                    break;
                case SubMissionTrigger.RainWeather:
                    if (WeatherManager.Current == WeatherManager.Weather.Rain) s.Discover();
                    break;
                // FirstKill / ConsumableUsed는 이벤트에서
            }
        }
    }

    void PollSubProgress(SubMission s, Vector3 me)
    {
        if (s.Rewarded) return;
        if (s.Type == SubMissionType.ExplorePark && MissionPlaces.TryGet(MissionPlaces.Kind.Park, out var park)
            && FlatDistSq(park.position, me) <= parkReachRadius * parkReachRadius)
            Advance(s, 1);
    }

    static float FlatDistSq(Vector3 a, Vector3 b) { a.y = b.y = 0f; return (a - b).sqrMagnitude; }

    bool NearUnopenedCrate(Vector3 me, float r)
    {
        if (Time.time >= nextCrateScan) { nextCrateScan = Time.time + 2f; crates = new List<LootCrate>(FindObjectsByType<LootCrate>(FindObjectsSortMode.None)); }
        foreach (var c in crates) if (c != null && !c.IsOpened && FlatDistSq(c.transform.position, me) <= r * r) return true;
        return false;
    }

    void Advance(SubMission s, int amount)
    {
        if (Finished || !s.Discovered || s.Rewarded) return;
        s.progress += amount;
        if (!s.Done) return;
        s.Rewarded = true;
        CompletedSubMissions++;
        GiveReward(s);
        Hud?.ShowToast(MissionHUD.ToastKind.SubComplete, "서브 미션 완료", $"{s.title} · 보상 {s.RewardText}");
    }

    void AdvanceType(SubMissionType type, int amount = 1)
    {
        foreach (var s in subMissions) if (s.Type == type) Advance(s, amount);
    }

    void OnZombieDied(ZombieController z)
    {
        if (Finished) return;
        // 서브: 그날 첫 처치로 발견되는 미션은 그 처치부터 셈
        foreach (var s in subMissions) if (!s.Discovered && s.data.trigger == SubMissionTrigger.FirstKill) s.Discover();
        AdvanceType(SubMissionType.KillZombies);
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) AdvanceType(SubMissionType.NightKills);
        if (WeatherManager.Current == WeatherManager.Weather.Rain) AdvanceType(SubMissionType.RainKills);

        // 메인: 좀비 떼 지역 안에서 처치
        if (CurrentMain != null && CurrentMain.type == MainMissionType.ZombieHorde && !MainDone && z != null
            && FlatDistSq(z.transform.position, hordeCenter) <= hordeRadius * hordeRadius)
        {
            MainProgress++;
            RefreshMainDetail();
            if (MainProgress >= CurrentMain.target) CompleteMain();
        }
    }

    void OnCrateOpened(LootCrate c)
    {
        foreach (var s in subMissions) if (!s.Discovered && s.data.trigger == SubMissionTrigger.NearCrate) s.Discover(); // 근처 확인 전에 열어도 인정
        AdvanceType(SubMissionType.OpenCrates);
    }

    void OnBuildingEntered(BuildingInterior b)
    {
        foreach (var s in subMissions)
        {
            if (s.data.trigger != SubMissionTrigger.NearPlace || s.Type == SubMissionType.ExplorePark) continue;
            if (MissionPlaces.TryGet(PlaceOf(s.Type), out var p) && p.building == b) { s.Discover(); Advance(s, 1); }
        }
    }

    void OnConsumableUsed(ConsumableData d)
    {
        foreach (var s in subMissions) if (!s.Discovered && s.data.trigger == SubMissionTrigger.ConsumableUsed) s.Discover();
        AdvanceType(SubMissionType.UseItems);
    }

    // 이동 거리: 오늘 이동(발견 조건) + "500m 이동" / 안개 속 이동(실외). 프레임당 10m 넘는 이동(순간이동)은 제외
    void TrackMovement()
    {
        Vector3 d = playerTf.position - lastPlayerPos; d.y = 0f;
        lastPlayerPos = playerTf.position;
        if (d.sqrMagnitude > 100f) return;
        float m = d.magnitude;
        dayMeters += m;
        foreach (var s in subMissions)
        {
            if (!s.Discovered || s.Rewarded) continue;
            if (s.Type == SubMissionType.Walk) AddMeters(s, m);
            else if (s.Type == SubMissionType.FogWalk && WeatherManager.Current == WeatherManager.Weather.Fog && !BuildingInterior.IsAnyoneIndoors) AddMeters(s, m);
        }
    }

    // 소수점 거리 누적 → 1m 단위로 진행
    readonly Dictionary<SubMission, float> meterRemainder = new Dictionary<SubMission, float>();
    void AddMeters(SubMission s, float m)
    {
        meterRemainder.TryGetValue(s, out float r);
        r += m;
        int whole = Mathf.FloorToInt(r);
        meterRemainder[s] = r - whole;
        if (whole > 0) Advance(s, whole);
    }

    void GiveReward(SubMission s)
    {
        PlaySound(l => l.missionComplete);
        var item = FindItem(s.data.rewardItemName);
        var player = GameObject.FindWithTag("Player");
        if (item is AmmoData ad && AmmoInventory.Instance != null)
            AmmoInventory.Instance.AddAmmo(ad.weaponType, s.data.rewardAmount); // 탄약은 탄 개수 그대로
        else if (item != null && player != null)
        {
            var inv = player.GetComponent<InventoryManager>();
            if (inv == null || !inv.AddItem(item, s.data.rewardAmount))
                WorldItemDrop.Spawn(player.transform.position + player.transform.forward, item, s.data.rewardAmount); // 가방이 꽉 차면 발밑에
        }
        else Debug.LogWarning("[MissionManager] 보상 아이템을 못 찾음: " + s.data.rewardItemName);
        if (debugLog) Debug.Log($"[MissionManager] 서브 미션 완료: {s.title} → {s.RewardText} (오늘 진행률 {DayProgress}%)");
    }

    // 미션 알림/결과음 (2D). 라이브러리가 없으면 무시
    static void PlaySound(System.Func<SoundLibrary, AudioClip[]> clips)
    {
        if (AudioManager.Library != null) AudioManager.PlayUI(SoundLibrary.Pick(clips(AudioManager.Library)), AudioManager.Vol.Notification);
    }

    /// <summary>아이템 에셋 이름으로 찾기 (Resources의 LootDatabase가 참조하는 루팅 테이블들에서 검색)</summary>
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

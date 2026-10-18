using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// 미션 시스템.
///
/// ━ 메인 미션 (하나라도 못 하면 게임오버) ━
///  1) 7일 생존       : 죽으면 게임오버 (기존 사망 화면)
///  2) 열쇠 카드 4개   : 경찰서/병원/마트/교회 안에 1개씩 (없는 종류는 창고/집으로 대체)
///  3) 탈출 지점 도달  : 마지막 날(escapeDay)에 열림 + 카드 4개 필요. 마지막 날이 끝날 때까지 못 가면 게임오버(구조 헬기 떠남)
///  → 3개 다 하면 클리어
///
/// ━ 서브 미션 (랜덤 3개, 실패 없음) ━
///  좀비 처치 / 밤에 좀비 처치 / 상자 열기 / 건물 방문 / 소비 아이템 사용 중에서 뽑음. 달성하면 아이템 또는 점수 보상
///
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성 (DeathUI와 같은 방식).
/// 씬에 직접 두면 Inspector 값(탈출 지점 위치 등)을 쓸 수 있음.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("메인 미션")]
    public int keyCardsRequired = 4;
    [Tooltip("탈출 지점이 열리는 날. 0이면 DayNightCycle.totalDays (7일차)")]
    public int escapeDay = 0;
    [Tooltip("열쇠 카드를 놓을 건물 종류 (앞에서부터 1개씩). 맵에 없는 종류는 창고/집 중 랜덤으로 대체")]
    public LootDatabase.BuildingKind[] keyCardBuildings =
        { LootDatabase.BuildingKind.Police, LootDatabase.BuildingKind.Hospital, LootDatabase.BuildingKind.Mart, LootDatabase.BuildingKind.Church };
    [Tooltip("탈출 지점 위치. 비워두면 시작 지점에서 먼 야외 NavMesh 위로 자동 선택")]
    public Transform escapePointOverride;
    [Tooltip("자동 선택 시 시작 지점에서 최소 거리 (m)")]
    public float escapeMinDistance = 180f;

    [Header("서브 미션")]
    public int subMissionCount = 3;

    [Header("디버그")]
    public bool debugLog = true;

    // ── 상태 ──
    public int KeyCards { get; private set; }
    public int Score { get; private set; }
    public bool Finished { get; private set; }
    public bool Escaped { get; private set; }
    public readonly List<SubMission> subMissions = new List<SubMission>();
    public readonly List<KeyCardPickup> keyCardsInWorld = new List<KeyCardPickup>();
    public EscapePoint EscapePointObj { get; private set; }

    public int EscapeDay => escapeDay > 0 ? escapeDay : (DayNightCycle.Instance != null ? DayNightCycle.Instance.totalDays : 7);
    public int CurrentDay => DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
    public bool SurvivedEnough => CurrentDay >= EscapeDay;
    public bool HasAllKeyCards => KeyCards >= keyCardsRequired;
    public bool CanEscape => !Finished && SurvivedEnough && HasAllKeyCards;

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
        ZombieController.AnyDied -= OnZombieDied;
        LootCrate.AnyOpened -= OnCrateOpened;
        BuildingInterior.PlayerEntered -= OnBuildingEntered;
        PlayerBuffManager.AnyConsumableUsed -= OnConsumableUsed;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        var buildings = new List<BuildingInterior>(FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None));
        PlaceKeyCards(buildings);
        PlaceEscapePoint(buildings);
        PickSubMissions(buildings);
        gameObject.AddComponent<MissionHUD>();
    }

    // ── 메인 미션 ─────────────────────────────────

    void PlaceKeyCards(List<BuildingInterior> buildings)
    {
        Vector3 start = GameObject.FindWithTag("Player").transform.position;
        var used = new HashSet<BuildingInterior>();
        for (int i = 0; i < keyCardsRequired; i++)
        {
            var kind = i < keyCardBuildings.Length ? keyCardBuildings[i] : LootDatabase.BuildingKind.House;
            var b = PickBuilding(buildings, kind, used, start);
            if (b == null) b = PickBuilding(buildings, LootDatabase.BuildingKind.Warehouse, used, start);
            if (b == null) b = PickBuilding(buildings, LootDatabase.BuildingKind.House, used, start);
            if (b == null) { Debug.LogWarning("[MissionManager] 열쇠 카드 놓을 건물이 모자람"); break; }
            used.Add(b);

            var trig = LootWorldSpawner.FindTrigger(b);
            if (trig == null || !LootWorldSpawner.TryInteriorPoint(trig.bounds, out Vector3 p)) { i--; continue; } // 다른 건물로 다시
            var card = KeyCardPickup.Create(p, BuildingLabel(LootDatabase.Classify(b)));
            card.transform.SetParent(transform, true);
            keyCardsInWorld.Add(card);
            if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 {i + 1} → {card.buildingLabel} ({b.name}) {p}");
        }
    }

    /// <summary>kind 종류 건물 중 아직 안 쓴 것 랜덤 (집은 시작 지점 40m 안 제외 - 바로 줍지 않게)</summary>
    static BuildingInterior PickBuilding(List<BuildingInterior> all, LootDatabase.BuildingKind kind, HashSet<BuildingInterior> used, Vector3 start)
    {
        var list = all.FindAll(b => !used.Contains(b) && LootDatabase.Classify(b) == kind && LootWorldSpawner.FindTrigger(b) != null
                                    && (kind != LootDatabase.BuildingKind.House || (b.transform.position - start).sqrMagnitude > 40f * 40f));
        return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
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
            var p = new Vector3(Random.Range(area.min.x, area.max.x), 0f, Random.Range(area.min.z, area.max.z));
            p.y = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : start.y;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(hit.position.y - p.y) > 1.5f) continue;                       // 지붕/위층 제외
            if ((hit.position - start).sqrMagnitude < escapeMinDistance * escapeMinDistance) continue;
            if (trigs.Exists(t => t.Contains(hit.position))) continue;                   // 건물 안/바로 옆 제외
            if (Physics.CheckSphere(hit.position + Vector3.up * 2.6f, 2f, ~0, QueryTriggerInteraction.Ignore)) continue; // 주변이 트여 있어야 (구 아래 끝이 지면 0.6m 위 - 지면에 닿으면 전부 막힘)
            candidates.Add(hit.position);
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

    public void CollectKeyCard(KeyCardPickup card)
    {
        if (Finished) return;
        keyCardsInWorld.Remove(card);
        KeyCards++;
        if (HasAllKeyCards)
            PickupFeedbackUI.Instance?.Show(SurvivedEnough ? "열쇠 카드 전부 획득! 탈출 지점으로" : $"열쇠 카드 전부 획득! {EscapeDay}일차에 탈출 지점이 열린다");
        else
            PickupFeedbackUI.Instance?.Show($"열쇠 카드 획득 ({KeyCards}/{keyCardsRequired})");
        if (debugLog) Debug.Log($"[MissionManager] 열쇠 카드 {KeyCards}/{keyCardsRequired} ({card.buildingLabel})");
    }

    public string EscapeLockedReason()
    {
        if (!HasAllKeyCards && !SurvivedEnough) return $"잠김: 열쇠 카드 {KeyCards}/{keyCardsRequired} · {EscapeDay}일차에 열림";
        if (!HasAllKeyCards) return $"잠김: 열쇠 카드 {KeyCards}/{keyCardsRequired}";
        return $"잠김: {EscapeDay}일차에 열림 (지금 {CurrentDay}일차)";
    }

    /// <summary>탈출 지점 도달 → 클리어</summary>
    public void Escape()
    {
        if (Finished) return;
        Finished = true;
        Escaped = true;
        if (debugLog) Debug.Log($"[MissionManager] 탈출 성공! 점수 {Score}");
        FindFirstObjectByType<DeathUI>()?.ShowEnding("탈출 성공", new Color(0.3f, 1f, 0.45f), $"{CurrentDay}일 생존 · 열쇠 카드 {KeyCards}/{keyCardsRequired} · 점수 {Score}");
    }

    /// <summary>마지막 날이 끝남 (DayNightCycle이 호출) → 탈출 못 했으면 게임오버</summary>
    public void OnTimeUp()
    {
        if (Finished) return;
        Finished = true;
        string why = HasAllKeyCards ? "구조 헬기가 떠났다" : $"열쇠 카드를 다 못 모았다 ({KeyCards}/{keyCardsRequired})";
        if (debugLog) Debug.Log($"[MissionManager] 시간 초과 → 미션 실패: {why}");
        FindFirstObjectByType<DeathUI>()?.ShowEnding("MISSION FAILED", new Color(0.85f, 0.1f, 0.1f), $"{why} · 점수 {Score}");
    }

    // ── 서브 미션 ─────────────────────────────────

    void PickSubMissions(List<BuildingInterior> buildings)
    {
        bool Has(LootDatabase.BuildingKind k) => buildings.Exists(b => LootDatabase.Classify(b) == k);

        // 후보 (보상: 아이템 또는 점수)
        var pool = new List<SubMission>
        {
            new SubMission { kind = SubMission.Kind.KillZombies, title = "좀비 15마리 처치", target = 15, rewardScore = 300 },
            new SubMission { kind = SubMission.Kind.KillAtNight, title = "밤에 좀비 10마리 처치", target = 10, rewardScore = 500 },
            new SubMission { kind = SubMission.Kind.OpenCrates, title = "상자 5개 열기", target = 5, rewardItemName = "MedKit", rewardLabel = "구급상자 x1", rewardAmount = 1 },
            new SubMission { kind = SubMission.Kind.UseConsumables, title = "소비 아이템 4번 사용", target = 4, rewardItemName = "CannedFood", rewardLabel = "통조림 x2", rewardAmount = 2 },
        };
        if (Has(LootDatabase.BuildingKind.Hospital))
            pool.Add(new SubMission { kind = SubMission.Kind.VisitBuilding, building = LootDatabase.BuildingKind.Hospital, title = "병원 들어가기", rewardItemName = "Painkiller", rewardLabel = "진통제 x2", rewardAmount = 2 });
        if (Has(LootDatabase.BuildingKind.Police))
            pool.Add(new SubMission { kind = SubMission.Kind.VisitBuilding, building = LootDatabase.BuildingKind.Police, title = "경찰서 들어가기", rewardItemName = "Pistol Ammo", rewardLabel = "권총탄 x30", rewardAmount = 30 });
        if (Has(LootDatabase.BuildingKind.Church))
            pool.Add(new SubMission { kind = SubMission.Kind.VisitBuilding, building = LootDatabase.BuildingKind.Church, title = "교회 들어가기", rewardScore = 200 });

        for (int i = 0; i < subMissionCount && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            subMissions.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        if (debugLog) Debug.Log("[MissionManager] 서브 미션: " + string.Join(" / ", subMissions.ConvertAll(s => $"{s.title}(보상 {s.RewardText})")));
    }

    void Advance(SubMission.Kind kind, LootDatabase.BuildingKind building = default)
    {
        if (Finished) return;
        foreach (var s in subMissions)
        {
            if (s.Done || s.kind != kind) continue;
            if (kind == SubMission.Kind.VisitBuilding && s.building != building) continue;
            s.progress++;
            if (s.Done) GiveReward(s);
        }
    }

    void OnZombieDied(ZombieController z)
    {
        Advance(SubMission.Kind.KillZombies);
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) Advance(SubMission.Kind.KillAtNight);
    }

    void OnCrateOpened(LootCrate c) => Advance(SubMission.Kind.OpenCrates);
    void OnBuildingEntered(BuildingInterior b) => Advance(SubMission.Kind.VisitBuilding, LootDatabase.Classify(b));
    void OnConsumableUsed(ConsumableData d) => Advance(SubMission.Kind.UseConsumables);

    void GiveReward(SubMission s)
    {
        if (!s.IsItemReward)
        {
            Score += s.rewardScore;
        }
        else
        {
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
        }
        PickupFeedbackUI.Instance?.Show($"서브 미션 완료: {s.title} → {s.RewardText}");
        if (debugLog) Debug.Log($"[MissionManager] 서브 미션 완료: {s.title} → {s.RewardText} (점수 {Score})");
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

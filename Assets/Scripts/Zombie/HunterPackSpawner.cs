using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// '사냥꾼 무리' 스포너. 밤에 5마리 정도의 무리를 한꺼번에 투입한다. 일반/달리기/탱커 좀비는
/// 기존 ZombieSpawner(FSM)가 그대로 맡고, 이 컴포넌트는 그와 별개로 동작한다.
///
/// 무리가 움직이는 방식 (Tactic):
///   Slot    : 무리 조율기(HunterPackTactics)가 플레이어 둘레에 자리를 나눠 주고, 각 좀비는 FSM 몸으로
///             그 자리까지 돌아 들어간 뒤 빈틈이 좁아지면 다 같이 덮친다. 벽 쪽은 자리에서 빼므로
///             막힌 곳으로 몰아넣는 모양이 된다. 프리팹은 아무 좀비 프리팹이나 된다 (_RL 프리팹도 RL을 끄고 쓴다).
///   RLModel : 강화학습 모델이 직접 움직인다 (_RL 프리팹 필요).
///   SlotRL  : 조율기가 자리를 정하고, 그 자리까지 움직이는 건 '조율기+RL 모델'이 한다
///             (_RL_Slot 프리팹 필요 —— 'RL 프리팹 만들기 (조율기+RL 모델)' 메뉴).
///
/// 등장 조건:
///   상자 열기 : 상자를 열면 소리를 듣고 무리가 몰려온다 (등급이 높을수록 잘 나온다 —— 군용 상자를 지키는 사냥꾼).
///   밤        : 밤마다 일정에 따라 투입 (spawnAtNight를 켰을 때).
///
/// 설치 (씬을 안 건드리는 방법 —— main 씬은 팀원도 자주 고쳐서 머지 때 우리 변경이 날아간다):
///   설정을 마친 이 컴포넌트를 프리팹으로 만들어 Assets/Resources/HunterPackSpawner.prefab 으로 두면,
///   ZombieSpawner가 있는 씬(= 게임 씬)이 열릴 때 자동으로 생긴다. 씬에 이미 있으면 그걸 쓴다.
///
/// 설치 (씬에 직접):
///   1. 게임 씬에 빈 오브젝트를 만들고 이 컴포넌트를 붙인 뒤 Hunter Prefab에 좀비 프리팹을 넣는다
///   2. 플레이 → 상자를 열면(또는 밤이 되면) 등장. F6으로 언제든 바로 소환해 볼 수 있다.
///      (예전엔 F10이었는데 녹화 프로그램 단축키와 겹쳐서 바꿨다. 테스트 소환기는 F7~F9)
/// </summary>
public class HunterPackSpawner : MonoBehaviour
{
    public enum HunterTactic { Slot, RLModel, SlotRL }

    [Header("움직이는 방식")]
    [Tooltip("Slot = 무리 조율기가 자리를 나눠 주는 포위 (학습 없음, 확실하게 둘러싼다)\n" +
             "RLModel = 강화학습 모델이 직접 움직임 (_RL 프리팹 필요)\n" +
             "SlotRL = 조율기가 자리를 정하고 강화학습 모델이 움직임 (_RL_Slot 프리팹 필요)")]
    public HunterTactic tactic = HunterTactic.Slot;
    [Tooltip("Slot / SlotRL 방식의 조절값. 플레이 중에 바꿔도 바로 반영된다.")]
    public HunterTacticsSettings slotSettings = new HunterTacticsSettings();

    [Header("프리팹")]
    [Tooltip("Slot: 아무 좀비 프리팹 (일반 FSM 프리팹 권장)\nRLModel: RL 뇌를 단 _RL 프리팹\nSlotRL: _RL_Slot 프리팹")]
    public GameObject hunterPrefab;
    [Tooltip("채우면 무리의 각 마리가 이 중에서 무작위로 나온다 (생김새 섞기). 비우면 위의 Hunter Prefab만.\n" +
             "모두 같은 방식의 프리팹이어야 한다 (SlotRL이면 전부 _RL_Shoot). 설치 메뉴가 자동으로 채운다.")]
    public GameObject[] hunterVariants;

    [Header("무리 구성 — 기본값 = 학습 조건")]
    [Tooltip("한 무리의 마리 수. 학습은 5마리로 했다 (3~6 정도까지는 무난)")]
    public int packSize = 5;
    [Tooltip("사냥꾼 이동 속도(m/s). 학습 때 3.5. 0이면 프리팹 프로필 속도를 그대로 쓴다.\n" +
             "플레이어 걷기 속도(5m/s)가 학습 때 더미보다 빨라서, 너무 쉽게 느껴지면 4.0~4.5로 올려볼 것.")]
    public float moveSpeed = 3.5f;

    [System.Serializable]
    public class PackSlot
    {
        [Tooltip("RL 프리팹 (지형·역할 모델이면 '지형·역할 모델' 메뉴로 만든 프리팹)")]
        public GameObject prefab;
        [Tooltip("이 종류의 최고 속도(m/s). 학습: 일반 3.5 / 달리기 4.5 / 탱커 2.5")]
        public float moveSpeed = 3.5f;
        [Min(0)] public int count = 1;
    }

    [Header("섞인 무리 (지형·역할 모델용)")]
    [Tooltip("비워두면 위의 Hunter Prefab × Pack Size로 나온다.\n" +
             "채우면 이 구성으로 나온다. 예: 일반 3.5 ×2, 달리기 4.5 ×2, 탱커 2.5 ×1\n" +
             "학습은 5마리 중 탱커 0~1, 달리기 0~2로 했으니 그 범위 안에서 쓰는 게 안전하다.")]
    public PackSlot[] composition;

    [Header("등장 조건: 상자 열기")]
    [Tooltip("상자를 열면 그 소리를 듣고 사냥꾼 무리가 몰려온다")]
    public bool spawnOnCrateOpen = true;
    [Tooltip("나무 상자(Common)를 열었을 때 무리가 나올 확률")]
    [Range(0f, 1f)] public float woodCrateChance = 0.05f;
    [Tooltip("금속 상자(Rare)를 열었을 때 무리가 나올 확률")]
    [Range(0f, 1f)] public float metalCrateChance = 0.25f;
    [Tooltip("군용 상자(Epic 이상)를 열었을 때 무리가 나올 확률 —— 좋은 무기가 든 상자는 사냥꾼이 지킨다")]
    [Range(0f, 1f)] public float militaryCrateChance = 0.6f;
    [Tooltip("상자를 열고 무리가 등장하기까지(초) —— 소리를 듣고 오는 느낌")]
    public float crateSpawnDelay = 3f;
    [Tooltip("상자로 무리가 나온 뒤 이 시간(초) 동안은 상자를 열어도 다시 안 나온다")]
    public float crateCooldown = 120f;
    [Tooltip("이 일차부터 상자 무리가 나온다 (1 = 첫날부터)")]
    [Min(1)] public int crateMinDay = 1;
    public string crateWarningMessage = "상자 소리에 사냥꾼 무리가 몰려온다";

    [Header("등장 일정 (밤)")]
    [Tooltip("켜면 밤마다 아래 일정대로도 무리가 나온다 (상자 무리와 별개)")]
    public bool spawnAtNight = false;
    [Tooltip("Day별로 한 밤에 나오는 무리 수. 0번 칸 = Day 1. 날이 배열보다 길면 마지막 값을 쓴다.")]
    public int[] packsPerNightByDay = { 0, 1, 1, 2, 2, 3, 3 };
    [Tooltip("밤이 시작되고 첫 무리가 나오기까지(초)")]
    public float firstPackDelay = 5f;
    [Tooltip("같은 밤 안에서 다음 무리까지의 최소 간격(초)")]
    public float packInterval = 60f;
    [Tooltip("동시에 활동할 수 있는 무리 수. 앞 무리가 아직 남아 있으면 다음 무리를 미룬다.")]
    public int maxActivePacks = 1;
    [Tooltip("살아남은 비율이 이 값 이하로 떨어진 무리는 '와해'로 보고 다음 무리를 허용한다.\n" +
             "(1~2마리만 남으면 포위가 성립하지 않으므로)")]
    [Range(0f, 1f)] public float brokenPackRatio = 0.4f;

    [Header("등장 위치")]
    public float minDistance = 22f;
    public float maxDistance = 30f;
    [Tooltip("무리가 퍼져서 등장하는 각도.\n" +
             "90 = 한쪽에서 몰려와 스스로 퍼지며 둘러쌈 (학습된 협동이 잘 보인다)\n" +
             "360 = 처음부터 사방에서 등장")]
    [Range(0f, 360f)] public float spreadAngle = 90f;
    [Tooltip("사냥꾼끼리 최소 간격(m)")]
    public float minSeparation = 4f;
    [Tooltip("켜면 탱커(무리 중간 속도의 0.85배 이하)는 나머지 무리의 반대편(플레이어 앞쪽)에서 나온다.\n" +
             "조율기의 탱커 매복(Tank Intercept)과 같이 쓴다 —— 느린 탱커가 뒤에서 나오면 길목에 영영 못 간다.")]
    public bool tankAmbushSpawn = false;
    [Tooltip("켜면 등장 즉시 플레이어 위치를 안다 (학습장 조건과 같음).\n끄면 시야나 소리로 직접 찾아야 한다.")]
    public bool alertOnSpawn = true;

    [Header("새벽")]
    [Tooltip("새벽이 되면 플레이어에서 이 거리보다 멀리 있는 사냥꾼은 물러난다(제거). 0이면 끝까지 남는다.")]
    public float retreatDistanceAtDawn = 20f;

    [Header("사냥꾼 강화 (엘리트)")]
    [Tooltip("몸 크기 배율. 공격 사거리도 같이 늘어난다 (팔이 길어지니까)")]
    [Range(1f, 1.6f)] public float hunterScale = 1.2f;
    [Tooltip("체력 배율 (프로필 체력 × 이 값)")]
    public float healthMultiplier = 2.5f;
    [Tooltip("공격력 배율")]
    public float damageMultiplier = 1.5f;
    [Tooltip("맞았을 때 멈칫·휘청하는 정도 배율 —— 엘리트는 덜 흔들린다")]
    [Range(0f, 1f)] public float staggerScale = 0.5f;
    [Tooltip("켜면 무리가 다 둘러싼 뒤 조율기가 '덮쳐' 할 때 다 같이 '왁!' 하고 달려든다 (일반 돌진형과 같은 연출·속도).\n" +
             "끄면 학습한 속도 그대로 조여 들어온다. 플레이 중에 바꿔도 바로 반영된다.")]
    public bool lungeOnClose = true;
    // 이동 속도는 일부러 안 올린다: 학습 조건(위의 moveSpeed 3.5)과 다르면 RL 모델이 배운 거리감이 어긋난다

    [Header("연출")]
    [Tooltip("사냥꾼 몸을 붉게 칠해 일반 좀비와 구분한다")]
    public bool tint = true;
    [Tooltip("사냥꾼 몸 색")]
    public Color hunterColor = new Color(1f, 0.22f, 0.2f);
    [Tooltip("원래 색에서 얼마나 붉게 바꿀지 (1 = hunterColor 그대로 곱함)")]
    [Range(0f, 1f)] public float colorStrength = 0.9f;
    [Tooltip("붉게 빛나는 눈 (+ 얼굴을 비추는 붉은 빛). 죽으면 천천히 꺼진다")]
    public bool glowingEyes = true;
    public Color eyeColor = new Color(1f, 0.12f, 0.05f);
    [Tooltip("눈 주변 붉은 조명 (밤에 특히 잘 보인다). 성능이 걱정되면 끈다")]
    public bool eyeLight = true;
    [Tooltip("포위하는 동안 몸을 웅크리고 고개를 숙인 채 노려본다 (덮칠 때 '왁!'으로 일어선다)")]
    public bool stalkPose = true;
    [Tooltip("등장할 때 한 마리가 울부짖고 나머지가 차례로 응답한다")]
    public bool howlRelay = true;
    [Tooltip("응답 간격(초) 범위")]
    public Vector2 relayDelay = new Vector2(0.25f, 0.7f);
    [Tooltip("무리가 다 둘러싸고 덮치기 시작하면 화면 가장자리가 붉게 맥박친다")]
    public bool closingVignette = true;
    public Color vignetteColor = new Color(0.75f, 0f, 0f);
    [Range(0f, 1f)] public float vignetteAlpha = 0.45f;

    [Tooltip("비워두면 Canvas 상단에 자동으로 만든다")]
    public TMPro.TextMeshProUGUI warningText;
    public string warningMessage = "사냥꾼 무리가 냄새를 맡았다";
    public float warningDuration = 3f;
    [Tooltip("등장할 때 플레이어 위치에서 재생할 소리 (선택)")]
    public AudioClip spawnSound;

    [Header("테스트")]
    [Tooltip("F6: 일정과 상관없이 지금 바로 한 무리 소환")]
    public bool enableHotkey = true;
    public bool debugLog = true;

    // ─────────────────────────────────────────────
    private class Pack
    {
        public readonly List<ZombieController> members = new List<ZombieController>();
        public int spawned;
        public HunterPackTactics tactics;   // Slot 방식일 때만
        public HunterPackTactics.Phase lastPhase = HunterPackTactics.Phase.Searching;

        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var m in members) if (m != null && !m.IsDead) n++;
                return n;
            }
        }
    }

    private readonly List<Pack> packs = new List<Pack>();
    private Transform player;
    private DayNightCycle.Phase lastPhase = DayNightCycle.Phase.Day;
    private int   spawnedThisNight;
    private float nightTimer;
    private Coroutine warningRoutine;
    private float     crateReadyAt;
    private bool      crateSpawnPending;
    private string    nextWarning;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    // ─────────────────────────────────────────────
    void Start()
    {
        bool useComposition = composition != null && composition.Length > 0;
        if (!useComposition && hunterPrefab == null)
            Debug.LogWarning("[사냥꾼] Hunter Prefab이 비어 있습니다. _RL 프리팹을 넣어주세요.");

        // 프리팹 점검: RL 뇌가 있는지, 한 무리 안에서 모델 종류(센서 구성)가 섞이지 않았는지
        var prefabs = new List<GameObject>();
        if (useComposition) { foreach (var s in composition) if (s != null && s.prefab != null) prefabs.Add(s.prefab); }
        else if (hunterPrefab != null) prefabs.Add(hunterPrefab);

        if (tactic == HunterTactic.SlotRL)
        {
            foreach (var p in prefabs)
                if (p.GetComponent<ZombieSlotSensor>() == null || p.GetComponent<ZombieMLBrain>() == null)
                    Debug.LogWarning($"[사냥꾼] {p.name}에 자리 센서(ZombieSlotSensor) 또는 ZombieMLBrain이 없습니다. " +
                                     "'RL 프리팹 만들기 (조율기+RL 모델)' 메뉴로 만든 _RL_Slot 프리팹을 넣어주세요.");
        }

        if (tactic == HunterTactic.RLModel)
        {
            int withRoleSensor = 0;
            foreach (var p in prefabs)
            {
                if (p.GetComponent<ZombieMLBrain>() == null)
                    Debug.LogWarning($"[사냥꾼] {p.name}에 ZombieMLBrain이 없습니다. FSM으로 움직이게 됩니다. " +
                                     "RL 프리팹 만들기 메뉴로 만든 프리팹을 넣거나, Tactic을 Slot으로 바꿔주세요.");
                if (p.GetComponent<ZombieRoleSensor>() != null) withRoleSensor++;
            }
            if (withRoleSensor > 0 && withRoleSensor < prefabs.Count)
                Debug.LogWarning("[사냥꾼] 한 무리에 지형·역할 모델 프리팹과 기존 모델 프리팹이 섞여 있습니다. " +
                                 "서로를 동료로 읽긴 하지만 학습한 조건과 달라집니다. 한 종류로 맞추는 걸 권장합니다.");
        }

        if (DayNightCycle.Instance != null) lastPhase = DayNightCycle.Instance.CurrentPhase;
    }

    // ── 자동 생성: Resources/HunterPackSpawner 프리팹이 있으면 게임 씬(ZombieSpawner가 있는 씬)에 하나 만든다 ──
    public const string ResourcePath = "HunterPackSpawner";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateOnLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedAuto;
        SceneManager.sceneLoaded += OnSceneLoadedAuto;
        TryAutoCreate();
    }

    private static void OnSceneLoadedAuto(Scene scene, LoadSceneMode mode) => TryAutoCreate();

    private static void TryAutoCreate()
    {
        if (FindFirstObjectByType<HunterPackSpawner>(FindObjectsInactive.Include) != null) return;   // 씬에 이미 있다
        if (FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include) == null) return;       // 게임 씬이 아니다 (학습장 등)
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null) return;
        var go = Instantiate(prefab);
        go.name = prefab.name + " (자동)";
        Debug.Log("[사냥꾼] Resources/HunterPackSpawner 프리팹으로 사냥꾼 스포너를 자동 생성했습니다.");
    }

    void OnEnable()  { LootCrate.AnyOpened += OnCrateOpened; }
    void OnDisable() { LootCrate.AnyOpened -= OnCrateOpened; }

    /// <summary>상자가 열렸다 → 등급별 확률로 사냥꾼 무리가 몰려온다</summary>
    private void OnCrateOpened(LootCrate crate)
    {
        if (!spawnOnCrateOpen || crate == null || crateSpawnPending) return;
        if (Time.time < crateReadyAt) return;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (day < crateMinDay) return;
        if (ActivePacks() >= maxActivePacks) return;   // 이미 무리가 돌아다니면 더 안 부른다

        float chance = crate.crateTier == Rarity.Common || crate.crateTier == Rarity.Uncommon ? woodCrateChance
                     : crate.crateTier == Rarity.Rare ? metalCrateChance
                     : militaryCrateChance;
        if (Random.value >= chance)
        {
            if (debugLog) Debug.Log($"[사냥꾼] {LootCrate.TierName(crate.crateTier)} 상자 열림 — 이번엔 무리 없음 (확률 {chance:P0})");
            return;
        }
        StartCoroutine(CrateSpawnRoutine(crate.crateTier, chance));
    }

    private IEnumerator CrateSpawnRoutine(Rarity tier, float chance)
    {
        crateSpawnPending = true;
        if (crateSpawnDelay > 0f) yield return new WaitForSeconds(crateSpawnDelay);
        crateSpawnPending = false;
        nextWarning = crateWarningMessage;
        if (SpawnPack())
        {
            crateReadyAt = Time.time + crateCooldown;
            if (debugLog) Debug.Log($"[사냥꾼] {LootCrate.TierName(tier)} 상자 소리를 듣고 무리 등장 (확률 {chance:P0}, 다음 상자 무리는 {crateCooldown:0}초 뒤부터)");
        }
        nextWarning = null;
    }

    void Update()
    {
        if (enableHotkey && HotkeyPressed()) SpawnPack();

        packs.RemoveAll(p => p.Alive == 0);
        SyncLungeOnClose();
        WatchClosing();

        var cycle = DayNightCycle.Instance;
        if (cycle == null) return;   // 낮밤 시스템이 없는 씬이면 F6 소환만 동작

        var phase = cycle.CurrentPhase;
        if (phase != lastPhase)
        {
            if (phase == DayNightCycle.Phase.Night)
            {
                spawnedThisNight = 0;
                nightTimer = firstPackDelay;
                if (debugLog && spawnAtNight) Debug.Log($"[사냥꾼] Day {cycle.CurrentDay} 밤 — 오늘 밤 무리 {PacksTonight(cycle.CurrentDay)}개");
            }
            else if (phase == DayNightCycle.Phase.Dawn)
            {
                Retreat();
            }
            lastPhase = phase;
        }

        if (!spawnAtNight || phase != DayNightCycle.Phase.Night) return;
        if (spawnedThisNight >= PacksTonight(cycle.CurrentDay)) return;

        nightTimer -= Time.deltaTime;
        if (nightTimer > 0f) return;

        if (ActivePacks() >= maxActivePacks)
        {
            nightTimer = 2f;   // 앞 무리가 와해될 때까지 잠깐씩 미룬다
            return;
        }

        if (SpawnPack()) spawnedThisNight++;
        nightTimer = packInterval;
    }

    // ─────────────────────────────────────────────
    private int PacksTonight(int day)
    {
        if (packsPerNightByDay == null || packsPerNightByDay.Length == 0) return 0;
        int i = Mathf.Clamp(day - 1, 0, packsPerNightByDay.Length - 1);
        return Mathf.Max(0, packsPerNightByDay[i]);
    }

    private int ActivePacks()
    {
        int n = 0;
        foreach (var p in packs)
            if (p.Alive > Mathf.FloorToInt(p.spawned * brokenPackRatio)) n++;
        return n;
    }

    // ─────────────────────────────────────────────
    [ContextMenu("지금 사냥꾼 무리 소환")]
    private void SpawnPackFromMenu() => SpawnPack();

    public bool SpawnPack()
    {
        if (!FindPlayer()) { Debug.LogWarning("[사냥꾼] Player 태그를 가진 오브젝트가 없습니다."); return false; }

        var members = BuildMemberList();
        if (members.Count == 0) { Debug.LogWarning("[사냥꾼] 프리팹이 비어 있습니다 (Hunter Prefab 또는 Composition)."); return false; }

        var positions = PickPositions(members.Count);
        if (positions.Count == 0)
        {
            if (debugLog) Debug.LogWarning("[사냥꾼] 플레이어 주변에서 갈 수 있는 등장 위치를 못 찾았습니다.");
            return false;
        }

        if (tankAmbushSpawn) MoveTanksToAmbush(members, positions);

        // 자리보다 마리가 많으면(위치를 다 못 찾은 경우) 앞에서부터 자른다 —— 순서는 이미 섞여 있다
        var pack = new Pack();
        var summary = new System.Text.StringBuilder();
        for (int i = 0; i < positions.Count && i < members.Count; i++)
        {
            var (prefab, speed) = members[i];
            Vector3 pos = positions[i];

            var go = Instantiate(prefab, pos, Quaternion.LookRotation(Flat(player.position - pos)));
            go.name = $"{prefab.name} (사냥꾼)";
            if (hunterScale > 1.001f) go.transform.localScale = prefab.transform.localScale * hunterScale;

            var na = go.GetComponent<NavMeshAgent>();
            if (na != null) na.Warp(pos);

            var c = go.GetComponent<ZombieController>();
            if (c != null) pack.members.Add(c);
            if (tint) ApplyTint(go);
            if (glowingEyes) HunterEyes.Attach(go, eyeColor, eyeLight);
            StartCoroutine(SetupNextFrame(c, speed));

            summary.Append(speed > 0f ? speed.ToString("0.0") : "프로필").Append(' ');
        }
        pack.spawned = pack.members.Count;
        packs.Add(pack);

        // Slot   : 무리 조율기가 자리를 나눠 준다 (RL 부품이 있으면 끄고 FSM 몸으로 움직인다)
        // SlotRL : 조율기는 자리만 계산하고(조언 모드), 움직임은 RL 모델이 자리 센서를 보고 정한다
        if ((tactic == HunterTactic.Slot || tactic == HunterTactic.SlotRL) && pack.members.Count > 0)
        {
            pack.tactics = HunterPackTactics.Create("사냥꾼 무리 조율기", transform, pack.members, player, slotSettings,
                                                    takeControl: tactic == HunterTactic.Slot);
            if (alertOnSpawn) pack.tactics.Alert(player.position);
            // 조율기+RL: '언제 덮칠지'를 RL이 정하는 모델(shoot9~)이면 slotSettings.rlDecidesClose를 켠다 → 좀비 뇌도 맞춘다
            if (tactic == HunterTactic.SlotRL)
                foreach (var m in pack.members)
                {
                    var ml = m != null ? m.GetComponent<ZombieMLBrain>() : null;
                    if (ml != null) ml.attackAsRush = slotSettings.rlDecidesClose;
                }
        }

        ShowWarning();
        if (spawnSound != null) AudioSource.PlayClipAtPoint(spawnSound, player.position);
        if (howlRelay) StartCoroutine(HowlRelay(pack, 0.6f));
        if (closingVignette) PulseVignette(vignetteAlpha * 0.4f);
        string tacticName = tactic == HunterTactic.Slot ? "자리 배정 포위"
                          : tactic == HunterTactic.SlotRL ? "조율기+RL"
                          : "RL 모델";
        if (debugLog) Debug.Log($"[사냥꾼] 무리 등장 ({tacticName}): {pack.spawned}마리 (속도 {summary.ToString().TrimEnd()})");
        return true;
    }

    /// <summary>이번 무리의 (프리팹, 속도) 목록. composition이 있으면 그 구성, 없으면 Hunter Prefab × Pack Size. 순서는 섞는다.</summary>
    /// <summary>생김새 섞기: 후보 중 무작위 (없으면 Hunter Prefab)</summary>
    private GameObject PickHunterPrefab()
    {
        if (hunterVariants != null && hunterVariants.Length > 0)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                var p = hunterVariants[Random.Range(0, hunterVariants.Length)];
                if (p != null) return p;
            }
        }
        return hunterPrefab;
    }

    private List<(GameObject prefab, float speed)> BuildMemberList()
    {
        var list = new List<(GameObject, float)>();

        if (composition != null && composition.Length > 0)
        {
            foreach (var slot in composition)
            {
                if (slot == null || slot.prefab == null) continue;
                for (int k = 0; k < slot.count; k++) list.Add((slot.prefab, slot.moveSpeed));
            }
        }
        else if (hunterPrefab != null || (hunterVariants != null && hunterVariants.Length > 0))
        {
            for (int k = 0; k < Mathf.Max(1, packSize); k++) list.Add((PickHunterPrefab(), moveSpeed));
            list.RemoveAll(m => m.Item1 == null);
        }

        // 종류별로 한쪽에 몰려 나오지 않게 등장 자리를 섞는다
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>
    /// 플레이어에서 min~max 거리, spreadAngle 부채꼴 안에 서로 떨어진 위치를 고른다.
    /// 플레이어까지 NavMesh 경로가 이어진 곳만 쓴다 (지붕 위, 막힌 건물 안 등 제외).
    /// </summary>
    private List<Vector3> PickPositions(int count)
    {
        var result = new List<Vector3>(count);
        var path   = new NavMeshPath();

        // 방향을 몇 번 바꿔 가며 시도 (한쪽이 강이나 건물로 막혀 있을 수 있으므로)
        for (int attempt = 0; attempt < 6 && result.Count < count; attempt++)
        {
            result.Clear();
            float baseAngle = Random.Range(0f, 360f);
            bool  ring      = spreadAngle >= 359f;

            for (int i = 0; i < count; i++)
            {
                float t     = count > 1 ? (float)i / (count - 1) : 0.5f;
                float slot  = ring ? 360f / count * i : Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, t);
                float slack = ring ? 360f / count * 0.3f : Mathf.Max(5f, spreadAngle / Mathf.Max(count, 1) * 0.4f);

                for (int tries = 0; tries < 10; tries++)
                {
                    float ang  = baseAngle + slot + Random.Range(-slack, slack);
                    float dist = Random.Range(minDistance, maxDistance);
                    Vector3 cand = player.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;

                    if (!NavMesh.SamplePosition(cand, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                    if (TooClose(hit.position, result)) continue;
                    if (!NavMesh.CalculatePath(hit.position, player.position, NavMesh.AllAreas, path) ||
                        path.status != NavMeshPathStatus.PathComplete) continue;

                    result.Add(hit.position);
                    break;
                }
            }

            // 절반도 못 놓으면 무리가 성립하지 않으니 다른 방향으로 다시 시도
            if (result.Count < Mathf.CeilToInt(count * 0.5f)) result.Clear();
        }
        return result;
    }

    /// <summary>
    /// 탱커(속도가 무리 중간값의 0.85배 이하)의 등장 위치를 나머지 무리의 반대편으로 옮긴다.
    /// 플레이어는 무리 반대쪽으로 도망치므로, 탱커가 처음부터 그 길목 근처에 있게 된다.
    /// </summary>
    private void MoveTanksToAmbush(List<(GameObject prefab, float speed)> members, List<Vector3> positions)
    {
        int n = Mathf.Min(members.Count, positions.Count);
        if (n < 2) return;
        var speeds = new List<float>();
        for (int i = 0; i < n; i++) if (members[i].speed > 0f) speeds.Add(members[i].speed);
        if (speeds.Count < 2) return;   // 속도를 프로필에 맡긴 경우는 판정하지 않는다
        speeds.Sort();
        float median = speeds[speeds.Count / 2];

        var tanks = new List<int>();
        Vector3 others = Vector3.zero; int otherCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (members[i].speed > 0f && members[i].speed <= median * 0.85f) tanks.Add(i);
            else { others += positions[i] - player.position; otherCount++; }
        }
        if (tanks.Count == 0 || otherCount == 0) return;

        Vector3 away = -Flat(others / otherCount);
        if (away.sqrMagnitude < 0.01f) return;
        float baseAng = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
        var path = new NavMeshPath();
        foreach (int ti in tanks)
        {
            var keep = new List<Vector3>(positions); keep.RemoveAt(ti);
            for (int tries = 0; tries < 15; tries++)
            {
                float ang  = baseAng + Random.Range(-30f, 30f);
                float dist = Random.Range(minDistance, maxDistance);
                Vector3 cand = player.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;
                if (!NavMesh.SamplePosition(cand, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                if (TooClose(hit.position, keep)) continue;
                if (!NavMesh.CalculatePath(hit.position, player.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                positions[ti] = hit.position;
                break;
            }
        }
    }

    private bool TooClose(Vector3 p, List<Vector3> others)
    {
        float min2 = minSeparation * minSeparation;
        foreach (var o in others) if ((o - p).sqrMagnitude < min2) return true;
        return false;
    }

    /// <summary>프로필 적용(Awake)이 끝난 뒤에 속도를 덮어쓰고 플레이어 위치를 알려준다.</summary>
    private IEnumerator SetupNextFrame(ZombieController c, float speed)
    {
        yield return null;
        if (c == null || c.IsDead) yield break;

        if (speed > 0f) c.moveSpeed = speed;
        ApplyEliteStats(c);
        if (alertOnSpawn && player != null) c.HearNoise(player.position);
    }

    // ─────────────────────────────────────────────
    private void Retreat()
    {
        if (retreatDistanceAtDawn <= 0f || !FindPlayer()) return;

        float r2 = retreatDistanceAtDawn * retreatDistanceAtDawn;
        int removed = 0;
        foreach (var p in packs)
            foreach (var m in p.members)
            {
                if (m == null || m.IsDead) continue;
                if ((m.transform.position - player.position).sqrMagnitude <= r2) continue;
                Destroy(m.gameObject);
                removed++;
            }
        if (debugLog && removed > 0) Debug.Log($"[사냥꾼] 새벽 — {removed}마리 물러남");
    }

    /// <summary>'덮칠 때 돌진'·'웅크린 포위' 설정을 살아 있는 사냥꾼들에게 맞춘다 (플레이 중 토글 바로 반영)</summary>
    private void SyncLungeOnClose()
    {
        foreach (var p in packs)
            foreach (var m in p.members)
            {
                if (m == null || m.IsDead) continue;
                var q = m.GetComponent<ZombieQuirks>();
                if (q == null) continue;
                if (q.hunterLungeOnClose != lungeOnClose) q.hunterLungeOnClose = lungeOnClose;
                if (q.hunterStalkPose != stalkPose) q.hunterStalkPose = stalkPose;
            }
    }

    // ── 연출: 울부짖음 릴레이 · 덮칠 때 붉은 화면 ──────────────
    /// <summary>한 마리가 울부짖고 나머지가 차례로 응답한다</summary>
    private IEnumerator HowlRelay(Pack pack, float firstDelay)
    {
        if (firstDelay > 0f) yield return new WaitForSeconds(firstDelay);
        var order = new List<ZombieController>(pack.members);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (int i = 0; i < order.Count; i++)
        {
            var m = order[i];
            if (m != null && !m.IsDead) ZombieVoiceManager.Play(m, ZombieVoiceManager.State.Attack);
            float lo = Mathf.Min(relayDelay.x, relayDelay.y), hi = Mathf.Max(relayDelay.x, relayDelay.y);
            yield return new WaitForSeconds(i == 0 ? hi + 0.2f : Random.Range(lo, hi));   // 첫 울음 뒤엔 잠깐 정적
        }
    }

    /// <summary>무리가 덮치기 시작하는 순간을 잡는다 (조율기 단계가 Closing으로 바뀔 때)</summary>
    private void WatchClosing()
    {
        foreach (var p in packs)
        {
            if (p.tactics == null) continue;
            var ph = p.tactics.CurrentPhase;
            if (ph == HunterPackTactics.Phase.Closing && p.lastPhase != HunterPackTactics.Phase.Closing)
            {
                if (closingVignette) PulseVignette(vignetteAlpha);
                // 돌진이 꺼져 있으면 '왁!' 비명이 없으니 울음으로 덮치는 순간을 알린다
                if (!lungeOnClose && howlRelay) StartCoroutine(HowlRelay(p, 0f));
                if (debugLog) Debug.Log("[사냥꾼] 포위 완료 — 덮친다");
            }
            p.lastPhase = ph;
        }
    }

    private UnityEngine.UI.Image vignette;
    private Coroutine vignetteRoutine;

    private void PulseVignette(float peak)
    {
        if (peak <= 0f) return;
        if (vignette == null) BuildVignette();
        if (vignette == null) return;
        if (vignetteRoutine != null) StopCoroutine(vignetteRoutine);
        vignetteRoutine = StartCoroutine(VignetteRoutine(peak));
    }

    private IEnumerator VignetteRoutine(float peak)
    {
        // 두 번 맥박친다 (쿵-쿵)
        for (int beat = 0; beat < 2; beat++)
        {
            float a = beat == 0 ? peak : peak * 0.6f;
            for (float t = 0f; t < 0.12f; t += Time.deltaTime) { SetVignette(Mathf.Lerp(0f, a, t / 0.12f)); yield return null; }
            for (float t = 0f; t < 0.45f; t += Time.deltaTime) { SetVignette(Mathf.Lerp(a, 0f, t / 0.45f)); yield return null; }
        }
        SetVignette(0f);
        vignetteRoutine = null;
    }

    private void SetVignette(float a)
    {
        if (vignette != null) vignette.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, a);
    }

    private void BuildVignette()
    {
        var canvasGo = GameObject.Find("Canvas");
        Canvas canvas = canvasGo != null ? canvasGo.GetComponent<Canvas>() : null;
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var go = new GameObject("HunterVignette", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        vignette = go.AddComponent<UnityEngine.UI.Image>();
        vignette.raycastTarget = false;
        vignette.sprite = BuildVignetteSprite();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.transform.SetAsLastSibling();
        SetVignette(0f);
    }

    /// <summary>가운데는 투명하고 가장자리로 갈수록 진해지는 방사형 그라디언트</summary>
    private static Sprite BuildVignetteSprite()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HunterVignette(Runtime)" };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;          // 0 가운데 ~ 1 모서리
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    /// <summary>엘리트 능력치: 체력·공격력·사거리·경직 (프로필이 적용된 뒤 —— Awake 다음 프레임)</summary>
    private void ApplyEliteStats(ZombieController c)
    {
        if (healthMultiplier > 0f && !Mathf.Approximately(healthMultiplier, 1f))
        {
            c.maxHealth *= healthMultiplier;
            var h = c.GetComponent<Health>();
            if (h != null) h.SetMaxHealth(c.maxHealth);
        }
        if (damageMultiplier > 0f) c.attackDamage *= damageMultiplier;
        if (hunterScale > 1.001f)  c.attackRange  *= Mathf.Lerp(1f, hunterScale, 0.6f);

        var q = c.GetComponent<ZombieQuirks>();
        if (q != null) q.staggerTime *= staggerScale;
        var bm = c.GetComponent<ZombieBodyMotion>();
        if (bm != null) bm.flinchAngle *= Mathf.Lerp(1f, staggerScale, 0.6f);   // 휘청은 조금만 줄인다 (맞는 손맛은 남게)
    }

    private void ApplyTint(GameObject go)
    {
        Color mul = Color.Lerp(Color.white, hunterColor, colorStrength);
        var block = new MaterialPropertyBlock();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mat = r.sharedMaterial;
            if (mat == null) continue;

            int id = mat.HasProperty(BaseColorId) ? BaseColorId
                   : mat.HasProperty(ColorId)     ? ColorId
                   : -1;
            if (id == -1) continue;

            r.GetPropertyBlock(block);
            block.SetColor(id, mat.GetColor(id) * mul);
            r.SetPropertyBlock(block);
        }
    }

    // ── 경고 문구 ────────────────────────────────────
    private string currentWarning;

    private void ShowWarning()
    {
        currentWarning = string.IsNullOrEmpty(nextWarning) ? warningMessage : nextWarning;
        if (string.IsNullOrEmpty(currentWarning) || warningDuration <= 0f) return;
        if (warningText == null) BuildWarningUI();
        if (warningText == null) return;

        if (!warningColorSaved) { warningColor = warningText.color; warningColorSaved = true; }
        if (warningRoutine != null) StopCoroutine(warningRoutine);
        warningRoutine = StartCoroutine(WarningRoutine());
    }

    private Color warningColor;
    private bool  warningColorSaved;

    private IEnumerator WarningRoutine()
    {
        warningText.text = currentWarning;
        warningText.gameObject.SetActive(true);

        float t = 0f;
        Color c = warningColor;   // 앞 문구가 사라지는 도중에 새로 떠도 원래 색에서 시작
        while (t < warningDuration)
        {
            t += Time.deltaTime;
            float fadeOut = Mathf.Clamp01((warningDuration - t) / 0.6f);   // 마지막 0.6초 동안 사라짐
            warningText.color = new Color(c.r, c.g, c.b, fadeOut);
            yield return null;
        }
        warningText.gameObject.SetActive(false);
        warningText.color = c;
        warningRoutine = null;
    }

    private void BuildWarningUI()
    {
        var canvasGo = GameObject.Find("Canvas");
        if (canvasGo == null) return;

        var go = new GameObject("HunterWarningText", typeof(RectTransform));
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -110f);
        rt.sizeDelta = new Vector2(800f, 50f);

        warningText = go.AddComponent<TMPro.TextMeshProUGUI>();
        warningText.alignment = TMPro.TextAlignmentOptions.Center;
        warningText.fontSize = 30;
        warningText.fontStyle = TMPro.FontStyles.Bold;
        warningText.color = new Color(1f, 0.35f, 0.3f);
        warningText.raycastTarget = false;
        go.SetActive(false);
    }

    // ─────────────────────────────────────────────
    private bool FindPlayer()
    {
        if (player != null) return true;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.transform;
        return player != null;
    }

    private static bool HotkeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F6);
#endif
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v : Vector3.forward;
    }

    void OnDrawGizmosSelected()
    {
        var p = player != null ? player
              : (GameObject.FindGameObjectWithTag("Player") is GameObject g ? g.transform : null);
        if (p == null) return;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(p.position, minDistance);
        Gizmos.DrawWireSphere(p.position, maxDistance);
    }
}

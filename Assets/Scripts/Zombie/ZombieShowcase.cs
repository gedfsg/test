#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 촬영 모드 —— 발표 영상의 좀비 장면을 실제 맵에서 쉽게 찍기 위한 도구 (에디터·개발 빌드에서만 존재).
///
/// 게임 씬(ZombieSpawner가 있는 씬)을 플레이하면 자동으로 생긴다. 프리팹·씬 설정 필요 없음.
///
/// 단축키 (녹화 중에 쓰는 것) —— 먼저 Game 뷰를 한 번 클릭해 둘 것
///   F1  소리 반응 세트 : 등 돌린 좀비 4마리(그중 1마리만 '멍한 좀비') → 총을 쏘면 3마리만 돌아본다
///   F2  머뭇형 좀비 3마리 (걷다 서다, 가끔 급발진)
///   F3  무리 6마리 (배회) → 한 마리가 발견하면 다 같이 몰려온다
///   F4  노리는 좀비 세트 : 미끼 좀비 2마리 + 노리는 좀비 1마리(조금 비켜서) → 미끼를 쏘거나 재장전할 때 덮친다
///   (모든 세트는 캐릭터 오른쪽 화면 밖에 생겨서 걸어 들어온다 —— 화면 위쪽 UI에 가리지 않게)
///   F6  사냥꾼 무리 (HunterPackSpawner 기존 키)
///   F9  다시 찍기 : 방금 세트를 지우고 같은 세트를 다시 소환
///   F10 슬로모션 순환 (1 → 0.5 → 0.25)        F11 일시정지
///   F12 화면 UI 전부 숨기기 (사냥꾼 경고·붉은 화면은 남김)   F7 카메라 순환 (기본 → 위에서 → 낮은 각도)
///   F8  촬영 패널 열기/닫기 (주변 좀비 지우기, 스폰 멈춤, 무적·무한 탄약, 시간 고정, 날씨, 디버그 라벨)
///
/// 사냥꾼 장면 (패널 '군용 상자 놓기'):
///   캐릭터 앞에 새 군용 상자를 놓는다 → F키로 열면 반드시 사냥꾼 무리가 온다 (경고 문구·울부짖음 릴레이 그대로).
///   촬영 모드 동안 군용 상자 사냥꾼 확률 100%, 쿨타임 0. 이 상자는 열어도 일반 좀비를 부르지 않는다.
///   다시 누르면 기존 좀비·상자를 지우고 새 상자를 놓는다 (몇 번이든 다시 찍기).
///
/// 7일차 탈출 장면 (패널 '7일차 밤 · 탈출 장면 준비'):
///   7일차(마지막 날)로 넘기고 밤이 escapeNightSecondsLeft초 남은 시점으로 맞춘다 → 최종 공격(좀비 떼)이 시작되고,
///   잠시 뒤 새벽이 오며 밝아진다. 캐릭터는 탈출 지점에서 escapeStartDistance m 떨어진 길 위로 옮긴다.
///   일반 스포너가 꺼져 있으면 켠다 (최종 공격 좀비 떼를 스포너가 만든다). 시간 고정은 '흐름'으로 돌린다.
///   탈출에 성공하면 엔딩이 떠서 게임이 끝난다 → 다시 찍으려면 Play를 멈췄다 다시 시작.
///
/// 시작하면 루팅 목록에서 가장 좋은 총(등급 → 초당 피해 순)을 핫바 2번에 넣고 든다. 패널의 '총 바꾸기'로 다른 총(샷건 등).
///
/// 촬영 모드가 켜져 있는 동안 잠시 꺼 두는 것 (끝나면 그대로 돌아온다):
///   · 다른 디버그 단축키 (DebugTools F1~F4, ZombieTestSpawner F7~F9)
///   · F5 재시작 (실수로 눌러 촬영이 날아가지 않게)
///   · 시작 10초 스폰 보호 (좀비가 바로 움직이게)
///
/// 녹화는 Unity Recorder(Window ▸ General ▸ Recorder)를 쓰면 슬로모션도 끊기지 않는다 (고정 60fps).
/// </summary>
[DefaultExecutionOrder(1000)]
public class ZombieShowcase : MonoBehaviour
{
    public enum ShotSet { None, NoiseDull, Hesitant, Herd, Lunge, Targets }
    public enum CamPreset { Default, TopDown, Low }
    public enum TimeLock { Free, Night, Day, Dusk }

    [Header("배치: 캐릭터 오른쪽 화면 밖 (화면 위쪽 UI에 가리지 않게)")]
    [Tooltip("화면 오른쪽 끝에서 얼마나 더 바깥(m)에 놓을지 —— 세트별")]
    public float noiseSetMargin = 2f;
    public float hesitantMargin = 2f;
    public float herdMargin     = 6f;
    public float decoyMargin    = 1f;
    public float lungerMargin   = 5f;
    public float targetsMargin  = 1f;
    [Tooltip("노리는 좀비가 미끼 쪽 방향에서 위아래로 벗어난 각도(°)")]
    public float lungerSideAngle = 35f;
    [Tooltip("화면 밖 거리를 못 구했을 때 쓰는 거리(m)")]
    public float fallbackOffscreen = 25f;

    [Header("카메라 프리셋")]
    public Vector3 topDownOffset = new Vector3(0f, 1f, -0.05f);
    public Vector3 lowOffset     = new Vector3(0f, 0.32f, -1f);
    [Tooltip("낮은 각도일 때 카메라 거리 (이때는 휠 줌이 꺼진다)")]
    public float lowDistance = 14f;

    [Header("사냥꾼 장면 (군용 상자)")]
    [Tooltip("캐릭터에서 상자까지 거리(m)")]
    public float crateDistance = 3f;
    [Tooltip("켜면 이 상자를 열 때 일반 좀비(2~3마리)를 부르지 않는다 —— 사냥꾼만 나오게")]
    public bool crateWithoutExtraZombies = true;

    [Header("7일차 탈출 장면")]
    [Tooltip("탈출 지점에서 몇 m 떨어진 곳에서 시작할지 (길을 따라 걸어서 갈 수 있는 곳)")]
    public float escapeStartDistance = 40f;
    [Tooltip("밤이 몇 초 남은 상태로 시작할지 —— 이 시간 동안 좀비 떼가 오고, 그 뒤 새벽(밝아짐)")]
    public float escapeNightSecondsLeft = 45f;

    [Header("알림")]
    [Tooltip("켜면 왼쪽 위에 알림 문구를 잠깐 띄운다. 끄면 Console에만 남긴다")]
    public bool showToasts = false;

    // ── 상태 ──
    private ShotSet lastSet = ShotSet.None;
    private readonly List<GameObject> currentSet = new List<GameObject>();
    private static readonly float[] SlowSteps = { 1f, 0.5f, 0.25f };
    private int slowIndex;
    private bool paused, hudHidden, panelOpen = true, god, spawnPaused;
    private CamPreset cam = CamPreset.Default;
    private TimeLock timeLock = TimeLock.Free;
    private float baseFixedDelta = 0.02f;
    private string toast;
    private float toastUntil;

    private Transform player;
    private Health playerHealth;
    private ZombieSpawner spawner;
    private CameraFollow camFollow;
    private CameraZoom camZoom;
    private Vector3 savedOffset;
    private bool offsetSaved;
    private readonly List<(GameObject go, bool wasActive)> hiddenUi = new List<(GameObject, bool)>();
    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    private float ammoTimer;
    private ZombieDebugHUD hiddenDebugHud;
    private HunterPackSpawner hunterSpawner;
    private bool hunterSaved;
    private float savedMilitaryChance, savedCrateCooldown;
    private bool savedSpawnOnCrate;
    private LootCrate showcaseCrate;
    private static readonly FieldInfo CrateReadyAtField = typeof(HunterPackSpawner).GetField("crateReadyAt", BindingFlags.Instance | BindingFlags.NonPublic);

    // ── 자동 생성 ─────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }
    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryCreate();
    private static void TryCreate()
    {
        if (FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include) == null) return;   // 게임 씬에서만
        EnsureExists();
    }

    /// <summary>없으면 만든다 (HunterPackSpawner도 켜질 때 부른다 —— 씬 구성이 달라도 확실히 생기게)</summary>
    public static void EnsureExists()
    {
        if (FindFirstObjectByType<ZombieShowcase>(FindObjectsInactive.Include) != null) return;
        new GameObject("ZombieShowcase (촬영 모드)").AddComponent<ZombieShowcase>();
    }

    void Start()
    {
        baseFixedDelta = Time.fixedDeltaTime;
        SuppressOtherKeys(true);
        gunGiveAt = Time.unscaledTime + 0.5f;   // 플레이어가 시작 도끼를 받은 뒤에
        Toast("촬영 모드 준비됨: F8 패널 · F1~F4 장면 세트 · F9 다시 찍기 (F5 재시작·다른 디버그 키·스폰 보호는 꺼둠)");
        Debug.Log("[촬영 모드] 준비됨");
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = baseFixedDelta;
        foreach (var b in suppressed) if (b != null) b.enabled = true;
        if (restartAction != null) restartAction.Enable();
        if (hunterSaved && hunterSpawner != null)
        {
            hunterSpawner.militaryCrateChance = savedMilitaryChance;
            hunterSpawner.crateCooldown = savedCrateCooldown;
            hunterSpawner.spawnOnCrateOpen = savedSpawnOnCrate;
        }
    }

    // ── 촬영 중 방해되는 것 끄기 ──────────────────
    private static readonly string[] OtherDebugTools = { "DebugTools", "ZombieTestSpawner" };
    private readonly List<Behaviour> suppressed = new List<Behaviour>();
    private InputAction restartAction;
    private float suppressTimer;
    private List<WeaponData> guns;
    private int gunIndex = -1;
    private bool gunGiven;
    private float gunGiveAt;
    private int suppressPasses;

    private void SuppressOtherKeys(bool scanTools)
    {
        // 다른 디버그 도구 (이름으로 찾는다 —— 에디터 전용 클래스라 직접 참조하면 빌드가 깨진다)
        if (scanTools)
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null || !mb.enabled || mb == this) continue;
            if (System.Array.IndexOf(OtherDebugTools, mb.GetType().Name) < 0) continue;
            mb.enabled = false;
            suppressed.Add(mb);
            Debug.Log($"[촬영 모드] {mb.GetType().Name}({mb.name}) 단축키를 잠시 끔");
        }

        // F5 재시작 (PlayerController의 입력 액션)
        if (restartAction == null)
        {
            var pc = FindFirstObjectByType<PlayerController>();
            var field = typeof(PlayerController).GetField("inputActions", BindingFlags.Instance | BindingFlags.NonPublic);
            object actions = pc != null && field != null ? field.GetValue(pc) : null;
            object map = actions?.GetType().GetProperty("Player")?.GetValue(actions);
            restartAction = map?.GetType().GetProperty("Restart")?.GetValue(map) as InputAction;
        }
        if (restartAction != null && restartAction.enabled) restartAction.Disable();
    }

    // ── 입력 ─────────────────────────────────────
    void Update()
    {
        FindRefs();
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.f1Key.wasPressedThisFrame)  SpawnSet(ShotSet.NoiseDull);
            if (kb.f2Key.wasPressedThisFrame)  SpawnSet(ShotSet.Hesitant);
            if (kb.f3Key.wasPressedThisFrame)  SpawnSet(ShotSet.Herd);
            if (kb.f4Key.wasPressedThisFrame)  SpawnSet(ShotSet.Lunge);
            if (kb.f9Key.wasPressedThisFrame)  { if (lastSet != ShotSet.None) SpawnSet(lastSet); else Toast("다시 찍을 세트가 없습니다 (F1~F4 먼저)"); }
            if (kb.f10Key.wasPressedThisFrame) CycleSlow();
            if (kb.f11Key.wasPressedThisFrame) TogglePause();
            if (kb.f12Key.wasPressedThisFrame) SetHudHidden(!hudHidden);
            if (kb.f7Key.wasPressedThisFrame)  CycleCamera();
            if (kb.f8Key.wasPressedThisFrame)  panelOpen = !panelOpen;
        }

        if (god) KeepGod();
        if (!gunGiven && Time.unscaledTime >= gunGiveAt && player != null && WeaponHotbarUI.Instance != null)
        {
            gunGiven = true;
            GiveGun(0);
        }

        // 시작 10초 스폰 보호 해제 + (플레이어가 다시 켜며 되살린) F5 재시작 다시 끄기
        if (SpawnProtection.Active) SpawnProtection.Cancel();
        suppressTimer -= Time.unscaledDeltaTime;
        if (suppressTimer <= 0f)
        {
            suppressTimer = 1f;
            SuppressOtherKeys(++suppressPasses <= 3);   // 무거운 전체 검사는 처음 몇 번만 (녹화 중 끊김 방지)
        }
    }

    void LateUpdate()
    {
        HoldTime();
        if (cam == CamPreset.Low && camFollow != null) camFollow.distance = lowDistance;
    }

    private void FindRefs()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) { player = p.transform; playerHealth = p.GetComponent<Health>(); }
        }
        // 좀비 프리팹 목록만 빌려 쓴다 —— 스포너를 꺼 두거나 오브젝트를 비활성화해도 찾는다
        if (spawner == null) spawner = FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include);
        if (camFollow == null && Camera.main != null)
        {
            camFollow = Camera.main.GetComponent<CameraFollow>();
            camZoom   = Camera.main.GetComponent<CameraZoom>();
        }
    }

    // ── 장면 세트 ─────────────────────────────────
    private void SpawnSet(ShotSet set)
    {
        if (player == null || spawner == null || spawner.zombiePrefabs == null || spawner.zombiePrefabs.Length == 0)
        {
            Toast(player == null ? "플레이어(Player 태그)를 못 찾았습니다" : "씬에서 ZombieSpawner(좀비 프리팹 목록)를 못 찾았습니다");
            return;
        }
        ClearSet();
        lastSet = set;
        string msg = null;
        Vector3 p = player.position;
        // fwd = 캐릭터에서 화면 오른쪽으로 / right = 그와 직각 (화면 위아래) —— 이름은 예전 그대로
        Vector3 fwd = ScreenRight();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float off = OffscreenDistance(p, fwd);

        switch (set)
        {
            case ShotSet.NoiseDull:
            {
                // 등 돌린 4마리 일렬 (그중 1마리만 멍한 좀비). 총을 쏘면 나머지는 돌아본다
                Vector3 c = p + fwd * (off + noiseSetMargin);
                int dull = Random.Range(0, 4);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 pos = c + right * ((i - 1.5f) * 2.6f);
                    var q = Spawn(pos, AwayFrom(pos, p), i == dull ? ZombieQuirks.ShowcaseTrait.Dull : ZombieQuirks.ShowcaseTrait.Plain, false);
                    if (q != null && i != dull) q.noiseIgnoreChance = 0f;   // 비교가 확실하게: 나머지는 반드시 듣는다
                }
                msg = "소리 반응 세트: 총을 한 발 쏘세요 (1마리만 못 듣습니다)";
                break;
            }
            case ShotSet.Hesitant:
            {
                // 3마리를 조금씩 엇갈리게 —— 박자가 서로 달라서 '걷다 서다'가 더 잘 보인다
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pos = p + fwd * (off + hesitantMargin + i * 1.5f) + right * ((i - 1f) * 3f);
                    Spawn(pos, Toward(pos, p), ZombieQuirks.ShowcaseTrait.Hesitant, false);
                }
                msg = "머뭇형 좀비: 걷다 서다 급발진";
                break;
            }
            case ShotSet.Herd:
            {
                Vector3 c = p + fwd * (off + herdMargin);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 r = Random.insideUnitCircle * 3f;
                    Vector3 pos = c + new Vector3(r.x, 0f, r.y);
                    var q = Spawn(pos, AwayFrom(pos, p), ZombieQuirks.ShowcaseTrait.Herd, false);
                    if (q != null) q.noiseIgnoreChance = 0f;
                }
                msg = "무리: 다가가거나 총을 쏘면 다 같이 몰려옵니다";
                break;
            }
            case ShotSet.Lunge:
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 pos = p + fwd * (off + decoyMargin) + right * (i == 0 ? -1.6f : 1.6f);
                    Spawn(pos, Toward(pos, p), ZombieQuirks.ShowcaseTrait.Plain, false);
                }
                float side = Random.value < 0.5f ? -1f : 1f;
                Vector3 dir = Quaternion.AngleAxis(lungerSideAngle * side, Vector3.up) * fwd;
                Vector3 lp = p + dir * (off + lungerMargin);
                var lq = Spawn(lp, Toward(lp, p), ZombieQuirks.ShowcaseTrait.Lunger, true);
                bool rl = lq != null && lq.GetComponent<ZombieLungeAgent>() != null;
                msg = lq == null ? "노리는 좀비를 못 놓았습니다 (자리가 없거나 돌진 가능한 프리팹이 없음)"
                    : rl ? "노리는 좀비 세트: 미끼를 쏘거나 재장전하세요 (강화학습 모델)"
                         : "노리는 좀비 세트: 모델이 없어 규칙 돌진입니다 (Tools/Zombie/노리는 좀비 모델 설치)";
                break;
            }
            case ShotSet.Targets:
            {
                Vector3 c = p + fwd * (off + targetsMargin);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pos = c + right * ((i - 1f) * 2.2f);
                    Spawn(pos, Toward(pos, p), ZombieQuirks.ShowcaseTrait.Plain, false);
                }
                msg = "사격용 좀비: 휘청·래그돌 (슬로모션 F10)";
                break;
            }
        }
        if (currentSet.Count == 0)
            Toast($"소환 실패: 앞쪽에 좀비를 놓을 NavMesh 자리를 못 찾았습니다. 넓은 길에서 앞을 보고 다시 눌러 보세요 (플레이어 {p})");
        else Toast($"{msg} — {currentSet.Count}마리");
    }

    /// <summary>원하는 성격의 좀비 한 마리. 돌진이 필요하면 탱커(느린 좀비)는 피한다</summary>
    private ZombieQuirks Spawn(Vector3 pos, Quaternion rot, ZombieQuirks.ShowcaseTrait trait, bool needLunge)
    {
        if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas)
            && !NavMesh.SamplePosition(pos, out hit, 12f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[촬영 모드] {pos} 근처에 NavMesh가 없어 건너뜁니다");
            return null;
        }
        var prefabs = spawner.zombiePrefabs;
        int start = Random.Range(0, prefabs.Length);
        for (int k = 0; k < prefabs.Length; k++)
        {
            var prefab = prefabs[(start + k) % prefabs.Length];
            if (prefab == null) continue;
            var go = Instantiate(prefab, hit.position, rot);
            var na = go.GetComponent<NavMeshAgent>();
            if (na != null) na.Warp(hit.position);
            var zc = go.GetComponent<ZombieController>();
            var q  = go.GetComponent<ZombieQuirks>();
            if (needLunge && (zc == null || q == null || zc.moveSpeed < q.lungeMinBaseSpeed))
            {
                Destroy(go);   // 탱커는 돌진을 못 한다 → 다른 프리팹으로
                continue;
            }
            go.name = $"{prefab.name} (촬영 {trait})";
            if (q != null) q.ForceTraitForShowcase(trait);
            currentSet.Add(go);
            return q;
        }
        return null;
    }

    private void ClearSet()
    {
        foreach (var go in currentSet) if (go != null) Destroy(go);
        currentSet.Clear();
    }

    private void ClearAllZombies()
    {
        int n = 0;
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None))
        {
            if (z == null) continue;
            Destroy(z.gameObject);
            n++;
        }
        currentSet.Clear();
        Toast($"좀비 {n}마리를 지웠습니다");
    }

    // ── 사냥꾼 장면: 군용 상자 ─────────────────────
    /// <summary>캐릭터 앞에 새 군용 상자를 놓고, 열면 반드시 사냥꾼 무리가 오게 해 둔다</summary>
    private void PlaceMilitaryCrate()
    {
        if (player == null) { Toast("플레이어(Player 태그)를 못 찾았습니다"); return; }
        var db = LootDatabase.Instance;
        if (db == null || db.crateMilitary == null) { Toast("군용 상자 루팅 테이블을 못 찾았습니다 (Resources/LootDatabase)"); return; }

        // 다시 찍기: 이전 상자·좀비(사냥꾼 포함) 정리
        if (showcaseCrate != null) Destroy(showcaseCrate.gameObject);
        showcaseCrate = null;
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None))
            if (z != null) Destroy(z.gameObject);
        currentSet.Clear();

        // 사냥꾼 스포너: 군용 상자 100%, 쿨타임 0 (촬영 모드가 끝나면 원래 값으로)
        if (hunterSpawner == null) hunterSpawner = FindFirstObjectByType<HunterPackSpawner>(FindObjectsInactive.Include);
        if (hunterSpawner != null)
        {
            if (!hunterSaved)
            {
                savedMilitaryChance = hunterSpawner.militaryCrateChance;
                savedCrateCooldown  = hunterSpawner.crateCooldown;
                savedSpawnOnCrate   = hunterSpawner.spawnOnCrateOpen;
                hunterSaved = true;
            }
            hunterSpawner.militaryCrateChance = 1f;
            hunterSpawner.crateCooldown = 0f;
            hunterSpawner.spawnOnCrateOpen = true;
            if (CrateReadyAtField != null) CrateReadyAtField.SetValue(hunterSpawner, 0f);
        }

        // 자리 찾기: 화면 위쪽 방향(카메라 앞) 먼저, 막혀 있으면 조금씩 돌려 본다
        Vector3 fwd = Camera.main != null ? Camera.main.transform.forward : player.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = player.forward;
        fwd.Normalize();
        float[] angles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };
        bool found = false;
        Vector3 pos = Vector3.zero;
        foreach (float d in new[] { crateDistance, crateDistance + 1.5f })
        {
            foreach (float a in angles)
            {
                Vector3 want = player.position + Quaternion.Euler(0f, a, 0f) * fwd * d;
                if (!NavMesh.SamplePosition(want, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                if (Physics.CheckBox(hit.position + Vector3.up * 0.4f, new Vector3(0.6f, 0.3f, 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                pos = hit.position;
                found = true;
                break;
            }
            if (found) break;
        }
        if (!found) { Toast("주변에 상자 놓을 자리가 없습니다 —— 조금 넓은 곳으로 옮겨 주세요"); return; }

        Vector3 toPlayer = player.position - pos;
        toPlayer.y = 0f;
        float yaw = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer).eulerAngles.y : 0f;   // 앞면이 캐릭터 쪽
        showcaseCrate = LootCrate.CreateCrate(pos, yaw, Rarity.Epic, db.crateMilitary, db.crateMilitaryGuaranteed);
        if (crateWithoutExtraZombies) showcaseCrate.zombiesOnOpenMin = showcaseCrate.zombiesOnOpenMax = 0;

        Toast(hunterSpawner != null
            ? "군용 상자를 놓았습니다 —— 다가가 F키로 열면 3초 뒤 사냥꾼 무리"
            : "상자는 놓았지만 사냥꾼 스포너가 없습니다 (Tools ▸ Zombie ▸ 사냥꾼 스포너 설치)");
    }

    /// <summary>화면 오른쪽 방향 (땅 위)</summary>
    private Vector3 ScreenRight()
    {
        Vector3 r = Camera.main != null ? Camera.main.transform.right : player.right;
        r.y = 0f;
        return r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
    }

    /// <summary>캐릭터에서 dir 쪽으로 몇 m 가야 화면 밖으로 나가는지 (줌·카메라 각도에 맞춰 매번 잰다)</summary>
    private float OffscreenDistance(Vector3 from, Vector3 dir)
    {
        var c = Camera.main;
        if (c == null) return fallbackOffscreen;
        for (float d = 4f; d <= 80f; d += 1f)
        {
            Vector3 vp = c.WorldToViewportPoint(from + dir * d + Vector3.up * 1f);
            if (vp.z < 0f || vp.x > 1.02f || vp.x < -0.02f || vp.y > 1.02f || vp.y < -0.02f) return d;
        }
        return fallbackOffscreen;
    }

    private static Quaternion Toward(Vector3 from, Vector3 to)  { Vector3 d = to - from; d.y = 0f; return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Quaternion.identity; }
    private static Quaternion AwayFrom(Vector3 from, Vector3 to) { Vector3 d = from - to; d.y = 0f; return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Quaternion.identity; }

    // ── 시간 ─────────────────────────────────────
    private void CycleSlow()
    {
        slowIndex = (slowIndex + 1) % SlowSteps.Length;
        paused = false;
        ApplyTimeScale();
        Toast(SlowSteps[slowIndex] >= 1f ? "정상 속도" : $"슬로모션 ×{SlowSteps[slowIndex]}");
    }

    private void TogglePause()
    {
        paused = !paused;
        ApplyTimeScale();
        Toast(paused ? "일시정지" : "재생");
    }

    private void ApplyTimeScale()
    {
        float s = paused ? 0f : SlowSteps[slowIndex];
        Time.timeScale = s;
        // 물리(래그돌)도 같은 비율로 촘촘하게 → 슬로모션에서 끊기지 않는다
        Time.fixedDeltaTime = baseFixedDelta * Mathf.Max(0.05f, s);
    }

    // ── 낮밤 고정 (DayNightCycle 내부를 건드리지 않고 반사로 단계·진행도만 붙잡는다) ──
    private static readonly FieldInfo PhaseTimerField = typeof(DayNightCycle).GetField("phaseTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo SetPhaseMethod = typeof(DayNightCycle).GetMethod("SetPhase", BindingFlags.Instance | BindingFlags.NonPublic);

    private void HoldTime()
    {
        var dnc = DayNightCycle.Instance;
        if (dnc == null || timeLock == TimeLock.Free || PhaseTimerField == null || SetPhaseMethod == null) return;
        DayNightCycle.Phase want = timeLock == TimeLock.Night ? DayNightCycle.Phase.Night
                                 : timeLock == TimeLock.Dusk  ? DayNightCycle.Phase.Dusk
                                 : DayNightCycle.Phase.Day;
        if (dnc.CurrentPhase != want) SetPhaseMethod.Invoke(dnc, new object[] { want });
        float dur = want == DayNightCycle.Phase.Night ? dnc.nightDuration
                  : want == DayNightCycle.Phase.Dusk  ? dnc.duskDuration
                  : dnc.dayDuration;
        PhaseTimerField.SetValue(dnc, dur * 0.5f);   // 그 단계의 한가운데에 붙잡아 둔다
    }

    // ── 7일차 탈출 장면 ──────────────────────────
    private static readonly MethodInfo SetCurrentDayMethod = typeof(DayNightCycle).GetProperty("CurrentDay")?.GetSetMethod(true);

    /// <summary>마지막 날 밤(끝나기 직전)으로 넘기고 캐릭터를 탈출 지점 근처로 옮긴다</summary>
    private void PrepareEscapeScene()
    {
        var dnc = DayNightCycle.Instance;
        if (dnc == null || player == null) { Toast("DayNightCycle 또는 플레이어를 못 찾았습니다"); return; }
        if (SetCurrentDayMethod == null || SetPhaseMethod == null || PhaseTimerField == null) { Toast("DayNightCycle 구조가 바뀌어 날짜를 못 옮깁니다"); return; }
        var mm = MissionManager.Instance;
        if (mm != null && mm.Escaped) { Toast("이미 탈출해서 게임이 끝났습니다 —— Play를 멈췄다 다시 시작하세요"); return; }

        timeLock = TimeLock.Free;

        // 최종 공격 좀비 떼는 일반 스포너가 만든다 → 꺼져 있으면 켠다
        if (spawner != null)
        {
            if (!spawner.gameObject.activeSelf) spawner.gameObject.SetActive(true);
            spawner.enabled = true;
            spawnPaused = false;
        }
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None))
            if (z != null) Destroy(z.gameObject);
        currentSet.Clear();

        // 마지막 날로 (미션 시스템이 '탈출 지점 도달'을 공개하고 탈출 지점이 초록으로 열린다)
        int last = Mathf.Max(1, dnc.totalDays);
        if (dnc.CurrentDay != last)
        {
            SetCurrentDayMethod.Invoke(dnc, new object[] { last });
            dnc.onNewDay?.Invoke(last);
        }

        // 밤, 끝나기 escapeNightSecondsLeft초 전
        SetPhaseMethod.Invoke(dnc, new object[] { DayNightCycle.Phase.Night });
        PhaseTimerField.SetValue(dnc, Mathf.Max(0f, dnc.nightDuration - Mathf.Max(5f, escapeNightSecondsLeft)));

        // 탈출 지점 근처 길 위로
        string where = "";
        if (mm != null && mm.EscapePointObj != null)
        {
            Vector3 esc = mm.EscapePointObj.transform.position;
            if (FindEscapeApproach(esc, out Vector3 start))
            {
                MovePlayerTo(start, esc);
                where = $" · 탈출 지점까지 약 {Vector3.Distance(start, esc):0}m";
            }
            else where = " · 탈출 지점 근처 자리를 못 찾아 제자리에서 시작";
        }
        else where = " · 탈출 지점이 없습니다 (MissionManager)";

        Toast($"{last}일차 밤 (새벽까지 {Mathf.Max(5f, escapeNightSecondsLeft):0}초){where}");
    }

    /// <summary>탈출 지점에서 escapeStartDistance 안팎으로 떨어진, 걸어서 곧장 갈 수 있는 바깥 길 위의 점</summary>
    private bool FindEscapeApproach(Vector3 esc, out Vector3 result)
    {
        result = Vector3.zero;
        float baseAngle = Random.Range(0f, 360f);
        float[] dists = { escapeStartDistance, escapeStartDistance * 0.75f, escapeStartDistance * 1.4f };
        var path = new NavMeshPath();
        foreach (float d in dists)
        {
            for (int k = 0; k < 18; k++)
            {
                float a = (baseAngle + k * 20f) * Mathf.Deg2Rad;
                float x = esc.x + Mathf.Cos(a) * d, z = esc.z + Mathf.Sin(a) * d;
                if (!MissionManager.TryGroundPoint(x, z, esc.y, out Vector3 p)) continue;
                if (!NavMesh.CalculatePath(p, esc, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                float len = 0f;
                var c = path.corners;
                for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
                if (len > d * 1.6f) continue;   // 건물을 빙 돌아가야 하는 자리는 빼고
                result = p;
                return true;
            }
        }
        return false;
    }

    private void MovePlayerTo(Vector3 ground, Vector3 lookAt)
    {
        Vector3 pos = ground + Vector3.up * 0.6f;   // PlayerRandomSpawn과 같은 높이 보정
        player.position = pos;
        Vector3 dir = lookAt - ground;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f) player.rotation = Quaternion.LookRotation(dir);
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }

    private void CycleTimeLock()
    {
        timeLock = (TimeLock)(((int)timeLock + 1) % 4);
        if (timeLock != TimeLock.Free && (PhaseTimerField == null || SetPhaseMethod == null))
            Toast("DayNightCycle 구조가 바뀌어 시간 고정을 못 합니다");
        else Toast(timeLock == TimeLock.Free ? "시간 흐름" : $"시간 고정: {TimeLockName(timeLock)}");
    }
    private static string TimeLockName(TimeLock t) => t == TimeLock.Night ? "밤" : t == TimeLock.Day ? "낮" : t == TimeLock.Dusk ? "노을" : "흐름";

    // ── 시작 총 ──────────────────────────────────
    /// <summary>루팅 목록(LootDatabase)에 있는 총 전부 —— 좋은 순 (등급 → 초당 피해)</summary>
    private void CollectGuns()
    {
        guns = new List<WeaponData>();
        var db = LootDatabase.Instance;
        if (db == null) return;
        var tables = new List<LootTable> { db.crateMilitaryGuaranteed, db.crateMilitary, db.crateMetal, db.crateWood };
        foreach (var r in db.buildingRules) if (r != null) tables.Add(r.table);
        foreach (var r in db.zombieDrops)   if (r != null) tables.Add(r.table);
        foreach (var t in tables)
        {
            if (t == null || t.entries == null) continue;
            foreach (var e in t.entries)
                if (e != null && e.item is WeaponData w && w.type != WeaponType.Melee && !guns.Contains(w)) guns.Add(w);
        }
        guns.Sort((a, b) => GunScore(b).CompareTo(GunScore(a)));
    }

    private static int RarityRank(Rarity r) => r == Rarity.Legendary ? 4 : r == Rarity.Epic ? 3 : r == Rarity.Rare ? 2 : r == Rarity.Uncommon ? 1 : 0;

    private static float GunScore(WeaponData w)
    {
        float pellets = w.type == WeaponType.Shotgun ? Mathf.Max(1, w.pelletCount) : 1f;
        float dps = w.damage * pellets / Mathf.Max(0.05f, w.attackRate);   // attackRate = 발사 간격(초)
        return RarityRank(w.rarity) * 1000f + dps;
    }

    /// <summary>핫바 2번에 총을 넣고 든다 (예비 탄약도 채움)</summary>
    private void GiveGun(int index)
    {
        if (guns == null) CollectGuns();
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null || guns == null || guns.Count == 0) { Toast("지급할 총을 못 찾았습니다 (Resources/LootDatabase)"); return; }
        gunIndex = ((index % guns.Count) + guns.Count) % guns.Count;
        var gun = guns[gunIndex];
        if (!hotbar.SetSlot(1, gun, gun.maxAmmo)) { Toast($"{gun.itemName}을 핫바 2번에 못 넣었습니다"); return; }
        hotbar.SelectSlot(1);
        if (AmmoInventory.Instance != null)
            AmmoInventory.Instance.AddAmmo(gun.type, AmmoInventory.Instance.GetMax(gun.type));
        Toast($"총 지급: {gun.itemName} ({gun.rarity}, {gun.type}) — 핫바 2번");
    }

    private string CurrentGunName => guns != null && gunIndex >= 0 && gunIndex < guns.Count ? guns[gunIndex].itemName : "없음";

    // ── 무적 · 무한 탄약 ──────────────────────────
    private void KeepGod()
    {
        if (playerHealth != null) playerHealth.isInvincible = true;   // 구르기가 끝나며 꺼도 다시 켠다
        ammoTimer -= Time.unscaledDeltaTime;
        if (ammoTimer > 0f || AmmoInventory.Instance == null) return;
        ammoTimer = 0.5f;
        foreach (WeaponType t in System.Enum.GetValues(typeof(WeaponType)))
        {
            int max = AmmoInventory.Instance.GetMax(t);
            int have = AmmoInventory.Instance.GetAmmo(t);
            if (max > have) AmmoInventory.Instance.AddAmmo(t, max - have);
        }
    }

    private void ToggleGod()
    {
        god = !god;
        if (!god && playerHealth != null) playerHealth.isInvincible = false;
        Toast(god ? "무적 + 예비 탄약 무한 (재장전은 그대로 일어납니다)" : "무적 끔");
    }

    // ── 스폰 멈춤 ─────────────────────────────────
    private void ToggleSpawn()
    {
        if (spawner == null) return;
        if (!spawner.gameObject.activeInHierarchy) { Toast("스포너 오브젝트가 꺼져 있어 이미 스폰이 멈춰 있습니다"); return; }
        spawnPaused = !spawnPaused;
        spawner.enabled = !spawnPaused;
        Toast(spawnPaused ? "일반 좀비 스폰 멈춤" : "일반 좀비 스폰 재개");
    }

    // ── UI 숨기기 ────────────────────────────────
    private void SetHudHidden(bool hide)
    {
        if (hide == hudHidden) return;
        hudHidden = hide;
        if (hide)
        {
            hiddenUi.Clear();
            hiddenCanvases.Clear();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == null || !c.isRootCanvas || !c.enabled) continue;
                bool keepsHunterFx = false;
                foreach (Transform ch in c.transform)
                    if (ch.name.StartsWith("Hunter")) { keepsHunterFx = true; break; }
                if (!keepsHunterFx) { c.enabled = false; hiddenCanvases.Add(c); continue; }
                // 사냥꾼 경고·붉은 화면은 연출이라 남기고 나머지만 끈다
                foreach (Transform ch in c.transform)
                {
                    if (ch.name.StartsWith("Hunter")) continue;
                    hiddenUi.Add((ch.gameObject, ch.gameObject.activeSelf));
                    ch.gameObject.SetActive(false);
                }
            }
            var hud = FindFirstObjectByType<ZombieDebugHUD>();
            if (hud != null && hud.enabled) { hud.enabled = false; hiddenDebugHud = hud; }
        }
        else
        {
            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            foreach (var e in hiddenUi) if (e.go != null) e.go.SetActive(e.wasActive);
            hiddenCanvases.Clear();
            hiddenUi.Clear();
            if (hiddenDebugHud != null) hiddenDebugHud.enabled = true;
            hiddenDebugHud = null;
        }
    }

    private void ToggleDebugLabels()
    {
        var hud = FindFirstObjectByType<ZombieDebugHUD>();
        if (hud == null) { hud = new GameObject("ZombieDebugHUD (촬영 모드)").AddComponent<ZombieDebugHUD>(); hud.enabled = true; }
        else hud.enabled = !hud.enabled;
        Toast(hud.enabled ? "디버그 라벨 켬 (좀비 머리 위 상태)" : "디버그 라벨 끔");
    }

    // ── 카메라 ───────────────────────────────────
    private void CycleCamera()
    {
        if (camFollow == null) { Toast("CameraFollow를 못 찾았습니다"); return; }
        if (!offsetSaved) { savedOffset = camFollow.baseOffset; offsetSaved = true; }
        cam = (CamPreset)(((int)cam + 1) % 3);
        camFollow.baseOffset = cam == CamPreset.TopDown ? topDownOffset : cam == CamPreset.Low ? lowOffset : savedOffset;
        if (camZoom != null) camZoom.enabled = cam != CamPreset.Low;   // 낮은 각도는 거리를 고정
        Toast(cam == CamPreset.TopDown ? "카메라: 위에서 (휠로 높이)" : cam == CamPreset.Low ? "카메라: 낮은 각도" : "카메라: 기본");
    }

    private void CycleWeather()
    {
        var wm = WeatherManager.Instance;
        if (wm == null) { Toast("WeatherManager가 없습니다"); return; }
        var next = WeatherManager.Current == WeatherManager.Weather.Clear ? WeatherManager.Weather.Rain
                 : WeatherManager.Current == WeatherManager.Weather.Rain  ? WeatherManager.Weather.Fog
                 : WeatherManager.Weather.Clear;
        wm.SetWeather(next);
        Toast($"날씨: {(next == WeatherManager.Weather.Rain ? "비" : next == WeatherManager.Weather.Fog ? "안개" : "맑음")}");
    }

    // ── 화면 표시 ─────────────────────────────────
    private void Toast(string msg)
    {
        toast = msg;
        toastUntil = Time.unscaledTime + 2.5f;
        Debug.Log("[촬영 모드] " + msg);
    }

    private GUIStyle box, label, button;

    void OnGUI()
    {
        if (hudHidden) return;   // 녹화 화면에는 아무것도 안 그린다
        float scale = Mathf.Max(1f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        if (box == null)
        {
            box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(10, 10, 8, 8) };
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        }

        if (showToasts && toast != null && Time.unscaledTime < toastUntil)
            GUI.Box(new Rect(20, 20, 520, 34), toast, box);

        if (!panelOpen) return;
        float x = 20, y = 70, w = 330;
        GUILayout.BeginArea(new Rect(x, y, w, 640), box);
        GUILayout.Label("<b>촬영 모드</b>  (F8 닫기 · F12 화면 숨김)", label);
        GUILayout.Label("F1 소리 반응  F2 머뭇형  F3 무리  F4 노리는 좀비\nF6 사냥꾼  F9 다시 찍기  F7 카메라\nF10 슬로모션  F11 일시정지", label);
        GUILayout.Space(6);
        GUILayout.Label($"속도 ×{(paused ? 0f : SlowSteps[slowIndex])} · 카메라 {cam} · 시간 {TimeLockName(timeLock)}", label);
        GUILayout.Space(6);
        if (GUILayout.Button($"총 바꾸기 (지금: {CurrentGunName})", button)) GiveGun(gunIndex + 1);
        if (GUILayout.Button("사격용 좀비 3마리 (휘청·래그돌)", button)) SpawnSet(ShotSet.Targets);
        if (GUILayout.Button("군용 상자 놓기 (열면 사냥꾼 100%)", button)) { FindRefs(); PlaceMilitaryCrate(); }
        if (GUILayout.Button("7일차 밤 · 탈출 장면 준비", button)) { FindRefs(); PrepareEscapeScene(); }
        if (GUILayout.Button("주변 좀비 전부 지우기", button)) ClearAllZombies();
        if (GUILayout.Button(spawnPaused ? "일반 스폰: 멈춤 → 재개" : "일반 스폰: 진행 중 → 멈춤", button)) ToggleSpawn();
        if (GUILayout.Button(god ? "무적·무한 탄약: 켬" : "무적·무한 탄약: 끔", button)) ToggleGod();
        if (GUILayout.Button($"시간 고정: {TimeLockName(timeLock)} (누를 때마다 바뀜)", button)) CycleTimeLock();
        if (GUILayout.Button("날씨 바꾸기 (맑음 → 비 → 안개)", button)) CycleWeather();
        if (GUILayout.Button("디버그 라벨 켜기/끄기", button)) ToggleDebugLabels();
        GUILayout.Space(6);
        GUILayout.Label("<size=12>버튼을 누를 때 총이 한 발 나갈 수 있습니다. 녹화 전에 준비할 때만 쓰세요.</size>", label);
        GUILayout.EndArea();
    }
}
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// (2026-11-23) 튜토리얼: 무언가를 "처음 발견"하면 화면을 어둡게 하고 그 대상만 원형 스포트라이트로 비춘 뒤 짧은 말풍선을 띄움.
/// SPACE(또는 "계속" 클릭)로 넘기면 플레이 재개, Esc는 현재 말풍선 스킵.
/// (이전) 동료의 GuideHints = 화면 위쪽 안내 블록 6초 → 이 시스템으로 대체 (대사는 TutorialStep 에셋으로 옮김, 원문 유지)
///
/// 중복 방지 = 세션 기준 (사용자 결정 2026-11-23, PlayerPrefs 아님):
///  - 게임이 켜져 있는 동안만 기억 (static) → 죽고 다시 하기·메인 메뉴 갔다 오기에서는 다시 안 뜸
///  - 게임을 끄고 다시 켜면 처음부터 다시 나옴 (전시회에서 사람이 바뀔 때마다 자연스럽게 초기화)
///  - 미니맵 아래 "튜토리얼 끄기 ×" / 설정 > 게임플레이 > 튜토리얼 OFF = 이번 세션 동안 전부 끔 (다시 켜면 버튼도 다시 보임)
///  - 설정 > "튜토리얼 다시 보기" = 본 기록 초기화 (+ 꺼져 있었으면 다시 켬)
///
/// 감지: 0.25초마다 단계 목록 순서대로 조건을 보고, 아직 안 본 것 중 처음 맞는 하나를 띄움.
///  - 좀비·아이템·상자: 주변 콜라이더를 훑음 (계속 생기는 것이라 DiscoveryTrigger를 붙이지 않음)
///  - 건물(경찰서·병원·마트·창고·교회·학교)·주유소: 시작 때 DiscoveryTrigger를 자동으로 붙여 거리로 판정
///  - 월드 대상은 화면 안에 보일 때만 (스포트라이트를 비출 수 있게)
/// 씬에 둘 필요 없음 - 게임 씬(PlayerController + ZombieSpawner)이 열리면 자동 생성.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // ── 세션 상태 (게임을 끄면 사라짐) ─────────────────
    static readonly HashSet<string> seen = new HashSet<string>();
    static bool sessionOff;

    /// <summary>이번 세션에 튜토리얼을 띄우는지 (끄기 버튼·설정 토글)</summary>
    public static bool Enabled
    {
        get => !sessionOff;
        set
        {
            sessionOff = !value;
            if (Instance != null) Instance.OnEnabledChanged();
        }
    }

    /// <summary>본 기록 초기화 (설정 > 튜토리얼 다시 보기). 꺼져 있었으면 다시 켬</summary>
    public static void ResetSeen()
    {
        seen.Clear();
        Enabled = true;
        if (Instance != null) Instance.aliveFor = 0f; // 시작 안내부터 다시
    }

    public static bool HasSeen(string stepId) => seen.Contains(Key(stepId));
    static string Key(string stepId) => "tut_" + stepId;

    // 에디터에서 Play를 다시 누르면 새 세션 (도메인 리로드를 꺼 둔 경우에도 초기화)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        seen.Clear();
        sessionOff = false;
        DiscoveryTrigger.All.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryCreate();

    static void TryCreate()
    {
        if (FindFirstObjectByType<PlayerController>() == null) return;
        if (FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include) == null) return; // 게임 씬에서만 (메인 메뉴·학습장 제외)
        if (FindFirstObjectByType<TutorialManager>() != null) return;
        new GameObject("TutorialManager").AddComponent<TutorialManager>();
    }

    // ── 설정값 ────────────────────────────────────
    const float CheckInterval = 0.25f;
    const float GapAfterClose = 1.0f;     // 말풍선이 닫히고 다음 것까지 최소 간격 (실제 시간)
    const float MinShowTime = 0.35f;      // 뜨자마자 누르던 키로 바로 닫히지 않게
    const float ZombieNear = 14f, ItemNear = 4.5f, TraderNear = 30f, BuildingNear = 20f;
    const float BuildingSeen = 18f;       // (2026-11-24) 건물 안내: 입구가 이 거리 안 + 화면 안이면 (시야가 가로 약 17m라 사실상 "보이면")
    // (2026-11-24) 건물 종류별·주유소 자동 안내 끔 (건물 안내는 처음 보이는 건물 하나만). DiscoveryTrigger 손 배치는 그대로 동작
    public static bool AutoBuildingTriggers = false;
    const float FeatherMeters = 0.5f;
    const float LowHealthHp = 30f, NeedLow = 30f;

    readonly List<TutorialStep> steps = new List<TutorialStep>();
    readonly Dictionary<string, TutorialStep> byId = new Dictionary<string, TutorialStep>();
    readonly List<(Vector3 focus, Object building)> entrances = new List<(Vector3, Object)>();
    readonly Collider[] hits = new Collider[96];
    readonly List<MissionManager.MapPin> pins = new List<MissionManager.MapPin>();

    TutorialOverlay overlay;
    Transform player;
    Health health;
    SurvivalNeeds needs;
    PlayerController pc;
    Vector3 startPos;
    float aliveFor, checkTimer, gapLeft, shownFor;
    float prevTimeScale = 1f;
    bool waitRelease;                     // 닫은 뒤 SPACE/Esc를 뗄 때까지 조작을 계속 막음 (구르기·일시정지로 새지 않게)
    Object lastTarget;                    // 같은 대상으로 두 안내가 연달아 뜨지 않게
    TutorialStep current;

    // 후보 (이번 검사에서 찾은 대상)
    TutorialStep candStep;
    TutorialOverlay.SpotFn candSpot;
    Object candTarget;
    string candText;                      // 문구를 바꿔 넣는 단계 (건물 이름 등). null이면 에셋 문구

    void Awake()
    {
        Instance = this;
        foreach (var s in Resources.LoadAll<TutorialStep>("Tutorial/Steps"))
        {
            if (s == null || string.IsNullOrEmpty(s.stepId)) continue;
            steps.Add(s);
            byId[s.stepId] = s;
        }
        steps.Sort((a, b) => a.order.CompareTo(b.order));
        if (steps.Count == 0) Debug.LogWarning("[Tutorial] Resources/Tutorial/Steps에 단계 에셋이 없습니다 (Tools/Tutorial/Build Tutorial Steps 실행)");

        overlay = TutorialOverlay.Create(transform);
        overlay.onContinueClicked = () => { if (current != null && shownFor >= MinShowTime) Close(); };
        overlay.onSkipAllClicked = () => Enabled = false;
        overlay.SetSkipButtonVisible(Enabled);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // 열린 채로 씬이 바뀌면 MenuUI가 플래그·timeScale을 정리함
    }

    System.Collections.IEnumerator Start()
    {
        // 건물·주유소에 발견 표시 붙이기 (입구 위치를 쓰므로 EntranceMarkers·MissionPlaces가 준비된 뒤 조금 늦게)
        // Invoke는 게임 시간이라 씬 전환 페이드·첫 안내(timeScale 0) 중엔 안 불림 → 실제 시간으로 기다림
        yield return new WaitForSecondsRealtime(0.1f);
        SetupWorldTriggers();
    }

    void OnEnabledChanged()
    {
        overlay.SetSkipButtonVisible(Enabled);
        if (!Enabled && current != null) Close();
    }

    // ── 매 프레임 ─────────────────────────────────
    void Update()
    {
        var kb = Keyboard.current;
        float dt = Time.unscaledDeltaTime;

        // 1) 말풍선이 떠 있음: SPACE/Enter = 계속, Esc = 스킵
        if (current != null)
        {
            // (2026-11-24) 어떤 경로로든 상점 창이 열리면 말풍선은 바로 닫음 (겹치지 않게)
            if (ShopUI.IsOpen) { Close(); return; }
            shownFor += dt;
            if (kb != null && shownFor >= MinShowTime && !overlay.IsAnimating &&
                (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                Close();
            return;
        }

        // 2) 닫은 뒤 키를 뗄 때까지 조작 막기 유지
        if (waitRelease)
        {
            if (kb == null || !(kb.spaceKey.isPressed || kb.escapeKey.isPressed || kb.enterKey.isPressed))
            {
                waitRelease = false;
                MenuUI.TutorialOpen = false;
                MenuUI.RefreshGameplayInput();
            }
            return;
        }

        // 3) 감지 (끄기·일시정지·사망·컷신·인벤토리·상점 중엔 쉼)
        if (!Enabled || Time.timeScale <= 0f || MenuUI.GameplayBlocked || ShopUI.IsOpen) return;
        FindRefs();
        if (player == null) return;
        aliveFor += dt;
        if (gapLeft > 0f) { gapLeft -= dt; return; }
        checkTimer -= dt;
        if (checkTimer > 0f) return;
        checkTimer = CheckInterval;

        if (FindCandidate()) Open(candStep, candSpot, candTarget, candText);
    }

    void FindRefs()
    {
        if (player != null) return;
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        player = p.transform;
        startPos = player.position;
        health = p.GetComponent<Health>();
        pc = p.GetComponent<PlayerController>();
        needs = p.GetComponent<SurvivalNeeds>();
        if (needs == null) needs = FindFirstObjectByType<SurvivalNeeds>();
    }

    // ── 열기 / 닫기 ───────────────────────────────
    void Open(TutorialStep step, TutorialOverlay.SpotFn spot, Object target, string textOverride = null)
    {
        seen.Add(Key(step.stepId));
        current = step;
        lastTarget = target;
        shownFor = 0f;
        MenuUI.TutorialOpen = true;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        MenuUI.RefreshGameplayInput();
        UIAudio.Play(UIAudio.Sound.PanelOpen);
        overlay.Show(textOverride ?? step.Text, spot);
    }

    void Close()
    {
        if (current == null) return;
        current = null;
        UIAudio.Play(UIAudio.Sound.PanelClose);
        overlay.Hide(null);
        // 시간은 바로 되돌리고(사라지는 애니메이션은 unscaled), 조작은 키를 뗀 뒤에 풂
        if (!MenuUI.PauseOpen && !MenuUI.DeathShown) Time.timeScale = prevTimeScale;
        waitRelease = true;
        gapLeft = GapAfterClose;
    }

    // ── 후보 찾기 ─────────────────────────────────
    bool Want(DiscoveryType type, out TutorialStep step)
    {
        step = null;
        foreach (var s in steps)
            if (s.discoveryType == type && !seen.Contains(Key(s.stepId))) { step = s; return true; }
        return false;
    }

    bool Seen(DiscoveryType type)
    {
        foreach (var s in steps) if (s.discoveryType == type) return seen.Contains(Key(s.stepId));
        return true; // 에셋이 없으면 본 것으로 (뒤 단계가 막히지 않게)
    }

    void Set(TutorialStep step, TutorialOverlay.SpotFn spot, Object target)
    {
        candStep = step; candSpot = spot; candTarget = target; candText = null;
    }

    // ── 건물 안내 문구 (2026-11-24) ──────────────────
    /// <summary>건물 이름 (입구 표시 라벨과 같은 이름: 경찰서·병원·마트·교회·학교·창고·민가·상점·식당·사무실·주유소…)</summary>
    static string BuildingName(BuildingInterior b)
    {
        if (b == null) return "건물";
        if (MissionPlaces.TryGetByBuilding(b, out var place)) return place.label;     // 미션 장소로 바뀐 건물 (주유소·카페·서점·발전소)
        if (!string.IsNullOrEmpty(b.markerLabel)) return b.markerLabel;               // 자동 꾸민 테마 (상점·식당·사무실·창고·학교…)
        return MissionManager.BuildingLabel(LootDatabase.Classify(b));
    }

    static string InsideText(string name, bool en) => name switch
    {
        "경찰서" => en ? "Inside, you may find guns, ammo and key cards for missions." : "안에는 총과 탄약, 미션에 필요한 열쇠 카드가 있을 수 있어요.",
        "병원" => en ? "Inside, there are lots of healing supplies like bandages and medkits." : "안에는 붕대나 구급상자 같은 회복 물품이 많아요.",
        "마트" => en ? "Inside, there's plenty of food and water." : "안에는 음식과 물이 많아요.",
        "창고" => en ? "Inside, you may find supply crates." : "안에는 보급품 상자가 있을 수 있어요.",
        "학교" => en ? "You can find useful things all around the classrooms." : "교실 곳곳에서 쓸 만한 물건을 찾을 수 있어요.",
        "민가" => en ? "Inside, you may find food, water and everyday supplies." : "안에는 음식과 물, 생활용품이 있을 수 있어요.",
        "식당" => en ? "Inside, you may find food and drinks." : "안에는 음식과 마실 것이 있을 수 있어요.",
        _ => en ? "Inside, you can find crates and items." : "안에 들어가면 상자와 아이템을 찾을 수 있어요.",
    };

    static string EnglishName(string ko) => ko switch
    {
        "경찰서" => "police station", "병원" => "hospital", "마트" => "mart", "교회" => "church", "학교" => "school",
        "창고" => "warehouse", "민가" => "house", "상점" => "shop", "식당" => "restaurant", "사무실" => "office",
        "주유소" => "gas station", "카페" => "cafe", "서점" => "bookstore", "발전소" => "power plant", "야영지" => "campsite",
        _ => "building",
    };

    /// <summary>받침 있으면 "이", 없으면 "가" (경찰서가 / 병원이)</summary>
    static string WithSubject(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;
        char last = word[word.Length - 1];
        bool batchim = last >= 0xAC00 && last <= 0xD7A3 && (last - 0xAC00) % 28 != 0;
        return word + (batchim ? "이" : "가");
    }

    static string BuildingText(TutorialStep st, BuildingInterior b)
    {
        string name = BuildingName(b);
        bool en = Localization.IsEnglish;
        return st.Text.Replace("{building}", en ? EnglishName(name) : WithSubject(name))
                      .Replace("{inside}", InsideText(name, en));
    }

    /// <summary>목록 순서대로 조건을 보고 처음 맞는 것 하나</summary>
    bool FindCandidate()
    {
        candStep = null;
        Vector3 pos = player.position;

        // 시작: 이동 → 공격 (동료 상단 블록을 나눔)
        if (aliveFor > 1.0f && Want(DiscoveryType.GameStart, out var st)) { Set(st, PlayerSpot(st), player); return true; }
        if (!Seen(DiscoveryType.GameStart)) return false;
        if (Want(DiscoveryType.Attack, out st) && (aliveFor > 12f || FlatDist(pos, startPos) > 6f)) { Set(st, PlayerSpot(st), null); return true; }

        // 무기: 총을 들었을 때 / 탄창이 거의 비었을 때 / 탄약이 없을 때
        var hotbar = WeaponHotbarUI.Instance;
        var held = hotbar != null ? hotbar.GetActiveItem() as WeaponData : null;
        bool gun = held != null && held.type != WeaponType.Melee;
        if (gun && Seen(DiscoveryType.Attack) && Want(DiscoveryType.GunHeld, out st)) { Set(st, HotbarSpot(), hotbar); return true; }
        if (gun && pc != null && held.maxAmmo > 0 && pc.GetCurrentAmmo() <= Mathf.Max(1, held.maxAmmo / 4) && Want(DiscoveryType.Reload, out st))
        { Set(st, HotbarSpot(), null); return true; }
        var ammoInv = AmmoInventory.Instance;
        if (gun && ammoInv != null && ammoInv.GetAmmo(held.type) <= 0 && Want(DiscoveryType.NoAmmo, out st)) { Set(st, HotbarSpot(), null); return true; }

        // 주변 훑기: 좀비 · 상자 · 바닥 아이템
        if (ScanNearby(pos)) return true;

        // 좀비를 본 다음: 조준 자세
        if (Seen(DiscoveryType.Zombie) && Want(DiscoveryType.Aim, out st)) { Set(st, PlayerSpot(st), null); return true; }

        // 손으로 붙인 DiscoveryTrigger
        if (ScanTriggers(pos)) return true;
        // (이전) 아무 건물 입구 7m + 경찰서·병원 등 종류별로 각각 안내
        // (2026-11-24) 건물 안내는 한 번만: 화면에 처음 보이는 건물 입구 하나를 비추고 "○○ 보이죠? 들어가면 ~가 있어요"
        if (Want(DiscoveryType.BuildingEntrance, out st))
            foreach (var e in entrances)
                if (FlatDist(pos, e.focus) < BuildingSeen && OnScreen(e.focus))
                {
                    Set(st, WorldSpot(e.focus, st.spotlightRadius), e.building);
                    candText = BuildingText(st, e.building as BuildingInterior);
                    return true;
                }

        // 미션 핀 · 상인 (미니맵을 비춤)
        var mm = MissionManager.Instance;
        if (mm != null && aliveFor > 25f && Want(DiscoveryType.MissionPin, out st))
        {
            mm.GetMapPins(pins);
            if (pins.Count > 0) { Set(st, MinimapSpot(), null); return true; }
        }
        var tm = TraderManager.Instance;
        var tr = tm != null ? tm.Nearest(pos) : null;
        if (tr != null && Want(DiscoveryType.Trader, out st))
        {
            Vector3 d = tr.HintCenter - pos; d.y = 0f;
            if (d.magnitude < tr.HintRadius + TraderNear) { Set(st, MinimapSpot(), null); return true; }
        }

        // 상황: 밤(노을) · 비 · 안개 · 마지막 날 · 체력 · 배고픔 · 돈
        var dnc = DayNightCycle.Instance;
        if (dnc != null && dnc.CurrentPhase == DayNightCycle.Phase.Dusk && Want(DiscoveryType.Night, out st)) { Set(st, PlayerSpot(st), null); return true; }
        if (WeatherManager.Current == WeatherManager.Weather.Rain && WeatherManager.RainAmount > 0.5f && Want(DiscoveryType.Rain, out st)) { Set(st, PlayerSpot(st), null); return true; }
        if (WeatherManager.Current == WeatherManager.Weather.Fog && WeatherManager.FogAmount > 0.5f && Want(DiscoveryType.Fog, out st)) { Set(st, PlayerSpot(st), null); return true; }
        if (dnc != null && dnc.CurrentDay >= dnc.totalDays && Want(DiscoveryType.FinalDay, out st)) { Set(st, PlayerSpot(st), null); return true; }
        if (health != null && health.GetCurrentHealth() > 0f && health.GetCurrentHealth() <= LowHealthHp && Want(DiscoveryType.LowHealth, out st)) { Set(st, PlayerSpot(st), null); return true; }
        if (needs != null && (needs.food <= NeedLow || needs.water <= NeedLow) && Want(DiscoveryType.Hunger, out st)) { Set(st, PlayerSpot(st), null); return true; }
        if (PlayerMoney.Instance != null && PlayerMoney.Instance.TotalEarned > 0 && Want(DiscoveryType.Money, out st)) { Set(st, PlayerSpot(st), null); return true; }
        return false;
    }

    bool ScanNearby(Vector3 pos)
    {
        int n = Physics.OverlapSphereNonAlloc(pos, ZombieNear, hits, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i];
            if (c == null) continue;

            // 태그 자동 매칭 (TutorialStep.targetTag가 있는 단계)
            foreach (var s in steps)
                if (!string.IsNullOrEmpty(s.targetTag) && c.CompareTag(s.targetTag) && !seen.Contains(Key(s.stepId))
                    && c.gameObject != lastTarget && OnScreen(c.transform.position))
                { Set(s, TransformSpot(c.transform, s.spotlightRadius), c.gameObject); return true; }

            var z = c.GetComponentInParent<ZombieController>();
            if (z != null)
            {
                if (z.targetOverride == null && !z.IsDead && OnScreen(z.transform.position) && Want(DiscoveryType.Zombie, out var zs))
                { Set(zs, TransformSpot(z.transform, zs.spotlightRadius), z); return true; }
                continue;
            }
            if (FlatDist(c.transform.position, pos) > ItemNear) continue;

            var crate = c.GetComponentInParent<LootCrate>();
            if (crate != null)
            {
                if (!crate.IsOpened && crate != lastTarget && OnScreen(crate.transform.position) && Want(DiscoveryType.LootCrate, out var cs))
                { Set(cs, TransformSpot(crate.transform, cs.spotlightRadius), crate); return true; }
                continue;
            }

            var item = c.GetComponentInParent<PickupItem>();
            if (item == null || item.itemData == null || item == lastTarget || !OnScreen(item.transform.position)) continue;
            // 종류별 안내가 먼저, 일반 줍기 안내는 그다음 아이템에서
            DiscoveryType kind = item.itemData switch
            {
                WeaponData _ => DiscoveryType.WeaponItem,
                AmmoData _ => DiscoveryType.AmmoItem,
                ThrowableData _ => DiscoveryType.ThrowableItem,
                ConsumableData cd when cd.healAmount > 0f || cd.healPerSecond > 0f => DiscoveryType.HealItem,
                _ => DiscoveryType.AnyItem,
            };
            if (Want(kind, out var ks) || Want(DiscoveryType.AnyItem, out ks))
            { Set(ks, TransformSpot(item.transform, ks.spotlightRadius), item); return true; }
        }
        return false;
    }

    bool ScanTriggers(Vector3 pos)
    {
        foreach (var t in DiscoveryTrigger.All)
        {
            if (t == null || string.IsNullOrEmpty(t.stepId) || seen.Contains(Key(t.stepId))) continue;
            if (!byId.TryGetValue(t.stepId, out var s)) continue;
            Vector3 f = t.Focus;
            if (FlatDist(pos, f) > t.detectRadius || !OnScreen(f)) continue;
            Set(s, WorldSpot(f, s.spotlightRadius), t.transform.parent != null ? t.transform.parent.gameObject : t.gameObject);
            return true;
        }
        return false;
    }

    // ── 건물·주유소에 DiscoveryTrigger 자동 부착 ───────
    void SetupWorldTriggers()
    {
        entrances.Clear();
        var p = GameObject.FindWithTag("Player");
        float refY = p != null ? p.transform.position.y : 15f;
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            if (!EntranceMarkers.TryGetDoor(b, out Vector3 door, out Vector3 outDir)) continue;
            Vector3 focus = door + outDir * 1.4f; // 입구 매트 위치 (EntranceMarkers.MatOffset)
            // 문 중심 높이가 지면과 다를 수 있음 (경찰서 문 y≈41, 지면 ≈15) → 탑뷰에서 원이 엉뚱한 곳에 그려지지 않게 지면으로
            if (MissionManager.TryGroundPoint(focus.x, focus.z, refY, out Vector3 g)) focus = g;
            else focus.y = refY;
            entrances.Add((focus, b));

            if (!AutoBuildingTriggers) continue;
            string id = BuildingStepId(b);
            if (id == null) continue;
            AddTrigger(b.gameObject, id, BuildingNear, focus);
        }
        if (!AutoBuildingTriggers) return;
        // 주유소: 거리 장식 주유기 (Street_Decor/GasStation, low_poly_gas_station) + 미션 장소로 정해진 주유소 건물
        foreach (var name in new[] { "GasStation", "low_poly_gas_station" })
        {
            var go = GameObject.Find(name);
            if (go != null) AddTrigger(go, "gas_station", 15f, BoundsCenter(go));
        }
        if (MissionPlaces.TryGet(MissionPlaces.Kind.GasStation, out var place) && place.building != null)
            AddTrigger(place.building.gameObject, "gas_station", BuildingNear, place.position);
    }

    static string BuildingStepId(BuildingInterior b)
    {
        if (b.name.Contains("School") || b.markerLabel == "학교") return "school";
        switch (LootDatabase.Classify(b))
        {
            case LootDatabase.BuildingKind.Police: return "police";
            case LootDatabase.BuildingKind.Hospital: return "hospital";
            case LootDatabase.BuildingKind.Mart: return "mart";
            case LootDatabase.BuildingKind.Church: return "church";
            case LootDatabase.BuildingKind.Warehouse: return "warehouse";
        }
        return b.markerLabel == "창고" ? "warehouse" : null; // 자동 꾸민 창고 테마 건물
    }

    static void AddTrigger(GameObject host, string stepId, float radius, Vector3 focus)
    {
        // 건물 오브젝트에 직접 붙이지 않고 자식으로 (원래 오브젝트의 컴포넌트 목록을 건드리지 않게)
        var go = new GameObject("DiscoveryTrigger_" + stepId);
        go.transform.SetParent(host.transform, false);
        var t = go.AddComponent<DiscoveryTrigger>();
        t.stepId = stepId;
        t.detectRadius = radius;
        t.useFocusPoint = true;
        t.focusPoint = focus;
    }

    static Vector3 BoundsCenter(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return go.transform.position;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    // ── 스포트라이트 위치 ─────────────────────────────
    static float FlatDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

    static bool OnScreen(Vector3 world)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 v = cam.WorldToViewportPoint(world);
        // 화면 안 (카메라 시야가 좁아서 - 플레이어 위치 가로 약 17m - 너무 엄격하면 거의 안 뜸)
        // 단, 위쪽 좌우 모서리는 미니맵·미션 패널에 가려서 비춰도 안 보이므로 제외
        if (v.z <= 0f || v.x < 0.08f || v.x > 0.92f || v.y < 0.1f || v.y > 0.85f) return false;
        if (v.y > 0.6f && (v.x < 0.15f || v.x > 0.77f)) return false;
        return true;
    }

    /// <summary>월드 점 + 반경(m) → 화면 픽셀 원 (카메라 오른쪽 방향으로 반경만큼 간 점까지 거리)</summary>
    static bool WorldToScreenCircle(Vector3 world, float radiusM, out Vector2 center, out float radius, out float feather)
    {
        center = Vector2.zero; radius = feather = 0f;
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 p = cam.WorldToScreenPoint(world);
        if (p.z <= 0f) return false;
        Vector3 r = cam.WorldToScreenPoint(world + cam.transform.right * radiusM);
        Vector3 f = cam.WorldToScreenPoint(world + cam.transform.right * FeatherMeters);
        center = p;
        radius = Vector2.Distance(p, r);
        feather = Vector2.Distance(p, f);
        return radiusM > 0f;
    }

    static TutorialOverlay.SpotFn WorldSpot(Vector3 world, float radiusM) =>
        (out Vector2 c, out float r, out float f) => WorldToScreenCircle(world, radiusM, out c, out r, out f);

    /// <summary>대상 Transform (몸통 높이 +1m를 비춤)</summary>
    static TutorialOverlay.SpotFn TransformSpot(Transform t, float radiusM)
    {
        Vector3 last = t.position;
        return (out Vector2 c, out float r, out float f) =>
        {
            if (t != null) last = t.position;
            return WorldToScreenCircle(last + Vector3.up * 1f, radiusM, out c, out r, out f);
        };
    }

    TutorialOverlay.SpotFn PlayerSpot(TutorialStep s) => TransformSpot(player, s.spotlightRadius);

    /// <summary>UI 영역을 감싸는 원 (화면 픽셀)</summary>
    static bool UiCircle(Rect rect, out Vector2 c, out float r, out float f)
    {
        c = rect.center;
        r = Mathf.Max(rect.width, rect.height) * 0.5f * 1.1f + 12f;
        f = 24f;
        return true;
    }

    TutorialOverlay.SpotFn MinimapSpot() => (out Vector2 c, out float r, out float f) =>
    {
        c = Vector2.zero; r = f = 0f;
        return overlay.TryGetMinimapScreenRect(out Rect rect) && UiCircle(rect, out c, out r, out f);
    };

    TutorialOverlay.SpotFn HotbarSpot() => (out Vector2 c, out float r, out float f) =>
    {
        c = Vector2.zero; r = f = 0f;
        var hb = WeaponHotbarUI.Instance;
        if (hb == null || !TutorialOverlay.TryGetScreenRect(hb.transform as RectTransform, out Rect rect)) return false;
        // 핫바는 가로로 길어서 양 끝 칸까지 들어오게 가로폭 절반 + 여유
        c = rect.center;
        r = rect.width * 0.5f + 40f;
        f = 24f;
        return true;
    };
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// (2026-11-23) 상황별 안내 문구: 어떤 상황이 처음 생겼을 때 한 번만, 화면 위쪽(날짜 HUD 아래)에 몇 초 띄운다.
/// 전시회에서 처음 해 본 사람들이 키도 모르고 할 일도 몰라 어려워해서 추가.
///  - 저장하지 않는다: 새 게임마다 다시 나옴 (전시회처럼 매번 다른 사람이 하므로)
///  - 한 번에 하나씩, 6초 (뒤에 쌓인 게 있으면 순서대로). 일시정지 중엔 멈춰 있음
///  - 문구는 한국어/영어 (설정 언어)
/// 안내 목록과 조건은 아래 Check()에 모여 있다.
/// </summary>
public class GuideHints : MonoBehaviour
{
    enum Hint { Move, Weapon, Zombie, Pickup, Crate, Money, Mission, Trader, Night, Needs, LowHealth, NoAmmo }

    const float ShowSeconds = 6f, FadeSeconds = 0.35f, GapSeconds = 0.6f, CheckInterval = 0.5f;
    const float ZombieNear = 14f, ItemNear = 4.5f, TraderNear = 30f;

    readonly HashSet<Hint> done = new HashSet<Hint>();
    readonly Queue<Hint> queue = new Queue<Hint>();
    CanvasGroup group;
    TextMeshProUGUI label;
    float shownFor = -1f, gapLeft, checkTimer, aliveFor;
    Transform player;
    Health health;
    SurvivalNeeds needs;
    readonly Collider[] hits = new Collider[64];

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
        if (FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include) == null) return;   // 게임 씬에서만
        if (FindFirstObjectByType<GuideHints>() != null) return;
        new GameObject("GuideHints").AddComponent<GuideHints>();
    }

    void Awake() => BuildUi();

    // ── 화면 ─────────────────────────────────────
    void BuildUi()
    {
        var canvasGo = new GameObject("GuideHintsCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6;   // 미션 HUD(5) 위, 사망 화면(1000)·메뉴 아래
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("Hint", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(canvasGo.transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -300f);   // 날짜 HUD(위쪽 약 280px) 바로 아래
        rt.sizeDelta = new Vector2(820f, 96f);
        var bg = panel.GetComponent<Image>();
        bg.color = HudStyle.ApocPanel;
        bg.raycastTarget = false;

        var bar = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(panel.transform, false);
        var brt = (RectTransform)bar.transform;
        brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0f, 1f);
        brt.pivot = new Vector2(0f, 0.5f);
        brt.sizeDelta = new Vector2(5f, 0f);
        brt.anchoredPosition = Vector2.zero;
        var bi = bar.GetComponent<Image>();
        bi.color = HudStyle.Highlight;
        bi.raycastTarget = false;

        label = HudStyle.NewText(panel.transform, "Text", "", HudStyle.Regular, 25f, HudStyle.Text,
            TextAlignmentOptions.Center, Vector2.zero, new Vector2(790f, 90f));
        label.richText = true;
        var lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = Vector2.zero;

        group = panel.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    // ── 진행 ─────────────────────────────────────
    void Update()
    {
        if (Time.timeScale <= 0f) return;   // 일시정지·사망 연출 중엔 멈춤
        float dt = Time.unscaledDeltaTime;
        aliveFor += dt;
        FindRefs();

        checkTimer -= dt;
        if (checkTimer <= 0f) { checkTimer = CheckInterval; Check(); }

        if (shownFor >= 0f)
        {
            shownFor += dt;
            float a = shownFor < FadeSeconds ? shownFor / FadeSeconds
                    : shownFor > ShowSeconds - FadeSeconds ? Mathf.Clamp01((ShowSeconds - shownFor) / FadeSeconds) : 1f;
            group.alpha = a;
            if (shownFor >= ShowSeconds) { shownFor = -1f; group.alpha = 0f; gapLeft = GapSeconds; }
            return;
        }
        if (gapLeft > 0f) { gapLeft -= dt; return; }
        if (queue.Count > 0) Show(queue.Dequeue());
    }

    void FindRefs()
    {
        if (player != null) return;
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        player = p.transform;
        health = p.GetComponent<Health>();
        needs = p.GetComponent<SurvivalNeeds>();
        if (needs == null) needs = FindFirstObjectByType<SurvivalNeeds>();
    }

    void Want(Hint h)
    {
        if (done.Contains(h)) return;
        done.Add(h);
        queue.Enqueue(h);
    }

    void Show(Hint h)
    {
        label.text = TextOf(h);
        shownFor = 0f;
    }

    /// <summary>안내를 띄울 조건 — 처음 한 번만</summary>
    void Check()
    {
        if (player == null) return;
        Vector3 pos = player.position;

        if (aliveFor > 1.5f) { Want(Hint.Move); Want(Hint.Weapon); }
        if (aliveFor < 3f) return;   // 시작 안내 두 개가 먼저

        // 주변: 좀비 · 바닥 아이템 · 상자
        int n = Physics.OverlapSphereNonAlloc(pos, ZombieNear, hits, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            if (!done.Contains(Hint.Zombie))
            {
                var z = c.GetComponentInParent<ZombieController>();
                if (z != null && z.targetOverride == null && !z.IsDead) Want(Hint.Zombie);
            }
            float d = Vector3.Distance(c.transform.position, pos);
            if (d > ItemNear) continue;
            if (!done.Contains(Hint.Pickup) && c.GetComponentInParent<PickupItem>() != null) Want(Hint.Pickup);
            if (!done.Contains(Hint.Crate))
            {
                var crate = c.GetComponentInParent<LootCrate>();
                if (crate != null && !crate.IsOpened) Want(Hint.Crate);
            }
        }

        // 돈 · 미션 · 상인
        if (PlayerMoney.Instance != null && PlayerMoney.Instance.TotalEarned > 0) Want(Hint.Money);
        var mm = MissionManager.Instance;
        if (mm != null && mm.TodayMain != null && aliveFor > 25f) Want(Hint.Mission);
        var tm = TraderManager.Instance;
        var t = tm != null ? tm.Nearest(pos) : null;
        if (t != null)
        {
            Vector3 d = t.HintCenter - pos; d.y = 0f;
            if (d.magnitude < t.HintRadius + TraderNear) Want(Hint.Trader);
        }

        // 밤이 오기 전 (노을)
        var dnc = DayNightCycle.Instance;
        if (dnc != null && dnc.CurrentPhase == DayNightCycle.Phase.Dusk) Want(Hint.Night);

        // 배고픔·갈증 · 체력 · 탄약
        if (needs != null && (needs.water < 40f || needs.food < 40f)) Want(Hint.Needs);
        if (health != null && health.GetMaxHealth() > 0f && health.GetCurrentHealth() > 0f
            && health.GetCurrentHealth() / health.GetMaxHealth() < 0.5f) Want(Hint.LowHealth);
        var hotbar = WeaponHotbarUI.Instance;
        var ammo = AmmoInventory.Instance;
        if (hotbar != null && ammo != null && hotbar.GetActiveItem() is WeaponData w
            && w.type != WeaponType.Melee && ammo.GetAmmo(w.type) <= 0) Want(Hint.NoAmmo);
    }

    // ── 문구 ─────────────────────────────────────
    static string K(string key) => $"<color=#FFD700><b>{key}</b></color>";

    static string TextOf(Hint h)
    {
        bool en = Localization.IsEnglish;
        switch (h)
        {
            case Hint.Move:
                return en ? $"{K("WASD")} move · {K("Mouse")} aim · {K("Left click")} attack\n{K("Shift")} sprint · {K("Space")} roll (invincible while rolling)"
                          : $"{K("WASD")} 이동 · {K("마우스")} 조준 · {K("좌클릭")} 공격\n{K("Shift")} 달리기 · {K("Space")} 구르기 (구르는 동안 무적)";
            case Hint.Weapon:
                return en ? $"{K("1·2")} guns · {K("4")} axe · {K("R")} reload\nHold {K("Right click")} to aim"
                          : $"{K("1·2")} 총 · {K("4")} 도끼 · {K("R")} 재장전\n{K("우클릭")}을 누르고 있으면 조준 자세";
            case Hint.Zombie:
                return en ? "A zombie is coming! Keep your distance and shoot\nGunshots attract other zombies too"
                          : "좀비가 다가와요! 거리를 두고 쏘세요\n총소리는 다른 좀비도 불러요";
            case Hint.Pickup:
                return en ? $"{K("F")} pick up · {K("I")} / {K("Tab")} open bag\nRight-click an item in the bag to use or craft"
                          : $"{K("F")} 줍기 · {K("I")} 또는 {K("Tab")} 가방 열기\n가방에서 아이템을 우클릭하면 사용·제작";
            case Hint.Crate:
                return en ? $"Press {K("F")} to open the crate\nBetter crates hold better loot"
                          : $"{K("F")}로 상자를 열어 보세요\n좋은 상자일수록 좋은 물건이 나와요";
            case Hint.Money:
                return en ? "Killing zombies earns money\nBuy ammo, healing and upgrades from traders"
                          : "좀비를 잡으면 돈이 생겨요\n상인에게서 탄약·회복템·강화를 살 수 있어요";
            case Hint.Mission:
                return en ? "The pin on the minimap is today's mission\nFinish missions to get closer to escaping on Day 7"
                          : "미니맵의 핀이 오늘의 미션이에요\n미션을 끝낼수록 7일차 탈출이 가까워져요";
            case Hint.Trader:
                return en ? "A trader is somewhere in the gold circle on the minimap\nGet close and press F to trade"
                          : "미니맵의 금색 원 안에 상인이 있어요\n가까이 가서 F로 거래하세요";
            case Hint.Night:
                return en ? "Night is coming — more zombies, and they notice you from farther away\nPress T for the flashlight"
                          : "곧 밤이에요 — 좀비가 많아지고 더 멀리서 알아채요\nT로 손전등을 켜세요";
            case Hint.Needs:
                return en ? "Thirst and hunger drain your health\nRight-click water or food in the bag → Use"
                          : "목마르거나 배고프면 체력이 줄어요\n가방에서 물·음식을 우클릭 → 사용";
            case Hint.LowHealth:
                return en ? "Low health! Heal with bandages or a medkit\nA zombie hit interrupts healing — find a safe spot first"
                          : "체력이 낮아요! 붕대·구급상자로 회복하세요\n좀비에게 맞으면 끊기니 안전한 곳에서";
            case Hint.NoAmmo:
                return en ? "Out of spare ammo for this gun\nFind more in crates, from zombies or traders — use the axe (4) meanwhile"
                          : "이 총의 예비 탄약이 떨어졌어요\n상자·좀비·상인에게서 구하고, 그동안은 도끼(4)로";
        }
        return "";
    }
}

using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 영구 강화 (상점 4단계). 이번 판 동안만 유지 (죽거나 다시 시작하면 처음부터).
///   이동속도     +5% / 단계      (걷기·달리기)
///   스태미나 최대치 +15 / 단계
///   스태미나 회복  +20% / 단계
///   최대 체력     +10 / 단계  (살 때 그만큼 회복도 됨)
///  - 강화마다 최대 3단계, 가격 $40 → $70 → $110
///  - 상인마다 0~1개 진열 (ShopStock.Generate). 사면 바로 적용, 가방 안 차지함
///  - 인벤토리를 열면 창 왼쪽 바깥에 '강화' 카드 (UpgradeCard) —— 하나라도 샀을 때만
///  - 아드레날린 같은 속도 버프(PlayerBuffManager)가 끝나도 강화된 속도로 돌아오게 그쪽 '원래 속도'도 같이 올림
/// </summary>
public class PlayerUpgrades : MonoBehaviour
{
    public static PlayerUpgrades Instance { get; private set; }

    public const int MaxLevel = 3;
    private static readonly int[] Prices = { 40, 70, 110 };
    private const float SpeedPerLevel = 0.05f, StaminaPerLevel = 15f, RegenPerLevel = 0.20f, HealthPerLevel = 10f;

    public static readonly UpgradeType[] All = { UpgradeType.MoveSpeed, UpgradeType.StaminaMax, UpgradeType.StaminaRegen, UpgradeType.MaxHealth };

    private readonly int[] levels = new int[4];
    private UpgradeItemData[] items;

    // 강화 전 원래 값 (처음 적용할 때 잡음)
    private bool baseCaptured;
    private float baseWalk, baseSprint, baseStaminaMax, baseRegen;

    private static readonly FieldInfo BuffBaseWalk = typeof(PlayerBuffManager).GetField("baseWalkSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo BuffBaseSprint = typeof(PlayerBuffManager).GetField("baseSprintSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

    private float nextCardTry;
    private int cardTries;
    private bool cardBuilt;

    /// <summary>강화 단계가 바뀌면 (종류, 새 단계)</summary>
    public event System.Action<UpgradeType, int> Changed;

    // ── 자동 생성 ─────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != null) return;
        if (FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include) == null) return;   // 게임 씬에서만
        new GameObject("PlayerUpgrades").AddComponent<PlayerUpgrades>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildItems();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (items != null) foreach (var it in items) if (it != null) Destroy(it);
    }

    private void Update()
    {
        // 강화 카드는 인벤토리 창을 찾아 붙인다 (늦게 생겨도 몇 번 다시 시도)
        if (cardBuilt || cardTries >= 20 || Time.unscaledTime < nextCardTry) return;
        nextCardTry = Time.unscaledTime + 0.5f;
        cardTries++;
        cardBuilt = UpgradeCard.Create(this);
    }

    // ── 정보 ─────────────────────────────────────
    public int Level(UpgradeType t) => levels[(int)t];
    public bool IsMaxed(UpgradeType t) => Level(t) >= MaxLevel;
    public bool Any() { foreach (var l in levels) if (l > 0) return true; return false; }

    /// <summary>다음 단계 가격 (최대면 0)</summary>
    public static int NextPrice(UpgradeType t)
    {
        int lv = Instance != null ? Instance.Level(t) : 0;
        return lv >= MaxLevel ? 0 : Prices[lv];
    }

    public UpgradeItemData Item(UpgradeType t) => items[(int)t];

    public static string Name(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.MoveSpeed: return "이동속도 강화";
            case UpgradeType.StaminaMax: return "스태미나 최대치 강화";
            case UpgradeType.StaminaRegen: return "스태미나 회복 강화";
            default: return "최대 체력 강화";
        }
    }

    /// <summary>카드에 쓰는 짧은 이름</summary>
    public static string ShortName(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.MoveSpeed: return "이동속도";
            case UpgradeType.StaminaMax: return "스태미나";
            case UpgradeType.StaminaRegen: return "스태미나 회복";
            default: return "최대 체력";
        }
    }

    /// <summary>한 단계 효과</summary>
    public static string EffectText(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.MoveSpeed: return "이동속도 +5%";
            case UpgradeType.StaminaMax: return "스태미나 최대치 +15";
            case UpgradeType.StaminaRegen: return "스태미나 회복 +20%";
            default: return "최대 체력 +10";
        }
    }

    private void BuildItems()
    {
        items = new UpgradeItemData[4];
        foreach (var t in All)
        {
            var it = ScriptableObject.CreateInstance<UpgradeItemData>();
            it.name = "Upgrade_" + t;
            it.upgrade = t;
            it.itemName = Name(t);
            it.rarity = Rarity.Epic;   // 보라색 등급 줄로 눈에 띄게
            it.itemType = ItemType.Material;
            switch (t)
            {
                case UpgradeType.MoveSpeed: it.icon = HudStyle.NavArrow; break;
                case UpgradeType.StaminaMax: it.icon = HudStyle.Bolt; break;
                case UpgradeType.StaminaRegen: it.icon = HudStyle.RingThick; break;
                default: it.icon = HudStyle.Heart; break;
            }
            items[(int)t] = it;
        }
    }

    // ── 사기 ─────────────────────────────────────
    /// <summary>한 단계 올린다 (돈은 상점이 뺌). 최대면 false</summary>
    public bool LevelUp(UpgradeType t)
    {
        if (IsMaxed(t)) return false;
        levels[(int)t]++;
        Apply(t);
        Changed?.Invoke(t, levels[(int)t]);
        return true;
    }

    private void Apply(UpgradeType t)
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        var loco = player.GetComponent<Locomotion>();
        var buff = player.GetComponent<PlayerBuffManager>();
        int lv = Level(t);

        if (loco != null && !baseCaptured)
        {
            // 버프가 걸려 있을 수 있으니 속도 원래 값은 버프 쪽 기록을 우선
            baseWalk = buff != null && BuffBaseWalk != null ? (float)BuffBaseWalk.GetValue(buff) : loco.walkSpeed;
            baseSprint = buff != null && BuffBaseSprint != null ? (float)BuffBaseSprint.GetValue(buff) : loco.sprintSpeed;
            baseStaminaMax = loco.maxStamina;
            baseRegen = loco.staminaRegen;
            baseCaptured = true;
        }

        switch (t)
        {
            case UpgradeType.MoveSpeed:
            {
                if (loco == null) break;
                float mul = 1f + SpeedPerLevel * lv;
                float newWalk = baseWalk * mul, newSprint = baseSprint * mul;
                // 지금 버프로 빨라져 있으면 그 배율은 유지
                float curBaseWalk = buff != null && BuffBaseWalk != null ? (float)BuffBaseWalk.GetValue(buff) : baseWalk * (1f + SpeedPerLevel * (lv - 1));
                float curBaseSprint = buff != null && BuffBaseSprint != null ? (float)BuffBaseSprint.GetValue(buff) : baseSprint * (1f + SpeedPerLevel * (lv - 1));
                float ratioW = curBaseWalk > 0f ? loco.walkSpeed / curBaseWalk : 1f;
                float ratioS = curBaseSprint > 0f ? loco.sprintSpeed / curBaseSprint : 1f;
                loco.walkSpeed = newWalk * ratioW;
                loco.sprintSpeed = newSprint * ratioS;
                if (buff != null)
                {
                    BuffBaseWalk?.SetValue(buff, newWalk);     // 버프가 끝나면 강화된 속도로 돌아옴
                    BuffBaseSprint?.SetValue(buff, newSprint);
                }
                break;
            }
            case UpgradeType.StaminaMax:
                if (loco != null) loco.maxStamina = baseStaminaMax + StaminaPerLevel * lv;
                break;
            case UpgradeType.StaminaRegen:
                if (loco != null) loco.staminaRegen = baseRegen * (1f + RegenPerLevel * lv);
                break;
            case UpgradeType.MaxHealth:
            {
                var h = player.GetComponent<Health>();
                if (h == null) break;
                h.maxHealth += HealthPerLevel;
                h.Heal(HealthPerLevel);   // 늘어난 만큼 채워 줌
                break;
            }
        }
    }
}

/// <summary>
/// 인벤토리 창 왼쪽 바깥에 붙는 '강화' 카드.
///   강화
///   이동속도      ● ● ○
///   스태미나      ● ○ ○ …
///  - 인벤토리 창의 자식이라 인벤토리를 열 때만 보임. 하나도 안 샀으면 숨김
///  - 가방에 따라 창 크기가 바뀌어도 창의 왼쪽 아래 모서리를 따라감 (위쪽은 생존 날짜 HUD와 겹쳐서 아래에 붙임)
/// </summary>
public class UpgradeCard : MonoBehaviour
{
    private static readonly Color PanelBg = new Color32(32, 34, 38, 225);
    private static readonly Color PanelBorder = new Color32(100, 98, 96, 255);
    private static readonly Color PipOn = new Color32(0xB0, 0x6C, 0xFF, 0xFF);   // 영웅 등급 보라
    private static readonly Color PipOff = new Color(1f, 1f, 1f, 0.15f);
    private const float CardW = 230f, Gap = 12f, RowH = 30f, HeadH = 44f, Pad = 12f;

    private PlayerUpgrades upgrades;
    private RectTransform card, window;
    private GameObject body;
    private UnityEngine.UI.Image[,] pips;
    private bool dirty = true;

    public static bool Create(PlayerUpgrades up)
    {
        var ui = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (ui == null || ui.inventoryWindow == null) return false;
        var win = ui.inventoryWindow.transform as RectTransform;
        if (win == null) return false;

        float h = HeadH + RowH * PlayerUpgrades.All.Length + Pad;
        var rt = HudStyle.NewRect(win, "UpgradeCard", Vector2.zero, new Vector2(CardW, h));
        var c = rt.gameObject.AddComponent<UpgradeCard>();
        c.Build(up, rt, win, h);
        return true;
    }

    private void Build(PlayerUpgrades up, RectTransform rt, RectTransform win, float h)
    {
        upgrades = up;
        card = rt;
        window = win;
        body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(card, false);
        var brt = (RectTransform)body.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(CardW, h);

        var bg = HudStyle.NewImage(body.transform, "Bg", HudStyle.RoundedRect, PanelBg, Vector2.zero, new Vector2(CardW, h));
        bg.pixelsPerUnitMultiplier = 2f;
        var border = HudStyle.NewImage(body.transform, "Border", HudStyle.RoundedFrame, PanelBorder, Vector2.zero, new Vector2(CardW, h));
        border.pixelsPerUnitMultiplier = 2f;

        // 제목 글꼴·색은 인벤토리 '탄약' 라벨과 맞춤
        var refLabel = window.Find("HUD_AmmoLabel")?.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset labelFont = refLabel != null ? refLabel.font : HudStyle.SemiBold;
        float labelSize = refLabel != null ? refLabel.fontSize : 18f;
        Color labelColor = refLabel != null ? refLabel.color : HudStyle.TextDim;

        float top = h / 2f;
        HudStyle.NewText(body.transform, "Title", "강화", labelFont, labelSize, labelColor,
            TextAlignmentOptions.Left, new Vector2(-CardW / 2f + Pad + 60f, top - HeadH / 2f), new Vector2(120f, HeadH));

        pips = new UnityEngine.UI.Image[PlayerUpgrades.All.Length, PlayerUpgrades.MaxLevel];
        for (int i = 0; i < PlayerUpgrades.All.Length; i++)
        {
            float y = top - HeadH - RowH * i - RowH / 2f;
            HudStyle.NewText(body.transform, "Name", PlayerUpgrades.ShortName(PlayerUpgrades.All[i]), HudStyle.Regular, 17f, new Color(1f, 1f, 1f, 0.85f),
                TextAlignmentOptions.Left, new Vector2(-CardW / 2f + Pad + 70f, y), new Vector2(140f, RowH));
            for (int k = 0; k < PlayerUpgrades.MaxLevel; k++)
            {
                var pip = HudStyle.NewImage(body.transform, "Pip", HudStyle.Circle, PipOff,
                    new Vector2(CardW / 2f - Pad - 8f - (PlayerUpgrades.MaxLevel - 1 - k) * 20f, y), new Vector2(12f, 12f));
                pip.raycastTarget = false;
                pips[i, k] = pip;
            }
        }

        upgrades.Changed += OnChanged;
        Follow();
        Refresh();
    }

    private void OnDestroy()
    {
        if (upgrades != null) upgrades.Changed -= OnChanged;
    }

    private void OnChanged(UpgradeType t, int lv) => dirty = true;

    private void LateUpdate()   // 인벤토리 창이 열려 있을 때만 돈다
    {
        Follow();
        if (dirty) Refresh();
    }

    /// <summary>창 왼쪽 바깥, 아래쪽을 맞춰 붙음</summary>
    private void Follow()
    {
        if (card == null || window == null) return;
        Rect r = window.rect;
        float h = card.sizeDelta.y;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        // 가운데 기준 배치라 창의 왼쪽 끝 = -너비/2, 아래 끝 = -높이/2
        card.anchoredPosition = new Vector2(-r.width / 2f - Gap - CardW / 2f, -r.height / 2f + h / 2f);
    }

    private void Refresh()
    {
        dirty = false;
        if (upgrades == null || pips == null) return;
        body.SetActive(upgrades.Any());
        for (int i = 0; i < PlayerUpgrades.All.Length; i++)
        {
            int lv = upgrades.Level(PlayerUpgrades.All[i]);
            for (int k = 0; k < PlayerUpgrades.MaxLevel; k++) pips[i, k].color = k < lv ? PipOn : PipOff;
        }
    }
}

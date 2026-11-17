using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이어 돈 (상점 1단계).
///  - 좀비를 잡으면 바로 지급 + 시체 위에 작게 "+$2" 표시. 보상 = 좀비 최대 체력 × moneyPerHealth
///    (일반 좀비 체력 50 → $2, 날이 갈수록 좀비 체력이 늘어 보상도 조금씩 늘어남), 사냥꾼(붉은 눈)은 hunterMultiplier배.
///  - 아이템 판매·구매는 상인(다음 단계)에서 Add / TrySpend로 처리.
///  - 게임 씬(PlayerController가 있는 씬)에 자동 생성 → 씬을 수정하지 않아도 됨. 다시 시작하면 0부터.
///  - 화면 표시는 MoneyHUD (인벤토리 창의 탄약 패널 아래 줄).
/// </summary>
[DefaultExecutionOrder(-50)]
public class PlayerMoney : MonoBehaviour
{
    public static PlayerMoney Instance { get; private set; }

    [Header("시작 금액")]
    public int startingMoney = 0;

    [Header("좀비 처치 보상")]
    [Tooltip("좀비 최대 체력 1당 돈 (체력 50 → $2)")]
    public float moneyPerHealth = 0.04f;
    public int minKillReward = 1;
    public int maxKillReward = 15;
    [Tooltip("사냥꾼(붉은 눈) 보상 배율")]
    public float hunterMultiplier = 2f;

    [Header("표시")]
    [Tooltip("좀비를 잡았을 때 시체 위에 '+$2'를 작게 띄울지")]
    public bool showPopup = true;
    [Tooltip("튀지 않게 살짝 바랜 금색")]
    public Color popupColor = new Color(0.92f, 0.80f, 0.42f, 0.85f);
    [Tooltip("글자 크기 (월드). 데미지 숫자보다 작게")]
    public float popupSize = 2.6f;

    /// <summary>지금 가진 돈</summary>
    public int Amount { get; private set; }
    /// <summary>이번 판에 번 돈 합계</summary>
    public int TotalEarned { get; private set; }
    /// <summary>(지금 금액, 변화량) —— 벌면 +, 쓰면 -</summary>
    public event Action<int, int> Changed;

    private float nextHudTry;
    private int hudTries;
    private bool hudBuilt;

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
        // 게임 씬에서만 (메인 메뉴·학습장에는 PlayerController가 없음)
        if (FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include) == null) return;
        new GameObject("PlayerMoney").AddComponent<PlayerMoney>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Amount = Mathf.Max(0, startingMoney);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnEnable() => ZombieController.AnyDied += OnZombieDied;
    private void OnDisable() => ZombieController.AnyDied -= OnZombieDied;

    private void Update()
    {
        // 돈 표시는 인벤토리 창을 찾아 탄약 패널 아래에 붙인다 (늦게 생겨도 몇 번 다시 시도)
        if (hudBuilt || hudTries >= 20 || Time.unscaledTime < nextHudTry) return;
        nextHudTry = Time.unscaledTime + 0.5f;
        hudTries++;
        hudBuilt = MoneyHUD.Create(this);
        if (!hudBuilt && hudTries >= 20) Debug.LogWarning("[PlayerMoney] 인벤토리 창(InventoryUI)을 못 찾아 돈 표시를 만들지 못했습니다");
    }

    // ── 돈 ───────────────────────────────────────
    /// <summary>돈을 번다. popupAt을 주면 그 자리(월드)에 '+$n'을 띄운다</summary>
    public void Add(int amount, Vector3? popupAt = null)
    {
        if (amount <= 0) return;
        Amount += amount;
        TotalEarned += amount;
        Changed?.Invoke(Amount, amount);
        if (showPopup && popupAt.HasValue) MoneyPopup.Spawn(popupAt.Value, "+$" + amount, popupColor, popupSize);
    }

    public bool CanAfford(int price) => price >= 0 && price <= Amount;

    /// <summary>돈을 쓴다. 모자라면 아무 일도 없이 false</summary>
    public bool TrySpend(int price)
    {
        if (!CanAfford(price)) return false;
        if (price == 0) return true;
        Amount -= price;
        Changed?.Invoke(Amount, -price);
        return true;
    }

    /// <summary>이 좀비를 잡으면 받을 돈</summary>
    public int KillReward(ZombieController z)
    {
        var h = z.GetComponent<Health>();
        float hp = h != null ? h.maxHealth : z.maxHealth;
        float reward = Mathf.Clamp(hp * moneyPerHealth, minKillReward, maxKillReward);
        if (z.GetComponentInChildren<HunterEyes>(true) != null) reward *= hunterMultiplier;
        return Mathf.Max(1, Mathf.RoundToInt(reward));
    }

    private void OnZombieDied(ZombieController z)
    {
        if (z == null || z.targetOverride != null) return;   // 학습장 좀비 제외 (GameStats와 같은 기준)
        Add(KillReward(z), z.transform.position + Vector3.up * 1.8f);
    }

    public static string Format(int amount) => "$" + amount.ToString("N0");
}

/// <summary>월드에 잠깐 떠올랐다 사라지는 작은 글자 ('+$2')</summary>
public class MoneyPopup : MonoBehaviour
{
    private const float Life = 0.9f;
    private const float Rise = 0.7f;
    private TextMeshPro text;
    private Color color;
    private Vector3 start;
    private float age;

    public static void Spawn(Vector3 position, string label, Color color, float size = 2.6f)
    {
        var go = new GameObject("MoneyPopup");
        go.transform.position = position;
        var p = go.AddComponent<MoneyPopup>();
        p.start = position;
        p.color = color;
        p.text = go.AddComponent<TextMeshPro>();
        if (HudStyle.SemiBold != null) p.text.font = HudStyle.SemiBold;
        p.text.text = label;
        p.text.fontSize = size;
        p.text.alignment = TextAlignmentOptions.Center;
        p.text.textWrappingMode = TextWrappingModes.NoWrap;
        p.text.rectTransform.sizeDelta = new Vector2(4f, 1.5f);
        p.text.color = color;
        p.text.sortingOrder = 50;
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / Life);
        transform.position = start + Vector3.up * (Rise * (1f - (1f - k) * (1f - k)));   // 빠르게 솟았다 멈춤
        if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
        var c = color;
        c.a = color.a * (k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f);
        text.color = c;
        if (age >= Life) Destroy(gameObject);
    }
}

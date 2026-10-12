using UnityEngine;

/// <summary>
/// 수분/포만감 생존 수치 (0~100%). Player에 붙임.
/// 게임 내 하루(DayNightCycle 1사이클 길이) 기준으로 서서히 감소하고,
/// 0%가 되면 그 수치마다 초당 starvationDamagePerSecond 만큼 체력이 깎임 (둘 다 0이면 2배).
/// 소비 아이템(ConsumableData.waterAmount / foodAmount)을 먹으면 회복 — PlayerBuffManager.ApplyConsumable에서 호출.
/// </summary>
public class SurvivalNeeds : MonoBehaviour
{
    [Header("100% → 0%까지 걸리는 게임 내 일수")]
    [Min(0.1f)] public float waterDaysToEmpty = 6f;
    [Min(0.1f)] public float foodDaysToEmpty = 10f;

    [Header("0%일 때 체력 감소 (수치 하나당, 초당)")]
    public float starvationDamagePerSecond = 1f;

    [Header("현재 값 (0~100)")]
    [Range(0f, 100f)] public float water = 100f;
    [Range(0f, 100f)] public float food = 100f;

    [Tooltip("DayNightCycle이 없을 때 쓰는 하루 길이(초)")]
    public float fallbackDayLengthSeconds = 120f;

    Health health;
    float damageTimer;

    public float WaterNormalized => water / 100f;
    public float FoodNormalized => food / 100f;

    void Awake() => health = GetComponent<Health>();

    void Update()
    {
        var dnc = DayNightCycle.Instance;
        float dayLength = dnc != null ? dnc.GetTotalCycleDuration() : fallbackDayLengthSeconds;

        water = Mathf.Max(0f, water - 100f / (waterDaysToEmpty * dayLength) * Time.deltaTime);
        food = Mathf.Max(0f, food - 100f / (foodDaysToEmpty * dayLength) * Time.deltaTime);

        // 0%인 수치가 있으면 1초마다 한 번씩 데미지 (매 프레임 소수점 데미지는 데미지 숫자 UI가 도배되므로)
        int empty = (water <= 0f ? 1 : 0) + (food <= 0f ? 1 : 0);
        if (empty == 0 || health == null) { damageTimer = 0f; return; }

        damageTimer += Time.deltaTime;
        if (damageTimer >= 1f)
        {
            damageTimer -= 1f;
            health.TakeDamage(starvationDamagePerSecond * empty);
        }
    }

    public void Drink(float amount) => water = Mathf.Clamp(water + amount, 0f, 100f);
    public void Eat(float amount) => food = Mathf.Clamp(food + amount, 0f, 100f);
}

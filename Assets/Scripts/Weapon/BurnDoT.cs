using UnityEngine;

/// <summary>
/// 화염방사기 점화 DoT (2026-11-16).
/// 맞은 좀비에 붙어서 burnDuration 동안 초당 burnDps 데미지. 다시 맞으면 시간만 갱신 (중첩 X).
/// 데미지는 0.5초마다 나눠서 줌 (매 프레임 주면 데미지 숫자가 너무 많이 뜸).
/// </summary>
public class BurnDoT : MonoBehaviour
{
    const float TickInterval = 0.5f;

    float dps;
    float endTime;
    float nextTick;
    Health health;
    ZombieController zombie;
    Light glow;

    public static void Apply(GameObject target, float dps, float duration)
    {
        if (target == null) return;
        var b = target.GetComponent<BurnDoT>();
        if (b == null) b = target.AddComponent<BurnDoT>();
        b.dps = Mathf.Max(b.dps, dps);
        b.endTime = Time.time + duration;
        if (b.nextTick <= 0f) b.nextTick = Time.time + TickInterval;
    }

    void Awake()
    {
        health = GetComponent<Health>();
        zombie = GetComponent<ZombieController>();
        // 불붙은 표시: 작은 주황 점광 (전용 불꽃 이펙트 에셋이 없어서)
        var go = new GameObject("BurnGlow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.2f;
        glow = go.AddComponent<Light>();
        glow.type = LightType.Point; glow.color = new Color(1f, 0.4f, 0.05f); glow.range = 3f; glow.intensity = 1.5f;
        glow.shadows = LightShadows.None;
    }

    void Update()
    {
        bool dead = zombie != null && zombie.IsDead;
        if (dead || Time.time >= endTime || health == null) { Destroy(glow.gameObject); Destroy(this); return; }
        glow.intensity = 1.2f + Mathf.PerlinNoise(Time.time * 8f, 0f) * 0.8f; // 일렁임
        if (Time.time < nextTick) return;
        nextTick = Time.time + TickInterval;
        health.TakeDamage(dps * TickInterval);
    }
}

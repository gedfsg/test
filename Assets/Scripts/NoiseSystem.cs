using UnityEngine;

/// <summary>
/// 소음 시스템 (최소 구현). 플레이어가 총을 쏘거나 근접 무기를 휘두르면 반경 안의 좀비가 그 위치로 확인하러 옴.
///  - 반경 = 무기별 기본 반경 × WeatherManager.NoiseMultiplier (비 0.5 → 빗소리에 묻힘)
///  - 이미 플레이어를 쫓는 좀비는 그대로 (ZombieController.HearNoise에서 처리)
/// </summary>
public static class NoiseSystem
{
    // 무기별 기본 소음 반경 (m)
    public const float PistolRadius = 25f;
    public const float RifleRadius = 35f;
    public const float ShotgunRadius = 40f;
    public const float SniperRadius = 50f;
    public const float MeleeRadius = 8f;

    public static bool debugLog = false;

    public static float RadiusFor(WeaponType type) => type switch
    {
        WeaponType.Pistol => PistolRadius,
        WeaponType.Shotgun => ShotgunRadius,
        WeaponType.Sniper => SniperRadius,
        WeaponType.Melee => MeleeRadius,
        _ => RifleRadius, // AR / SMG(AR 탄 공유) / Ranged(하위 호환)
    };

    /// <summary>pos에서 소음 발생. 실제 반경(날씨 반영)을 반환</summary>
    public static float Emit(Vector3 pos, float baseRadius)
    {
        float radius = baseRadius * WeatherManager.NoiseMultiplier;
        float sqr = radius * radius;
        int heard = 0;
        foreach (var z in ZombieController.All)
        {
            if (z == null || (z.transform.position - pos).sqrMagnitude > sqr) continue;
            if (z.HearNoise(pos)) heard++;
        }
        if (debugLog) Debug.Log($"[Noise] 반경 {radius:F0}m (기본 {baseRadius:F0} × 날씨 {WeatherManager.NoiseMultiplier:F2}) → 좀비 {heard}마리 반응");
        return radius;
    }
}

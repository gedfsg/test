using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소음 전파. 무기가 Emit하면 반경 안의 좀비들이 듣는다.
/// (kimozero/dev의 NoiseSystem과 하나로 합침: 무기별 기본 반경 RadiusFor, 날씨 배수 WeatherManager.NoiseMultiplier,
///  실제 반경 반환. 듣는 쪽은 좀비다움을 거친다 —— 못 들은 척 / 늦은 반응)
///
/// 사용법 (Weapon.cs의 Shoot() 안에 한 줄):
///     NoiseSystem.Emit(firePoint.position, noiseRadius);
///
/// 좀비를 매번 FindObjectsByType으로 찾으면 총 쏠 때마다 렉이 걸리므로,
/// ZombieController가 OnEnable/OnDisable에서 스스로 등록/해제하는 레지스트리를 쓴다.
/// </summary>
public static class NoiseSystem
{
    private static readonly List<ZombieController> listeners = new List<ZombieController>(64);

    // 무기별 기본 소음 반경 (m) —— 팀원이 플레이테스트로 맞춘 값
    public const float PistolRadius  = 25f;
    public const float RifleRadius   = 35f;
    public const float ShotgunRadius = 40f;
    public const float SniperRadius  = 50f;
    public const float MeleeRadius   = 8f;

    public static bool debugLog = false;

    public static float RadiusFor(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Pistol:  return PistolRadius;
            case WeaponType.Shotgun: return ShotgunRadius;
            case WeaponType.Sniper:  return SniperRadius;
            case WeaponType.Melee:   return MeleeRadius;
            default:                 return RifleRadius;   // AR / SMG / Ranged(하위 호환)
        }
    }

    public static int ListenerCount => listeners.Count;

    public static void Register(ZombieController z)
    {
        if (z != null && !listeners.Contains(z)) listeners.Add(z);
    }

    public static void Unregister(ZombieController z)
    {
        listeners.Remove(z);
    }

    /// <summary>
    /// position에서 radius만큼 퍼지는 소음 발생.
    /// 벽은 소리를 막지 않는다 (현실적으로도 총소리는 벽을 통과함).
    /// </summary>
    /// <summary>pos에서 소음 발생 (날씨 반영 —— 비 오면 반경 절반). 실제 반경을 돌려준다.</summary>
    public static float Emit(Vector3 position, float baseRadius)
    {
        if (baseRadius <= 0f) return 0f;
        float radius = baseRadius * WeatherManager.NoiseMultiplier;
        if (radius <= 0f) return 0f;

        float sqRadius = radius * radius;
        int heard = 0;

        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            ZombieController z = listeners[i];
            if (z == null) { listeners.RemoveAt(i); continue; }

            // 좀비마다 청력이 다를 수 있다 (탱커는 둔하게, 달리기형은 예민하게)
            float sensitivity = z.vision != null ? Mathf.Max(z.vision.hearingSensitivity, 0f) : 1f;
            if (sensitivity <= 0f) continue;

            float effectiveSq = sqRadius * sensitivity * sensitivity;
            if ((z.transform.position - position).sqrMagnitude > effectiveSq) continue;

            if (z.HearNoise(position)) heard++;
        }
        if (debugLog) Debug.Log($"[Noise] 반경 {radius:F0}m (기본 {baseRadius:F0} × 날씨 {WeatherManager.NoiseMultiplier:F2}) → 좀비 {heard}마리 반응");
        return radius;
    }

#if UNITY_EDITOR
    /// <summary>에디터에서 Play 정지 후 도메인 리로드를 끈 경우 대비.</summary>
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => listeners.Clear();
#endif
}

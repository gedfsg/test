using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 총구 불빛 (2026-11-16) - 모든 원거리 무기 공통.
///  - 무기 프리팹의 총구(MuzzlePoint / Muzzle) 자식에 붙는 Point Light. (Tools/Weapons/Add Muzzle Light to All)
///  - 손에 들었을 때만 켜짐: Weapon이 Activate(data)를 부름. 등에 멘 총·바닥에 떨어진 총(같은 프리팹 복제)은 꺼진 채로 둠.
///  - 상시: 은은한 빛 (WeaponData.lightColor / lightIntensity / lightRange, 기본 #FFD070 · 0.8 · 2.5m)
///  - 발사 순간: 밝기 ×3, 범위 ×1.5 → flashTime(0.1초) 동안 원래 값으로 부드럽게 돌아옴
///  - 우클릭(조준) 누르는 동안: 범위 2.5m → 8m (손전등처럼 앞을 밝힘)
/// </summary>
[RequireComponent(typeof(Light))]
public class MuzzleLight : MonoBehaviour
{
    [Header("발사 번쩍")]
    public float flashIntensityMul = 3f;
    public float flashRangeMul = 1.5f;
    [Tooltip("번쩍인 뒤 상시 밝기로 돌아오는 시간(초)")]
    public float flashTime = 0.1f;

    [Header("조준 (우클릭)")]
    public float aimLightRange = 8f;
    [Tooltip("조준 시 범위가 늘어나는/줄어드는 속도 (초당 m)")]
    public float aimRangeSpeed = 40f;

    Light lightComp;
    float baseIntensity = 0.8f, baseRange = 2.5f;
    float flash01;       // 1 = 방금 발사, 0 = 상시
    float aimRange;      // 조준 반영된 현재 기본 범위
    bool active;

    /// <summary>우클릭 조준 중인지 (UI 위에서는 무시). 조준 전용 모드가 따로 없어서 마우스 오른쪽 버튼 상태로 판단</summary>
    public static bool IsAiming =>
        Mouse.current != null && Mouse.current.rightButton.isPressed &&
        (UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());

    void Awake()
    {
        lightComp = GetComponent<Light>();
        lightComp.type = LightType.Point;
        lightComp.shadows = LightShadows.None;
        if (!active) lightComp.enabled = false; // 손에 들기 전엔 꺼둠 (등/바닥 복제품)
    }

    /// <summary>손에 든 무기로 켜기 + 무기별 색/밝기/범위 적용</summary>
    public void Activate(WeaponData data)
    {
        if (lightComp == null) lightComp = GetComponent<Light>();
        if (data != null)
        {
            lightComp.color = data.lightColor;
            baseIntensity = data.lightIntensity > 0f ? data.lightIntensity : 0.8f;
            baseRange = data.lightRange > 0f ? data.lightRange : 2.5f;
        }
        aimRange = baseRange;
        flash01 = 0f;
        active = true;
        lightComp.enabled = true;
        Apply();
    }

    public void Deactivate()
    {
        active = false;
        if (lightComp != null) lightComp.enabled = false;
    }

    /// <summary>발사 순간 번쩍 (Weapon.Shoot)</summary>
    public void Flash() => flash01 = 1f;

    void Update()
    {
        if (!active) return;
        float targetRange = IsAiming ? Mathf.Max(baseRange, aimLightRange) : baseRange;
        aimRange = Mathf.MoveTowards(aimRange, targetRange, aimRangeSpeed * Time.deltaTime);
        flash01 = Mathf.MoveTowards(flash01, 0f, Time.deltaTime / Mathf.Max(0.01f, flashTime));
        Apply();
    }

    void Apply()
    {
        // 번쩍임은 Lerp로 상시 값과 섞음 (flash01: 1 → 0, 0.1초)
        lightComp.intensity = Mathf.Lerp(baseIntensity, baseIntensity * flashIntensityMul, flash01);
        lightComp.range = Mathf.Lerp(aimRange, aimRange * flashRangeMul, flash01);
    }

    /// <summary>무기 모델 안에서 총구 불빛 찾기 (없으면 null)</summary>
    public static MuzzleLight FindIn(Transform visual) => visual != null ? visual.GetComponentInChildren<MuzzleLight>(true) : null;
}

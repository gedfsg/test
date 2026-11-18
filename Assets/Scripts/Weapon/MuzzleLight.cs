using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 총구 불빛 - 모든 원거리 무기 공통. 무기 프리팹의 총구(MuzzlePoint) 자식 "MuzzleLight"에 붙음 (Tools/Weapons/Add Muzzle Light to All).
/// 손에 들었을 때만 켜짐 (Weapon이 Activate/Deactivate). 등에 멘 총·바닥에 떨어진 총(같은 프리팹 복제)은 꺼진 채.
///
/// 이력:
///  (2026-11-16) Point Light 상시 0.8/2.5m + 발사 순간 ×3 + 우클릭 조준 8m → 발밑에 빛 원
///  (2026-11-17 1차) 섬광 Quad 0.15~0.25m 흰 원 + Spot 25°/6m/1.75 → "너무 약하고 뭉개져서 발사 느낌이 안 남"
///  (2026-11-17 2차, 지금):
///  a) 섬광 스프라이트: 총구 앞 0.1m 빌보드 Quad, 0.4~0.6m, 0.04~0.06초 동안 크기 1.0→1.3배 + 알파 1→0, 매번 랜덤 Z 회전.
///     텍스처 = 중앙 흰 → 노랑 FFE060 → 주황 FF8020 별 모양 (가산 합성)
///  b) 섬광 조명: 별도 Point Light, Range 4m, Intensity 8, #FFC060, 0.04초만 → 바닥 원이 남을 틈 없이 번쩍. 쏠 때마다 다시 켜짐
///  c) 조준등: Spot Light 상시 (총 든 동안), 총구 forward, Range 6m, Spot Angle 28°, Intensity 2.
///     바닥이 Default 레이어라(Ground 레이어 쓰는 렌더러 없음) cullingMask로는 못 빼서 좁은 각도로 막음 (총구 1.3m 높이에서 아래 가장자리 14°가 바닥 닿는 곳 ≈ 5.2m, 범위 끝 근처라 흐림)
///  d) 특수 무기: 화염방사기 #FF5500 0.8m 0.1초 / RPG #FF8800 1.0m 0.12초 / 미니건 = 일반 섬광 + 섬광 조명이 매 프레임 밝기가 튀며 깜빡 / 레이저 #00EEFF
///  (2026-11-18 3차) 피드백 "섬광 터질 때 발 밑에 원형 빛" → 섬광 조명(Point)은 총구 끝(MuzzlePoint 자식, 앞 0.1m)에서만, Range 3.5 / Intensity 9 / 0.04초,
///     cullingMask에서 지면(Default·Ground)·물·플레이어 제외 → 바닥에 원이 안 생기고, 근처 벽·나무·차(Occludable 레이어)만 번쩍.
///     (좀비도 Default 레이어라 섬광 조명은 안 받음 - 섬광 스프라이트로만). 별 모양 섬광 스프라이트·조준등(Spot 28°/6m)은 그대로
/// </summary>
[RequireComponent(typeof(Light))]
public class MuzzleLight : MonoBehaviour
{
    [Header("조준등 (Spot, 상시)")]
    public float spotRange = 6f;
    public float spotAngle = 28f;
    public float spotIntensity = 2f;

    [Header("섬광 스프라이트")]
    public float flashForward = 0.1f;
    public Vector2 flashSize = new Vector2(0.4f, 0.6f);
    public Vector2 flashTime = new Vector2(0.04f, 0.06f);
    public float flashGrow = 1.3f;

    [Header("섬광 조명 (Point, 아주 짧게)")]
    // public float flashLightRange = 4f; // (이전 2026-11-17)
    // public float flashLightIntensity = 8f; // (이전) 스펙 8~12 중 하한: 10이면 가까운 벽·바닥이 하얗게 날아가 섬광 스프라이트가 묻힘 (Play 캡처)
    // (2026-11-18) 3~4m / 8~10 - 바닥을 cullingMask로 빼서 밝혀도 바닥이 하얗게 안 날아감.
    // 이름을 바꾼 이유: 무기 프리팹에 예전 값(flashLightRange 4 / flashLightIntensity 10)이 저장돼 있어 코드 기본값이 안 먹음
    public float flashPointRange = 3.5f;
    public float flashPointIntensity = 9f;
    public float flashLightTime = 0.04f;
    static readonly Color FlashLightColor = new Color32(0xFF, 0xC0, 0x60, 0xFF);

    Light lightComp;       // 조준등 (이 오브젝트의 Light)
    Light flashLight;      // 섬광 조명 (자식, 런타임 생성)
    bool active;
    Color flashTint = Color.white;  // 텍스처 색에 곱함 (일반 총 = 흰 → 텍스처 그대로 흰·노랑·주황)
    Color flashLightColor = FlashLightColor;
    SpecialWeaponKind kind;

    Transform flashQuad;
    MeshRenderer flashRenderer;
    MaterialPropertyBlock mpb;
    float flashStart = -1f, flashDur, flashBaseSize, flashRoll;
    float lightUntil = -1f;
    static Material flashMat;
    static readonly int TintColor = Shader.PropertyToID("_TintColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>우클릭 조준 중인지 (UI 위에서는 무시). (조준등에는 안 씀 - 다른 곳 호환용)</summary>
    public static bool IsAiming =>
        Mouse.current != null && Mouse.current.rightButton.isPressed &&
        (UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());

    void Awake()
    {
        lightComp = GetComponent<Light>();
        lightComp.type = LightType.Spot;
        lightComp.spotAngle = spotAngle;
        lightComp.innerSpotAngle = spotAngle * 0.5f;
        lightComp.range = spotRange;
        lightComp.shadows = LightShadows.None;
        transform.localRotation = Quaternion.identity; // 총구 forward 그대로
        if (!active) lightComp.enabled = false; // 손에 들기 전엔 꺼둠 (등/바닥 복제품)
    }

    /// <summary>손에 든 무기로 켜기 + 무기별 색 적용</summary>
    public void Activate(WeaponData data)
    {
        if (lightComp == null) Awake();
        Color c = data != null ? data.lightColor : (Color)new Color32(0xFF, 0xD0, 0x70, 0xFF);
        kind = data != null ? data.specialKind : SpecialWeaponKind.None;
        lightComp.color = c;
        lightComp.intensity = data != null && data.lightIntensity > spotIntensity ? data.lightIntensity : spotIntensity;
        lightComp.range = spotRange;
        lightComp.spotAngle = spotAngle;
        // 섬광 색: 일반 총·미니건 = 텍스처 그대로 (흰·노랑·주황), 화염·RPG·레이저 = 무기 색으로 물들임
        bool tinted = kind == SpecialWeaponKind.Flamethrower || kind == SpecialWeaponKind.RPG || kind == SpecialWeaponKind.Laser;
        flashTint = tinted ? Color.Lerp(c, Color.white, 0.25f) : Color.white;
        flashLightColor = tinted ? c : FlashLightColor;
        active = true;
        lightComp.enabled = true;
    }

    public void Deactivate()
    {
        active = false;
        if (lightComp != null) lightComp.enabled = false;
        if (flashRenderer != null) flashRenderer.enabled = false;
        if (flashLight != null) flashLight.enabled = false;
    }

    /// <summary>발사 순간 섬광 (Weapon.Shoot) - 연사하면 매 발 다시 터짐</summary>
    public void Flash()
    {
        EnsureFlashObjects();
        // 무기별 크기·시간
        Vector2 size = flashSize, time = flashTime;
        if (kind == SpecialWeaponKind.Flamethrower) { size = new Vector2(0.8f, 0.8f); time = new Vector2(0.1f, 0.1f); }
        else if (kind == SpecialWeaponKind.RPG) { size = new Vector2(1.0f, 1.0f); time = new Vector2(0.12f, 0.12f); }

        if (flashQuad != null)
        {
            flashDur = Random.Range(time.x, time.y);
            flashStart = Time.time;
            flashBaseSize = Random.Range(size.x, size.y);
            flashQuad.localPosition = Vector3.forward * flashForward;
            flashRoll = Random.Range(0f, 360f); // 매번 다른 모양
            flashRenderer.enabled = true;
            UpdateFlash();
        }
        if (flashLight != null)
        {
            flashLight.color = flashLightColor;
            flashLight.range = flashPointRange;
            flashLight.intensity = flashPointIntensity;
            // 화염·RPG는 섬광이 긴 만큼 조명도 같이
            lightUntil = Time.time + Mathf.Max(flashLightTime, kind == SpecialWeaponKind.Flamethrower || kind == SpecialWeaponKind.RPG ? flashDur : 0f);
            flashLight.enabled = true;
        }
    }

    void LateUpdate()
    {
        if (flashRenderer != null && flashRenderer.enabled) UpdateFlash();
        if (flashLight != null && flashLight.enabled)
        {
            if (!active || Time.time >= lightUntil) flashLight.enabled = false;
            else if (kind == SpecialWeaponKind.Minigun) flashLight.intensity = flashPointIntensity * Random.Range(0.4f, 1.2f); // 매 프레임 깜빡
        }
    }

    void UpdateFlash()
    {
        float t = (Time.time - flashStart) / Mathf.Max(0.01f, flashDur);
        if (t >= 1f || !active) { flashRenderer.enabled = false; return; }
        // 빌보드: 카메라를 정면으로 + 발사마다 다른 회전, 1.0 → 1.3배로 커지며 사라짐
        var cam = Camera.main;
        if (cam != null) flashQuad.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up) * Quaternion.Euler(0f, 0f, flashRoll);
        flashQuad.localScale = Vector3.one * flashBaseSize * Mathf.Lerp(1f, flashGrow, t);
        float a = 1f - t;
        // Legacy Particles/Additive = 텍스처 × 틴트 × 2 → 틴트 0.5가 원래 밝기. 알파도 같이 줄여 사라지게
        Color c = flashTint * 0.5f * a; c.a = a;
        mpb.SetColor(TintColor, c);
        mpb.SetColor(ColorId, c);
        flashRenderer.SetPropertyBlock(mpb);
    }

    void EnsureFlashObjects()
    {
        if (flashQuad == null)
        {
            if (flashMat == null) flashMat = Resources.Load<Material>("FX/MuzzleFlash"); // Tools/Weapons/Add Muzzle Light to All이 만듦
            if (flashMat != null)
            {
                // CreatePrimitive는 MeshCollider가 붙어 나와서 (Destroy가 프레임 끝이라) 한 프레임 동안 플레이어 Rigidbody 밑 오목 콜라이더 에러 → 메시만 씀
                var go = new GameObject("MuzzleFlashQuad");
                go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                flashRenderer = go.AddComponent<MeshRenderer>();
                go.transform.SetParent(transform, false);
                flashQuad = go.transform;
                flashRenderer.sharedMaterial = flashMat;
                flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flashRenderer.receiveShadows = false;
                flashRenderer.enabled = false;
                mpb = new MaterialPropertyBlock();
            }
        }
        if (flashLight == null)
        {
            var lgo = new GameObject("MuzzleFlashLight");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = Vector3.forward * flashForward;
            flashLight = lgo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.shadows = LightShadows.None;
            flashLight.renderMode = LightRenderMode.ForcePixel; // 짧아도 확실히 보이게
            flashLight.cullingMask = FlashCullingMask(); // (2026-11-18) 지면·플레이어 제외 → 발밑 원형 빛 없음
            flashLight.enabled = false;
        }
    }

    /// <summary>섬광 조명이 비추는 레이어: 지면(Default·Ground)·물·플레이어·UI 빼고 전부 (건물·나무·차 = Occludable)</summary>
    static int FlashCullingMask()
    {
        int mask = ~0;
        foreach (var n in new[] { "Default", "Ground", "Water", "Player", "UI", "TransparentFX", "Ignore Raycast" })
        {
            int l = LayerMask.NameToLayer(n);
            if (l >= 0) mask &= ~(1 << l);
        }
        return mask;
    }

    /// <summary>무기 모델 안에서 총구 불빛 찾기 (없으면 null)</summary>
    public static MuzzleLight FindIn(Transform visual) => visual != null ? visual.GetComponentInChildren<MuzzleLight>(true) : null;
}

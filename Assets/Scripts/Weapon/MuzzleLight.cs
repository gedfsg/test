using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 총구 불빛 - 모든 원거리 무기 공통. 무기 프리팹의 총구(MuzzlePoint) 자식 "MuzzleLight"에 붙음 (Tools/Weapons/Add Muzzle Light to All).
/// 손에 들었을 때만 켜짐 (Weapon이 Activate/Deactivate). 등에 멘 총·바닥에 떨어진 총(같은 프리팹 복제)은 꺼진 채.
///
/// 2026-11-17 재작업 (피드백: Point Light가 바닥을 동그랗게 비춤):
///  (이전) Point Light 상시 0.8/2.5m + 발사 순간 밝기 ×3·범위 ×1.5 + 우클릭 조준 시 범위 8m → 발밑에 빛 원이 생김
///  a) 발사 섬광(shot flash): 총구 앞 0.07m에 빌보드 Quad가 0.05~0.08초 터짐 (지름 0.15~0.25m, 가산 합성, #FFE090).
///     조명이 아니라서 바닥에 원이 안 생김. 화염방사기·RPG·레이저는 무기 색(WeaponData.lightColor)
///  b) 조준등(aim light): Spot Light 하나. 총구 방향(이 오브젝트의 forward = 총구 +Z, 로컬 회전 0)으로만 비춤.
///     Range 6m, Spot Angle 25°, Intensity 1.75. 바닥이 Default 레이어라(Ground 레이어 쓰는 렌더러 없음) cullingMask로는 못 빼서
///     각도로 막음: 총구 높이 약 1.3m에서 원뿔 아래 가장자리(12.5°)가 바닥에 닿는 곳은 약 5.9m = 범위 끝(밝기 0) → 바닥 원 없음
///  c) 바닥을 비추던 Point Light 코드 제거 (프리팹의 Light는 Awake에서 Spot으로 바꿔 씀)
/// </summary>
[RequireComponent(typeof(Light))]
public class MuzzleLight : MonoBehaviour
{
    [Header("조준등 (Spot)")]
    public float spotRange = 6f;
    public float spotAngle = 25f;
    public float spotIntensity = 1.75f;

    [Header("발사 섬광 (빌보드 Quad)")]
    public float flashForward = 0.07f;
    public Vector2 flashSize = new Vector2(0.15f, 0.25f);
    public Vector2 flashTime = new Vector2(0.05f, 0.08f);
    static readonly Color DefaultFlashColor = new Color32(0xFF, 0xE0, 0x90, 0xFF);

    Light lightComp;
    bool active;
    Color flashColor = DefaultFlashColor;

    Transform flashQuad;
    MeshRenderer flashRenderer;
    MaterialPropertyBlock mpb;
    float flashStart = -1f, flashDur;
    static Material flashMat;
    static readonly int TintColor = Shader.PropertyToID("_TintColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>우클릭 조준 중인지 (UI 위에서는 무시). (2026-11-17부터 조준등에는 안 씀 - 다른 곳 호환용)</summary>
    public static bool IsAiming =>
        Mouse.current != null && Mouse.current.rightButton.isPressed &&
        (UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());

    void Awake()
    {
        lightComp = GetComponent<Light>();
        // (이전) lightComp.type = LightType.Point;
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
        lightComp.color = c;
        // 일반 총 섬광 = #FFE090, 특수 무기(화염 #FF5500 / RPG #FF8800 / 레이저 #00EEFF)는 그 색
        flashColor = data != null && data.specialKind != SpecialWeaponKind.None && data.specialKind != SpecialWeaponKind.Minigun ? c : DefaultFlashColor;
        // 조준등 밝기: 무기에 더 밝게 지정된 경우(화염방사기 2.0)만 그 값
        lightComp.intensity = data != null && data.lightIntensity > spotIntensity ? data.lightIntensity : spotIntensity;
        lightComp.range = spotRange;
        lightComp.spotAngle = spotAngle;
        active = true;
        lightComp.enabled = true;
    }

    public void Deactivate()
    {
        active = false;
        if (lightComp != null) lightComp.enabled = false;
        if (flashRenderer != null) flashRenderer.enabled = false;
    }

    /// <summary>발사 순간 섬광 (Weapon.Shoot)</summary>
    public void Flash()
    {
        EnsureFlashQuad();
        if (flashQuad == null) return;
        flashDur = Random.Range(flashTime.x, flashTime.y);
        flashStart = Time.time;
        float size = Random.Range(flashSize.x, flashSize.y);
        flashQuad.localScale = Vector3.one * size;
        flashQuad.localPosition = Vector3.forward * flashForward;
        flashRoll = Random.Range(0f, 360f);
        flashRenderer.enabled = true;
        UpdateFlash();
    }

    float flashRoll;

    void LateUpdate()
    {
        if (flashRenderer != null && flashRenderer.enabled) UpdateFlash();
    }

    void UpdateFlash()
    {
        float t = (Time.time - flashStart) / Mathf.Max(0.01f, flashDur);
        if (t >= 1f || !active) { flashRenderer.enabled = false; return; }
        // 빌보드: 카메라를 정면으로 + 매번 다른 회전
        var cam = Camera.main;
        if (cam != null) flashQuad.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up) * Quaternion.Euler(0f, 0f, flashRoll);
        Color c = flashColor * (1f - t) * 1.6f; // 빠르게 사그라짐 (가산 합성이라 색 자체를 어둡게)
        c.a = 1f - t;
        mpb.SetColor(TintColor, c);
        mpb.SetColor(ColorId, c);
        flashRenderer.SetPropertyBlock(mpb);
    }

    void EnsureFlashQuad()
    {
        if (flashQuad != null) return;
        if (flashMat == null) flashMat = Resources.Load<Material>("FX/MuzzleFlash"); // Tools/Weapons/Add Muzzle Light to All이 만듦
        if (flashMat == null) return;
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

    /// <summary>무기 모델 안에서 총구 불빛 찾기 (없으면 null)</summary>
    public static MuzzleLight FindIn(Transform visual) => visual != null ? visual.GetComponentInChildren<MuzzleLight>(true) : null;
}

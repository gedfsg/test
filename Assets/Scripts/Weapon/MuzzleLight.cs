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
///  (2026-11-20 4차) 피드백 "조준등이 어색하게 빛남" → Intensity 2 → 1.2, Spot Angle 28° → 22° (아래 가장자리 11°가 바닥에 닿는 곳 ≈ 6.7m > Range 6m → 바닥 원 없음),
///     낮엔 0.3배(거의 안 보임) ~ 밤·실내 1.0배 (DayNightCycle 단계·진행률로 자동, 0.5초에 걸쳐 바뀜). 섬광은 그대로.
///     프리팹에 예전 값(spotIntensity 2 / spotAngle 28)이 저장돼 있어서 새 이름(aimLight*)으로 씀
///  (2026-11-22 5차) 실사 레퍼런스(Assets/UI/References/muzzle_flash_*) 톤으로 섬광 재작업:
///   a) 별 모양 빌보드 Quad → "메인 플레어 Quad + 스파크 파티클" 2단
///      - 메인 플레어: Custom/MuzzleFlare (흰 코어 → 노랑 FFE060 → 주황 FF8030 전방 분출, 가산, HDR 1.5배, ZWrite Off / ZTest On)
///        축 빌보드: 긴 축 = 총구 방향, 면 = 카메라 쪽 → 총구 앞으로 길쭉하게 뻗음
///        크기(가로 × 길이, 월드 m): 권총 0.25×0.4 / AR·SMG·스나이퍼 0.35×0.55 / 샷건 0.45×0.7 / 화염방사기 0.6×1.0 / RPG 0.7×1.1
///      - 스파크: 파티클 버스트 10~15개(샷건 24개·넓게), 수명 0.1초, 방사형으로 튐
///      - 둘 다 총구 끝(이 오브젝트 = MuzzlePoint 자식) 위치·방향 기준. 월드 크기 고정 (예전엔 무기 모델 스케일에 따라 크기가 달라졌음)
///   b) 애니메이션: 0.04~0.06초 동안 크기 1.0 → 1.2, 알파 1 → 0 (화염방사기 0.1초 / RPG 0.12초 유지)
///   c) 화염방사기 #FF5500 / RPG #FF8800 / 레이저 색은 그대로 (무기 색으로 물들임)
///   d) 섬광 조명: 3.5m / 9 / #FFC060 / 0.04초 그대로 (스펙 3~4m, 8~12), 바닥 원형 빛 없음(cullingMask) 유지
///  (2026-11-22 6차) 피드백 "총 쏘면 캐릭터 바닥에 원형 빛" → cullingMask는 Default 바닥만 빠져서 실내 등 다른 레이어 바닥엔 원이 생김
///     → 섬광 Point 조명을 끔 (useFlashPointLight = false). 섬광 플레어·스파크·조준등은 그대로
[RequireComponent(typeof(Light))]
public class MuzzleLight : MonoBehaviour
{
    [Header("조준등 (Spot, 상시)")]
    public float spotRange = 6f;
    public float spotAngle = 28f;      // (2026-11-20부터 안 씀 → aimLightAngle)
    public float spotIntensity = 2f;   // (2026-11-20부터 안 씀 → aimLightIntensity)
    [Header("조준등 (2026-11-20)")]
    public float aimLightIntensity = 1.2f;
    public float aimLightAngle = 22f;
    [Tooltip("밝은 낮의 조준등 배율 (밤·실내 = 1)")]
    [Range(0f, 1f)] public float dayLightWeight = 0.3f;
    float baseIntensity, lightWeight = -1f;

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
    [Tooltip("(2026-11-22) 발사 순간 Point 조명 사용 여부. 실내 바닥 등 Default가 아닌 레이어 바닥에 발밑 원형 빛이 생겨서 끔 (섬광 그림·스파크는 그대로)")]
    public bool useFlashPointLight = false;
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
    // (2026-11-22) 메인 플레어 + 스파크
    WeaponType weaponType = WeaponType.AR;
    Vector2 flareSize;            // (가로, 길이) 월드 m
    ParticleSystem sparks;
    static Material flareMat, sparkMat;
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    const float FlareGrow = 1.2f;
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
        // lightComp.spotAngle = spotAngle; // (이전 28°)
        // lightComp.innerSpotAngle = spotAngle * 0.5f;
        lightComp.spotAngle = aimLightAngle;            // (2026-11-20) 22°
        lightComp.innerSpotAngle = aimLightAngle * 0.5f;
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
        weaponType = data != null ? data.type : WeaponType.AR; // (2026-11-22) 섬광 크기용
        lightComp.color = c;
        // lightComp.intensity = data != null && data.lightIntensity > spotIntensity ? data.lightIntensity : spotIntensity; // (이전) 2, 화염방사기 2.0
        // (2026-11-20) 모든 총 1.2 (무기 색은 그대로), 실제 밝기 = 1.2 × 낮/밤 배율 (LateUpdate)
        baseIntensity = aimLightIntensity;
        if (lightWeight < 0f) lightWeight = TargetLightWeight();
        lightComp.intensity = baseIntensity * lightWeight;
        lightComp.range = spotRange;
        // lightComp.spotAngle = spotAngle; // (이전)
        lightComp.spotAngle = aimLightAngle;
        lightComp.innerSpotAngle = aimLightAngle * 0.5f;
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

    // (2026-11-22) 플레어·스파크는 무기 모델 스케일 영향을 안 받게 씬 루트에 둠 → 무기가 사라지면 같이 정리
    void OnDestroy()
    {
        if (flashQuad != null) Destroy(flashQuad.gameObject);
        if (sparks != null) Destroy(sparks.gameObject);
    }

    /// <summary>(2026-11-22) 무기 종류별 메인 플레어 크기 (가로, 길이) m · 스파크 수 · 퍼짐 각도</summary>
    void FlareSpec(out Vector2 size, out int sparkCount, out float sparkAngle)
    {
        sparkCount = 12; sparkAngle = 35f;
        switch (weaponType)
        {
            case WeaponType.Pistol: size = new Vector2(0.25f, 0.4f); sparkCount = 10; break;
            case WeaponType.Shotgun: size = new Vector2(0.45f, 0.7f); sparkCount = 24; sparkAngle = 70f; break;
            default: size = new Vector2(0.35f, 0.55f); sparkCount = 13; break; // AR·SMG·스나이퍼
        }
        if (kind == SpecialWeaponKind.Flamethrower) { size = new Vector2(0.6f, 1.0f); sparkCount = 6; }
        else if (kind == SpecialWeaponKind.RPG) { size = new Vector2(0.7f, 1.1f); sparkCount = 20; sparkAngle = 60f; }
    }

    /// <summary>발사 순간 섬광 (Weapon.Shoot) - 연사하면 매 발 다시 터짐</summary>
    public void Flash()
    {
        EnsureFlashObjects();
        // 무기별 크기·시간
        Vector2 size = flashSize, time = flashTime;
        if (kind == SpecialWeaponKind.Flamethrower) { size = new Vector2(0.8f, 0.8f); time = new Vector2(0.1f, 0.1f); }
        else if (kind == SpecialWeaponKind.RPG) { size = new Vector2(1.0f, 1.0f); time = new Vector2(0.12f, 0.12f); }

        // (2026-11-22) 무기별 플레어 크기 · 지속 0.04~0.06초 (화염·RPG는 위 time 그대로)
        FlareSpec(out flareSize, out int sparkCount, out float sparkAngle);
        if (kind != SpecialWeaponKind.Flamethrower && kind != SpecialWeaponKind.RPG) time = new Vector2(0.04f, 0.06f);
        if (flashQuad != null)
        {
            flashDur = Random.Range(time.x, time.y);
            flashStart = Time.time;
            // flashBaseSize = Random.Range(size.x, size.y); // (이전) 별 모양 정사각 크기
            // flashQuad.localPosition = Vector3.forward * flashForward;
            flashBaseSize = Random.Range(0.9f, 1.1f);      // (2026-11-22) 발사마다 크기 ±10%
            flashRoll = Random.Range(0f, 360f); // 매번 다른 모양 (플레어 셰이더 _Seed)
            flashRenderer.enabled = true;
            UpdateFlash();
        }
        if (sparks != null) EmitSparks(sparkCount, sparkAngle);
        // if (flashLight != null) // (이전) 쏠 때마다 Point 조명 → 발밑 원형 빛
        if (flashLight != null && useFlashPointLight)
        {
            flashLight.color = flashLightColor;
            flashLight.range = flashPointRange;
            flashLight.intensity = flashPointIntensity;
            // 화염·RPG는 섬광이 긴 만큼 조명도 같이
            lightUntil = Time.time + Mathf.Max(flashLightTime, kind == SpecialWeaponKind.Flamethrower || kind == SpecialWeaponKind.RPG ? flashDur : 0f);
            flashLight.enabled = true;
        }
    }

    /// <summary>
    /// (2026-11-20) 조준등 낮/밤 배율: 낮 = dayLightWeight(0.3), 노을 = 0.3→1, 밤 = 1, 새벽 = 1→0.3, 실내 = 1
    /// </summary>
    float TargetLightWeight()
    {
        if (BuildingInterior.IsAnyoneIndoors) return 1f;
        var dn = DayNightCycle.Instance;
        if (dn == null) return 1f;
        float p = Mathf.Clamp01(dn.GetPhaseProgress());
        float night = dn.CurrentPhase switch
        {
            DayNightCycle.Phase.Day => 0f,
            DayNightCycle.Phase.Dusk => p,
            DayNightCycle.Phase.Night => 1f,
            _ => 1f - p, // Dawn
        };
        return Mathf.Lerp(dayLightWeight, 1f, night);
    }

    void LateUpdate()
    {
        // (2026-11-20) 조준등 밝기 = 1.2 × 낮/밤 배율 (실내 출입 시 0.5초에 걸쳐 바뀜)
        if (active && lightComp != null && lightComp.enabled)
        {
            float w = TargetLightWeight();
            lightWeight = lightWeight < 0f ? w : Mathf.MoveTowards(lightWeight, w, Time.deltaTime * 2f);
            lightComp.intensity = baseIntensity * lightWeight;
        }
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
        // (이전 ~2026-11-21) 빌보드: 카메라를 정면으로 + 발사마다 다른 회전, 1.0 → 1.3배로 커지며 사라짐 (별 모양 정사각)
        // var cam = Camera.main;
        // if (cam != null) flashQuad.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up) * Quaternion.Euler(0f, 0f, flashRoll);
        // flashQuad.localScale = Vector3.one * flashBaseSize * Mathf.Lerp(1f, flashGrow, t);
        // float a = 1f - t;
        // Color c = flashTint * 0.5f * a; c.a = a;
        // mpb.SetColor(TintColor, c);
        // mpb.SetColor(ColorId, c);
        // flashRenderer.SetPropertyBlock(mpb);

        // (2026-11-22) 축 빌보드: 긴 축(로컬 Y) = 총구 방향, 면(로컬 Z) = 카메라 쪽. 총구 끝에서 앞으로 뻗음
        Vector3 fwd = transform.forward, origin = transform.position - fwd * 0.02f; // 총구 끝과 이어지게 살짝 뒤에서 시작
        var cam = Camera.main;
        Vector3 toCam = cam != null ? cam.transform.position - origin : -fwd;
        Vector3 n = Vector3.ProjectOnPlane(toCam, fwd);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.ProjectOnPlane(Vector3.up, fwd);
        float grow = Mathf.Lerp(1f, FlareGrow, t) * flashBaseSize;
        float w = flareSize.x * grow, len = flareSize.y * grow;
        flashQuad.SetPositionAndRotation(origin + fwd * (len * 0.5f), Quaternion.LookRotation(n.normalized, fwd));
        flashQuad.localScale = new Vector3(w, len, 1f);
        float a = 1f - t;
        mpb.SetFloat(AlphaId, a);
        mpb.SetColor(TintId, flashTint);
        mpb.SetFloat(SeedId, flashRoll / 360f);
        flashRenderer.SetPropertyBlock(mpb);
    }

    /// <summary>(2026-11-22) 스파크 버스트: 총구 끝에서 전방 원뿔 + 사방으로 조금</summary>
    void EmitSparks(int count, float angle)
    {
        var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        Vector3 fwd = transform.forward, pos = transform.position + fwd * 0.05f;
        for (int i = 0; i < count; i++)
        {
            // 70%는 전방 원뿔, 30%는 사방 (레퍼런스의 사방 파편)
            Vector3 dir = Random.value < 0.7f
                ? Quaternion.AngleAxis(Random.Range(0f, angle), Random.onUnitSphere) * fwd
                : Random.onUnitSphere;
            if (Vector3.Dot(dir, fwd) < -0.2f) dir = Vector3.Reflect(dir, fwd); // 총 뒤쪽(플레이어 쪽)으로는 안 튀게
            ep.position = pos;
            ep.velocity = dir.normalized * Random.Range(3f, 7f);
            ep.startLifetime = Random.Range(0.07f, 0.12f);
            ep.startSize = Random.Range(0.02f, 0.045f);
            ep.startColor = Color.Lerp(new Color(1f, 0.95f, 0.75f), new Color(1f, 0.6f, 0.2f), Random.value) * flashTint;
            sparks.Emit(ep, 1);
        }
    }

    void EnsureFlashObjects()
    {
        if (flashQuad == null)
        {
            if (flashMat == null) flashMat = Resources.Load<Material>("FX/MuzzleFlash"); // Tools/Weapons/Add Muzzle Light to All이 만듦
            // (2026-11-22) 메인 플레어 재질 (Custom/MuzzleFlare) - 없으면 예전 별 모양 재질
            if (flareMat == null) { var sh = Shader.Find("Custom/MuzzleFlare"); if (sh != null) flareMat = new Material(sh) { name = "MuzzleFlare(Runtime)" }; }
            if (flashMat != null || flareMat != null)
            {
                // CreatePrimitive는 MeshCollider가 붙어 나와서 (Destroy가 프레임 끝이라) 한 프레임 동안 플레이어 Rigidbody 밑 오목 콜라이더 에러 → 메시만 씀
                var go = new GameObject("MuzzleFlashQuad");
                go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                flashRenderer = go.AddComponent<MeshRenderer>();
                // go.transform.SetParent(transform, false); // (이전) 무기 모델 스케일 영향 받음
                flashQuad = go.transform;                    // (2026-11-22) 씬 루트 (월드 크기 고정, OnDestroy에서 정리)
                // flashRenderer.sharedMaterial = flashMat; // (이전)
                flashRenderer.sharedMaterial = flareMat != null ? flareMat : flashMat;
                flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flashRenderer.receiveShadows = false;
                flashRenderer.enabled = false;
                mpb = new MaterialPropertyBlock();
            }
        }
        if (sparks == null) sparks = CreateSparks(); // (2026-11-22)
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

    /// <summary>(2026-11-22) 스파크 파티클 시스템 (월드 공간, 직접 Emit만 - 자동 방출 없음)</summary>
    static ParticleSystem CreateSparks()
    {
        var go = new GameObject("MuzzleSparks");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.gravityModifier = 0.3f;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; // 날아가는 방향으로 늘어난 선 모양 파편
        r.velocityScale = 0.025f;
        r.lengthScale = 1.5f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (sparkMat == null)
        {
            var shd = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shd == null && flashMat != null) shd = flashMat.shader;
            if (shd != null) sparkMat = new Material(shd) { name = "MuzzleSparks(Runtime)", mainTexture = DotTexture() };
            if (sparkMat != null && sparkMat.HasProperty("_TintColor")) sparkMat.SetColor("_TintColor", new Color(0.75f, 0.75f, 0.75f, 1f)); // Legacy Additive ×2 → 약 1.5배 발광
        }
        r.sharedMaterial = sparkMat;
        ps.Play();
        return ps;
    }

    static Texture2D DotTexture()
    {
        var t = new Texture2D(16, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8f, 8f)) / 8f;
                float a = Mathf.Clamp01(1f - d); a *= a;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
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

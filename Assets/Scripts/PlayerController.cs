using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    private Rigidbody rb;
    private Camera mainCamera;
    private Vector3 moveInput;

    private PlayerInputActions inputActions;
    private Locomotion locomotion;

    private bool isFireButtonPressed = false;
    private bool firePressed = false;

    [Header("Weapons")]
    public GameObject rangedWeaponObject;
    public GameObject meleeWeaponObject;

    [Tooltip("시작 시 핫바 1번에 자동 지급할 근접 무기 (도끼). 지정하면 시작 총은 지급 안 함 (2번 비움)")]
    public WeaponData startingMeleeWeapon;

    private Weapon rangedWeapon;
    private MeleeWeapon meleeWeapon;

    [Header("구르기 (Space) - 모든 무기")]
    public float rollDistance = 6f;          // 돌진 거리 (비: × WeatherManager.RollDistanceMultiplier 1.3)
    public float rollDuration = 0.5f;        // 돌진 시간
    public float rollCooldown = 1f;          // 구르기 끝난 뒤 다음 구르기까지
    public float rollStaminaCost = 20f;
    public float rollInvincibleTime = 0.3f;  // 시작부터 이 시간 동안 피해 무시
    [Tooltip("권총을 구르는 동안 옮겨 둘 허리 위치 (비워두면 숨김)")]
    public Transform pistolHolsterPoint;
    [Tooltip("도끼를 구르는 동안 옮겨 둘 등 위치 (비워두면 숨김)")]
    public Transform axeBackPoint;

    public bool IsRolling => isRolling;
    private bool isRolling;
    private float rollEndTime = -99f;
    private Vector3 rollVelocity;
    private Health health;
    // 구르는 동안 옮긴/숨긴 무기 모델 (끝나면 손으로 복귀)
    private Transform rollStowed, rollStowParent;
    private Vector3 rollStowPos;
    private Quaternion rollStowRot;
    private bool rollStowHidden;
    static readonly int RollHash = Animator.StringToHash("Roll");
    static readonly int RollStateHash = Animator.StringToHash("Base Layer.Roll");

    public float rotationSpeed = 15f;

    private enum WeaponMode { Ranged, Melee }
    private WeaponMode currentMode = WeaponMode.Ranged;

    private List<PickupItem> nearbyItems = new List<PickupItem>();
    private List<LootCrate> nearbyCrates = new List<LootCrate>();

    [Header("Animation Rigging")]
    public Transform aimTarget;

    [Tooltip("조준 지점 계산 시 기준 높이 = 캐릭터 루트(transform.position.y) + 이 값. 총구 높이 정도로 맞출 것(예: 1.0~1.3). firePoint.position.y처럼 애니메이션 때문에 흔들리는 값 대신 흔들리지 않는 고정 오프셋을 쓴다.")]
    public float aimHeightOffset = 1.2f;

    private WeaponHandAttacher weaponHandAttacher;

    [Header("도끼: 상체를 마우스 쪽으로")]
    [Tooltip("도끼 들었을 때 옆/대각선 이동 애니는 상체를 이동 방향으로 틀어서(최대 40°) 마우스를 안 보는 것처럼 보임 → 척추를 돌려 어깨가 조준 방향(루트 정면)을 보게. 0이면 끔")]
    [Range(0f, 1f)] public float meleeTorsoAimWeight = 1f;
    [Tooltip("척추로 돌려주는 최대 각도")]
    public float meleeTorsoMaxTwist = 60f;
    [Tooltip("보정 각도 부드럽게 따라가는 시간(초). 달릴 때 어깨가 좌우로 흔들리는 자연스러운 움직임은 남기고 평균 틀어짐만 지움")]
    public float meleeTorsoSmoothTime = 0.25f;
    [Tooltip("휘두르는 동안 상체를 더 돌리는 각도 (+ 오른쪽). 내려찍기가 가장 멀리 뻗는 순간(클릭 후 약 0.35초) 도끼 머리가 마우스 방향을 지나도록 Play 실측으로 맞춤. (이전 가로 베기용 meleeSwingYawOffset = -45는 씬에 저장된 값이 남아 덮어써서 이름을 바꿈)")]
    public float meleeChopYawOffset = -10f;
    [Tooltip("공격 애니(상체 레이어) 재생 시간. MixamoAxeSetup의 MeleeAttack 재생 시간과 같게")]
    public float meleeSwingDuration = 0.59f;
    [Tooltip("공격 등 상체 동작 중 골반이 돌아가면(휴머노이드 마스크의 Body에 골반 포함) 다리까지 돌아감 → 골반만 루트 정면으로 되돌리고 상체는 애니 그대로 유지")]
    public bool meleeStabilizeHips = true;
    private Vector3 hipsNeutralLocal;  // 상체 동작 없을 때 골반의 루트 기준 수평 위치
    private float meleeTorsoTwist;
    private float meleeSwingUntil;   // 휘두르기 재생 중 (상체 추가 회전 적용 구간)

    // ── Unity 생명주기 ────────────────────────────

    void Awake()
    {
        inputActions = new PlayerInputActions();
        locomotion = GetComponent<Locomotion>();
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;

        if (rangedWeaponObject != null) rangedWeapon = rangedWeaponObject.GetComponent<Weapon>();
        if (meleeWeaponObject != null) meleeWeapon = meleeWeaponObject.GetComponent<MeleeWeapon>();

        if (rangedWeapon != null) rangedWeapon.shooterTag = "Player";
        if (meleeWeapon != null) meleeWeapon.shooterTag = "Player";

        weaponHandAttacher = FindAnyObjectByType<WeaponHandAttacher>();

        // 가방(인벤토리 칸 수 + 등 모델) + 등 무기 비주얼 - 씬에 없으면 자동으로 붙임
        if (GetComponent<BackpackManager>() == null) gameObject.AddComponent<BackpackManager>();

        // 시작 지급: 도끼만 1번 (2번은 비움). 도끼가 지정 안 돼 있으면 예전처럼 시작 총 지급
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar != null && startingMeleeWeapon != null)
            hotbar.AddWeapon(startingMeleeWeapon);
        else if (rangedWeapon != null && rangedWeapon.weaponData != null)
            hotbar?.AddWeapon(rangedWeapon.weaponData, rangedWeapon.GetCurrentAmmo());

        EquipWeapon(WeaponMode.Ranged);
        if (hotbar != null && startingMeleeWeapon != null) hotbar.SelectSlot(0); // 도끼 들고 시작

        // 애니메이션 연결: 피격 / 사망 / 재장전
        var health = GetComponent<Health>();
        if (health != null)
        {
            if (health.onHurt == null) health.onHurt = new UnityEngine.Events.UnityEvent();
            if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
            health.onHurt.AddListener(OnPlayerHurt);
            health.onDeath.AddListener(OnPlayerDied);
        }
        if (rangedWeapon != null)
        {
            if (rangedWeapon.onReloadStart == null) rangedWeapon.onReloadStart = new UnityEngine.Events.UnityEvent<float>();
            if (rangedWeapon.onReloadComplete == null) rangedWeapon.onReloadComplete = new UnityEngine.Events.UnityEvent();
            rangedWeapon.onReloadStart.AddListener(OnReloadStarted);
            rangedWeapon.onReloadComplete.AddListener(() => reloadUntil = 0f);
        }
    }

    void OnEnable()
    {
        inputActions.Enable();

        inputActions.Player.Fire.started += OnFireStarted;
        inputActions.Player.Fire.canceled += OnFireCanceled;
        inputActions.Player.Reload.performed += OnReloadPerformed;

        inputActions.Player.Sprint.started += _ => locomotion.SetSprinting(true);
        inputActions.Player.Sprint.canceled += _ => locomotion.SetSprinting(false);
        // inputActions.Player.Roll.performed += _ => locomotion.TryRoll(moveInput); // (이전) 3m 짧은 구르기, 애니메이션/무적 없음
        inputActions.Player.Roll.performed += _ => TryRoll();

        // 1키(EquipRanged)/V키(EquipMelee): 핫바가 있으면 핫바가 무기 전환을 담당 → 무시
        // (안 그러면 1번 슬롯 도끼를 고를 때 1키가 총 모드로 덮어써버림)
        inputActions.Player.EquipRanged.performed += _ => { if (WeaponHotbarUI.Instance == null && !isRolling) EquipWeapon(WeaponMode.Ranged); };
        inputActions.Player.EquipMelee.performed += _ => { if (WeaponHotbarUI.Instance == null && !isRolling) EquipWeapon(WeaponMode.Melee); };

        inputActions.Player.Restart.performed += OnRestartPerformed;
        inputActions.Player.Interact.performed += OnInteractPerformed;
    }

    void OnDisable()
    {
        inputActions.Disable();

        inputActions.Player.Fire.started -= OnFireStarted;
        inputActions.Player.Fire.canceled -= OnFireCanceled;
        inputActions.Player.Reload.performed -= OnReloadPerformed;

        inputActions.Player.Sprint.started -= _ => locomotion.SetSprinting(true);
        inputActions.Player.Sprint.canceled -= _ => locomotion.SetSprinting(false);

        inputActions.Player.EquipRanged.performed -= _ => EquipWeapon(WeaponMode.Ranged);
        inputActions.Player.EquipMelee.performed -= _ => EquipWeapon(WeaponMode.Melee);

        inputActions.Player.Restart.performed -= OnRestartPerformed;
        inputActions.Player.Interact.performed -= OnInteractPerformed;
    }

    void Update()
    {
        if (inputActions == null) return;   // Awake 아직 실행 전 보호
        RefreshIKAndAim();
        if (isDead) { moveInput = Vector3.zero; return; } // 사망: 이동/조준/공격 입력 무시
        if (isRolling) { moveInput = Vector3.zero; return; } // 구르는 중: 이동/조준/공격 입력 무시 (방향은 구르기 시작 때 고정)

        // === MAP DEV: 카메라 회전(Q/E) 대응 이동 로직 - 아래 CameraRelativeMove() 사용 ===
        Vector2 inputVector = inputActions.Player.Move.ReadValue<Vector2>();
        moveInput = CameraRelativeMove(inputVector);
        // === MAP DEV END ===

        AimAtMouse();
        HandleFire();
        UpdateFootsteps();
        UpdateOutOfBreath();
    }

    // ── 스태미나 소진 숨소리 (2D) ──────────────────────
    [Header("숨소리")]
    [Tooltip("스태미나가 이 비율 이하로 떨어지면 숨 헐떡이는 소리 1번")]
    public float outOfBreathRatio = 0.1f;
    [Tooltip("다시 울리려면 스태미나가 이 비율 이상 회복돼야 함 (계속 반복 방지)")]
    public float outOfBreathResetRatio = 0.4f;
    private bool outOfBreathPlayed;

    void UpdateOutOfBreath()
    {
        if (locomotion == null || AudioManager.Library == null) return;
        float st = locomotion.GetStaminaNormalized();
        if (!outOfBreathPlayed && st <= outOfBreathRatio)
        {
            outOfBreathPlayed = true;
            AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.outOfBreath), AudioManager.Vol.OutOfBreath);
        }
        else if (outOfBreathPlayed && st >= outOfBreathResetRatio) outOfBreathPlayed = false;
    }

    // ── 발소리 (2D) ─────────────────────────────────
    [Header("발소리")]
    public float footstepWalkInterval = 0.4f;
    public float footstepRunInterval = 0.3f;
    [Tooltip("달릴 때 발소리 볼륨 배수")]
    public float footstepRunVolume = 1.3f;
    private float footstepTimer;

    void UpdateFootsteps()
    {
        Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero; v.y = 0f;
        if (moveInput.sqrMagnitude < 0.01f || v.magnitude < 0.5f) { footstepTimer = 0f; return; } // 멈춤/벽에 막힘 → 다음 걸음은 바로
        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f || AudioManager.Library == null) return;

        bool running = locomotion != null && locomotion.IsSprinting;
        footstepTimer = running ? footstepRunInterval : footstepWalkInterval;
        AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.footstep),
                            AudioManager.Vol.Footstep * (running ? footstepRunVolume : 1f));
    }

    // void FixedUpdate() => locomotion.Move(moveInput); // (이전)
    void FixedUpdate()
    {
        if (isRolling)
        {
            // 구르기 돌진: 매 물리 프레임 같은 수평 속도 (Rigidbody라 벽에 막히면 멈춤, 중력은 그대로)
            rb.linearVelocity = new Vector3(rollVelocity.x, rb.linearVelocity.y, rollVelocity.z);
            return;
        }
        locomotion.Move(moveInput);
    }

    // ── 구르기 ─────────────────────────────────────
    /// <summary>Space: 쿨타임 1초 + 스태미나 20 + 구르는 중 아님 → 이동 입력 방향(없으면 바라보는 방향)으로 6m 돌진</summary>
    void TryRoll()
    {
        if (isDead) return;
        if (isRolling) { Debug.Log("[Roll] 이미 구르는 중 - 무시"); return; }
        float left = rollEndTime + rollCooldown - Time.time;
        if (left > 0f) { Debug.Log($"[Roll] 쿨타임 {left:F2}초 남음 - 무시"); return; }
        if (!locomotion.TryUseStamina(rollStaminaCost)) { Debug.Log($"[Roll] 스태미나 부족 ({rollStaminaCost} 필요) - 무시"); return; }
        StartCoroutine(RollRoutine());
    }

    System.Collections.IEnumerator RollRoutine()
    {
        // 방향: WASD 입력(카메라 기준)이 있으면 그 방향, 없으면 캐릭터가 바라보는 방향
        Vector3 input = CameraRelativeMove(inputActions.Player.Move.ReadValue<Vector2>());
        Vector3 dir = input.sqrMagnitude > 0.01f ? input : transform.forward;
        dir.y = 0f; dir.Normalize();
        float mult = WeatherManager.RollDistanceMultiplier;
        float distance = rollDistance * mult;
        rollVelocity = dir * (distance / rollDuration);
        transform.rotation = Quaternion.LookRotation(dir); // 구르는 방향을 봄
        if (rb != null) rb.MoveRotation(transform.rotation);

        Vector3 startPos = transform.position;
        isRolling = true;
        // if (AudioManager.Library != null) AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.roll), AudioManager.Vol.Impact); // (이전) 하나만
        if (AudioManager.Library != null) // 시작: 옷 펄럭 (2D). 착지 소리는 아래 무적 끝날 때
            AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.rollSwoosh), AudioManager.Vol.Impact);
        if (health == null) health = GetComponent<Health>();
        if (health != null) health.isInvincible = true;
        if (EnsureAnimRefs())
        {
            // 보통은 Roll 트리거 → Any State → Roll. 단, 무기 바꾼 직후처럼 하체 자세 전환(0.2초) 중이면
            // Any State 전이가 그 전환을 못 끊어서 구르기 동작이 0.2초 늦게 나옴 → 그때만 바로 크로스페이드 (트리거는 지워서 나중에 또 구르지 않게)
            if (animator.IsInTransition(0)) { animator.ResetTrigger(RollHash); animator.CrossFadeInFixedTime(RollStateHash, 0.1f, 0); }
            else animator.SetTrigger(RollHash);
        }
        StowWeaponForRoll();
        Debug.Log($"[Roll] 시작 - 방향 {(input.sqrMagnitude > 0.01f ? "이동 입력" : "바라보는 방향")}, 거리 {distance:F1}m (날씨 ×{mult:F2}), 스태미나 -{rollStaminaCost}, 무적 {rollInvincibleTime}초");

        float start = Time.time;
        while (Time.time < start + rollInvincibleTime) yield return null;
        if (health != null) health.isInvincible = false;
        if (AudioManager.Library != null) // 착지: 몸이 바닥에 닿는 옷 소리 (펄럭과 섞여 구르는 느낌)
            AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.rollLand), AudioManager.Vol.Impact * 0.8f);
        while (Time.time < start + rollDuration) yield return null;

        isRolling = false;
        if (rb != null) rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f); // 미끄러지지 않게 멈춤
        RestoreWeaponAfterRoll();
        rollEndTime = Time.time;
        Vector3 moved = transform.position - startPos; moved.y = 0f;
        Debug.Log($"[Roll] 끝 - 실제 이동 {moved.magnitude:F2}m, 쿨타임 {rollCooldown}초");
    }

    // 무기별: 소총(AR/샷건/SMG/저격) = 손에 그대로 / 권총 = 허리(pistolHolsterPoint) / 도끼 = 등(axeBackPoint). 위치가 없으면 숨김
    void StowWeaponForRoll()
    {
        if (weaponHandAttacher == null) weaponHandAttacher = FindAnyObjectByType<WeaponHandAttacher>();
        Transform visual = weaponHandAttacher != null ? weaponHandAttacher.CurrentVisual : null;
        if (visual == null) return;
        bool axe = currentMode == WeaponMode.Melee;
        bool pistol = currentMode == WeaponMode.Ranged && rangedWeapon != null && rangedWeapon.weaponData != null && rangedWeapon.weaponData.type == WeaponType.Pistol;
        if (!axe && !pistol) return; // 소총: 들고 구름

        Transform point = pistol ? pistolHolsterPoint : axeBackPoint;
        rollStowed = visual;
        rollStowParent = visual.parent;
        rollStowPos = visual.localPosition;
        rollStowRot = visual.localRotation;
        rollStowHidden = point == null;
        if (rollStowHidden) visual.gameObject.SetActive(false);
        else { visual.SetParent(point, false); visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity; }
        Debug.Log($"[Roll] {(pistol ? "권총" : "도끼")} → {(rollStowHidden ? "숨김 (위치 미지정)" : point.name)}");
    }

    void RestoreWeaponAfterRoll()
    {
        if (rollStowed == null) return;
        if (rollStowHidden) rollStowed.gameObject.SetActive(true);
        else { rollStowed.SetParent(rollStowParent, false); rollStowed.localPosition = rollStowPos; rollStowed.localRotation = rollStowRot; }
        rollStowed = null; rollStowParent = null;
    }

    // 애니메이션 적용 뒤: 도끼 들었을 때 상체(어깨)를 조준 방향으로 비틀어 줌 (총은 상체 레이어 조준 자세가 담당)
    void LateUpdate()
    {
        if (!meleeHeld || isDead || meleeTorsoAimWeight <= 0f || !EnsureAnimRefs()) { meleeTorsoTwist = 0f; return; }
        Transform ls = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), rs = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (ls == null || rs == null) return;

        // 하체 안정: 상체 레이어(공격/피격/마시기)가 켜진 만큼 골반 방향을 루트 정면으로 되돌림.
        // 골반을 돌리면 자식인 척추도 같이 돌아가므로 척추는 반대로 돌려 상체는 애니 그대로(월드 기준) 유지
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips), spineRoot = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform lul = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rul = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        if (meleeStabilizeHips && upperWeight > 0.01f && hips != null && spineRoot != null && lul != null && rul != null)
        {
            Vector3 pelvisForward = Vector3.Cross(rul.position - lul.position, Vector3.up); pelvisForward.y = 0f;
            if (pelvisForward.sqrMagnitude > 1e-4f)
            {
                Quaternion fix = Quaternion.AngleAxis(Vector3.SignedAngle(pelvisForward, transform.forward, Vector3.up) * upperWeight, Vector3.up);
                Quaternion spineWorld = spineRoot.rotation;
                hips.rotation = fix * hips.rotation;
                spineRoot.rotation = spineWorld;
            }
            // 골반 수평 위치도 평소 위치로 (내려찍기 클립은 골반이 0.2m 앞으로 나가서 다리째 끌려가 발이 0.4m 미끄러졌음)
            // [버그 수정] 휘두르는 동안에만, 그리고 골반이 루트 근처(1m 안)일 때만 보정.
            //  예전엔 상체 동작 때마다 + 범위 제한 없이 적용 → 게임 시작 첫 프레임(모델이 아직 엉뚱한 위치)의 값을 기억해서
            //  골반을 236m 밖으로 옮김 → 화면 밖이라 Animator가 갱신을 멈춰(CullUpdateTransforms) 캐릭터가 영영 사라졌음
            Vector3 local = transform.InverseTransformPoint(hips.position);
            if (Time.time < meleeSwingUntil && local.x * local.x + local.z * local.z < 1f)
            {
                Vector3 delta = new Vector3(hipsNeutralLocal.x - local.x, 0f, hipsNeutralLocal.z - local.z) * upperWeight;
                hips.position = transform.TransformPoint(local + delta);
            }
        }
        else if (hips != null)
        {
            // 상체 동작이 없을 때의 골반 위치(루트 기준 수평)를 기억 (달릴 때 좌우 흔들림은 평균으로)
            // 정상 범위(1m 안)일 때만 배우고, 기억값도 0.5m 안으로 제한 (이상한 프레임 값이 섞여도 캐릭터가 날아가지 않게)
            Vector3 local = transform.InverseTransformPoint(hips.position);
            if (local.x * local.x + local.z * local.z < 1f)
            {
                hipsNeutralLocal = Vector3.Lerp(hipsNeutralLocal, new Vector3(local.x, 0f, local.z), 1f - Mathf.Exp(-Time.deltaTime / 0.3f));
                hipsNeutralLocal = Vector3.ClampMagnitude(hipsNeutralLocal, 0.5f);
            }
        }

        // 이번 프레임 애니 포즈의 어깨 정면 vs 루트 정면(= 마우스 방향, AimAtMouse가 맞춤)
        Vector3 bodyForward = Vector3.Cross(rs.position - ls.position, Vector3.up);
        bodyForward.y = 0f;
        if (bodyForward.sqrMagnitude < 1e-4f) return;
        float error = Mathf.Clamp(Vector3.SignedAngle(bodyForward, transform.forward, Vector3.up), -meleeTorsoMaxTwist, meleeTorsoMaxTwist);
        bool swinging = Time.time < meleeSwingUntil;
        // 휘두르는 동안엔 상체 회전(올려치기 비틀기)이 공격 동작 자체라 오차를 따라가지 않고 고정 보정만
        // float target = (error + (swinging ? meleeSwingYawOffset : 0f)) * meleeTorsoAimWeight;   // (이전) 가로 베기 (meleeSwingYawOffset = -45)
        float target = (swinging ? meleeChopYawOffset : error) * meleeTorsoAimWeight;
        // 휘두를 땐 빨리 따라가야 궤적이 바로 마우스 쪽으로 감
        float smooth = swinging ? 0.06f : meleeTorsoSmoothTime;
        meleeTorsoTwist = Mathf.Lerp(meleeTorsoTwist, target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, smooth)));

        // 척추 → 가슴 → 윗가슴에 나눠서 돌림 (한 뼈만 돌리면 허리가 꺾여 보임)
        var spine = new[] { animator.GetBoneTransform(HumanBodyBones.Spine), animator.GetBoneTransform(HumanBodyBones.Chest), animator.GetBoneTransform(HumanBodyBones.UpperChest) };
        int count = 0; foreach (var b in spine) if (b != null) count++;
        if (count == 0) return;
        Quaternion step = Quaternion.AngleAxis(meleeTorsoTwist / count, Vector3.up);
        foreach (var b in spine) if (b != null) b.rotation = step * b.rotation;
    }

    // === MAP DEV: 카메라 관련 - 병합 시 이 메서드 삭제/수정 전 카메라 회전 기능 영향 확인 ===
    /// <summary>
    /// WASD 입력을 카메라가 바라보는 방향 기준으로 변환.
    /// Q/E로 카메라가 회전해도 W는 항상 "화면 위쪽"으로 이동하게 함.
    /// (CameraRotate.cs와 짝을 이룸. 이 메서드 없으면 카메라 회전 시 이동 방향이 어긋남)
    /// </summary>
    private Vector3 CameraRelativeMove(Vector2 inputVector)
    {
        if (mainCamera == null) return new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = mainCamera.transform.right;
        camRight.y = 0f;
        camRight.Normalize();

        return (camRight * inputVector.x + camForward * inputVector.y).normalized;
    }
    // === MAP DEV END ===

    // ── 입력 콜백 ────────────────────────────────

    private void OnFireStarted(InputAction.CallbackContext context)
    {
        isFireButtonPressed = true;
        firePressed = true;
    }

    private void OnFireCanceled(InputAction.CallbackContext context)
        => isFireButtonPressed = false;

    private void OnReloadPerformed(InputAction.CallbackContext context)
    {
        if (isRolling) return; // 구르는 중엔 재장전 막음
        if (currentMode == WeaponMode.Ranged && rangedWeapon != null)
            rangedWeapon.TryReload();
    }

    private void OnRestartPerformed(InputAction.CallbackContext context)
        => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        InventoryManager inventory = GetComponent<InventoryManager>();
        if (inventory == null) return;

        PickupItem nearestItem = GetClosestNearbyItem();
        LootCrate nearestCrate = GetClosestNearbyCrate();

        // 박스가 더 가까우면 박스 먼저 열기
        if (nearestCrate != null &&
            (nearestItem == null || SqrDistTo(nearestCrate.transform) < SqrDistTo(nearestItem.transform)))
        {
            nearestCrate.Open(inventory);
            nearbyCrates.Remove(nearestCrate);
            return;
        }

        if (nearestItem == null) return;

        bool picked = false;

        if (nearestItem.itemData is BackpackData bag)
        {
            // 가방: 주우면 바로 등에 멤 (이미 있으면 무시 - 바닥에 그대로)
            var bm = BackpackManager.Instance;
            picked = bm != null && bm.TryPickup(bag);
        }
        else if (nearestItem.itemData is WeaponData wd)
        {
            var hotbar = WeaponHotbarUI.Instance;
            // 무기 슬롯(1,2)에 자리 있으면 핫바, 꽉 찼으면 인벤토리로
            if (hotbar != null) picked = hotbar.AddWeapon(wd);
            if (!picked) picked = inventory.AddItem(wd, 1);
        }
        else
        {
            picked = inventory.AddItem(nearestItem.itemData, nearestItem.amount);
        }

        if (picked)
        {
            if (AudioManager.Library != null) AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.pickup), AudioManager.Vol.Pickup);
            nearbyItems.Remove(nearestItem);
            Destroy(nearestItem.gameObject);
        }
    }

    private float SqrDistTo(Transform t)
        => (t.position - transform.position).sqrMagnitude;

    // ── 발사 처리 ────────────────────────────────

    private bool IsPointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void HandleFire()
    {
        if (IsPointerOverUI()) { firePressed = false; return; }

        ItemData activeItem = WeaponHotbarUI.Instance?.GetActiveItem();

        // 빈 슬롯이면 아무것도 안 함
        if (activeItem == null) { firePressed = false; return; }

        if (activeItem is ThrowableData)
        {
            if (firePressed) ThrowActiveItem();
            firePressed = false;
            return;
        }

        if (activeItem is ConsumableData)
        {
            if (firePressed) UseActiveConsumable();
            firePressed = false;
            return;
        }

        // 일반 무기
        if (currentMode == WeaponMode.Ranged && rangedWeapon != null)
        {
            bool auto = rangedWeapon.weaponData != null && rangedWeapon.weaponData.autoFire;
            if (auto && isFireButtonPressed) rangedWeapon.TryFire();
            else if (!auto && firePressed) rangedWeapon.TryFireSingle();
        }
        else if (currentMode == WeaponMode.Melee && meleeWeapon != null && isFireButtonPressed)
        {
            // if (meleeWeapon.TryAttack() && animator != null)   // (이전) 스테미나 없이 무한 휘두르기
            float staminaCost = meleeWeapon.weaponData != null ? meleeWeapon.weaponData.staminaCost : 0f;
            if (meleeWeapon.IsReady && locomotion.TryUseStamina(staminaCost) && meleeWeapon.TryAttack() && animator != null)
            {
                animator.SetTrigger(MeleeAttackHash); // 도끼 휘두르기 (Upper Body 레이어)
                // UpperAction(0.8f);                  // (이전) 가로 베기 0.8초
                // meleeSwingUntil = Time.time + 0.8f;
                UpperAction(meleeSwingDuration);       // 휘두르는 동안만 상체 레이어 켬 (내려찍기 0.59초)
                meleeSwingUntil = Time.time + meleeSwingDuration; // 휘두르는 동안: 몸 회전 고정 + 하체 안정 + 궤적 마우스 쪽
                ikBlockUntil = Mathf.Max(ikBlockUntil, Time.time + 1f); // 왼손 IK 1초간 확실히 끔 (도끼는 원래 꺼져 있지만 무기 전환 직후 공격 대비)
                FaceMouseNow();                        // 공격 시작 순간 마우스 방향으로 즉시 회전
            }
        }

        firePressed = false;
    }

    // ── 투척 ─────────────────────────────────────

    private void ThrowActiveItem()
    {
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null) return;

        ThrowableData throwable = hotbar.GetActiveItem() as ThrowableData;
        if (throwable == null || throwable.throwablePrefab == null) return;

        Vector3 spawnPos = transform.position + Vector3.up * 1.2f;
        Vector3 throwDir = (transform.forward + Vector3.up * 0.35f).normalized;

        GameObject obj = Instantiate(throwable.throwablePrefab, spawnPos, Quaternion.identity);
        ThrowableItem thrownItem = obj.GetComponent<ThrowableItem>();
        if (thrownItem != null)
        {
            thrownItem.data = throwable;
            thrownItem.Throw(throwDir, 12f);
        }

        hotbar.ConsumeThrowable(hotbar.GetActiveSlot());
    }

    // ── 소모품 ───────────────────────────────────

    private void UseActiveConsumable()
    {
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null) return;

        ConsumableData consumable = hotbar.GetActiveItem() as ConsumableData;
        if (consumable == null) return;

        UseConsumable(consumable);

        hotbar.ConsumeItem(hotbar.GetActiveSlot());
    }

    /// <summary>소비 아이템 효과 적용 (핫바 3~5 키에서도 호출). 개수 차감은 호출하는 쪽에서</summary>
    public void UseConsumable(ConsumableData consumable)
    {
        // 아이템 사용 동작 (마시기, 상체만 → 걸으면서 가능) - 그동안 왼손 IK 끔
        if (EnsureAnimRefs()) animator.SetTrigger(UseItemHash);
        ikBlockUntil = Time.time + 3.0f;
        UpperAction(3.0f);

        PlayerBuffManager buffMgr = GetComponent<PlayerBuffManager>();
        // 씬 플레이어에 PlayerBuffManager가 안 붙어 있으면 수분/포만감/지속 회복이 전부 무시됐음 → 없으면 붙임
        if (buffMgr == null) buffMgr = gameObject.AddComponent<PlayerBuffManager>();
        Health health = GetComponent<Health>();

        if (buffMgr != null && health != null)
            buffMgr.ApplyConsumable(consumable, health);
        else if (health != null)
            health.Heal(consumable.healAmount);
    }

    // ── 무기 ─────────────────────────────────────

    private void EquipWeapon(WeaponMode newMode)
    {
        currentMode = newMode;
        ApplyMeleePose(newMode == WeaponMode.Melee);

        if (currentMode == WeaponMode.Ranged)
        {
            if (rangedWeaponObject != null) rangedWeaponObject.SetActive(true);
            if (meleeWeaponObject != null) meleeWeaponObject.SetActive(false);
        }
        else
        {
            if (rangedWeaponObject != null) rangedWeaponObject.SetActive(false);
            if (meleeWeaponObject != null) meleeWeaponObject.SetActive(true);
        }
    }

    // ── 도끼 자세 (Mixamo Pro Melee Axe) ──
    static readonly int MeleeAttackHash = Animator.StringToHash("MeleeAttack");
    private Animator animator;
    private UnityEngine.Animations.Rigging.Rig handIKRig;
    private int upperBodyLayer = -1;

    static readonly int WeaponPoseHash = Animator.StringToHash("WeaponPose");
    static readonly int ReloadHash = Animator.StringToHash("Reload");
    static readonly int UseItemHash = Animator.StringToHash("UseItem");
    static readonly int HitHash = Animator.StringToHash("Hit");
    static readonly int DeadHash = Animator.StringToHash("Dead");
    static readonly int WeaponSwitchHash = Animator.StringToHash("WeaponSwitch");

    private bool meleeHeld;          // 도끼 들고 있음
    private bool isDead;
    private float reloadUntil;       // 재장전 중 (IK 끔 + 조준 멈춤)
    private float ikBlockUntil;      // 아이템 사용/무기 전환 중 (IK 끔)
    private float upperActionUntil;  // 상체 동작(휘두르기/마시기/전환/피격) 재생 중 - 도끼일 때 이 동안만 상체 레이어 켬
    private float upperWeight = 1f;
    private float moveBlend;          // 도끼 손 방향: 0 = 서 있을 때, 1 = 움직일 때
    private float swingGrip;          // 도끼 쥐는 방식: 0 = 들고 다닐 때, 1 = 휘두를 때

    /// <summary>상체 레이어로 재생하는 동작 시작 (도끼 들었을 땐 이 시간 동안만 상체 레이어가 켜짐)</summary>
    void UpperAction(float seconds) => upperActionUntil = Mathf.Max(upperActionUntil, Time.time + seconds);

    bool EnsureAnimRefs()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            handIKRig = GetComponentInChildren<UnityEngine.Animations.Rigging.Rig>();
            if (animator != null) upperBodyLayer = animator.GetLayerIndex("Upper Body");
        }
        return animator != null;
    }

    /// <summary>근접 무기 들면 도끼 자세(WeaponPose 0)로. IK/조준은 RefreshIKAndAim에서 매 프레임 정리</summary>
    private void ApplyMeleePose(bool melee)
    {
        meleeHeld = melee;
        if (EnsureAnimRefs() && melee) animator.SetInteger(WeaponPoseHash, 0);
        RefreshIKAndAim();
    }

    /// <summary>
    /// 왼손 IK / 총구 조준 규칙 (한 곳에서 관리):
    ///  IK 끔 = 도끼 / 재장전 / 아이템 사용 / 무기 전환 / 사망,  조준 멈춤 = 도끼 / 재장전 / 사망
    /// </summary>
    private void RefreshIKAndAim()
    {
        EnsureAnimRefs();
        bool reloading = Time.time < reloadUntil;
        bool ikOff = meleeHeld || isDead || reloading || isRolling || Time.time < ikBlockUntil;
        if (handIKRig != null) handIKRig.weight = ikOff ? 0f : 1f;

        // 상체 레이어: 총 = 항상 켬(권총/소총 자세), 도끼 = 평소 끔(맨손처럼 자연스럽게) + 상체 동작 중에만 켬, 사망 = 끔
        // 구르기: 전신 동작이라 상체 레이어를 바로 끔 (안 끄면 다리만 구르고 상체는 총 자세)
        float target = isDead || isRolling ? 0f : (!meleeHeld || Time.time < upperActionUntil) ? 1f : 0f;
        upperWeight = Mathf.MoveTowards(upperWeight, target, Time.deltaTime * (isRolling ? 20f : target > upperWeight ? 12f : 4f)); // 켤 땐 빠르게, 끌 땐 부드럽게
        if (animator != null && upperBodyLayer >= 0) animator.SetLayerWeight(upperBodyLayer, upperWeight);

        // 도끼 손 회전: 들고 다닐 때(carry) ↔ 휘두를 때(rotationOffset)를 상체 레이어 가중치에 맞춰 부드럽게 전환
        if (meleeHeld && !isRolling && meleeWeapon != null && meleeWeapon.weaponData != null && weaponHandAttacher != null) // 구르는 중엔 도끼가 등/숨김 상태라 손 자세 갱신 안 함
        {
            var wd = meleeWeapon.weaponData;
            Quaternion swing = Quaternion.Euler(wd.rotationOffset);
            if (wd.rigidGrip)
            {
                // 손잡이를 주먹으로 꽉 쥔 한 가지 방식으로 고정: 뛰든 휘두르든 손 뼈에 딱 붙어서 같이 움직임 (섞는 동안 손 안에서 돌며 흐물거리던 문제)
                weaponHandAttacher.SetVisualLocalRotation(swing);
                weaponHandAttacher.SetVisualLocalPosition(wd.swingPositionOffset);
            }
            else
            {
                Quaternion carryIdle = wd.carryRotationOffset != Vector3.zero ? Quaternion.Euler(wd.carryRotationOffset) : swing;
                Quaternion carryMove = wd.carryMoveRotationOffset != Vector3.zero ? Quaternion.Euler(wd.carryMoveRotationOffset) : carryIdle;
                // 서 있을 때(팔 늘어뜨림)와 움직일 때(팔 굽혀 흔듦)는 손 방향이 달라서 하나로는 둘 다 자연스럽게 안 됨 → 이동 속도로 섞음
                float planar = rb != null ? new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude : 0f;
                moveBlend = Mathf.MoveTowards(moveBlend, Mathf.Clamp01(planar / 3f), Time.deltaTime * 4f);
                Quaternion carry = Quaternion.Slerp(carryIdle, carryMove, moveBlend);
                // 쥐는 방향/위치 전환: 공격 시작엔 거의 즉시(돌면서 넘어가면 덜렁거려 보임), 끝날 땐 부드럽게
                float gripTarget = Time.time < upperActionUntil ? 1f : 0f;
                swingGrip = Mathf.MoveTowards(swingGrip, gripTarget, Time.deltaTime * (gripTarget > swingGrip ? 40f : 4f));
                weaponHandAttacher.SetVisualLocalRotation(Quaternion.Slerp(carry, swing, swingGrip));
                // 손 뼈 원점은 손목 → 휘두를 땐 손바닥 중심에서 손잡이 중간을 쥐도록 위치 이동 (손잡이 끝을 손목에 꽂은 것처럼 덜렁거리던 문제)
                weaponHandAttacher.SetVisualLocalPosition(Vector3.Lerp(wd.positionOffset, wd.swingPositionOffset, swingGrip));
            }
        }
        if (weaponHandAttacher == null) weaponHandAttacher = FindAnyObjectByType<WeaponHandAttacher>();
        if (weaponHandAttacher != null) weaponHandAttacher.aimAtTarget = !(meleeHeld || isDead || reloading || isRolling);
    }

    void OnReloadStarted(float reloadTime)
    {
        if (EnsureAnimRefs()) animator.SetTrigger(ReloadHash);
        reloadUntil = Time.time + reloadTime;
        UpperAction(1.8f);
    }

    void OnPlayerHurt()
    {
        if (!isDead && EnsureAnimRefs()) { animator.SetTrigger(HitHash); UpperAction(0.6f); }
    }

    void OnPlayerDied()
    {
        if (isDead) return;
        isDead = true;
        isFireButtonPressed = false;
        if (EnsureAnimRefs())
        {
            animator.SetBool(DeadHash, true);
            if (upperBodyLayer >= 0) animator.SetLayerWeight(upperBodyLayer, 0f); // 전신 사망 동작이 보이게
        }
        RefreshIKAndAim();
    }

    /// <summary>핫바 1↔2 전환 시 (WeaponHotbarUI.SelectSlot) - 무기 꺼내는 동작, 그동안 IK 끔</summary>
    public void PlayWeaponSwitch()
    {
        if (isDead) return;
        if (EnsureAnimRefs()) animator.SetTrigger(WeaponSwitchHash);
        ikBlockUntil = Time.time + 0.6f;
        UpperAction(0.6f);
    }

    public void HideCurrentWeapon()
    {
        if (rangedWeaponObject != null) rangedWeaponObject.SetActive(false);
        if (meleeWeaponObject != null) meleeWeaponObject.SetActive(false);
        if (weaponHandAttacher != null) weaponHandAttacher.HideVisual();
    }

    public void SwapWeaponData(WeaponData newData, int savedAmmo = -1)
    {
        if (newData == null) return;

        if (newData.type == WeaponType.Melee)
        {
            // 핫바 1,2에 근접 무기가 있을 수 있음 → 근접 모드로 전환 + 손에 도끼 모델
            EquipWeapon(WeaponMode.Melee);
            if (meleeWeapon != null) meleeWeapon.ChangeWeaponData(newData);
            if (weaponHandAttacher != null && newData.weaponPrefab != null)
                weaponHandAttacher.SwapVisual(newData.weaponPrefab, newData.positionOffset, newData.rotationOffset);
        }
        else
        {
            EquipWeapon(WeaponMode.Ranged);
            // 상체 대기 자세: 권총 1, 나머지(AR/SMG/샷건) 소총 2
            if (EnsureAnimRefs()) animator.SetInteger(WeaponPoseHash, newData.type == WeaponType.Pistol ? 1 : 2);
            if (rangedWeapon != null) rangedWeapon.ChangeWeaponData(newData, savedAmmo);
            if (weaponHandAttacher != null && newData.weaponPrefab != null)
                weaponHandAttacher.SwapVisual(newData.weaponPrefab, newData.positionOffset, newData.rotationOffset);
        }
    }

    // ── 아이템 픽업 ──────────────────────────────

    public void SetNearbyItem(PickupItem item)
    {
        if (!nearbyItems.Contains(item)) nearbyItems.Add(item);
    }

    public void ClearNearbyItem(PickupItem item) => nearbyItems.Remove(item);

    /// <summary>현재 F키 누르면 먹게 될 가장 가까운 아이템</summary>
    public PickupItem GetClosestNearbyItem()
    {
        nearbyItems.RemoveAll(i => i == null);
        if (nearbyItems.Count == 0) return null;

        PickupItem closest = nearbyItems[0];
        float minDist = (closest.transform.position - transform.position).sqrMagnitude;
        for (int i = 1; i < nearbyItems.Count; i++)
        {
            float d = (nearbyItems[i].transform.position - transform.position).sqrMagnitude;
            if (d < minDist) { minDist = d; closest = nearbyItems[i]; }
        }
        return closest;
    }
    // ── 박스(LootCrate) 픽업 ─────────────────────

    public void SetNearbyCrate(LootCrate crate)
    {
        if (!nearbyCrates.Contains(crate)) nearbyCrates.Add(crate);
    }

    public void ClearNearbyCrate(LootCrate crate) => nearbyCrates.Remove(crate);

    public LootCrate GetClosestNearbyCrate()
    {
        nearbyCrates.RemoveAll(c => c == null);
        if (nearbyCrates.Count == 0) return null;

        LootCrate closest = nearbyCrates[0];
        float minDist = SqrDistTo(closest.transform);
        for (int i = 1; i < nearbyCrates.Count; i++)
        {
            float d = SqrDistTo(nearbyCrates[i].transform);
            if (d < minDist) { minDist = d; closest = nearbyCrates[i]; }
        }
        return closest;
    }

    // ── 외부 조회 ────────────────────────────────

    public int GetCurrentAmmo()
        => rangedWeapon != null ? rangedWeapon.GetCurrentAmmo() : 0;

    public WeaponData GetCurrentWeaponData(WeaponType type)
        => type == WeaponType.Melee
            ? meleeWeapon?.weaponData
            : rangedWeapon?.weaponData;

    /// <summary>마우스 방향으로 즉시 회전 (부드럽게 돌지 않고 바로) - 공격 시작용</summary>
    void FaceMouseNow()
    {
        if (Mouse.current == null || mainCamera == null) return;
        if (!GameUtils.GetMouseWorldPosition(mainCamera, transform.position.y + aimHeightOffset, out Vector3 targetPoint)) return;
        Vector3 lookDir = targetPoint - transform.position; lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.LookRotation(lookDir.normalized);
        if (rb != null) rb.rotation = transform.rotation;
    }

    void AimAtMouse()
    {
        if (Mouse.current == null || mainCamera == null) return;

        // 기준 높이는 캐릭터 루트 + 고정 오프셋(총구 높이 정도). firePoint.position.y처럼
        // 애니메이션 때문에 흔들리는 값을 쓰면 마우스가 가만히 있어도 조준 지점이 미세하게
        // 계속 흔들리므로(특히 캐릭터와 가까운 거리일수록 각도로 크게 증폭), 반드시
        // 흔들리지 않는 고정값을 써야 한다.
        float aimY = transform.position.y + aimHeightOffset;

        if (GameUtils.GetMouseWorldPosition(mainCamera, aimY, out Vector3 targetPoint))
        {
            Vector3 lookDir = (targetPoint - transform.position).normalized;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude < 0.01f) return;

            // 도끼 휘두르는 동안은 몸 방향 고정 (공격 시작 때 FaceMouseNow로 이미 맞춤) → 내려찍는 중 몸이 돌지 않게
            if (!(meleeHeld && Time.time < meleeSwingUntil))
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(lookDir),
                    rotationSpeed * Time.deltaTime);

            // aimTarget은 실제 마우스가 가리키는 정확한 월드 좌표를 그대로 담는다.
            // Weapon.cs를 포함해 이 값을 참조하는 모든 곳이 정확히 같은 값을 보게 되므로
            // "캐릭터가 보는 방향과 총알 방향이 다르다"는 문제가 생기지 않는다.
            if (aimTarget != null) aimTarget.position = targetPoint;
        }
    }
}
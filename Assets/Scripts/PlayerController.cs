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
    }

    void OnEnable()
    {
        inputActions.Enable();

        inputActions.Player.Fire.started += OnFireStarted;
        inputActions.Player.Fire.canceled += OnFireCanceled;
        inputActions.Player.Reload.performed += OnReloadPerformed;

        inputActions.Player.Sprint.started += _ => locomotion.SetSprinting(true);
        inputActions.Player.Sprint.canceled += _ => locomotion.SetSprinting(false);
        inputActions.Player.Roll.performed += _ => locomotion.TryRoll(moveInput);

        // 1키(EquipRanged)/V키(EquipMelee): 핫바가 있으면 핫바가 무기 전환을 담당 → 무시
        // (안 그러면 1번 슬롯 도끼를 고를 때 1키가 총 모드로 덮어써버림)
        inputActions.Player.EquipRanged.performed += _ => { if (WeaponHotbarUI.Instance == null) EquipWeapon(WeaponMode.Ranged); };
        inputActions.Player.EquipMelee.performed += _ => { if (WeaponHotbarUI.Instance == null) EquipWeapon(WeaponMode.Melee); };

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

        // === MAP DEV: 카메라 회전(Q/E) 대응 이동 로직 - 아래 CameraRelativeMove() 사용 ===
        Vector2 inputVector = inputActions.Player.Move.ReadValue<Vector2>();
        moveInput = CameraRelativeMove(inputVector);
        // === MAP DEV END ===

        AimAtMouse();
        HandleFire();
    }

    void FixedUpdate() => locomotion.Move(moveInput);

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
            if (meleeWeapon.TryAttack() && animator != null)
                animator.SetTrigger(MeleeAttackHash); // 도끼 휘두르기 (Upper Body 레이어)
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

    /// <summary>근접 무기 들면: 상체 레이어(도끼 대기/휘두르기) 켜고, 왼손 IK와 총구 조준 회전 끔. 총이면 반대로</summary>
    private void ApplyMeleePose(bool melee)
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            handIKRig = GetComponentInChildren<UnityEngine.Animations.Rigging.Rig>();
            if (animator != null) upperBodyLayer = animator.GetLayerIndex("Upper Body");
        }
        if (animator != null && upperBodyLayer >= 0) animator.SetLayerWeight(upperBodyLayer, melee ? 1f : 0f);
        if (handIKRig != null) handIKRig.weight = melee ? 0f : 1f;
        if (weaponHandAttacher == null) weaponHandAttacher = FindAnyObjectByType<WeaponHandAttacher>();
        if (weaponHandAttacher != null) weaponHandAttacher.aimAtTarget = !melee;
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
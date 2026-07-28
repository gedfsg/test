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

        if (rangedWeapon != null && rangedWeapon.weaponData != null)
            WeaponHotbarUI.Instance?.AddWeapon(rangedWeapon.weaponData, rangedWeapon.GetCurrentAmmo());

        EquipWeapon(WeaponMode.Ranged);
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

        inputActions.Player.EquipRanged.performed += _ => EquipWeapon(WeaponMode.Ranged);
        inputActions.Player.EquipMelee.performed += _ => EquipWeapon(WeaponMode.Melee);

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

        Vector2 inputVector = inputActions.Player.Move.ReadValue<Vector2>();
        moveInput = new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        AimAtMouse();
        HandleFire();
    }

    void FixedUpdate() => locomotion.Move(moveInput);

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

        if (nearestItem.itemData is WeaponData wd)
        {
            var hotbar = WeaponHotbarUI.Instance;
            if (hotbar != null)
            {
                if (hotbar.IsFull()) return;
                picked = hotbar.AddWeapon(wd);
            }
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
            meleeWeapon.TryAttack();
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

        PlayerBuffManager buffMgr = GetComponent<PlayerBuffManager>();
        Health health = GetComponent<Health>();

        if (buffMgr != null && health != null)
            buffMgr.ApplyConsumable(consumable, health);
        else if (health != null)
            health.Heal(consumable.healAmount);

        hotbar.ConsumeItem(hotbar.GetActiveSlot());
    }

    // ── 무기 ─────────────────────────────────────

    private void EquipWeapon(WeaponMode newMode)
    {
        currentMode = newMode;

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
            if (meleeWeapon != null) meleeWeapon.ChangeWeaponData(newData);
        }
        else
        {
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
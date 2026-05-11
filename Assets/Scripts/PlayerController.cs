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
    private InventoryUI inventoryUI;

    private EquipmentManager equipmentManager;


    private bool isFireButtonPressed = false;

    [Header("Weapons")]
    public GameObject rangedWeaponObject;
    public GameObject meleeWeaponObject;

    private Weapon rangedWeapon;
    private MeleeWeapon meleeWeapon;

    public float rotationSpeed = 15f;

    private enum WeaponMode { Ranged, Melee }
    private WeaponMode currentMode = WeaponMode.Ranged;

    private bool firePressed = false; // 이번 프레임에 클릭했는지

    private List<PickupItem> nearbyItems = new List<PickupItem>();

    [Header("Animation Rigging")]
    public Transform aimTarget; // Inspector에서 AimTarget 연결

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

    // 마우스 포인터가 UI 위에 있는지 체크
    // Update()에서 호출하므로 IsPointerOverGameObject() 정상 작동
    private bool IsPointerOverUI()
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject();
    }

    private void OnFireStarted(InputAction.CallbackContext context)
    {
        isFireButtonPressed = true;
        firePressed = true; // 누른 순간만 true
    }

    private void OnFireCanceled(InputAction.CallbackContext context)
    {
        isFireButtonPressed = false;
    }
    private void OnReloadPerformed(InputAction.CallbackContext context)
    {
        if (currentMode == WeaponMode.Ranged && rangedWeapon != null)
            rangedWeapon.TryReload();
    }

    void Update()
    {
        Vector2 inputVector = inputActions.Player.Move.ReadValue<Vector2>();
        moveInput = new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        AimAtMouse();
        HandleFire();
    }

    void FixedUpdate()
    {
        locomotion.Move(moveInput);
    }

    private void HandleFire()
    {
        if (IsPointerOverUI()) { firePressed = false; return; }

        if (currentMode == WeaponMode.Ranged && rangedWeapon != null)
        {
            bool auto = rangedWeapon.weaponData != null && rangedWeapon.weaponData.autoFire;

            if (auto && isFireButtonPressed)
                rangedWeapon.TryFire();
            else if (!auto && firePressed) // 누른 순간 딱 1번만
                rangedWeapon.TryFireSingle();
        }
        else if (currentMode == WeaponMode.Melee && meleeWeapon != null && isFireButtonPressed)
        {
            meleeWeapon.TryAttack();
        }

        firePressed = false; // 매 프레임 끝에 초기화
    }

    void AimAtMouse()
    {
        if (Mouse.current == null || mainCamera == null) return;

        float aimY = rangedWeapon != null
            ? rangedWeapon.firePoint.position.y
            : transform.position.y;

        if (GameUtils.GetMouseWorldPosition(mainCamera, aimY, out Vector3 targetPoint))
        {
            // 기존 플레이어 회전
            Vector3 lookDirection = targetPoint - transform.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude < 0.01f) return;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            // AimTarget을 마우스 위치로 이동
            if (aimTarget != null)
                aimTarget.position = targetPoint;
        }
    }

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

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        nearbyItems.RemoveAll(item => item == null);
        if (nearbyItems.Count == 0) return;

        PickupItem target = nearbyItems[0];
        InventoryManager inventory = GetComponent<InventoryManager>();
        if (inventory != null)
        {
            bool isAdded = inventory.AddItem(target.itemData, target.amount);
            if (isAdded)
            {
                nearbyItems.Remove(target);
                Destroy(target.gameObject);
            }
        }
    }

    public void SetNearbyItem(PickupItem item)
    {
        if (!nearbyItems.Contains(item))
            nearbyItems.Add(item);
    }

    public void ClearNearbyItem(PickupItem item)
    {
        nearbyItems.Remove(item);
    }

    public void SwapWeaponData(WeaponData newData)
    {
        if (newData == null) return;

        if (newData.type == WeaponType.Ranged)
        {
            if (rangedWeapon != null) rangedWeapon.ChangeWeaponData(newData);
        }
        else if (newData.type == WeaponType.Melee)
        {
            if (meleeWeapon != null) meleeWeapon.ChangeWeaponData(newData);
        }
    }

    public int GetCurrentAmmo()
    {
        return rangedWeapon != null ? rangedWeapon.GetCurrentAmmo() : 0;
    }

    public WeaponData GetCurrentWeaponData(WeaponType type)
    {
        if (type == WeaponType.Melee)
            return meleeWeapon != null ? meleeWeapon.weaponData : null;
        else
            return rangedWeapon != null ? rangedWeapon.weaponData : null;
    }

    private void OnRestartPerformed(InputAction.CallbackContext context)
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
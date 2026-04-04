using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    private Rigidbody rb;
    private Camera mainCamera;
    private Vector3 moveInput;

    private PlayerInputActions inputActions;
    private Locomotion locomotion;
    private bool isFireButtonPressed = false;

    [Header("Weapons")]
    public GameObject rangedWeaponObject;
    public GameObject meleeWeaponObject;

    private Weapon rangedWeapon;
    private MeleeWeapon meleeWeapon;

    // 현재 무기 상태를 저장하는 열거형임.
    private enum WeaponMode { Ranged, Melee }
    private WeaponMode currentMode = WeaponMode.Ranged;

    private PickupItem nearbyItem = null;

    void Awake()
    {
        inputActions = new PlayerInputActions();
        locomotion = GetComponent<Locomotion>();
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

        // 무기 교체 입력 이벤트를 구독함.
        inputActions.Player.EquipRanged.performed += _ => EquipWeapon(WeaponMode.Ranged);
        inputActions.Player.EquipMelee.performed += _ => EquipWeapon(WeaponMode.Melee);

        // 재시작 입력 이벤트를 구독함.
        inputActions.Player.Restart.performed += OnRestartPerformed;

        // 상호작용 입력 이벤트를 구독함.
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

    private void OnFireStarted(InputAction.CallbackContext context) => isFireButtonPressed = true;
    private void OnFireCanceled(InputAction.CallbackContext context) => isFireButtonPressed = false;
    
    private void OnReloadPerformed(InputAction.CallbackContext context)
    {
        // 원거리 무기 모드일 경우에만 장전을 수행함.
        if (currentMode == WeaponMode.Ranged && rangedWeapon != null)
        {
            rangedWeapon.TryReload();
        }
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;

        // 할당된 오브젝트에서 컴포넌트를 가져와 초기화함.
        if (rangedWeaponObject != null) rangedWeapon = rangedWeaponObject.GetComponent<Weapon>();
        if (meleeWeaponObject != null) meleeWeapon = meleeWeaponObject.GetComponent<MeleeWeapon>();

        if (rangedWeapon != null) rangedWeapon.shooterTag = "Player";
        if (meleeWeapon != null) meleeWeapon.shooterTag = "Player";

        // 게임 시작 시 기본 무기를 원거리 무기로 설정함.
        EquipWeapon(WeaponMode.Ranged);
    }

    // 무기 교체 로직을 수행하는 함수임.
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

    void Update()
    {
        Vector2 inputVector = inputActions.Player.Move.ReadValue<Vector2>();
        moveInput = new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        AimAtMouse();

        if (isFireButtonPressed)
        {
            // 현재 무기 모드에 따라 적절한 공격 함수를 호출함.
            if (currentMode == WeaponMode.Ranged && rangedWeapon != null)
            {
                rangedWeapon.TryFire();
            }
            else if (currentMode == WeaponMode.Melee && meleeWeapon != null)
            {
                meleeWeapon.TryAttack();
            }
        }
    }

    void FixedUpdate()
    {
        locomotion.Move(moveInput);
    } 

    void AimAtMouse()
    {
        if (Mouse.current == null) return;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        Plane aimPlane = new Plane(Vector3.up, transform.position); 
        float rayDistance;

        if (aimPlane.Raycast(ray, out rayDistance))
        {
            Vector3 point = ray.GetPoint(rayDistance);
            transform.LookAt(point); 
        }
    }

    // 상호작용(F) 키 입력 시 호출되는 함수임.
    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        if (nearbyItem != null && nearbyItem.itemData != null)
        {
            // 1. 플레이어 자신의 InventoryManager 컴포넌트를 가져옴.
            InventoryManager inventory = GetComponent<InventoryManager>();
            
            if (inventory != null)
            {
                // 2. 가방에 아이템 추가를 시도함.
                bool isAdded = inventory.AddItem(nearbyItem.itemData, nearbyItem.amount);
                
                // 3. 가방에 무사히 들어갔다면(가방이 꽉 차지 않았다면) 바닥의 아이템을 파괴함.
                if (isAdded)
                {
                    Destroy(nearbyItem.gameObject);
                    nearbyItem = null;
                }
            }
        }
    }

    public void SetNearbyItem(PickupItem item) => nearbyItem = item;
    
    public void ClearNearbyItem(PickupItem item)
    {
        if (nearbyItem == item) nearbyItem = null;
    }

    // 데이터 타입(Ranged/Melee)을 구분하여 해당 무기에 데이터를 덮어씌움.
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



    private void OnRestartPerformed(InputAction.CallbackContext context)
    {
        // 현재 활성화된 씬의 이름을 가져와 해당 씬을 다시 로드함.
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
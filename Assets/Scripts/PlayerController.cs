using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{

    private Rigidbody rb;
    private Camera mainCamera;
    private Vector3 moveInput;

    private PlayerInputActions inputActions;

    private Weapon myWeapon;
    private Locomotion locomotion;
    private bool isFireButtonPressed = false;

    void Awake()
    {
        inputActions = new PlayerInputActions();
        
        myWeapon = GetComponent<Weapon>();
        locomotion = GetComponent<Locomotion>();
    }

    void OnEnable()
    {
        inputActions.Enable();
        
        // 사격 및 장전 이벤트
        inputActions.Player.Fire.started += OnFireStarted;
        inputActions.Player.Fire.canceled += OnFireCanceled;
        inputActions.Player.Reload.performed += OnReloadPerformed;

        inputActions.Player.Sprint.started += _ => locomotion.SetSprinting(true);
        inputActions.Player.Sprint.canceled += _ => locomotion.SetSprinting(false);
        inputActions.Player.Roll.performed += _ => locomotion.TryRoll(moveInput);
    }

    void OnDisable()
    {
        inputActions.Disable();
        
        inputActions.Player.Fire.started -= OnFireStarted;
        inputActions.Player.Fire.canceled -= OnFireCanceled;
        inputActions.Player.Reload.performed -= OnReloadPerformed;
        
        inputActions.Player.Sprint.started -= _ => locomotion.SetSprinting(true);
        inputActions.Player.Sprint.canceled -= _ => locomotion.SetSprinting(false);
    }

    private void OnFireStarted(InputAction.CallbackContext context) => isFireButtonPressed = true;
    private void OnFireCanceled(InputAction.CallbackContext context) => isFireButtonPressed = false;
    private void OnReloadPerformed(InputAction.CallbackContext context) => myWeapon.TryReload();

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;

        if (myWeapon != null) myWeapon.shooterTag = "Player";
    }

    void Update()
    {
        // 이동 입력 읽기
        Vector2 inputVector = inputActions.Player.Move.ReadValue<Vector2>();
        moveInput = new Vector3(inputVector.x, 0f, inputVector.y).normalized;

        AimAtMouse();

        if (isFireButtonPressed)
        {
            myWeapon.TryFire();
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
}
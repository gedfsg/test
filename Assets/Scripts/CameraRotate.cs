using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Q/E로 카메라를 90도씩 스냅 회전 (0/90/180/270도), 0.3초 동안 부드럽게 보간.
/// CameraFollow.yawOffset을 조절함. 캐릭터 이동은 PlayerController에서
/// 카메라 방향 기준으로 계산되므로 회전해도 항상 화면 기준(위=W)으로 이동함.
/// </summary>
public class CameraRotate : MonoBehaviour
{
    [SerializeField] private float rotateDuration = 0.3f;

    private CameraFollow cameraFollow;
    private PlayerInputActions inputActions;

    private float fromYaw;
    private float targetYaw;
    private float rotateStartTime = -999f;
    private bool rotating;

    void Awake()
    {
        cameraFollow = GetComponent<CameraFollow>();
        inputActions = new PlayerInputActions();
        targetYaw = cameraFollow.yawOffset;
    }

    void OnEnable()
    {
        inputActions.Enable();
        inputActions.Player.RotateLeft.performed += OnRotateLeft;
        inputActions.Player.RotateRight.performed += OnRotateRight;
    }

    void OnDisable()
    {
        inputActions.Player.RotateLeft.performed -= OnRotateLeft;
        inputActions.Player.RotateRight.performed -= OnRotateRight;
        inputActions.Disable();
    }

    private void OnRotateLeft(InputAction.CallbackContext ctx) => StartRotate(-90f);
    private void OnRotateRight(InputAction.CallbackContext ctx) => StartRotate(90f);

    private void StartRotate(float delta)
    {
        if (rotating) return; // 회전 중에는 추가 입력 무시 (Q/E 연타/겹침 방지)

        fromYaw = cameraFollow.yawOffset;
        targetYaw += delta;
        rotateStartTime = Time.time;
        rotating = true;
    }

    void Update()
    {
        if (!rotating) return;

        float t = (Time.time - rotateStartTime) / rotateDuration;
        if (t >= 1f)
        {
            t = 1f;
            rotating = false;
        }
        cameraFollow.yawOffset = Mathf.Lerp(fromYaw, targetYaw, t);
    }
}

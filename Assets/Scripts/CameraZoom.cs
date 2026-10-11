using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마우스 스크롤로 줌인/줌아웃 + 우클릭 홀드로 순간 확대.
/// CameraFollow의 distance(월드 단위 거리)를 조절해 캐릭터를 항상 중심에 유지.
/// </summary>
public class CameraZoom : MonoBehaviour
{
    [Header("줌 거리 범위 (월드 단위)")]
    [SerializeField] private float minDistance = 20f;
    [SerializeField] private float maxDistance = 80f;
    [SerializeField] private float defaultDistance = 40f;

    [Header("줌 속도")]
    [SerializeField] private float zoomStep = 3f;        // 스크롤 1회당 변화량
    [SerializeField] private float zoomSmoothSpeed = 8f;  // 보간 속도

    [Header("우클릭 홀드 확대 비율 (0.12 = 12% 확대)")]
    [SerializeField, Range(0.1f, 0.15f)] private float aimZoomRatio = 0.12f;

    [Header("건물 실내 진입 시 줌 거리 (BuildingInterior에서 호출)")]
    [SerializeField] private float indoorDistance = 33f;

    private CameraFollow cameraFollow;
    private PlayerInputActions inputActions;

    private float baseTargetDistance; // 스크롤로 조절되는 기준 거리
    private bool aimZoomHeld;

    // 건물 여러 개가 붙어있어 트리거가 겹칠 수 있어서 bool 대신 카운터로 관리.
    // (bool이면 A 건물 안에서 B 건물에도 겹쳐 들어간 뒤 A만 나가도 실외로 잘못 풀리거나,
    //  반대로 실내 상태가 안 풀리고 계속 남는 버그가 생김 - Fog 처리와 동일한 패턴)
    private int indoorCount;
    private bool isIndoor => indoorCount > 0;

    public void SetIndoor(bool indoor)
    {
        indoorCount = indoor ? indoorCount + 1 : Mathf.Max(0, indoorCount - 1);
    }

    // 개발 중 도구 재실행 등으로 상태가 꼬였을 때 강제로 실외 상태로 되돌림
    public void ForceOutdoor()
    {
        indoorCount = 0;
        baseTargetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        if (cameraFollow != null) cameraFollow.distance = baseTargetDistance;
    }

    void Awake()
    {
        cameraFollow = GetComponent<CameraFollow>();
        inputActions = new PlayerInputActions();

        baseTargetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
        cameraFollow.distance = baseTargetDistance; // 시작 시 즉시 적용
    }

    void OnEnable()
    {
        inputActions.Enable();
        inputActions.Player.AimZoom.started += OnAimZoomStarted;
        inputActions.Player.AimZoom.canceled += OnAimZoomCanceled;
    }

    void OnDisable()
    {
        inputActions.Player.AimZoom.started -= OnAimZoomStarted;
        inputActions.Player.AimZoom.canceled -= OnAimZoomCanceled;
        inputActions.Disable();
    }

    private void OnAimZoomStarted(InputAction.CallbackContext ctx) => aimZoomHeld = true;
    private void OnAimZoomCanceled(InputAction.CallbackContext ctx) => aimZoomHeld = false;

    void Update()
    {
        float scrollY = inputActions.Player.Zoom.ReadValue<float>();
        if (scrollY != 0f)
        {
            // 위로 스크롤 → 줌인(거리 감소), 아래로 → 줌아웃(거리 증가)
            baseTargetDistance -= (scrollY > 0f ? 1f : -1f) * zoomStep;
            baseTargetDistance = Mathf.Clamp(baseTargetDistance, minDistance, maxDistance);
        }

        float targetDistance = isIndoor ? indoorDistance
            : aimZoomHeld ? baseTargetDistance * (1f - aimZoomRatio) : baseTargetDistance;

        // 부드러운 줌 보간
        cameraFollow.distance = Mathf.Lerp(cameraFollow.distance, targetDistance, Time.deltaTime * zoomSmoothSpeed);
    }
}

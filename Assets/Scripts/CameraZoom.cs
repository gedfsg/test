using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마우스 스크롤로 줌인/줌아웃.
/// CameraFollow의 zoomMultiplier를 조절해 캐릭터를 항상 중심에 유지.
/// </summary>
public class CameraZoom : MonoBehaviour
{
    [Header("줌 범위")]
    [SerializeField] private float minZoom = 0.5f;  // 최대 줌인
    [SerializeField] private float maxZoom = 2.5f;  // 최대 줌아웃

    [Header("줌 속도")]
    [SerializeField] private float zoomStep        = 0.2f;  // 스크롤 1회당 변화량
    [SerializeField] private float zoomSmoothSpeed = 8f;    // 보간 속도

    private CameraFollow cameraFollow;
    private float targetZoom = 1.3f; // 시작 줌 레벨 (1보다 크면 더 멀리서 시작)

    void Awake()
    {
        cameraFollow = GetComponent<CameraFollow>();
        // 시작 시 즉시 목표 줌 레벨로 설정
        cameraFollow.zoomMultiplier = targetZoom; // 시작 시 즉시 적용
    }

    void Update()
    {
        if (Mouse.current == null) return;

        float scrollY = Mouse.current.scroll.ReadValue().y;
        if (scrollY != 0f)
        {
            // 위로 스크롤 → 줌인 (multiplier 감소), 아래로 → 줌아웃 (multiplier 증가)
            targetZoom -= (scrollY > 0f ? 1f : -1f) * zoomStep;
            targetZoom  = Mathf.Clamp(targetZoom, minZoom, maxZoom);
        }

        // 부드러운 줌 보간
        cameraFollow.zoomMultiplier = Mathf.Lerp(
            cameraFollow.zoomMultiplier, targetZoom, Time.deltaTime * zoomSmoothSpeed);
    }
}

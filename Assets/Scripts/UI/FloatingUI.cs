using UnityEngine;

public class FloatingUI : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform target;           // 추적할 대상 (Player)
    public Vector3 offset = new Vector3(1.2f, 2.5f, 0f); // 머리 오른쪽 위 위치

    [Header("카메라 기준 위치 (2026-11-21)")]
    [Tooltip("켜면 offset(월드 고정) 대신 '머리 높이 + 화면 오른쪽'에 놓음 → 카메라를 Q/E로 돌려도 화면에서 늘 머리 옆. ReloadUI가 자동으로 켬")]
    public bool cameraRelative = false;
    [Tooltip("발밑 기준 높이 (m)")]
    public float headHeight = 1.9f;
    [Tooltip("화면 오른쪽으로 떨어진 거리 (m)")]
    public float sideOffset = 0.8f;

    private Transform mainCameraTransform;

    void Start()
    {
        // 메인 카메라의 트랜스폼을 미리 찾아둔다.
        if (Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null || mainCameraTransform == null) return;

        // 1. 위치 추적 (대상 위치 + 오프셋)
        if (cameraRelative)
        {
            Vector3 right = mainCameraTransform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            transform.position = target.position + Vector3.up * headHeight + right * sideOffset;
        }
        else
            transform.position = target.position + offset;

        // 2. 빌보드 (카메라 정면 바라보기)
        transform.rotation = mainCameraTransform.rotation;
    }
}
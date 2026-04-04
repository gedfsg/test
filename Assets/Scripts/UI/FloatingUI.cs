using UnityEngine;

public class FloatingUI : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform target;           // 추적할 대상 (Player)
    public Vector3 offset = new Vector3(1.2f, 2.5f, 0f); // 머리 오른쪽 위 위치

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
        transform.position = target.position + offset;

        // 2. 빌보드 (카메라 정면 바라보기)
        transform.rotation = mainCameraTransform.rotation;
    }
}
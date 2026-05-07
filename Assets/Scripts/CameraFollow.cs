using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("카메라 오프셋 (플레이어 기준 상대 위치)")]
    public Vector3 baseOffset = new Vector3(0f, 18f, -14f);

    // CameraZoom이 이 값을 조절해서 줌인/줌아웃
    [HideInInspector] public float zoomMultiplier = 1f;

    void LateUpdate()
    {
        if (target == null) return;

        // 오프셋 방향은 유지하고 거리만 zoomMultiplier로 조절
        transform.position = target.position + baseOffset * zoomMultiplier;

        // 항상 플레이어를 바라봄 → 줌해도 캐릭터가 화면 중앙 유지
        transform.LookAt(target.position + Vector3.up * 1f);
    }
}

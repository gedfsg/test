using UnityEngine;

/// <summary>
/// 미니맵 카메라를 플레이어 위치로 따라오게 하고, 메인 카메라(Q/E 회전)와 같은 방향으로 지도를 돌림.
///  - 지도: 화면 위쪽 = 메인 카메라가 보는 방향 (화면에서 W로 가는 방향과 미니맵 위쪽이 항상 일치)
///  - 플레이어 마커(삼각형): 가운데 위쪽 고정 (markerFollowsPlayerHeading을 켜면 캐릭터가 보는 방향으로 회전)
///  - N 배지: 링 위를 돌면서 항상 실제 북쪽(+Z)을 가리킴 (지도와 반대 방향으로 회전), 글자는 똑바로 유지
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("따라갈 대상 (비워두면 Player 태그로 자동 탐색)")]
    public Transform target;

    [Header("플레이어 머리 위 높이 (오소그래픽 미니맵 카메라)")]
    public float height = 80f;

    [Header("미니맵 UI 플레이어 마커 (화면 고정)")]
    public RectTransform playerMarker;

    [Tooltip("켜면 마커가 캐릭터가 보는 방향으로 회전. 끄면 항상 위쪽(카메라 방향)을 가리킴")]
    public bool markerFollowsPlayerHeading = false;

    [Header("카메라 회전 동기화")]
    [Tooltip("메인 카메라의 좌우 회전(Q/E)을 따라 지도를 돌림. 끄면 항상 북쪽이 위")]
    public bool rotateWithMainCamera = true;

    [Tooltip("실제 북쪽을 표시하는 N 배지 (미니맵 중심 기준으로 링 위를 이동)")]
    public RectTransform northBadge;

    [Tooltip("N 배지가 도는 반지름 (미니맵 중심 기준, UI px)")]
    public float northBadgeRadius = 89f;

    void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        transform.position = new Vector3(target.position.x, target.position.y + height, target.position.z);

        // 메인 카메라가 수평으로 보는 방향의 각도 (0 = 북쪽 +Z, 90 = 동쪽 +X)
        float camYaw = 0f;
        Camera main = Camera.main;
        if (rotateWithMainCamera && main != null)
        {
            Vector3 f = main.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) camYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        // 위에서 내려다보는 카메라(90°)를 camYaw만큼 돌림 → 미니맵 위쪽 = 메인 카메라 방향
        transform.rotation = Quaternion.Euler(90f, camYaw, 0f);

        if (playerMarker != null)
        {
            float z = markerFollowsPlayerHeading ? -(target.eulerAngles.y - camYaw) : 0f;
            playerMarker.localEulerAngles = new Vector3(0f, 0f, z);
        }

        // 북쪽은 미니맵 위쪽에서 반시계로 camYaw만큼 돌아간 곳 (예: 카메라가 동쪽을 보면 북쪽은 왼쪽)
        if (northBadge != null)
        {
            float rad = camYaw * Mathf.Deg2Rad;
            northBadge.anchoredPosition = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)) * northBadgeRadius;
            northBadge.localEulerAngles = Vector3.zero; // 글자는 항상 똑바로
        }
    }
}

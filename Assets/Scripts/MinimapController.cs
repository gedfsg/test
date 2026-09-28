using UnityEngine;

/// <summary>
/// 미니맵 카메라를 플레이어 위치로 따라오게 하고, UI 플레이어 마커를 진행 방향으로 회전시킴.
/// 맵 자체는 항상 북쪽이 위(고정), 마커만 회전하는 방식.
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("따라갈 대상 (비워두면 Player 태그로 자동 탐색)")]
    public Transform target;

    [Header("플레이어 머리 위 높이 (오소그래픽 미니맵 카메라)")]
    public float height = 80f;

    [Header("미니맵 UI 플레이어 마커 (화면 고정, 진행 방향으로 회전)")]
    public RectTransform playerMarker;

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

        if (playerMarker != null)
            playerMarker.localEulerAngles = new Vector3(0f, 0f, -target.eulerAngles.y);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라와 플레이어 사이의 오브젝트를 숨김 (렌더러 OFF 방식).
/// 커스텀 셰이더(Polygon City 등) 호환을 위해 알파 페이드 대신 렌더러 토글 사용.
/// 카메라에 붙이고, 가릴 오브젝트는 "Obstacle" 레이어로 설정.
/// </summary>
public class CameraObstacleFader : MonoBehaviour
{
    [Header("타겟 (플레이어)")]
    public Transform target;
    public string playerTag = "Player";

    [Header("가릴 수 있는 오브젝트 레이어")]
    public LayerMask obstacleLayer = ~0;

    [Header("레이 설정")]
    [Tooltip("레이 두께 (캐릭터가 살짝 가려져도 인식)")]
    public float sphereRadius = 0.5f;

    [Tooltip("플레이어 위로 여유 거리 (머리 위까지 체크)")]
    public float playerHeightOffset = 1f;

    // 이번 프레임 가려진 렌더러들
    private readonly HashSet<Renderer> currentlyHidden = new HashSet<Renderer>();
    private readonly HashSet<Renderer> lastFrameHidden = new HashSet<Renderer>();

    void Start()
    {
        if (target == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag(playerTag);
            if (p != null) target = p.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 이전 프레임 데이터 보관, 현재 프레임 초기화
        lastFrameHidden.Clear();
        foreach (var r in currentlyHidden) lastFrameHidden.Add(r);
        currentlyHidden.Clear();

        // 카메라 → 플레이어(약간 위쪽) 스피어캐스트
        Vector3 targetPos = target.position + Vector3.up * playerHeightOffset;
        Vector3 dir   = targetPos - transform.position;
        float   dist  = dir.magnitude;
        if (dist < 0.01f) return;

        RaycastHit[] hits = Physics.SphereCastAll(
            transform.position, sphereRadius, dir.normalized, dist, obstacleLayer);

        foreach (var hit in hits)
        {
            // 플레이어 자신은 무시
            if (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))
                continue;

            // 콜라이더 + 그 자식들의 모든 Renderer 숨김
            Renderer[] rends = hit.collider.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (!currentlyHidden.Contains(r))
                {
                    r.enabled = false;
                    currentlyHidden.Add(r);
                }
            }
        }

        // 이전엔 가려졌는데 이번엔 안 가려진 것 → 다시 보이게
        foreach (var r in lastFrameHidden)
        {
            if (r != null && !currentlyHidden.Contains(r))
                r.enabled = true;
        }
    }

    void OnDisable()
    {
        // 비활성화 시 모두 복구
        foreach (var r in currentlyHidden)
            if (r != null) r.enabled = true;
        currentlyHidden.Clear();
    }
}

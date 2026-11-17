using UnityEngine;

/// <summary>
/// 낙하 복구 (2026-11-12). 플레이어가 맵 밖으로 떨어지거나 강바닥에 빠지면 마지막 안전 지점으로 되돌림.
///
///  - 안전 지점: 0.25초마다, 발밑에 바닥이 있고(트리거 제외) 물속이 아니고 낙하 중이 아닐 때 위치를 기록
///  - 복구 조건 1: y &lt; fallY (기본 -5) → 즉시
///  - 복구 조건 2: 강바닥(WaterZone 물속 + 수면보다 riverDepth 이상 아래)에 riverSeconds 이상 머묾
///    (이 맵의 강바닥은 y≈0이라 조건 1로는 못 잡음. 다리 옆에서 떨어지면 강바닥에 갇혔음)
/// PlayerController.Awake가 자동으로 붙임.
/// </summary>
[DisallowMultipleComponent]
public class FallSafetyNet : MonoBehaviour
{
    [Tooltip("이 높이보다 아래로 떨어지면 즉시 복구")]
    public float fallY = -5f;
    [Tooltip("수면보다 이만큼 아래(강바닥)에 있으면 강에 빠진 것으로 봄")]
    public float riverDepth = 3f;
    [Tooltip("강바닥에 이 시간 이상 있으면 복구")]
    public float riverSeconds = 2f;

    Rigidbody rb;
    Vector3 lastSafe;
    bool hasSafe;
    float sampleTimer, riverTimer;

    /// <summary>복구 횟수 (테스트·로그용)</summary>
    public int RecoverCount { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        lastSafe = transform.position;
    }

    void FixedUpdate()
    {
        Vector3 p = transform.position;

        bool inRiver = WaterZone.TryGetSurface(out float surfaceY) && p.y < surfaceY - riverDepth && WaterZone.IsUnderwater(p);
        riverTimer = inRiver ? riverTimer + Time.fixedDeltaTime : 0f;

        if (p.y < fallY || riverTimer >= riverSeconds)
        {
            Recover(p.y < fallY ? "맵 아래로 떨어짐" : "강바닥에 빠짐");
            return;
        }

        sampleTimer += Time.fixedDeltaTime;
        if (sampleTimer < 0.25f) return;
        sampleTimer = 0f;
        if (inRiver || p.y < fallY + 1f) return;
        if (rb != null && rb.linearVelocity.y < -2f) return; // 떨어지는 중
        if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore)) return;
        if (hit.collider.transform.IsChildOf(transform)) return;
        if (WaterZone.IsUnderwater(p)) return;
        lastSafe = hit.point;
        hasSafe = true;
    }

    void Recover(string reason)
    {
        Vector3 to = (hasSafe ? lastSafe : transform.position) + Vector3.up * 0.3f;
        if (rb != null) { rb.position = to; rb.linearVelocity = Vector3.zero; }
        transform.position = to;
        riverTimer = 0f;
        RecoverCount++;
        Debug.Log($"[FallSafetyNet] {reason} → 마지막 안전 지점 {to} 으로 복구");
    }
}

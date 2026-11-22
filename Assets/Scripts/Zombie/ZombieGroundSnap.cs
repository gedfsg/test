using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비 발 높이 보정 (2026-11-22). 피드백 "좀비들이 일부 바닥에 빠지는 곳이 있음".
/// 원인: 좀비(NavMeshAgent)는 NavMesh 표면 높이에 서는데, NavMesh는 복셀 근사라 실제 바닥보다 낮은 곳이 있음.
///  - 대부분(도로 가장자리 0.35~0.6m 등)은 NavMesh "Height Mesh"를 켜고 다시 구워서 해결 (시험 에이전트 31,000곳 기준 빠짐 1.2% → 0.1%)
///  - 남은 곳: 학교 앞 단상 바닥(0.28m), 굽고 나서 런타임에 생기는 보급 상자 등
/// 하는 일: checkInterval마다 발 위 rayStart(m)에서 아래로 레이 1개 → 실제 바닥이 에이전트 위치보다 높으면
///  그만큼 agent.baseOffset을 올림 (최대 maxLift, 부드럽게). 바닥이 같거나 낮으면 0으로 되돌림.
///  레이 시작이 낮아서(0.6m) 탁자·차 지붕처럼 높은 물체 위로 올라서지는 않음.
/// ZombieController.Awake가 붙임.
/// </summary>
[DisallowMultipleComponent]
public class ZombieGroundSnap : MonoBehaviour
{
    [Tooltip("발 위 이 높이에서 아래로 레이 (m). 이보다 높은 물체 위로는 안 올라감")]
    public float rayStart = 0.6f;
    [Tooltip("최대로 올릴 높이 (m)")]
    public float maxLift = 0.6f;
    [Tooltip("이 차이 이하면 무시 (m)")]
    public float tolerance = 0.04f;
    [Tooltip("검사 간격 (초)")]
    public float checkInterval = 0.1f;
    [Tooltip("높이를 따라가는 속도 (m/s)")]
    public float followSpeed = 3f;

    NavMeshAgent agent;
    ZombieController zombie;
    float targetOffset, nextCheck;
    static readonly RaycastHit[] hits = new RaycastHit[8];
    static int mask = -1;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        zombie = GetComponent<ZombieController>();
        if (mask == -1)
            mask = ~((1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("Water")));
        nextCheck = Random.value * checkInterval; // 좀비마다 검사 시점을 흩뜨림
    }

    void LateUpdate()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (zombie != null && zombie.IsDead) { agent.baseOffset = 0f; return; }

        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + checkInterval;
            // 에이전트의 NavMesh 위치 = transform - baseOffset
            Vector3 navPos = transform.position - Vector3.up * agent.baseOffset;
            float ground = float.NegativeInfinity;
            int n = Physics.RaycastNonAlloc(navPos + Vector3.up * rayStart, Vector3.down, hits, rayStart + 0.3f, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || hits[i].distance < 0.001f) continue;                 // 레이 시작이 콜라이더 안
                if (c.transform.IsChildOf(transform) || c.GetComponentInParent<ZombieController>() != null) continue; // 자기·다른 좀비
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // 굴러다니는 물체
                if (hits[i].normal.y < 0.6f) continue;                                // 벽면
                if (hits[i].point.y > ground) ground = hits[i].point.y;
            }
            float gap = ground - navPos.y;
            targetOffset = gap > tolerance ? Mathf.Min(gap, maxLift) : 0f;
        }
        if (!Mathf.Approximately(agent.baseOffset, targetOffset))
            agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, targetOffset, followSpeed * Time.deltaTime);
    }
}

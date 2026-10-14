using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 학습 씬 배선 진단용. 좀비가 안 움직일 때 어느 고리가 끊겼는지 찾는다.
///
/// 사용법: 학습 씬의 좀비 하나에 이 컴포넌트를 붙이고 Play → Console 확인.
/// 원인을 찾은 뒤에는 제거할 것.
///
/// 움직임 사슬:
///   Heuristic → OnActionReceived → SetActions → Think → SetDestination → velocity
/// 각 단계의 값을 찍어서, 0이 되는 첫 지점이 범인이다.
/// </summary>
[RequireComponent(typeof(ZombieController))]
public class ZombieWiringDebug : MonoBehaviour
{
    [Tooltip("로그 간격(초)")]
    public float interval = 1f;

    private ZombieController body;
    private ZombieMLBrain    mlBrain;
    private ZombieAgent      agent;
    private NavMeshAgent     nav;
    private float timer;

    void Start()
    {
        body    = GetComponent<ZombieController>();
        mlBrain = GetComponent<ZombieMLBrain>();
        agent   = GetComponent<ZombieAgent>();
        nav     = GetComponent<NavMeshAgent>();

        // ── 컴포넌트 존재 확인 ──
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[배선진단] {name} 초기 상태");
        sb.AppendLine($"  ZombieController : {(body    != null ? "OK" : "❌ 없음")}");
        sb.AppendLine($"  ZombieMLBrain    : {(mlBrain != null ? "OK" : "❌ 없음")}");
        sb.AppendLine($"  ZombieAgent      : {(agent   != null ? "OK" : "❌ 없음")}");
        sb.AppendLine($"  NavMeshAgent     : {(nav     != null ? "OK" : "❌ 없음")}");
        sb.AppendLine($"  DecisionRequester: {(GetComponent<Unity.MLAgents.DecisionRequester>() != null ? "OK" : "❌ 없음 → 결정 요청이 안 감")}");

        var bp = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        if (bp == null)
        {
            sb.AppendLine($"  BehaviorParameters: ❌ 없음");
        }
        else
        {
            sb.AppendLine($"  BehaviorType     : {bp.BehaviorType}");
            sb.AppendLine($"  관측 크기        : {bp.BrainParameters.VectorObservationSize} (기대값 {ZombieAgent.ObservationSize})");
            sb.AppendLine($"  연속 행동 수     : {bp.BrainParameters.ActionSpec.NumContinuousActions} (기대값 2)");
            sb.AppendLine($"  모델             : {(bp.Model != null ? bp.Model.name : "없음")}");
        }

        sb.AppendLine($"  Controller.brain : {(body.brain != null ? body.brain.GetType().Name : "❌ null → FSM으로 폴백됨")}");
        sb.AppendLine($"  targetOverride   : {(body.targetOverride != null ? body.targetOverride.name : "없음 (Player 태그로 탐색)")}");

        Debug.Log(sb.ToString());
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = interval;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[배선진단] {name}");

        // 1단계: 에이전트가 돌고 있나
        if (agent != null)
        {
            sb.AppendLine($"  1. Heuristic 호출 {agent.heuristicCallCount}회 / " +
                          $"OnActionReceived {agent.actionCallCount}회 / 에피소드 {agent.episodeCount}회");
            if (agent.heuristicCallCount == 0 && agent.actionCallCount == 0)
                sb.AppendLine($"     ❌ 에이전트가 전혀 안 돌고 있음 → DecisionRequester / Academy 문제");
            sb.AppendLine($"  2. 받은 이동 행동 : {agent.lastMoveAction}");
            if (agent.actionCallCount > 0 && agent.lastMoveAction.sqrMagnitude < 0.0025f)
                sb.AppendLine($"     ❌ 행동이 0 → Heuristic이 타겟을 못 보고 있음 (아래 3번 확인)");
        }

        // 2단계: 관측
        if (mlBrain != null)
        {
            sb.AppendLine($"  3. 관측 있음 : {mlBrain.HasObservation}");
            if (mlBrain.HasObservation)
            {
                var o = mlBrain.LastObservation;
                sb.AppendLine($"     타겟 있음 : {o.hasTarget} / 직접 보임 : {o.targetVisible}");
                sb.AppendLine($"     믿음 있음 : {o.hasBelief} / 믿는 거리 : {o.believedDistance:F1} / 경과 : {o.timeSinceSeen:F1}s");
                sb.AppendLine($"     믿는 방향 : {o.believedDirLocal} / canMove : {o.canMove}");
                if (!o.hasTarget)
                    sb.AppendLine($"     ❌ 타겟이 없음 → targetOverride 또는 Player 태그 확인");
            }
            else
            {
                sb.AppendLine($"     ❌ Think가 한 번도 안 불림 → ZombieController.Update 미동작");
            }
            sb.AppendLine($"  4. 사용 중인 brain : {body.State} 상태");
        }

        // 3단계: 실제 이동
        if (nav != null)
        {
            sb.AppendLine($"  5. isOnNavMesh : {nav.isOnNavMesh} / enabled : {nav.enabled}");
            if (nav.isOnNavMesh)
            {
                sb.AppendLine($"     목적지 : {nav.destination} / 남은거리 : {nav.remainingDistance:F1}");
                sb.AppendLine($"     속도 : {nav.velocity.magnitude:F2} / agent.speed : {nav.speed:F2}");
                sb.AppendLine($"     경로상태 : {nav.pathStatus} / hasPath : {nav.hasPath} / pathPending : {nav.pathPending}");
                if (nav.pathStatus == NavMeshPathStatus.PathPartial || nav.pathStatus == NavMeshPathStatus.PathInvalid)
                    sb.AppendLine($"     ❌ 목적지까지 경로가 없음 → 다른 구역을 쫓고 있거나 NavMesh가 끊김");
                if (nav.isStopped)
                    sb.AppendLine($"     ❌ agent.isStopped = true");
            }
            else
            {
                sb.AppendLine($"     ❌ NavMesh 위에 없음 → 이동 불가");
            }
        }

        Debug.Log(sb.ToString());
    }
}

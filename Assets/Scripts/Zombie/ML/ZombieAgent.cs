using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ML-Agents 쪽 창구. 관측을 모으고, 행동을 받아 ZombieMLBrain에 넘기고, 보상을 준다.
///
/// 인스펙터 설정 (Behavior Parameters 컴포넌트):
///   Behavior Name           : Zombie
///   Vector Observation Space Size : 10   ← ZombieObservation.VectorLength
///   Continuous Actions      : 2          ← 이동 (x, z) 로컬 기준
///   Discrete Branches       : 1, Size 2  ← 공격 안함 / 공격
///
/// 함께 붙일 컴포넌트: Decision Requester (Decision Period 5 권장)
/// </summary>
[RequireComponent(typeof(ZombieController))]
[RequireComponent(typeof(ZombieMLBrain))]
public class ZombieAgent : Agent
{
    [Header("보상")]
    [Tooltip("타겟과 가까워진 거리 1m당 보상")]
    public float approachReward = 0.05f;
    [Tooltip("타겟이 시야에 보이는 동안 매 결정마다 주는 보상")]
    public float visibleReward = 0.002f;
    [Tooltip("포획 성공 보상 (ZombieTrainingArena가 호출)")]
    public float captureReward = 1.0f;
    [Tooltip("시간 압박. MaxStep에 걸쳐 총 -1이 되도록 자동 분배된다.")]
    public bool useTimePenalty = true;

    [Header("참조")]
    public ZombieTrainingArena arena;

    private ZombieController body;
    private ZombieMLBrain     mlBrain;
    private float             prevDistance = -1f;

    public override void Initialize()
    {
        body    = GetComponent<ZombieController>();
        mlBrain = GetComponent<ZombieMLBrain>();

        if (arena == null) arena = GetComponentInParent<ZombieTrainingArena>();

        // Controller가 FSM 대신 이 브레인을 쓰도록
        if (body.brain == null) body.brain = mlBrain;
    }

    public override void OnEpisodeBegin()
    {
        prevDistance = -1f;
        if (arena != null) arena.ResetArena();
        mlBrain.OnResetEpisode();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 브레인이 아직 한 번도 Think되지 않았으면 0으로 채운다.
        // (관측 길이가 들쭉날쭉하면 ML-Agents가 바로 에러를 낸다)
        if (!mlBrain.HasObservation)
        {
            for (int i = 0; i < ZombieObservation.VectorLength; i++) sensor.AddObservation(0f);
            return;
        }

        var buffer = new float[ZombieObservation.VectorLength];
        mlBrain.LastObservation.ToVector(buffer);
        foreach (float v in buffer) sensor.AddObservation(v);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var move = new Vector2(
            Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f),
            Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f));

        bool attack = actions.DiscreteActions[0] == 1;

        mlBrain.SetActions(move, attack);

        ApplyRewards();
    }

    private void ApplyRewards()
    {
        if (useTimePenalty && MaxStep > 0)
            AddReward(-1f / MaxStep);

        if (!mlBrain.HasObservation) return;
        var obs = mlBrain.LastObservation;
        if (!obs.hasTarget) return;

        // 거리 변화 보상. 첫 결정에는 비교 대상이 없으므로 건너뛴다.
        if (prevDistance >= 0f)
        {
            float delta = prevDistance - obs.distance;
            AddReward(delta * approachReward);
        }
        prevDistance = obs.distance;

        // 시야 유지에 약한 보상 → 차폐를 돌아가는 행동이 학습될 여지
        if (obs.targetVisible) AddReward(visibleReward);
    }

    /// <summary>ZombieTrainingArena가 포획 판정 시 호출.</summary>
    public void OnCaptured()
    {
        AddReward(captureReward);
        EndEpisode();
    }

    /// <summary>
    /// 학습 없이 동작을 확인할 때 쓰는 수동 정책.
    /// Behavior Type을 Heuristic Only로 두면 이게 돌아간다.
    /// 단순 직선 추격이라, 학습된 정책의 비교 기준점으로도 쓸 수 있다.
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete   = actionsOut.DiscreteActions;

        continuous[0] = 0f;
        continuous[1] = 0f;
        discrete[0]   = 0;

        if (!mlBrain.HasObservation) return;
        var obs = mlBrain.LastObservation;
        if (!obs.hasTarget) return;

        continuous[0] = obs.toTargetLocal.x;
        continuous[1] = obs.toTargetLocal.z;
        discrete[0]   = obs.InAttackRange ? 1 : 0;
    }
}

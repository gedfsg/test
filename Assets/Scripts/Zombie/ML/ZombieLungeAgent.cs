using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// 노리는 좀비 —— '언제 돌진할지'만 강화학습이 정한다.
///
/// 움직임은 그대로다: 쫓는 건 FSM 뇌, 어슬렁·'왁!'·돌진 동작은 ZombieQuirks가 한다.
/// 이 에이전트는 0.1초마다 "지금 돌진할까?" 하나만 고른다 (이산 1분기, 0 = 기다림 / 1 = 돌진).
/// 돌진을 할 수 없는 때(안 보임, 쿨다운 중, 너무 멂/가까움)에는 1을 막아서(행동 마스크)
/// 정말로 고를 수 있는 순간의 선택만 배운다.
///
/// 배울 만한 틈 (규칙 5~9m에는 없는 것):
///   · 플레이어가 재장전 중일 때 (1.5초 동안 못 쏜다)
///   · 플레이어가 다른 좀비를 겨누고 있을 때 / 등을 보일 때
///   · 다른 좀비들이 플레이어 곁에 붙어 있을 때
///   · 돌진이 닿을 만한 거리일 때 (멀리서 쓰면 못 닿고 몇 초 동안 다시 못 쓴다)
///
/// 관측 (16):
///   [0] 플레이어가 보이는가          [1] 거리 / 20
///   [2] 조준 정렬 cos (1 = 나를 겨눔)  [3] 조준 옆 sin
///   [4] 나를 표적으로 잡았나          [5] 재장전 중인가
///   [6] 탄창 남은 비율               [7] 플레이어 속력 / 9
///   [8] 플레이어가 나에게서 멀어지는 속도 / 9 (+ = 도망)
///   [9] 지금 돌진 가능한가            [10] 내 돌진 속도 / 10
///   [11] 내 평소 속도 / 5             [12] 내 체력 비율
///   [13] 경직 중인가                  [14] 플레이어 5m 안의 다른 좀비 수 / 4
///   [15] 마지막 돌진 뒤 지난 시간 / 10
///
/// 모델이 없으면(Heuristic) 예전 규칙 그대로: 5~9m 안이면 돌진.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Lunge Agent (노리는 좀비)")]
public class ZombieLungeAgent : Agent
{
    public const int    ObservationSize = 16;
    public const string BehaviorNameDefault = "ZombieLunge";

    [Tooltip("돌진 학습장. 비워두면 부모에서 찾는다 (게임에서는 없음)")]
    public LungeTrainingArena arena;

    /// <summary>정책이 지금 돌진을 원하는가 (ZombieQuirks가 읽는다)</summary>
    public bool WantsLunge { get; private set; }

    private ZombieController body;
    private ZombieQuirks     quirks;

    public override void Initialize()
    {
        body   = GetComponent<ZombieController>();
        quirks = GetComponent<ZombieQuirks>();
        if (arena == null) arena = GetComponentInParent<LungeTrainingArena>();
    }

    public override void OnEpisodeBegin()
    {
        WantsLunge = false;
    }

    private ZombieQuirks Quirks
    {
        get
        {
            if (quirks == null) quirks = GetComponent<ZombieQuirks>();   // 컨트롤러가 Awake에서 늦게 붙일 수 있다
            return quirks;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        var buf = new float[ObservationSize];
        try { Fill(buf); }
        catch (System.Exception e)
        {
            Debug.LogError($"[ZombieLungeAgent] {name}: 관측 실패 → 0으로 채움\n{e}", this);
            System.Array.Clear(buf, 0, buf.Length);
        }
        sensor.AddObservation(buf);
    }

    private void Fill(float[] buf)
    {
        if (body == null) body = GetComponent<ZombieController>();
        var q = Quirks;
        if (body == null) return;

        Transform target = body.CurrentTarget;
        bool sees = target != null && body.SeesTarget;
        buf[0] = sees ? 1f : 0f;

        buf[9]  = q != null && q.LungeAvailableNow ? 1f : 0f;
        buf[10] = q != null ? Mathf.Clamp(q.LungeSpeedEstimate / 10f, 0f, 2f) : 0f;
        buf[11] = Mathf.Clamp(body.moveSpeed / 5f, 0f, 2f);
        buf[12] = arena != null ? arena.HealthFraction(this) : body.HealthNormalized;
        buf[13] = body.IsStunned ? 1f : 0f;
        float since = q != null && q.LastLungeStart >= 0f ? Time.time - q.LastLungeStart : 10f;
        buf[15] = Mathf.Clamp01(since / 10f);

        if (target == null) { buf[1] = 1.5f; return; }

        Vector3 toMe = transform.position - target.position;
        toMe.y = 0f;
        float dist = toMe.magnitude;
        buf[1] = Mathf.Clamp(dist / 20f, 0f, 1.5f);
        buf[14] = Mathf.Clamp(ZombieQuirks.CountNear(target.position, 5f, q) / 4f, 0f, 1f);

        if (!sees) return;   // 벽 너머의 플레이어 상태는 모른다

        var info = ZombieTargetInfo.Get(target);
        Vector3 aim = info.aim; aim.y = 0f;
        if (aim.sqrMagnitude > 1e-4f && dist > 1e-3f)
        {
            aim.Normalize();
            Vector3 dir = toMe / dist;
            float cos = Vector3.Dot(aim, dir);
            buf[2] = cos;
            buf[3] = -(aim.x * dir.z - aim.z * dir.x);
            buf[4] = ZombieTargetInfo.TargetsMe(target, transform, cos) ? 1f : 0f;
        }
        buf[5] = info.armed && info.reloading ? 1f : 0f;
        buf[6] = info.armed ? info.ammo01 : 1f;
        Vector3 v = info.velocity;
        buf[7] = Mathf.Clamp(v.magnitude / 9f, 0f, 1.5f);
        // (나 → 플레이어) 방향으로의 속도 = 나에게서 멀어지는 속도
        if (dist > 1e-3f) buf[8] = Mathf.Clamp(Vector3.Dot(v, -toMe / dist) / 9f, -1.5f, 1.5f);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        var q = Quirks;
        if (q == null || !q.LungeAvailableNow) actionMask.SetActionEnabled(0, 1, false);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        WantsLunge = actions.DiscreteActions.Length > 0 && actions.DiscreteActions[0] == 1;
        if (arena != null) arena.OnAgentStep(this);
    }

    /// <summary>모델이 없을 때: 예전 규칙 (좀비마다 5~9m 안이면 돌진)</summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        var q = Quirks;
        bool go = false;
        if (q != null && q.LungeAvailableNow && body != null && body.CurrentTarget != null)
        {
            Vector3 to = body.CurrentTarget.position - transform.position; to.y = 0f;
            go = to.magnitude <= q.MyLungeDistance;
        }
        d[0] = go ? 1 : 0;
    }
}

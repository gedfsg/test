using UnityEngine;

/// <summary>
/// 사냥꾼 무리용 판단 계층. 평소 판단(배회·수색·추적·공격)은 같은 좀비에 붙은 FSM에게 그대로 맡기고,
/// 무리 조율기(HunterPackTactics)가 자리를 정해 주면 이동 목표만 그 자리로 바꿔 끼운다.
///
///   공격 사거리 안   → FSM 그대로 (때리기)
///   조율기 명령 있음 → 명령 지점으로 이동 (포위 자리 / 덮치기)
///   명령 없음        → FSM 그대로 (무리가 플레이어를 놓쳤을 때 수색·배회)
///
/// 직접 붙일 필요 없다. HunterPackTactics.Enlist가 붙이고 ZombieController의 Brain 칸에 꽂는다.
/// </summary>
public class ZombieFlankBrain : ZombieBrain
{
    [HideInInspector] public HunterPackTactics pack;

    private ZombieFSMBrain   fsm;
    private ZombieController body;
    private ZombieState      state = ZombieState.Idle;

    public override ZombieState State => state;

    void Awake()
    {
        fsm  = GetComponent<ZombieFSMBrain>();
        body = GetComponent<ZombieController>();
    }

    public override ZombieAction Think(in ZombieObservation obs, float dt)
    {
        if (fsm == null) fsm = GetComponent<ZombieFSMBrain>();
        if (body == null) body = GetComponent<ZombieController>();

        ZombieAction action = fsm != null ? fsm.Think(in obs, dt) : ZombieAction.Stand();
        state = fsm != null ? fsm.State : ZombieState.Idle;

        if (pack == null) return action;

        // 내가 본 것은 무리 전체가 안다
        if (obs.hasTarget && obs.targetVisible) pack.ReportSighting(obs.targetPosition);

        if (obs.isStunned || state == ZombieState.Attack) return action;
        if (!obs.canMove) return action;

        if (!pack.TryGetOrder(body, out Vector3 point, out _)) return action;

        state = ZombieState.Chase;
        return ZombieAction.MoveTo(point, 1f);
    }

    public override void OnDamaged()
    {
        fsm?.OnDamaged();
    }

    public override void OnHeardNoise(Vector3 position)
    {
        fsm?.OnHeardNoise(position);
        // 총소리(또는 등장 직후 알림)는 무리 전체가 듣는다
        if (pack != null) pack.ReportSighting(position);
    }

    public override void OnResetEpisode()
    {
        fsm?.OnResetEpisode();
        state = ZombieState.Idle;
    }
}

using UnityEngine;

/// <summary>
/// 한 무리(좀비 팀)가 공유하는 타겟 정보. "누군가 봤으면 다 안다."
///
/// 왜 필요한가:
///   포위는 "타겟이 어디로 도망가는지"를 알아야 앞을 막을 수 있다.
///   예전 관측은 각자 자기 눈으로 본 위치만 있어서, 뒤쪽에서 돌아가는 좀비는
///   타겟을 못 보는 순간 정보가 끊겼다 —— 돌아가는 행동 자체가 손해가 됐다.
///
/// 벽 투시가 아니다:
///   팀원 중 누군가 '실제로 보고 있을 때'만 갱신된다. 아무도 못 보면 마지막 정보가
///   점점 낡아갈 뿐이다 (Age가 커짐). FSM의 '경계 전파'와 같은 수준의 정보다.
///
/// 속도는 목격 위치의 차분으로 추정한다 (타겟의 내부 속도값을 몰래 읽지 않는다).
/// 그래서 실제 게임의 플레이어(CharacterController 등)에도 그대로 쓸 수 있다.
/// </summary>
public class ZombieTeamIntel
{
    [Tooltip("속도 추정 샘플 간격(초). 너무 짧으면 프레임 떨림이 그대로 속도로 들어온다")]
    public float sampleInterval = 0.2f;
    [Tooltip("목격이 이 시간 이상 끊겼다가 다시 보이면 속도 추정을 새로 시작한다")]
    public float velocityResetGap = 1.0f;
    [Tooltip("속도 추정 평활 계수 (0~1, 클수록 새 값을 많이 반영)")]
    public float velocitySmoothing = 0.5f;

    public bool    HasInfo      { get; private set; }
    public Vector3 LastPosition { get; private set; }
    public Vector3 Velocity     { get; private set; }
    public float   LastSeenTime { get; private set; } = -999f;
    public bool    SeenNow      { get; private set; }

    /// <summary>마지막 목격 후 경과 시간(초).</summary>
    public float Age => HasInfo ? Time.time - LastSeenTime : 999f;

    private Vector3 samplePos;
    private float   sampleTime = -999f;
    private bool    hasVelocity;

    public void Reset()
    {
        HasInfo      = false;
        SeenNow      = false;
        LastPosition = Vector3.zero;
        Velocity     = Vector3.zero;
        LastSeenTime = -999f;
        sampleTime   = -999f;
        hasVelocity  = false;
    }

    /// <summary>
    /// 매 스텝 호출. seen = 팀원 중 누구라도 지금 타겟을 보고 있는가.
    /// 보고 있을 때만 targetPos를 받아들인다.
    /// </summary>
    public void Tick(bool seen, Vector3 targetPos)
    {
        SeenNow = seen;
        if (!seen) return;

        float now = Time.time;
        targetPos.y = 0f;

        bool gap = now - LastSeenTime > velocityResetGap;
        if (!HasInfo || gap)
        {
            // 새로 발견: 속도는 아직 모른다
            samplePos   = targetPos;
            sampleTime  = now;
            hasVelocity = false;
            Velocity    = Vector3.zero;
        }
        else if (now - sampleTime >= sampleInterval)
        {
            Vector3 v = (targetPos - samplePos) / (now - sampleTime);
            Velocity    = hasVelocity ? Vector3.Lerp(Velocity, v, velocitySmoothing) : v;
            hasVelocity = true;
            samplePos   = targetPos;
            sampleTime  = now;
        }

        HasInfo      = true;
        LastPosition = targetPos;
        LastSeenTime = now;
    }
}

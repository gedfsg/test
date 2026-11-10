using UnityEngine;

/// <summary>
/// 판단(Think) 계층의 공통 인터페이스.
///
/// ZombieController는 이 타입만 알고 있고, 실제 구현이 FSM인지 강화학습인지 신경 쓰지 않는다.
/// → ML-Agents 도입 시 ZombieMLBrain : ZombieBrain 을 만들어 프리팹에 붙이기만 하면 교체 완료.
/// → 그 brain이 비활성/예외 상태면 Controller가 자동으로 FSM으로 폴백한다.
///
/// 구현체가 지켜야 할 규칙:
///  - Think()는 부수효과 없이 ZombieAction만 반환할 것 (NavMeshAgent/Animator 직접 건드리지 말 것)
///  - 상태는 State 프로퍼티로 노출할 것 (디버그 HUD / 실험 로깅용)
/// </summary>
public abstract class ZombieBrain : MonoBehaviour
{
    /// <summary>현재 상태. FSM vs RL 비교 실험에서 상태 체류 시간 로깅에 쓴다.</summary>
    public abstract ZombieState State { get; }

    /// <summary>한 프레임 판단. obs만 보고 결정한다.</summary>
    public abstract ZombieAction Think(in ZombieObservation obs, float deltaTime);

    /// <summary>피격 알림. 기본은 무시.</summary>
    public virtual void OnDamaged() { }

    /// <summary>소음을 들었을 때. 위치는 이미 Perception에 힌트로 들어가 있다.</summary>
    public virtual void OnHeardNoise(Vector3 position) { }

    /// <summary>에피소드 시작/리스폰 시 내부 상태 초기화 (ML-Agents OnEpisodeBegin 대응).</summary>
    public virtual void OnResetEpisode() { }
}
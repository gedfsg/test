using UnityEngine;

/// <summary>
/// 노리는 좀비(돌진 타이밍 강화학습) 게임 설정. Assets/Resources/ZombieLungeBrain.asset 으로 둔다.
/// (메뉴: Tools/Zombie/노리는 좀비 모델 설치 —— 가장 최근의 'lunge' 모델로 자동 생성)
///
/// 이 설정이 있으면 게임의 돌진형 좀비(ZombieQuirks의 lungeChance)가 '언제 덮칠지'를 모델로 정한다:
/// 재장전·다른 좀비를 겨눌 때·등을 보일 때를 노린다. 없으면 예전 규칙(5~9m 안이면 돌진).
/// </summary>
[CreateAssetMenu(menuName = "Zombie/노리는 좀비 설정", fileName = "ZombieLungeBrain")]
public class ZombieLungeBrainSettings : ScriptableObject
{
    public const string ResourcePath = "ZombieLungeBrain";

    [Tooltip("학습한 돌진 타이밍 모델 (.onnx). 예: Assets/Models/Zombie_lunge2_10M.onnx")]
    public Object model;
    [Tooltip("돌진형 좀비 중 모델을 쓰는 비율. 1 = 전부 노리는 좀비, 0.5 = 절반은 예전 규칙(5~9m)")]
    [Range(0f, 1f)] public float useChance = 1f;
    [Tooltip("몇 물리 프레임마다 '지금 덮칠까?'를 고르나. 학습: 5 (0.1초)")]
    [Min(1)] public int decisionPeriod = 5;

    private static ZombieLungeBrainSettings cached;

    public static ZombieLungeBrainSettings Load()
    {
        if (cached == null) cached = Resources.Load<ZombieLungeBrainSettings>(ResourcePath);
        return cached;
    }
}

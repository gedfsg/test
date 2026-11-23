using UnityEngine;

/// <summary>
/// (2026-11-23) 튜토리얼 한 단계 = "처음 발견"했을 때 띄우는 말풍선 하나.
/// 에셋 위치: Assets/Resources/Tutorial/Steps (TutorialManager가 Resources.LoadAll로 읽음).
/// 에셋은 Tools/Tutorial/Build Tutorial Steps (TutorialStepBuilder)로 만들어짐 → 문구를 바꾸려면 빌더를 고쳐 다시 실행하거나 에셋을 직접 고침
/// (빌더를 다시 돌리면 에셋을 덮어씀).
/// </summary>
[CreateAssetMenu(menuName = "Tutorial/Tutorial Step", fileName = "TutorialStep")]
public class TutorialStep : ScriptableObject
{
    [Tooltip("중복 방지 키 (세션 동안 'tut_<stepId>'로 기록)")]
    public string stepId;

    [Tooltip("말풍선 문구 (한국어). <color>, <b> 같은 리치 텍스트 사용 가능")]
    [TextArea(2, 4)] public string dialogueText;

    [Tooltip("말풍선 문구 (English) - 설정 언어가 English일 때. 비면 한국어")]
    [TextArea(2, 4)] public string dialogueTextEn;

    [Tooltip("무엇을 발견했을 때 뜨는지 (TutorialManager가 종류별로 감지)")]
    public DiscoveryType discoveryType;

    [Tooltip("자동 매칭용 태그 (비워 두면 종류 규칙만 사용). 태그가 있으면 그 태그 오브젝트를 발견했을 때도 이 단계가 뜸")]
    public string targetTag;

    [Tooltip("스포트라이트 반경 (m). 작은 소품 2 / 중간(자동차·주유기) 3.5 / 건물 입구 4.5. 0이면 구멍 없이 전체만 어둡게")]
    public float spotlightRadius = 2f;

    [Tooltip("같은 시점에 여러 개가 겹치면 작은 값부터")]
    public int order;

    /// <summary>현재 언어 문구</summary>
    public string Text => Localization.IsEnglish && !string.IsNullOrEmpty(dialogueTextEn) ? dialogueTextEn : dialogueText;
}

/// <summary>튜토리얼 발견 종류 (새 종류는 끝에 추가 - 에셋에 int로 저장됨)</summary>
public enum DiscoveryType
{
    // 조작 (동료 상단 안내 블록에서 나눔)
    GameStart,       // 시작: 이동
    Attack,          // 조준·공격
    GunHeld,         // 총 들기: 1·2 총 · 4 도끼
    Reload,          // 탄창이 거의 빔: R 재장전
    Aim,             // 우클릭 조준 자세
    // 월드 오브젝트
    LootCrate,
    Zombie,
    AnyItem,         // 바닥 아이템 (줍기·가방)
    WeaponItem,
    HealItem,
    AmmoItem,
    ThrowableItem,
    BuildingEntrance,
    Police,
    Hospital,
    Mart,
    Warehouse,
    Church,
    School,
    GasStation,
    MissionPin,
    Trader,
    // 상황
    Night,
    Rain,
    Fog,
    LowHealth,
    Hunger,
    FinalDay,
    Money,
    NoAmmo,
    Custom,          // DiscoveryTrigger를 손으로 붙인 오브젝트 (stepId로 연결)
}

using UnityEngine;

/// <summary>서브 미션 종류 (새 종류는 끝에 추가 - 에셋에 int로 저장됨)</summary>
public enum SubMissionType
{
    KillZombies,       // 좀비 N마리 처치
    OpenCrates,        // 상자 N개 열기
    SearchGasStation,  // 주유소 수색 (건물 들어가기)
    SearchCafe,        // 카페 수색
    SearchBookstore,   // 서점 수색
    ExplorePark,       // 공원 중심 도달
    UseItems,          // 소비 아이템 N번 사용
    Walk,              // N m 이동
    NightKills,        // 밤에 좀비 N마리 처치
    FogWalk,           // 안개 날씨에 N m 이동
    RainKills,         // 비 날씨에 좀비 N마리 처치
}

/// <summary>서브 미션이 UI에 나타나는(발견되는) 조건. 충족 전엔 HUD에서 완전히 숨김</summary>
public enum SubMissionTrigger
{
    FirstKill,         // 그날 좀비를 처음 처치했을 때
    NearCrate,         // 안 열린 상자 triggerRadius m 안에 들어왔을 때
    NearPlace,         // 해당 장소(주유소/카페/서점/공원) triggerRadius m 안에 들어왔을 때
    ConsumableUsed,    // 소비 아이템을 처음 사용했을 때
    Moved,             // 그날 triggerRadius m 이상 움직였을 때
    NightStart,        // 밤이 됐을 때
    FogWeather,        // 안개 날씨일 때
    RainWeather,       // 비 날씨일 때
}

/// <summary>
/// 서브 미션 1개 정의 (Assets/Resources/Missions/Sub). MissionManager가 매일 1~2개를 랜덤으로 할당하고,
/// trigger 조건이 충족되면 "발견" 알림과 함께 HUD에 나타남. 에셋은 Tools/Mission/Build Mission Data로 생성.
/// </summary>
[CreateAssetMenu(menuName = "Mission/Sub Mission")]
public class SubMissionData : ScriptableObject
{
    public SubMissionType type;
    [Tooltip("HUD에 표시되는 미션명")]
    public string title;
    [Tooltip("목표 수 (처치 수, 상자 수, 사용 횟수, 이동 m). 장소 수색은 1")]
    public int target = 1;
    public SubMissionTrigger trigger;
    [Tooltip("NearCrate/NearPlace: 거리(m), Moved: 이동 거리(m)")]
    public float triggerRadius = 100f;

    [Header("보상 (아이템)")]
    [Tooltip("아이템 에셋 이름 (LootDatabase 루팅 테이블에서 찾음)")]
    public string rewardItemName;
    [Tooltip("알림 표시용 (예: 구급상자 x1)")]
    public string rewardLabel;
    public int rewardAmount = 1;
}

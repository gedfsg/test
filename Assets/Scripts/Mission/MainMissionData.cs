using UnityEngine;

/// <summary>메인 미션 종류. MissionManager가 종류별로 목표 오브젝트 배치/판정을 함 (새 종류는 끝에 추가 - 에셋에 int로 저장됨)</summary>
public enum MainMissionType
{
    EmergencyRadio,   // 비상 무전기 가동 - 경찰서 실내 or 라디오 타워 아래 무전기 상호작용
    KeyCardA,         // 열쇠 카드 A 수집 - 맵 전체 랜덤 위치
    KeyCardB,
    KeyCardC,
    MedicalSupplies,  // 붕대 N개 or 진통제 M개 소지
    PowerRestore,     // 발전소 발전기 상호작용
    ZombieHorde,      // 특정 지역 좀비 N마리 처치
    FuelCollect,      // 주유소 연료통 획득
    Armory,           // 경찰서 무기고 상호작용
    WaterSupply,      // 생수 N병 소지
    Barricade,        // 바리케이드 설치 지점 상호작용
    FoodSupply,       // 통조림 N개 or 식량 M개 소지
    Recon,            // 맵 4개 구역 정찰 지점 방문
    RadioSignal,      // 라디오 타워 근처에서 N초 대기
    Escape,           // 탈출 지점 도달 (Day 7 고정)
}

/// <summary>
/// 메인 미션 1개 정의 (Assets/Resources/Missions/Main). 매 게임 시작 시 MissionManager가 풀에서 6개를 랜덤으로 골라 Day 1~6에,
/// fixedLastDay(탈출 지점 도달)는 Day 7에 배치. 에셋은 Tools/Mission/Build Mission Data로 생성 (MissionDefaults 값).
/// </summary>
[CreateAssetMenu(menuName = "Mission/Main Mission")]
public class MainMissionData : ScriptableObject
{
    public MainMissionType type;
    [Tooltip("HUD에 표시되는 미션명")]
    public string title;
    [Tooltip("목표 수 (처치 수, 대기 초, 구역 수, 아이템 개수 등). 종류에 따라 의미가 다름")]
    public int target = 1;
    [Tooltip("두 번째 조건 개수 (의료품: 진통제 수 / 식량: 식량 수). 0이면 없음")]
    public int altTarget;
    [Tooltip("상호작용 미션: F키를 누르고 있어야 하는 시간(초)")]
    public float holdSeconds = 2f;
    [Tooltip("항상 마지막 날(Day 7)에 배치 (탈출 지점 도달)")]
    public bool fixedLastDay;
}

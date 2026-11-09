using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 미션 풀 기본값 (한 곳에서 관리).
///  - 에디터: Tools/Mission/Build Mission Data가 이 값으로 Assets/Resources/Missions/Main, Sub 에셋을 만듦
///  - 런타임: Resources에 에셋이 없으면 MissionManager가 이 값으로 임시 인스턴스를 만들어 씀
/// </summary>
public static class MissionDefaults
{
    public static List<MainMissionData> Main()
    {
        return new List<MainMissionData>
        {
            M(MainMissionType.EmergencyRadio, "비상 무전기 가동", 1, hold: 3f),
            M(MainMissionType.KeyCardA, "열쇠 카드 A 수집"),
            M(MainMissionType.KeyCardB, "열쇠 카드 B 수집"),
            M(MainMissionType.KeyCardC, "열쇠 카드 C 수집"),
            M(MainMissionType.MedicalSupplies, "의료품 긴급 수집", 10, alt: 5),     // 붕대 10개 or 진통제 5개
            M(MainMissionType.PowerRestore, "전력 복구", 1, hold: 4f),
            M(MainMissionType.ZombieHorde, "좀비 떼 섬멸", 20),
            M(MainMissionType.FuelCollect, "연료 수집", 1, hold: 1.5f),
            M(MainMissionType.Armory, "무기고 발견", 1, hold: 2.5f),
            M(MainMissionType.WaterSupply, "식수 확보", 5),                        // 생수 5병
            M(MainMissionType.Barricade, "바리케이드 설치", 1, hold: 4f),
            M(MainMissionType.FoodSupply, "긴급 식량 조달", 10, alt: 15),          // 통조림 10개 or 식량 15개
            M(MainMissionType.Recon, "정찰 완료", 4),                              // 4개 구역
            M(MainMissionType.RadioSignal, "무전 신호 수신", 30),                   // 라디오 타워 근처 30초
            M(MainMissionType.Escape, "탈출 지점 도달", 1, lastDay: true),
        };
    }

    public static List<SubMissionData> Sub()
    {
        return new List<SubMissionData>
        {
            S(SubMissionType.KillZombies, "좀비 10마리 처치", 10, SubMissionTrigger.FirstKill, 0f, "Pistol Ammo", "권총탄 x20", 20),
            S(SubMissionType.OpenCrates, "상자 3개 열기", 3, SubMissionTrigger.NearCrate, 15f, "MedKit", "구급상자 x1", 1),
            S(SubMissionType.SearchGasStation, "주유소 수색", 1, SubMissionTrigger.NearPlace, 100f, "CannedFood", "통조림 x2", 2),
            S(SubMissionType.SearchCafe, "카페 수색", 1, SubMissionTrigger.NearPlace, 100f, "Coffee", "커피 x2", 2),
            S(SubMissionType.SearchBookstore, "서점 수색", 1, SubMissionTrigger.NearPlace, 100f, "Painkiller", "진통제 x2", 2),
            S(SubMissionType.ExplorePark, "공원 탐색", 1, SubMissionTrigger.NearPlace, 100f, "Water", "생수 x2", 2),
            S(SubMissionType.UseItems, "아이템 2번 사용", 2, SubMissionTrigger.ConsumableUsed, 0f, "Bandage", "붕대 x2", 2),
            S(SubMissionType.Walk, "500m 이동", 500, SubMissionTrigger.Moved, 10f, "Bread", "빵 x2", 2),
            S(SubMissionType.NightKills, "밤에 좀비 5마리 처치", 5, SubMissionTrigger.NightStart, 0f, "MedKit", "구급상자 x1", 1),
            S(SubMissionType.FogWalk, "안개 속 100m 이동", 100, SubMissionTrigger.FogWeather, 0f, "Painkiller", "진통제 x2", 2),
            S(SubMissionType.RainKills, "빗속 좀비 3마리 처치", 3, SubMissionTrigger.RainWeather, 0f, "Pistol Ammo", "권총탄 x20", 20),
        };
    }

    static MainMissionData M(MainMissionType type, string title, int target = 1, int alt = 0, float hold = 2f, bool lastDay = false)
    {
        var d = ScriptableObject.CreateInstance<MainMissionData>();
        d.name = "Main_" + type;
        d.type = type; d.title = title; d.target = target; d.altTarget = alt; d.holdSeconds = hold; d.fixedLastDay = lastDay;
        return d;
    }

    static SubMissionData S(SubMissionType type, string title, int target, SubMissionTrigger trigger, float radius, string rewardItem, string rewardLabel, int rewardAmount)
    {
        var d = ScriptableObject.CreateInstance<SubMissionData>();
        d.name = "Sub_" + type;
        d.type = type; d.title = title; d.target = target; d.trigger = trigger; d.triggerRadius = radius;
        d.rewardItemName = rewardItem; d.rewardLabel = rewardLabel; d.rewardAmount = rewardAmount;
        return d;
    }
}

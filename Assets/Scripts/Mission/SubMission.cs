using UnityEngine;
/// <summary>
/// 서브 미션 1개 (MissionManager가 매일 후보 중 랜덤으로 할당).
/// 실패 조건 없음 - 달성하면 보상(아이템 또는 점수) 지급.
/// </summary>
public class SubMission
{
    public enum Kind { KillZombies, KillAtNight, OpenCrates, VisitBuilding, UseConsumables, KillInFog, MoveInRain }

    public Kind kind;
    public string title;          // HUD 표시용 (예: "좀비 15마리 처치")
    public int target = 1;        // 목표 수
    public int progress;          // 현재 수
    public LootDatabase.BuildingKind building; // VisitBuilding 전용

    // 보상: 아이템
    public string rewardItemName; // 아이템 에셋 이름 (LootDatabase 테이블에서 찾음)
    public string rewardLabel;    // HUD 표시용 (예: "구급상자 x1")
    public int rewardAmount = 1;

    public bool Done => progress >= target;
    public string RewardText => rewardLabel;
    public int Percent => Mathf.FloorToInt(Mathf.Clamp01((float)progress / target) * 100f);
}

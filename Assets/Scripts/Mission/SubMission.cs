/// <summary>
/// 서브 미션 1개 (MissionManager가 후보 중 랜덤 3개를 뽑아 진행).
/// 실패 조건 없음 - 달성하면 보상(아이템 또는 점수) 지급.
/// </summary>
public class SubMission
{
    public enum Kind { KillZombies, KillAtNight, OpenCrates, VisitBuilding, UseConsumables }

    public Kind kind;
    public string title;          // HUD 표시용 (예: "좀비 15마리 처치")
    public int target = 1;        // 목표 수
    public int progress;          // 현재 수
    public LootDatabase.BuildingKind building; // VisitBuilding 전용

    // 보상: rewardItemName이 있으면 아이템, 없으면 점수
    public string rewardItemName; // 아이템 에셋 이름 (LootDatabase 테이블에서 찾음)
    public string rewardLabel;    // HUD 표시용 (예: "구급상자 x1")
    public int rewardAmount = 1;
    public int rewardScore;

    public bool Done => progress >= target;
    public bool IsItemReward => !string.IsNullOrEmpty(rewardItemName);
    public string RewardText => IsItemReward ? rewardLabel : $"점수 {rewardScore}";
}

using UnityEngine;
/// <summary>
/// 오늘 할당된 서브 미션 1개 (런타임 상태). 정의는 SubMissionData (MissionManager가 매일 1~2개 랜덤 할당).
///  - 발견 전(Discovered = false): HUD에서 완전히 숨김, 진행도 안 셈
///  - 조건 충족 → Discover() → OnDiscovered 이벤트 (MissionManager가 알림음 + 토스트 + HUD "발견" 라벨)
/// 실패 조건 없음 - 달성하면 진행률 +20% + 아이템 보상. 미완료는 다음날 사라짐 (패널티 없음).
/// </summary>
public class SubMission
{
    // (이전) 미션마다 kind/title/target/보상 필드를 코드에서 직접 채웠음 → 이제 SubMissionData 에셋에서 읽음
    // public enum Kind { KillZombies, KillAtNight, OpenCrates, VisitBuilding, UseConsumables, KillInFog, MoveInRain }
    // public Kind kind;
    // public LootDatabase.BuildingKind building; // VisitBuilding 전용

    public readonly SubMissionData data;
    public int progress;                 // 현재 수
    public bool Discovered { get; private set; }
    public float DiscoveredTime { get; private set; } = -999f; // HUD "발견" 라벨 표시 시간 계산용
    public bool Rewarded;                // 완료 처리(진행률/보상) 했는지

    /// <summary>조건 충족으로 처음 나타났을 때 (MissionManager가 구독: 알림음 + 토스트)</summary>
    public event System.Action<SubMission> OnDiscovered;

    public SubMission(SubMissionData data) { this.data = data; }

    public SubMissionType Type => data.type;
    public string title => data.title;   // HUD 표시용 (예: "좀비 10마리 처치") - 기존 이름 유지 (PPT 촬영 도구 등)
    public int target => Mathf.Max(1, data.target);
    public bool Done => progress >= target;
    public string RewardText => data.rewardLabel;

    /// <summary>발견 처리 (이미 발견됐으면 무시)</summary>
    public void Discover()
    {
        if (Discovered) return;
        Discovered = true;
        DiscoveredTime = Time.time;
        OnDiscovered?.Invoke(this);
    }
}

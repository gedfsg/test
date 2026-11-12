using UnityEngine;

/// <summary>
/// 이번 판 기록 (생존 종료 화면용). 게임 씬마다 새로 생김 (MenuUI가 자동 생성 - 다시 시작하면 0부터).
///  - PlayTime: 실제 시간 기준 생존 시간. 일시정지·사망(timeScale 0) 중에는 안 셈
///  - ZombiesKilled: ZombieController.AnyDied 구독
///  - CurrentDay / CompletedMissions: DayNightCycle / MissionManager에서 조회
/// 사망하면 Freeze()로 값이 멈춤.
/// </summary>
public class GameStats : MonoBehaviour
{
    public static GameStats Instance { get; private set; }

    public float PlayTime { get; private set; }
    public int ZombiesKilled { get; private set; }
    public bool Frozen { get; private set; }

    int frozenDay = -1, frozenMissions = -1;

    /// <summary>지금 일차 (1~)</summary>
    public int CurrentDay => frozenDay > 0 ? frozenDay : DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
    /// <summary>목표 일수 (7)</summary>
    public int TotalDays => DayNightCycle.Instance != null ? DayNightCycle.Instance.totalDays : 7;
    /// <summary>완료한 미션 수 (메인 + 서브)</summary>
    public int CompletedMissions => frozenMissions >= 0 ? frozenMissions
        : MissionManager.Instance != null ? MissionManager.Instance.CompletedMainMissions + MissionManager.Instance.CompletedSubMissions : 0;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnEnable() => ZombieController.AnyDied += OnZombieDied;
    void OnDisable() => ZombieController.AnyDied -= OnZombieDied;

    void OnZombieDied(ZombieController z)
    {
        if (!Frozen && z != null && z.targetOverride == null) ZombiesKilled++; // 학습장 좀비 제외
    }

    void Update()
    {
        if (!Frozen && Time.timeScale > 0f) PlayTime += Time.unscaledDeltaTime;
    }

    /// <summary>사망 시점 값으로 고정</summary>
    public void Freeze()
    {
        if (Frozen) return;
        frozenDay = CurrentDay;
        frozenMissions = CompletedMissions;
        Frozen = true;
    }

    /// <summary>mm:ss (1시간 넘으면 h:mm:ss)</summary>
    public static string FormatTime(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }
}

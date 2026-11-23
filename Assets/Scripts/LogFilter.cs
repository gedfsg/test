using UnityEngine;

/// <summary>
/// (2026-11-22) 콘솔 정리: 일반 로그(Debug.Log)는 숨기고 경고·에러만 남긴다.
/// 스크립트마다 흩어진 디버그 출력(약 185개)을 지우지 않고 한 곳에서 거른다 → 팀원 파일을 건드리지 않음.
///  - 게임 시작 때 Debug.unityLogger.filterLogType = Warning → Log만 막히고 Warning·Error·Assert·Exception은 그대로
///    (막힌 로그는 콘솔·Player.log에 안 쓰이고 스택 트레이스도 안 만듦 → 빌드에서도 조금 가벼워짐)
///  - 다시 보고 싶으면 에디터 메뉴 Tools > 로그 > 일반 로그(Debug.Log) 보기 를 체크 (이 컴퓨터에만 저장, 바로 적용)
///  - 플레이를 멈추면 에디터 도구(Tools 메뉴 등)의 로그가 다시 보이도록 원래대로 돌린다
/// </summary>
public static class LogFilter
{
    public const string PrefKey = "LogFilter.ShowInfoLogs";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Apply()
    {
        bool showInfo = false;
#if UNITY_EDITOR
        showInfo = UnityEditor.EditorPrefs.GetBool(PrefKey, false);
#endif
        Debug.unityLogger.filterLogType = showInfo ? LogType.Log : LogType.Warning;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
    static class EditorSide
    {
        const string MenuPath = "Tools/로그/일반 로그(Debug.Log) 보기";

        static EditorSide()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayMode;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayMode;
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.Menu.SetChecked(MenuPath, UnityEditor.EditorPrefs.GetBool(PrefKey, false));
        }

        static void OnPlayMode(UnityEditor.PlayModeStateChange s)
        {
            if (s == UnityEditor.PlayModeStateChange.EnteredEditMode) Debug.unityLogger.filterLogType = LogType.Log;   // 편집 중엔 전부 보임
        }

        [UnityEditor.MenuItem(MenuPath)]
        static void Toggle()
        {
            bool show = !UnityEditor.EditorPrefs.GetBool(PrefKey, false);
            UnityEditor.EditorPrefs.SetBool(PrefKey, show);
            UnityEditor.Menu.SetChecked(MenuPath, show);
            if (Application.isPlaying) Debug.unityLogger.filterLogType = show ? LogType.Log : LogType.Warning;
            Debug.LogWarning(show ? "[LogFilter] 일반 로그 보이기" : "[LogFilter] 일반 로그 숨기기 (플레이 중 경고·에러만)");
        }
    }
#endif
}

using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 발표 영상 "날씨별 소음 반경 비교" 자동 녹화 (Tools → Presentation → Record Weather Comparison). 2026-11-12
///
/// 순서: Play 진입 → (메인 메뉴로 시작하면) main 씬 로드 → 3초 대기(씬 초기화) → WeatherComparisonDirector 생성
///       → 촬영 위치·좀비 준비 끝(Ready) → Unity Recorder 녹화 시작(1920×1080, 60fps, H.264 MP4, Game View)
///       → 20초 시나리오(Director) → 녹화 정지 → Play 종료
/// 저장: 바탕 화면\발표영상_날씨비교\weather_sound_comparison.mp4
/// Play 진입 시 도메인 리로드가 있어도 SessionState 플래그로 이어서 진행.
/// </summary>
[InitializeOnLoad]
public static class WeatherComparisonRecorder
{
    const string PendingKey = "WeatherCompare.Pending";
    const string FileName = "weather_sound_comparison";
    const string FontPath = "Assets/Fonts/Pretendard/Pretendard-Bold.otf";
    const string RiflePath = "Assets/Items/Weapons/Liberator/AR-15.asset";
    const float SceneSettleSeconds = 3f;
    const float TimeoutSeconds = 120f; // 안전장치 (실시간)

    static string OutDir => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "발표영상_날씨비교");

    enum Stage { Idle, LoadScene, Settle, Prepare, Recording }
    static Stage stage = Stage.Idle;
    static double stageTime, startTime;
    static bool sceneRequested;
    static WeatherComparisonDirector director;
    static RecorderController recorder;

    static WeatherComparisonRecorder()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Presentation/Record Weather Comparison")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[WeatherCompare] Play 모드를 끄고 실행하세요."); return; }
        Directory.CreateDirectory(OutDir);
        SessionState.SetBool(PendingKey, true);
        Debug.Log("[WeatherCompare] Play 진입 → 자동 녹화 시작");
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
        {
            SessionState.SetBool(PendingKey, false);
            stage = Stage.LoadScene; sceneRequested = false; director = null; recorder = null;
            startTime = stageTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (s == PlayModeStateChange.ExitingPlayMode && stage != Stage.Idle)
        {
            if (recorder != null && recorder.IsRecording()) recorder.StopRecording();
            Finish("Play 종료로 중단");
        }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - startTime > TimeoutSeconds && stage != Stage.Recording) { Debug.LogError("[WeatherCompare] 준비 시간 초과 → 중단"); Exit(); return; }

        switch (stage)
        {
            case Stage.LoadScene:
                if (SceneManager.GetActiveScene().name != "main")
                {
                    if (!sceneRequested) { sceneRequested = true; SceneManager.LoadScene("main"); }
                    return;
                }
                stage = Stage.Settle; stageTime = now;
                break;

            case Stage.Settle:
                if (now - stageTime < SceneSettleSeconds) return;
                var go = new GameObject("WeatherComparisonDirector");
                director = go.AddComponent<WeatherComparisonDirector>();
                director.sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                director.rifle = AssetDatabase.LoadAssetAtPath<WeaponData>(RiflePath);
                stage = Stage.Prepare; stageTime = now;
                break;

            case Stage.Prepare:
                if (director == null) { Debug.LogError("[WeatherCompare] Director 사라짐"); Exit(); return; }
                if (!director.Ready) return;
                StartRecorder();
                director.Begin();
                stage = Stage.Recording; stageTime = now;
                break;

            case Stage.Recording:
                if (director == null || director.Finished || now - stageTime > TimeoutSeconds)
                {
                    if (recorder != null && recorder.IsRecording()) recorder.StopRecording();
                    string path = Path.Combine(OutDir, FileName + ".mp4");
                    Debug.Log($"[WeatherCompare] 저장 완료: {path}" + (director != null ? $"\n{director.Report}" : ""));
                    Exit();
                }
                break;
        }
    }

    static void StartRecorder()
    {
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "WeatherComparison";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,              // H.264 MP4
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
        };
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
        movie.AudioInputSettings.PreserveAudio = true;                  // 총소리·빗소리 포함
        movie.OutputFile = Path.Combine(OutDir, FileName);              // 확장자는 Recorder가 붙임

        controllerSettings.AddRecorderSettings(movie);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 60f;
        controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
        controllerSettings.CapFrameRate = true;

        recorder = new RecorderController(controllerSettings);
        recorder.PrepareRecording();
        bool ok = recorder.StartRecording();
        Debug.Log($"[WeatherCompare] 녹화 시작 {(ok ? "OK" : "실패")} (1920×1080, 60fps, H.264 MP4)");
    }

    static void Exit()
    {
        Finish("완료");
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Finish(string reason)
    {
        stage = Stage.Idle;
        EditorApplication.update -= Tick;
        recorder = null;
        director = null;
    }
}

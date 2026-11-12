using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 에디터에서 Play를 누르면 지금 열린 씬과 상관없이 MainMenu.unity부터 시작 (빌드와 같은 흐름).
/// 에디터는 원래 "열려 있는 씬"을 바로 실행해서 main.unity를 열어 두면 메인 메뉴가 건너뛰어졌음.
///  - 메뉴 Tools → Play Mode → Start From Main Menu 로 켜고 끔 (기본 ON, EditorPrefs에 저장 = 사람마다 따로)
///  - 끄면 예전처럼 열린 씬을 바로 실행 (게임 씬만 빠르게 테스트할 때)
/// EditorSceneManager.playModeStartScene은 도메인 리로드(스크립트 컴파일) 때마다 풀리므로 InitializeOnLoad에서 다시 설정
/// </summary>
[InitializeOnLoad]
public static class PlayModeSceneLoader
{
    const string MenuPath = "Tools/Play Mode/Start From Main Menu";
    const string PrefKey = "PlayModeSceneLoader.StartFromMainMenu";
    const string MainMenuPath = "Assets/Scenes/MainMenu.unity";

    static PlayModeSceneLoader()
    {
        EditorApplication.delayCall += Apply; // 에셋 DB가 준비된 뒤
        // 도메인 리로드 직후 에셋 로드가 실패하는 경우가 있어서, Play 들어가기 직전에도 한 번 더 확인
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Apply(); };
    }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set { EditorPrefs.SetBool(PrefKey, value); Apply(); }
    }

    [MenuItem(MenuPath, false, 0)]
    static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    static void Apply()
    {
        var scene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath) : null;
        if (EditorSceneManager.playModeStartScene != scene) EditorSceneManager.playModeStartScene = scene;
    }
}

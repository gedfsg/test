using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 설정값 저장·적용 (싱글톤, 게임 시작 시 자동 생성 + DontDestroyOnLoad). 설정 패널은 이 값을 읽고 Apply로 넘김.
///
/// 저장: PlayerPrefs. 사운드 5개는 AudioManager가 원래 쓰던 키(MasterVolume / SFXVolume ...)를 그대로 씀
///   → 기존 볼륨 저장값과 중복되지 않음. AudioManager가 시작 시 알아서 복원
/// 사운드 슬라이더 → AudioManager.SetMasterVolume / SetChannelVolume → AudioMixer 노출 파라미터 (dB = 20·log10)
///   믹서 파라미터 이름: MasterVolume / BGMVolume / SFXVolume / ZombieVolume / UIVolume
///   (BGM = 메인 메뉴 음악·죽음 스팅어 (UIAudio). 비·바람 같은 배경음(AmbientVolume)은 마스터로만 조절)
///
/// 적용 대상:
///   그래픽 → Screen / QualitySettings (해상도·화면 모드는 빌드에서만 실제로 바뀜)
///   마우스 민감도 → 휠 줌 속도 (CameraZoom.WheelSensitivity) - 탑뷰라 조준은 커서 위치를 그대로 따라감
///   카메라 회전 → Q/E 회전 속도 (CameraRotate.SpeedMultiplier)
///   미니맵 크기 → 미니맵 패널 크기 / HUD 투명도 → HUD 캔버스 CanvasGroup.alpha (게임 씬이 뜰 때마다 다시 적용)
///   언어 → 메뉴 UI 글자 (Localization). 화면 흔들림 / 색맹 모드 → 값만 저장 (게임에 아직 해당 기능이 없음: ScreenShake / ColorBlindMode로 조회)
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [System.Serializable]
    public class Data
    {
        // 사운드 (0~100)
        public float master = 100f, bgm = 100f, sfx = 100f, zombie = 100f, ui = 100f;
        // 게임플레이
        public float mouseSensitivity = 1f;  // 0.1~3
        public float cameraRotation = 1f;    // 0.5~3
        public bool screenShake = true;
        // 그래픽
        public int resolutionWidth, resolutionHeight; // 0이면 현재 모니터 해상도
        public int screenMode;   // 0 전체화면 / 1 테두리 없는 창 / 2 창 모드
        public int quality;      // QualitySettings 레벨
        public bool vsync = true;
        public int shadows = 3;  // 0 끄기 / 1 낮음 / 2 중간 / 3 높음 / 4 최고
        // 접근성
        public int language;     // 0 한국어 / 1 English (메뉴 UI만, Localization)
        public float minimapSize = 100f; // 50~150 %
        public float hudOpacity = 100f;  // 20~100 %
        public int colorBlind;   // 0 끄기 / 1 적색맹 / 2 녹색맹 / 3 청색맹

        public Data Clone() => (Data)MemberwiseClone();
    }

    /// <summary>저장·적용된 현재 값</summary>
    public Data Current { get; private set; }

    public static bool ScreenShake => Instance == null || Instance.Current.screenShake;
    public static int Language => Instance != null ? Instance.Current.language : 0;
    public static int ColorBlindMode => Instance != null ? Instance.Current.colorBlind : 0;

    public static readonly string[] ScreenModeNames = { "전체화면", "테두리 없는 창", "창 모드" };
    public static readonly string[] QualityNames = { "매우 낮음", "낮음", "중간", "높음", "매우 높음", "최고" };
    public static readonly string[] ShadowNames = { "끄기", "낮음", "중간", "높음", "최고" };
    public static readonly string[] LanguageNames = { "한국어", "English" }; // 각 언어 이름은 번역하지 않음
    public static readonly string[] ColorBlindNames = { "끄기", "적색맹", "녹색맹", "청색맹" };

    static int projectQuality = -1;   // 프로젝트 기본 품질 레벨 (처음 실행 기본값)
    static bool projectVsync = true;
    static readonly System.Collections.Generic.Dictionary<RectTransform, Vector3> minimapBaseScale =
        new System.Collections.Generic.Dictionary<RectTransform, Vector3>();

    const string P = "Settings.";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        projectQuality = QualitySettings.GetQualityLevel();
        projectVsync = QualitySettings.vSyncCount > 0;
        var go = new GameObject("SettingsManager");
        DontDestroyOnLoad(go);
        go.AddComponent<SettingsManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Current = Load();
        ApplyGraphics(Current);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) { Instance = null; SceneManager.sceneLoaded -= OnSceneLoaded; }
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m) => StartCoroutine(ApplySceneDelayed());

    // HUD 일부(미션 HUD 등)는 Start에서 만들어져서 한 프레임 + 1초 뒤에도 다시 적용
    IEnumerator ApplySceneDelayed()
    {
        ApplyGameplay(Current);
        yield return null;
        ApplyGameplay(Current);
        yield return new WaitForSecondsRealtime(1f);
        ApplyGameplay(Current);
    }

    // ── 기본값 / 저장 ───────────────────────────────
    public static Data Defaults()
    {
        var d = new Data();
        d.quality = projectQuality >= 0 ? projectQuality : QualitySettings.GetQualityLevel();
        d.vsync = projectVsync;
        var r = Screen.currentResolution;
        d.resolutionWidth = r.width;
        d.resolutionHeight = r.height;
        d.screenMode = 0;
        return d;
    }

    Data Load()
    {
        var d = Defaults();
        d.master = AudioManager.GetMasterVolume() * 100f;
        d.bgm = AudioManager.GetChannelVolume(AudioManager.Channel.BGM) * 100f;
        d.sfx = AudioManager.GetChannelVolume(AudioManager.Channel.SFX) * 100f;
        d.zombie = AudioManager.GetChannelVolume(AudioManager.Channel.Zombie) * 100f;
        d.ui = AudioManager.GetChannelVolume(AudioManager.Channel.UI) * 100f;

        d.mouseSensitivity = PlayerPrefs.GetFloat(P + "MouseSensitivity", d.mouseSensitivity);
        d.cameraRotation = PlayerPrefs.GetFloat(P + "CameraRotation", d.cameraRotation);
        d.screenShake = PlayerPrefs.GetInt(P + "ScreenShake", d.screenShake ? 1 : 0) == 1;
        d.resolutionWidth = PlayerPrefs.GetInt(P + "ResolutionWidth", d.resolutionWidth);
        d.resolutionHeight = PlayerPrefs.GetInt(P + "ResolutionHeight", d.resolutionHeight);
        d.screenMode = PlayerPrefs.GetInt(P + "ScreenMode", d.screenMode);
        d.quality = Mathf.Clamp(PlayerPrefs.GetInt(P + "Quality", d.quality), 0, QualitySettings.names.Length - 1);
        d.vsync = PlayerPrefs.GetInt(P + "VSync", d.vsync ? 1 : 0) == 1;
        d.shadows = Mathf.Clamp(PlayerPrefs.GetInt(P + "Shadows", d.shadows), 0, ShadowNames.Length - 1);
        d.language = Mathf.Clamp(PlayerPrefs.GetInt(P + "Language", d.language), 0, LanguageNames.Length - 1);
        d.minimapSize = PlayerPrefs.GetFloat(P + "MinimapSize", d.minimapSize);
        d.hudOpacity = PlayerPrefs.GetFloat(P + "HudOpacity", d.hudOpacity);
        d.colorBlind = Mathf.Clamp(PlayerPrefs.GetInt(P + "ColorBlind", d.colorBlind), 0, ColorBlindNames.Length - 1);
        return d;
    }

    /// <summary>값을 저장하고 전부 적용 (설정 패널의 "적용")</summary>
    public void Apply(Data d)
    {
        bool languageChanged = Current == null || Current.language != d.language;
        Current = d.Clone();
        PreviewSound(d); // AudioManager가 볼륨 키를 PlayerPrefs에 같이 저장
        PlayerPrefs.SetFloat(P + "MouseSensitivity", d.mouseSensitivity);
        PlayerPrefs.SetFloat(P + "CameraRotation", d.cameraRotation);
        PlayerPrefs.SetInt(P + "ScreenShake", d.screenShake ? 1 : 0);
        PlayerPrefs.SetInt(P + "ResolutionWidth", d.resolutionWidth);
        PlayerPrefs.SetInt(P + "ResolutionHeight", d.resolutionHeight);
        PlayerPrefs.SetInt(P + "ScreenMode", d.screenMode);
        PlayerPrefs.SetInt(P + "Quality", d.quality);
        PlayerPrefs.SetInt(P + "VSync", d.vsync ? 1 : 0);
        PlayerPrefs.SetInt(P + "Shadows", d.shadows);
        PlayerPrefs.SetInt(P + "Language", d.language);
        PlayerPrefs.SetFloat(P + "MinimapSize", d.minimapSize);
        PlayerPrefs.SetFloat(P + "HudOpacity", d.hudOpacity);
        PlayerPrefs.SetInt(P + "ColorBlind", d.colorBlind);
        PlayerPrefs.Save();
        ApplyGraphics(d);
        ApplyGameplay(d);
        if (languageChanged) Localization.NotifyChanged(); // 열려 있는 메뉴 글자를 바로 바꿈
    }

    // ── 적용 ───────────────────────────────────────
    /// <summary>사운드만 즉시 반영 (슬라이더를 움직이는 동안 실시간으로 들리게)</summary>
    public static void PreviewSound(Data d)
    {
        AudioManager.SetMasterVolume(d.master / 100f);
        AudioManager.SetChannelVolume(AudioManager.Channel.BGM, d.bgm / 100f);
        AudioManager.SetChannelVolume(AudioManager.Channel.SFX, d.sfx / 100f);
        AudioManager.SetChannelVolume(AudioManager.Channel.Zombie, d.zombie / 100f);
        AudioManager.SetChannelVolume(AudioManager.Channel.UI, d.ui / 100f);
    }

    static void ApplyGraphics(Data d)
    {
        // 품질 레벨을 바꾸면 그림자·VSync가 그 레벨 값으로 덮이므로 레벨 먼저
        if (QualitySettings.GetQualityLevel() != d.quality) QualitySettings.SetQualityLevel(d.quality, true);
        QualitySettings.vSyncCount = d.vsync ? 1 : 0;
        switch (d.shadows)
        {
            case 0: QualitySettings.shadows = ShadowQuality.Disable; break;
            case 1: QualitySettings.shadows = ShadowQuality.HardOnly; QualitySettings.shadowResolution = ShadowResolution.Low; break;
            case 2: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.Medium; break;
            case 3: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.High; break;
            default: QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowResolution = ShadowResolution.VeryHigh; break;
        }
#if !UNITY_EDITOR
        // 에디터 Game 뷰에서는 해상도/전체화면 전환이 의미 없어서 빌드에서만
        var mode = d.screenMode == 0 ? FullScreenMode.ExclusiveFullScreen : d.screenMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        if (d.resolutionWidth > 0 && d.resolutionHeight > 0) Screen.SetResolution(d.resolutionWidth, d.resolutionHeight, mode);
        else Screen.fullScreenMode = mode;
#endif
    }

    static void ApplyGameplay(Data d)
    {
        CameraZoom.WheelSensitivity = d.mouseSensitivity;
        CameraRotate.SpeedMultiplier = d.cameraRotation;
        if (!WorldDecor.IsGameScene) return; // 메인 메뉴·학습장에는 HUD가 없음 (메뉴 캔버스가 흐려지면 안 됨)

        // 미니맵 크기: 미니맵 패널(MinimapController.playerMarker의 부모)을 원래 크기 기준으로 확대/축소
        var mm = Object.FindFirstObjectByType<MinimapController>();
        if (mm != null && mm.playerMarker != null && mm.playerMarker.parent is RectTransform panel)
        {
            if (!minimapBaseScale.TryGetValue(panel, out Vector3 baseScale))
            {
                baseScale = panel.localScale;
                minimapBaseScale[panel] = baseScale;
            }
            panel.localScale = baseScale * (d.minimapSize / 100f);
        }

        // HUD 투명도: 게임 HUD 캔버스들 (인벤토리 창·우클릭 메뉴·조준선·메뉴 화면은 제외)
        float alpha = Mathf.Clamp01(d.hudOpacity / 100f);
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (c.sortingOrder >= 1000 || c.GetComponentInChildren<PauseMenuController>(true) != null) continue; // 메뉴·사망 화면
            if (c.transform.Find("MinimapPanel") != null) // 메인 HUD 캔버스 (씬의 "Canvas")
            {
                // 메인 HUD 캔버스: 자식별로 (인벤토리 등은 그대로 1)
                foreach (Transform child in c.transform)
                {
                    bool exclude = child.name == "InventoryWindow" || child.name == "ContextMenuPanel" || child.name == "Crosshair";
                    SetAlpha(child.gameObject, exclude ? 1f : alpha);
                }
            }
            else SetAlpha(c.gameObject, alpha);
        }
    }

    static void SetAlpha(GameObject go, float a)
    {
        var g = go.GetComponent<CanvasGroup>();
        if (g == null)
        {
            if (Mathf.Approximately(a, 1f)) return;
            g = go.AddComponent<CanvasGroup>();
        }
        g.alpha = a;
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 패널 (SettingsPanel.prefab) - 사운드 / 게임플레이 / 그래픽 / 접근성 2×2.
/// 메인 메뉴와 일시정지 메뉴가 같은 프리팹을 씀.
///  - 열 때 SettingsManager.Current를 복사해서 편집 (working)
///  - 사운드 슬라이더는 움직이는 즉시 AudioMixer에 반영 (미리 듣기)
///  - "적용": 저장 + 전부 적용 / "기본값 복원": 기본값으로 채움 (적용을 눌러야 저장)
///  - "뒤로"(또는 ESC): 적용 안 한 변경은 버림 (미리 듣던 볼륨도 원래대로)
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [System.Serializable]
    public class SliderRow
    {
        public Slider slider;
        public TextMeshProUGUI value;
    }

    [Header("사운드")]
    public SliderRow master;
    public SliderRow bgm, sfx, zombie, ui;
    [Header("게임플레이")]
    public SliderRow mouseSensitivity;
    public SliderRow cameraRotation;
    public Toggle screenShake;
    [Header("그래픽")]
    public TMP_Dropdown resolution;
    public TMP_Dropdown screenMode, quality, shadows;
    public Toggle vsync;
    [Header("접근성")]
    public TMP_Dropdown language;
    public SliderRow minimapSize, hudOpacity;
    public TMP_Dropdown colorBlind;
    [Header("버튼")]
    public Button backButton;
    public Button defaultsButton, applyButton;

    /// <summary>뒤로/ESC로 닫혔을 때</summary>
    public System.Action onClosed;
    public bool IsOpen => gameObject.activeSelf;

    SettingsManager.Data working;
    readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    bool filling; // UI를 채우는 중에는 값 변경 이벤트 무시

    void Awake()
    {
        SetupSlider(master, 0, 100, true, v => working.master = v, true);
        SetupSlider(bgm, 0, 100, true, v => working.bgm = v, true);
        SetupSlider(sfx, 0, 100, true, v => working.sfx = v, true);
        SetupSlider(zombie, 0, 100, true, v => working.zombie = v, true);
        SetupSlider(ui, 0, 100, true, v => working.ui = v, true);
        SetupSlider(mouseSensitivity, 0.1f, 3f, false, v => working.mouseSensitivity = v, false);
        SetupSlider(cameraRotation, 0.5f, 3f, false, v => working.cameraRotation = v, false);
        SetupSlider(minimapSize, 50, 150, true, v => working.minimapSize = v, false);
        SetupSlider(hudOpacity, 20, 100, true, v => working.hudOpacity = v, false);

        if (screenShake != null) screenShake.onValueChanged.AddListener(v => { if (!filling) working.screenShake = v; });
        if (vsync != null) vsync.onValueChanged.AddListener(v => { if (!filling) working.vsync = v; });

        SetOptions(screenMode, SettingsManager.ScreenModeNames);
        SetOptions(quality, QualityOptions());
        SetOptions(shadows, SettingsManager.ShadowNames);
        SetOptions(language, SettingsManager.LanguageNames);
        SetOptions(colorBlind, SettingsManager.ColorBlindNames);
        Listen(screenMode, v => working.screenMode = v);
        Listen(quality, v => working.quality = v);
        Listen(shadows, v => working.shadows = v);
        Listen(language, v => working.language = v);
        Listen(colorBlind, v => working.colorBlind = v);
        Listen(resolution, v => { if (v >= 0 && v < resolutions.Count) { working.resolutionWidth = resolutions[v].x; working.resolutionHeight = resolutions[v].y; } });

        if (backButton != null) backButton.onClick.AddListener(Close);
        if (defaultsButton != null) defaultsButton.onClick.AddListener(RestoreDefaults);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyNow);
    }

    // ── 열기 / 닫기 ─────────────────────────────────
    public void Open()
    {
        gameObject.SetActive(true);
        UIAudio.Play(UIAudio.Sound.PanelOpen);
        var sm = SettingsManager.Instance;
        working = sm != null ? sm.Current.Clone() : SettingsManager.Defaults();
        Fill();
    }

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        // 적용 안 한 볼륨 미리 듣기 되돌림
        if (SettingsManager.Instance != null) SettingsManager.PreviewSound(SettingsManager.Instance.Current);
        gameObject.SetActive(false);
        UIAudio.Play(UIAudio.Sound.PanelClose);
        onClosed?.Invoke();
    }

    void RestoreDefaults()
    {
        working = SettingsManager.Defaults();
        Fill();
        SettingsManager.PreviewSound(working);
    }

    void ApplyNow()
    {
        if (SettingsManager.Instance != null) SettingsManager.Instance.Apply(working);
        working = working.Clone();
    }

    // ── UI 채우기 ───────────────────────────────────
    void Fill()
    {
        filling = true;
        SetSlider(master, working.master);
        SetSlider(bgm, working.bgm);
        SetSlider(sfx, working.sfx);
        SetSlider(zombie, working.zombie);
        SetSlider(ui, working.ui);
        SetSlider(mouseSensitivity, working.mouseSensitivity);
        SetSlider(cameraRotation, working.cameraRotation);
        SetSlider(minimapSize, working.minimapSize);
        SetSlider(hudOpacity, working.hudOpacity);
        SetToggle(screenShake, working.screenShake);
        SetToggle(vsync, working.vsync);

        FillResolutions();
        SetDropdown(screenMode, working.screenMode);
        SetDropdown(quality, working.quality);
        SetDropdown(shadows, working.shadows);
        SetDropdown(language, working.language);
        SetDropdown(colorBlind, working.colorBlind);
        filling = false;
    }

    void FillResolutions()
    {
        if (resolution == null) return;
        resolutions.Clear();
        foreach (var r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (!resolutions.Contains(v)) resolutions.Add(v);
        }
        var cur = new Vector2Int(working.resolutionWidth, working.resolutionHeight);
        if (cur.x > 0 && !resolutions.Contains(cur)) resolutions.Add(cur);
        if (resolutions.Count == 0) resolutions.Add(new Vector2Int(1920, 1080));
        resolutions.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y)); // 큰 해상도부터
        var names = new List<string>();
        foreach (var r in resolutions) names.Add($"{r.x}×{r.y}");
        SetOptions(resolution, names.ToArray());
        int idx = resolutions.IndexOf(cur);
        SetDropdown(resolution, idx >= 0 ? idx : 0);
    }

    static string[] QualityOptions()
    {
        int n = QualitySettings.names.Length;
        var names = new string[n];
        for (int i = 0; i < n; i++)
            names[i] = n == SettingsManager.QualityNames.Length ? SettingsManager.QualityNames[i] : QualitySettings.names[i];
        return names;
    }

    // ── 헬퍼 ───────────────────────────────────────
    void SetupSlider(SliderRow row, float min, float max, bool whole, System.Action<float> set, bool sound)
    {
        if (row == null || row.slider == null) return;
        row.slider.minValue = min;
        row.slider.maxValue = max;
        row.slider.wholeNumbers = whole;
        row.slider.onValueChanged.AddListener(v =>
        {
            UpdateValueText(row);
            if (filling) return;
            set(v);
            UIAudio.SliderTick(row.slider.normalizedValue); // 미세 틱
            if (sound) SettingsManager.PreviewSound(working); // 실시간 반영
        });
    }

    static void SetSlider(SliderRow row, float v)
    {
        if (row == null || row.slider == null) return;
        row.slider.SetValueWithoutNotify(v);
        UpdateValueText(row);
    }

    static void UpdateValueText(SliderRow row)
    {
        if (row.value == null) return;
        float v = row.slider.value;
        bool percent = row.slider.maxValue > 100f || row.slider.minValue >= 20f; // 미니맵 50~150 / HUD 20~100
        row.value.text = !row.slider.wholeNumbers ? v.ToString("0.0") : percent ? $"{Mathf.RoundToInt(v)}%" : Mathf.RoundToInt(v).ToString();
    }

    static void SetToggle(Toggle t, bool on)
    {
        if (t == null) return;
        t.SetIsOnWithoutNotify(on);
        var sw = t.GetComponent<UIToggleSwitch>();
        if (sw != null) sw.Refresh();
    }

    static void SetOptions(TMP_Dropdown d, string[] names)
    {
        if (d == null) return;
        d.ClearOptions();
        d.AddOptions(new List<string>(names));
    }

    static void SetDropdown(TMP_Dropdown d, int v)
    {
        if (d == null) return;
        d.SetValueWithoutNotify(Mathf.Clamp(v, 0, Mathf.Max(0, d.options.Count - 1)));
        d.RefreshShownValue();
    }

    void Listen(TMP_Dropdown d, System.Action<int> set)
    {
        if (d == null) return;
        d.onValueChanged.AddListener(v => { if (!filling) set(v); });
    }
}

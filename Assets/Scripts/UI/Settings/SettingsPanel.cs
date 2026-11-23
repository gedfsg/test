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

        SetOptions(language, SettingsManager.LanguageNames);
        SetTranslatedOptions();
        Listen(screenMode, v => working.screenMode = v);
        Listen(quality, v => working.quality = v);
        Listen(shadows, v => working.shadows = v);
        Listen(language, v => working.language = v);
        Listen(colorBlind, v => working.colorBlind = v);
        Listen(resolution, v => { if (v >= 0 && v < resolutions.Count) { working.resolutionWidth = resolutions[v].x; working.resolutionHeight = resolutions[v].y; } });

        if (backButton != null) backButton.onClick.AddListener(Close);
        if (defaultsButton != null) defaultsButton.onClick.AddListener(RestoreDefaults);
        if (applyButton != null) applyButton.onClick.AddListener(ApplyNow);

        AddTutorialRows(); // (2026-11-23) 게임플레이 카드에 튜토리얼 ON/OFF + 다시 보기 (언어 등록 전에 만들어야 번역됨)

        // 언어: 패널 글자 등록 + 적용으로 언어가 바뀌면 드롭다운 목록도 다시 채움
        Localization.Register(gameObject);
        Localization.Changed += OnLanguageChanged;
        // 영어 "Restore Defaults"는 버튼 폭에 꽉 차서 넘치면 글자를 줄임
        var defLabel = defaultsButton != null ? defaultsButton.GetComponent<UIButton>()?.label : null;
        if (defLabel != null)
        {
            defLabel.margin = new Vector4(10f, 0f, 10f, 0f);
            defLabel.fontSizeMax = defLabel.fontSize;
            defLabel.fontSizeMin = 16f;
            defLabel.enableAutoSizing = true;
        }
    }

    void OnDestroy() => Localization.Changed -= OnLanguageChanged;

    void SetTranslatedOptions()
    {
        SetOptions(screenMode, Translate(SettingsManager.ScreenModeNames));
        SetOptions(quality, Translate(QualityOptions()));
        SetOptions(shadows, Translate(SettingsManager.ShadowNames));
        SetOptions(colorBlind, Translate(SettingsManager.ColorBlindNames));
    }

    static string[] Translate(string[] names)
    {
        var r = new string[names.Length];
        for (int i = 0; i < names.Length; i++) r[i] = Localization.T(names[i]);
        return r;
    }

    void OnLanguageChanged()
    {
        SetTranslatedOptions();
        if (IsOpen && working != null) Fill(); // 선택값 다시 표시
    }

    // ── 열기 / 닫기 ─────────────────────────────────
    public void Open()
    {
        gameObject.SetActive(true);
        UIAudio.Play(UIAudio.Sound.PanelOpen);
        var sm = SettingsManager.Instance;
        working = sm != null ? sm.Current.Clone() : SettingsManager.Defaults();
        Fill();
        SetToggle(tutorialToggle, TutorialManager.Enabled);
    }

    // ── 튜토리얼 (2026-11-23) ────────────────────────
    // 프리팹(MenuUIBuilder)을 다시 빌드하지 않고 런타임에 게임플레이 카드를 3줄 → 5줄로 다시 배치하고 2줄을 추가.
    // 값은 저장하지 않음 (세션 기준 - TutorialManager): 누르는 즉시 반영, "적용"과 무관
    Toggle tutorialToggle;

    void AddTutorialRows()
    {
        var gp = transform.Find("Content/Gameplay") as RectTransform;
        if (gp == null || screenShake == null || defaultsButton == null) return;

        // 기존 3줄(중심 102/179/256)과 구분선(140/217)을 사운드 카드와 같은 5줄 간격(87/133/179/226/272)으로 올림
        foreach (RectTransform c in gp)
        {
            float top = -c.anchoredPosition.y;
            if (top < 70f) continue; // 헤더 (제목·아이콘·윗선)
            float shift;
            if (c.name == "Line") shift = top < 180f ? 30f : 61f;   // 140→110, 217→156
            else shift = top < 125f ? 15f : top < 205f ? 46f : 77f;  // 102→87, 179→133, 256→179
            c.anchoredPosition += new Vector2(0f, shift);
        }
        Transform label = null, line = null, switchLabel = null;
        foreach (Transform c in gp)
        {
            if (c.name == "Label" && label == null) label = c;
            if (c.name == "Line" && -((RectTransform)c).anchoredPosition.y > 70f) line = c;
            if (c.name == "SwitchLabel") switchLabel = c;
        }
        if (label == null || line == null || switchLabel == null) return;

        // 구분선 2개 (202, 249)
        foreach (float y in new[] { 202f, 249f }) SetTop(Instantiate(line, gp), y);

        // 4줄: 튜토리얼 ON/OFF (화면 흔들림 스위치 복제)
        SetText(SetTop(Instantiate(label, gp), 226f - 17.6f), "튜토리얼");
        var sw = SetTop(Instantiate(screenShake.transform, gp), 226f - 14f);
        var swLabel = SetTop(Instantiate(switchLabel, gp), 226f - 14.4f);
        var swComp = sw.GetComponent<UIToggleSwitch>();
        if (swComp != null) swComp.stateLabel = swLabel.GetComponent<TextMeshProUGUI>(); // 복제본은 원래 라벨을 가리키므로 새 라벨로
        tutorialToggle = sw.GetComponent<Toggle>();
        tutorialToggle.onValueChanged.RemoveAllListeners();
        tutorialToggle.onValueChanged.AddListener(v =>
        {
            TutorialManager.Enabled = v;
            if (swComp != null) swComp.Refresh();
        });

        // 5줄: 튜토리얼 다시 보기 (기본값 복원 버튼 복제, 작게)
        SetText(SetTop(Instantiate(label, gp), 272f - 17.6f), "튜토리얼 다시 보기");
        var btn = Instantiate(defaultsButton.transform, gp) as RectTransform;
        btn.name = "TutorialReplayButton";
        btn.anchorMin = btn.anchorMax = btn.pivot = new Vector2(0f, 1f);
        btn.sizeDelta = new Vector2(150f, 34f);
        SetTop(btn, 272f - 17f).anchoredPosition = new Vector2(570f, btn.anchoredPosition.y);
        var ub = btn.GetComponent<UIButton>();
        if (ub != null && ub.label != null)
        {
            SetText(ub.label.transform, "다시 보기");
            ub.label.enableAutoSizing = false;
            ub.label.fontSize = 18f;
            ub.label.alignment = TextAlignmentOptions.Center;
            var lr = ub.label.rectTransform; // 버튼 전체에 맞춰 가운데
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.pivot = new Vector2(0.5f, 0.5f);
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            ub.label.margin = Vector4.zero;
        }
        var b = btn.GetComponent<Button>();
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() =>
        {
            TutorialManager.ResetSeen();
            SetToggle(tutorialToggle, TutorialManager.Enabled);
            if (ub != null && ub.label != null) StartCoroutine(FlashLabel(ub.label, "다시 보기"));
        });
    }

    static RectTransform SetTop(Transform t, float top)
    {
        var rt = (RectTransform)t;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -top);
        return rt;
    }

    static void SetText(Transform t, string s)
    {
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp == null) return;
        var lt = tmp.GetComponent<LocalizedText>();
        // 복제 원본 글자를 기억하고 있으므로 지움 → Register가 새로 붙임 (Destroy는 프레임 끝이라 바로 뒤 Register가 옛 글자로 되돌림)
        if (lt != null) DestroyImmediate(lt);
        tmp.text = s;
    }

    // 눌렀다는 표시: "초기화됨" 1초
    System.Collections.IEnumerator FlashLabel(TextMeshProUGUI label, string original)
    {
        label.text = Localization.IsEnglish ? "Reset!" : "초기화됨";
        yield return new WaitForSecondsRealtime(1f);
        label.text = Localization.T(original);
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
        // 눌렀다는 표시: 잠깐 눌림 + "적용됨" (예전엔 소리만 나서 안 눌리는 것처럼 보였음)
        if (applyFeedback != null) StopCoroutine(applyFeedback);
        applyFeedback = StartCoroutine(ApplyFeedback());
    }

    Coroutine applyFeedback;

    System.Collections.IEnumerator ApplyFeedback()
    {
        var t = applyButton.transform;
        var ub = applyButton.GetComponent<UIButton>();
        var label = ub != null ? ub.label : null;
        if (label != null) label.text = Localization.T("적용됨");
        // 일시정지 중(timeScale 0)에도 움직이게 unscaled 시간
        for (float e = 0f; e < 0.15f; e += Time.unscaledDeltaTime)
        {
            t.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, e / 0.15f);
            yield return null;
        }
        t.localScale = Vector3.one;
        yield return new WaitForSecondsRealtime(1.1f);
        RestoreApplyLabel();
        applyFeedback = null;
    }

    void RestoreApplyLabel()
    {
        applyButton.transform.localScale = Vector3.one;
        var ub = applyButton.GetComponent<UIButton>();
        if (ub == null || ub.label == null) return;
        var lt = ub.label.GetComponent<LocalizedText>();
        if (lt != null) lt.Refresh();
        else ub.label.text = Localization.T("적용");
    }

    void OnDisable()
    {
        // 표시 중에 닫히면 코루틴이 멈추므로 글자를 바로 되돌림
        if (applyFeedback != null) { applyFeedback = null; if (applyButton != null) RestoreApplyLabel(); }
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

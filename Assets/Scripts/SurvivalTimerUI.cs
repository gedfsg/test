using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 프로스트펑크/사이버펑크 스타일 화면 상단 중앙 생존 타이머 HUD.
/// 전부 코드로 생성(프로시저럴 스프라이트, 외부 에셋 없음). DayNightCycle.Instance를
/// 매 프레임 읽기만 해서 갱신 - DayNightCycle 쪽은 전혀 안 건드림.
///
/// 구성: 다중 레이어 원형 Day 배지(바깥 링+안쪽 글로우, 밤엔 빨갛게 경고 펄스) +
/// 좌우 낮/밤 바(눈금, 해/달 아이콘, 화살표, 현재 페이즈 쪽에 흐르는 하이라이트) +
/// 이동 마커 + 큰 시계 + 생존율(%) 바 + 네온 프레임(코너 브라켓).
/// </summary>
public class SurvivalTimerUI : MonoBehaviour
{
    [Header("패널 크기")]
    public float panelWidth = 820f;
    public float panelHeight = 266f;
    public float circleDiameter = 140f;
    public float barWidth = 290f;
    public float barHeight = 16f;

    [Header("색상")]
    public Color dayColor    = new Color(0.98f, 0.8f, 0.18f);
    public Color dayColorDim = new Color(0.5f, 0.42f, 0.14f, 0.5f);
    public Color nightColor  = new Color(0.2f, 0.5f, 0.95f);
    public Color nightColorDim = new Color(0.14f, 0.24f, 0.42f, 0.5f);
    public Color panelBg     = new Color(0.03f, 0.05f, 0.09f, 0.82f);
    public Color frameColor  = new Color(0.2f, 0.95f, 1f, 0.9f);
    public Color cyanAccent  = new Color(0.25f, 0.95f, 1f);
    public Color dangerColor = new Color(1f, 0.15f, 0.2f);

    [Header("생존 목표")]
    public int survivalGoalDays = 7;

    // 런타임 참조
    RectTransform markerRect;
    TextMeshProUGUI dayNumberText, clockText, survivalTimeLabel, survivalPctText, goalText;
    Image innerGlow, outerRing, midRing;
    RectTransform dayHighlight, nightHighlight;
    Image survivalBarFill;
    AudioSource audioSource;
    AudioClip beepLow, beepHigh;

    DayNightCycle.Phase lastPhase;
    int lastDay;
    float phasePulseTimer;
    float dayFlashTimer;
    float idlePulseT;

    void Start()
    {
        Build();
        var dnc = DayNightCycle.Instance;
        lastPhase = dnc != null ? dnc.CurrentPhase : DayNightCycle.Phase.Day;
        lastDay   = dnc != null ? dnc.CurrentDay : 1;
    }

    void Update()
    {
        var dnc = DayNightCycle.Instance;
        if (dnc == null) return;

        UpdateMarkerAndBars(dnc);
        UpdateCircleText(dnc);
        UpdateIdlePulse();
        UpdatePhaseTransition(dnc);
        UpdateDayTransition(dnc);
        UpdateNightWarning(dnc);
        UpdateFlowingHighlights(dnc);
    }

    // ── 매 프레임 갱신 ──────────────────────────────────────────
    void UpdateMarkerAndBars(DayNightCycle dnc)
    {
        float progress = dnc.GetCycleProgress01();
        float dayFraction = dnc.dayDuration / dnc.GetTotalCycleDuration();

        float x;
        if (progress < dayFraction)
        {
            float t = dayFraction > 0f ? progress / dayFraction : 0f;
            x = Mathf.Lerp(-panelWidth / 2f + 50f, -barWidth / 2f - 60f, 0f); // placeholder unused
            x = Mathf.Lerp(GetBarLeftEdgeX(true), GetBarRightEdgeX(true), t);
        }
        else
        {
            float t = (1f - dayFraction) > 0f ? (progress - dayFraction) / (1f - dayFraction) : 0f;
            x = Mathf.Lerp(GetBarLeftEdgeX(false), GetBarRightEdgeX(false), t);
        }
        markerRect.anchoredPosition = new Vector2(x, markerRect.anchoredPosition.y);
    }

    float GetBarLeftEdgeX(bool day)  => day ? -(circleDiameter / 2f + barWidth) : (circleDiameter / 2f);
    float GetBarRightEdgeX(bool day) => day ? -(circleDiameter / 2f) : (circleDiameter / 2f + barWidth);

    void UpdateCircleText(DayNightCycle dnc)
    {
        dayNumberText.text = dnc.CurrentDay.ToString();
        var (hour, minute) = dnc.GetVirtualClock();
        clockText.text = $"{hour:00}:{minute:00}";
        survivalPctText.text = $"생존율 {Mathf.RoundToInt(Mathf.Clamp01((dnc.CurrentDay - 1f) / dnc.totalDays) * 100f)}%";
        survivalBarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((dnc.CurrentDay - 1f) / dnc.totalDays), 1f);
        goalText.text = $"목표 {survivalGoalDays}일";
    }

    // 중앙 원 미세 pulse (항상)
    void UpdateIdlePulse()
    {
        idlePulseT += Time.deltaTime;
        float s = 1f + Mathf.Sin(idlePulseT * 1.6f) * 0.015f;
        outerRing.transform.localScale = Vector3.one * s;
    }

    // 페이즈 전환 시 배지 테두리 pulse
    void UpdatePhaseTransition(DayNightCycle dnc)
    {
        if (dnc.CurrentPhase != lastPhase)
        {
            lastPhase = dnc.CurrentPhase;
            phasePulseTimer = 0.5f;
        }
        if (phasePulseTimer > 0f)
        {
            phasePulseTimer -= Time.deltaTime;
            float s = 1f + Mathf.Sin(phasePulseTimer / 0.5f * Mathf.PI) * 0.18f;
            midRing.transform.localScale = Vector3.one * s;
        }
        else
        {
            midRing.transform.localScale = Vector3.one;
        }
    }

    // Day 숫자 깜빡임 + 사운드 (새 날짜가 될 때)
    void UpdateDayTransition(DayNightCycle dnc)
    {
        if (dnc.CurrentDay != lastDay)
        {
            lastDay = dnc.CurrentDay;
            dayFlashTimer = 0.6f;
            if (audioSource != null && beepLow != null && beepHigh != null)
            {
                audioSource.PlayOneShot(beepLow, 0.5f);
                audioSource.PlayOneShot(beepHigh, 0.5f);
            }
        }
        if (dayFlashTimer > 0f)
        {
            dayFlashTimer -= Time.deltaTime;
            float f = Mathf.Clamp01(dayFlashTimer / 0.6f);
            dayNumberText.color = Color.Lerp(Color.white, cyanAccent, 1f - f);
            dayNumberText.transform.localScale = Vector3.one * (1f + f * 0.4f);
        }
    }

    // 밤이면 중앙 원 안쪽 글로우가 빨갛게 번쩍 (위험 신호)
    void UpdateNightWarning(DayNightCycle dnc)
    {
        if (dnc.CurrentPhase == DayNightCycle.Phase.Night)
        {
            float pulse = (Mathf.Sin(Time.time * 2.2f) + 1f) * 0.5f;
            innerGlow.color = Color.Lerp(new Color(cyanAccent.r, cyanAccent.g, cyanAccent.b, 0.35f), new Color(dangerColor.r, dangerColor.g, dangerColor.b, 0.55f), pulse);
        }
        else
        {
            innerGlow.color = new Color(cyanAccent.r, cyanAccent.g, cyanAccent.b, 0.3f);
        }
    }

    // 현재 페이즈 쪽 바에 흐르는 하이라이트 효과
    void UpdateFlowingHighlights(DayNightCycle dnc)
    {
        bool dayActive = dnc.CurrentPhase == DayNightCycle.Phase.Day;
        float flow = Mathf.PingPong(Time.time * 120f, barWidth);

        dayHighlight.gameObject.SetActive(dayActive);
        nightHighlight.gameObject.SetActive(!dayActive);

        if (dayActive)
            dayHighlight.anchoredPosition = new Vector2(flow, dayHighlight.anchoredPosition.y);
        else
            nightHighlight.anchoredPosition = new Vector2(flow, nightHighlight.anchoredPosition.y);
    }

    // ── 빌드 ────────────────────────────────────────────────────
    void Build()
    {
        var canvasGo = GameObject.Find("Canvas");
        var canvas = canvasGo != null ? canvasGo.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        beepLow  = BuildBeep(880f, 0.12f);
        beepHigh = BuildBeep(1320f, 0.18f);

        var panel = NewUI("SurvivalTimerPanel", canvas.transform);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -14f);
        panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

        BuildFrame(panel.transform);
        BuildBars(panel.transform);
        BuildCircleBadge(panel.transform);
        BuildTimeDisplay(panel.transform);
        BuildSurvivalBar(panel.transform);
    }

    void BuildFrame(Transform parent)
    {
        var bg = NewImage("FrameBG", parent, panelBg);
        var bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

        // 얇은 기본 테두리 + 코너에 훨씬 두꺼운 브라켓을 겹쳐서 "장식 코너"가 또렷이 튀어나와 보이게 함
        Color thinBorder = new Color(frameColor.r, frameColor.g, frameColor.b, 0.35f);
        const float t = 1.5f;
        Border("FrameTop", parent, new Vector2(0.5f, 1f), new Vector2(panelWidth, t), thinBorder);
        Border("FrameBottom", parent, new Vector2(0.5f, 0f), new Vector2(panelWidth, t), thinBorder);
        Border("FrameLeft", parent, new Vector2(0f, 0.5f), new Vector2(t, panelHeight), thinBorder);
        Border("FrameRight", parent, new Vector2(1f, 0.5f), new Vector2(t, panelHeight), thinBorder);

        CornerBracket(parent, new Vector2(0f, 1f), true, false);
        CornerBracket(parent, new Vector2(1f, 1f), false, false);
        CornerBracket(parent, new Vector2(0f, 0f), true, true);
        CornerBracket(parent, new Vector2(1f, 0f), false, true);
    }

    void Border(string name, Transform parent, Vector2 anchor, Vector2 size, Color color)
    {
        var img = NewImage(name, parent, color);
        var r = img.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = anchor;
        r.sizeDelta = size;
        r.anchoredPosition = Vector2.zero;
    }

    void CornerBracket(Transform parent, Vector2 anchor, bool left, bool bottom)
    {
        float len = 42f, th = 5f;
        var h = NewImage($"Bracket_{anchor}_H", parent, cyanAccent);
        var hr = h.GetComponent<RectTransform>();
        hr.anchorMin = hr.anchorMax = anchor;
        hr.pivot = new Vector2(left ? 0f : 1f, bottom ? 0f : 1f);
        hr.sizeDelta = new Vector2(len, th);
        hr.anchoredPosition = Vector2.zero;

        var v = NewImage($"Bracket_{anchor}_V", parent, cyanAccent);
        var vr = v.GetComponent<RectTransform>();
        vr.anchorMin = vr.anchorMax = anchor;
        vr.pivot = new Vector2(left ? 0f : 1f, bottom ? 0f : 1f);
        vr.sizeDelta = new Vector2(th, len);
        vr.anchoredPosition = Vector2.zero;
    }

    void BuildBars(Transform parent)
    {
        BuildOneBar(parent, true);
        BuildOneBar(parent, false);
    }

    void BuildOneBar(Transform parent, bool day)
    {
        string side = day ? "Day" : "Night";
        Color baseColor = day ? dayColor : nightColor;
        Color dimColor  = day ? dayColorDim : nightColorDim;
        float centerX = day ? -(circleDiameter / 2f + barWidth / 2f) : (circleDiameter / 2f + barWidth / 2f);
        float y = -(circleDiameter / 2f + 20f);

        var track = NewImage($"{side}BarTrack", parent, new Color(0.06f, 0.08f, 0.12f, 0.8f));
        var trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 1f);
        trackRect.pivot = new Vector2(0.5f, 0.5f);
        trackRect.anchoredPosition = new Vector2(centerX, y);
        trackRect.sizeDelta = new Vector2(barWidth, barHeight);

        var fill = NewImage($"{side}BarFill", track.transform, dimColor);
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f); fillRect.offsetMax = new Vector2(-2f, -2f);

        // 흐르는 하이라이트 (활성 페이즈일 때만 보임)
        var highlight = NewImage($"{side}Highlight", track.transform, new Color(1f, 1f, 1f, 0.35f));
        var hlRect = highlight.GetComponent<RectTransform>();
        hlRect.anchorMin = new Vector2(0f, 0f); hlRect.anchorMax = new Vector2(0f, 1f);
        hlRect.pivot = new Vector2(0f, 0.5f);
        hlRect.sizeDelta = new Vector2(26f, 0f);
        hlRect.anchoredPosition = new Vector2(0f, 0f);
        if (day) dayHighlight = hlRect; else nightHighlight = hlRect;

        // 눈금
        int tickCount = 6;
        for (int i = 1; i < tickCount; i++)
        {
            var tick = NewImage($"{side}Tick{i}", track.transform, new Color(0f, 0f, 0f, 0.35f));
            var tr = tick.GetComponent<RectTransform>();
            tr.anchorMin = tr.anchorMax = new Vector2((float)i / tickCount, 0.5f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(1.5f, barHeight - 2f);
            tr.anchoredPosition = Vector2.zero;
        }

        // 바 바깥쪽 끝 아이콘 (해/달)
        var icon = day ? BuildSunIcon(parent) : BuildMoonIcon(parent);
        var iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        float iconX = day ? centerX - barWidth / 2f - 24f : centerX + barWidth / 2f + 24f;
        iconRect.anchoredPosition = new Vector2(iconX, y);
        iconRect.sizeDelta = new Vector2(26f, 26f);

        // 중앙 원 쪽을 가리키는 화살표(쉐브론)
        var chevron = NewImage($"{side}Chevron", parent, baseColor);
        chevron.sprite = BuildChevronSprite(32);
        var chevRect = chevron.GetComponent<RectTransform>();
        chevRect.anchorMin = chevRect.anchorMax = new Vector2(0.5f, 1f);
        chevRect.pivot = new Vector2(0.5f, 0.5f);
        float chevX = day ? centerX + barWidth / 2f + 12f : centerX - barWidth / 2f - 12f;
        chevRect.anchoredPosition = new Vector2(chevX, y);
        chevRect.sizeDelta = new Vector2(14f, 14f);
        chevRect.localRotation = Quaternion.Euler(0f, 0f, day ? 0f : 180f);

        // 라벨
        var label = NewText($"{side}Label", parent, day ? "낮" : "밤", 14, baseColor, TextAlignmentOptions.Center);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(centerX, y - 20f);
        labelRect.sizeDelta = new Vector2(60f, 18f);

        // 이동 마커 (낮/밤 바 둘 다의 부모인 panel 밑에 공용으로 하나만 필요하므로 day쪽 호출 때 한 번만 생성)
        if (day)
        {
            var marker = NewImage("TimeMarker", parent, cyanAccent);
            marker.sprite = BuildGlowDotSprite(64);
            markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 1f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.sizeDelta = new Vector2(22f, 22f);
            markerRect.anchoredPosition = new Vector2(centerX, y);
        }
    }

    void BuildCircleBadge(Transform parent)
    {
        float y = -(circleDiameter / 2f + 20f);

        // 바깥 두꺼운 사이안 링
        var outer = NewImage("CircleOuterRing", parent, cyanAccent);
        outer.sprite = BuildRingSprite(160, 0.78f);
        outerRing = outer;
        var outerRect = outer.GetComponent<RectTransform>();
        outerRect.anchorMin = outerRect.anchorMax = new Vector2(0.5f, 1f);
        outerRect.pivot = new Vector2(0.5f, 0.5f);
        outerRect.anchoredPosition = new Vector2(0f, y);
        outerRect.sizeDelta = new Vector2(circleDiameter, circleDiameter);

        // 중간 어두운 네이비 링
        var mid = NewImage("CircleMidRing", parent, new Color(0.08f, 0.12f, 0.2f, 0.95f));
        mid.sprite = BuildRingSprite(160, 0.6f);
        midRing = mid;
        var midRect = mid.GetComponent<RectTransform>();
        midRect.anchorMin = midRect.anchorMax = new Vector2(0.5f, 1f);
        midRect.pivot = new Vector2(0.5f, 0.5f);
        midRect.anchoredPosition = new Vector2(0f, y);
        midRect.sizeDelta = new Vector2(circleDiameter * 0.86f, circleDiameter * 0.86f);

        // 안쪽 배경 (거의 꽉 채움)
        var innerBg = NewImage("CircleInnerBG", parent, new Color(0.02f, 0.03f, 0.05f, 0.96f));
        innerBg.sprite = BuildCircleSprite(160, true);
        var innerBgRect = innerBg.GetComponent<RectTransform>();
        innerBgRect.anchorMin = innerBgRect.anchorMax = new Vector2(0.5f, 1f);
        innerBgRect.pivot = new Vector2(0.5f, 0.5f);
        innerBgRect.anchoredPosition = new Vector2(0f, y);
        innerBgRect.sizeDelta = new Vector2(circleDiameter * 0.78f, circleDiameter * 0.78f);

        // 안쪽 글로우 (밤에 빨갛게 변함)
        var glow = NewImage("CircleInnerGlow", parent, new Color(cyanAccent.r, cyanAccent.g, cyanAccent.b, 0.3f));
        glow.sprite = BuildGlowSprite(128);
        innerGlow = glow;
        var glowRect = glow.GetComponent<RectTransform>();
        glowRect.anchorMin = glowRect.anchorMax = new Vector2(0.5f, 1f);
        glowRect.pivot = new Vector2(0.5f, 0.5f);
        glowRect.anchoredPosition = new Vector2(0f, y);
        glowRect.sizeDelta = new Vector2(circleDiameter * 0.74f, circleDiameter * 0.74f);

        // 하단 호 장식 (작은 대시들)
        for (int i = -2; i <= 2; i++)
        {
            float angleDeg = 180f + i * 14f; // 원 아래쪽 부채꼴
            float rad = angleDeg * Mathf.Deg2Rad;
            float r = circleDiameter * 0.47f;
            Vector2 pos = new Vector2(Mathf.Sin(rad) * r, -Mathf.Cos(rad) * r);
            var dash = NewImage($"ArcDash{i}", parent, new Color(cyanAccent.r, cyanAccent.g, cyanAccent.b, 0.55f));
            var dr = dash.GetComponent<RectTransform>();
            dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 1f);
            dr.pivot = new Vector2(0.5f, 0.5f);
            dr.anchoredPosition = new Vector2(pos.x, y + pos.y);
            dr.sizeDelta = new Vector2(2.5f, 8f);
            dr.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);
        }

        var dayLabelSmall = NewText("DaySmallLabel", parent, "DAY", 14, cyanAccent, TextAlignmentOptions.Center);
        dayLabelSmall.fontStyle = FontStyles.Bold;
        dayLabelSmall.characterSpacing = 4f;
        var dsRect = dayLabelSmall.GetComponent<RectTransform>();
        dsRect.anchorMin = dsRect.anchorMax = new Vector2(0.5f, 1f);
        dsRect.pivot = new Vector2(0.5f, 0.5f);
        dsRect.anchoredPosition = new Vector2(0f, y + 38f);
        dsRect.sizeDelta = new Vector2(circleDiameter, 16f);

        dayNumberText = NewText("DayNumber", parent, "1", 42, Color.white, TextAlignmentOptions.Center);
        dayNumberText.fontStyle = FontStyles.Bold;
        var dayNumRect = dayNumberText.GetComponent<RectTransform>();
        dayNumRect.anchorMin = dayNumRect.anchorMax = new Vector2(0.5f, 1f);
        dayNumRect.pivot = new Vector2(0.5f, 0.5f);
        dayNumRect.anchoredPosition = new Vector2(0f, y + 10f);
        dayNumRect.sizeDelta = new Vector2(circleDiameter, 48f);

        goalText = NewText("GoalText", parent, "목표 7일", 13, new Color(0.7f, 0.85f, 0.9f, 0.85f), TextAlignmentOptions.Center);
        var goalRect = goalText.GetComponent<RectTransform>();
        goalRect.anchorMin = goalRect.anchorMax = new Vector2(0.5f, 1f);
        goalRect.pivot = new Vector2(0.5f, 0.5f);
        goalRect.anchoredPosition = new Vector2(0f, y - 22f);
        goalRect.sizeDelta = new Vector2(circleDiameter, 14f);
    }

    void BuildTimeDisplay(Transform parent)
    {
        float topY = -(circleDiameter + 48f);

        clockText = NewText("ClockText", parent, "06:00", 30, cyanAccent, TextAlignmentOptions.Center);
        clockText.fontStyle = FontStyles.Bold;
        var clockRect = clockText.GetComponent<RectTransform>();
        clockRect.anchorMin = clockRect.anchorMax = new Vector2(0.5f, 1f);
        clockRect.pivot = new Vector2(0.5f, 1f);
        clockRect.anchoredPosition = new Vector2(0f, topY);
        clockRect.sizeDelta = new Vector2(200f, 38f);

        survivalTimeLabel = NewText("SurvivalTimeLabel", parent, "SURVIVAL TIME", 13, new Color(0.6f, 0.75f, 0.8f, 0.8f), TextAlignmentOptions.Center);
        survivalTimeLabel.characterSpacing = 3f;
        var stRect = survivalTimeLabel.GetComponent<RectTransform>();
        stRect.anchorMin = stRect.anchorMax = new Vector2(0.5f, 1f);
        stRect.pivot = new Vector2(0.5f, 1f);
        stRect.anchoredPosition = new Vector2(0f, topY - 34f);
        stRect.sizeDelta = new Vector2(200f, 14f);
    }

    void BuildSurvivalBar(Transform parent)
    {
        float y = -(circleDiameter + 94f);
        float w = 260f;

        var track = NewImage("SurvivalBarTrack", parent, new Color(0.06f, 0.08f, 0.12f, 0.8f));
        var trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 1f);
        trackRect.pivot = new Vector2(0.5f, 0.5f);
        trackRect.anchoredPosition = new Vector2(0f, y);
        trackRect.sizeDelta = new Vector2(w, 6f);

        var fill = NewImage("SurvivalBarFill", track.transform, cyanAccent);
        survivalBarFill = fill;
        var fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;

        survivalPctText = NewText("SurvivalPctText", parent, "생존율 0%", 14, new Color(0.7f, 0.85f, 0.9f, 0.9f), TextAlignmentOptions.Center);
        var pctRect = survivalPctText.GetComponent<RectTransform>();
        pctRect.anchorMin = pctRect.anchorMax = new Vector2(0.5f, 1f);
        pctRect.pivot = new Vector2(0.5f, 0.5f);
        pctRect.anchoredPosition = new Vector2(0f, y - 16f);
        pctRect.sizeDelta = new Vector2(200f, 14f);
    }

    GameObject BuildSunIcon(Transform parent)
    {
        var go = NewImage("SunIcon", parent, dayColor).gameObject;
        go.GetComponent<Image>().sprite = BuildSunSprite(64);
        return go;
    }

    GameObject BuildMoonIcon(Transform parent)
    {
        var go = NewImage("MoonIcon", parent, nightColor).gameObject;
        go.GetComponent<Image>().sprite = BuildMoonSprite(64);
        return go;
    }

    // ── 유틸 ────────────────────────────────────────────────────
    GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    Image NewImage(string name, Transform parent, Color color)
    {
        var go = NewUI(name, parent);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = NewUI(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        return tmp;
    }

    static AudioClip BuildBeep(float freq, float duration)
    {
        int sampleRate = 44100;
        int samples = Mathf.CeilToInt(sampleRate * duration);
        var clip = AudioClip.Create("Beep" + freq, samples, 1, sampleRate, false);
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float env = Mathf.Clamp01(1f - t / duration);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.35f;
        }
        clip.SetData(data, 0);
        return clip;
    }

    static Sprite BuildCircleSprite(int size, bool filled)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = Mathf.Clamp01((r - d) * 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, filled ? a : 0f));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static Sprite BuildRingSprite(int size, float innerRatio)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float outerR = size / 2f - 1f;
        float innerR = outerR * innerRatio;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float outerA = Mathf.Clamp01((outerR - d) * 2f);
                float innerA = Mathf.Clamp01((d - innerR) * 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Min(outerA, innerA)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static Sprite BuildGlowSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // 중심이 밝고 빠르게 퍼지는 글로우 느낌
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static Sprite BuildGlowDotSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float core = Mathf.Clamp01((r * 0.4f - d) * 2f);
                float glow = Mathf.Clamp01(1f - d / r) * 0.5f;
                float a = Mathf.Max(core, glow);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // 오른쪽을 가리키는 선명한 '>' 화살표. 두 변(/, \)까지의 거리로 삼각형을 직접 그림.
    static Sprite BuildChevronSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float thickness = size * 0.16f;
        Vector2 tip = new Vector2(size * 0.85f, size * 0.5f);
        Vector2 topBack = new Vector2(size * 0.2f, size * 0.08f);
        Vector2 botBack = new Vector2(size * 0.2f, size * 0.92f);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float dTop = DistToSegment(p, tip, topBack);
                float dBot = DistToSegment(p, tip, botBack);
                float a = Mathf.Clamp01(1f - Mathf.Min(dTop, dBot) / thickness);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }

    static Sprite BuildSunSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float coreR = size * 0.22f;
        float rayInner = size * 0.3f;
        float rayOuter = size * 0.46f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, c);
                float a = Mathf.Clamp01((coreR - d) * 2f);

                float ang = Mathf.Atan2(p.y - c.y, p.x - c.x);
                float rayPhase = Mathf.Repeat(ang / (Mathf.PI * 2f) * 8f, 1f);
                float rayMask = rayPhase < 0.18f ? 1f : 0f;
                if (d > rayInner && d < rayOuter)
                    a = Mathf.Max(a, rayMask * Mathf.Clamp01((rayOuter - d) * 3f));

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    static Sprite BuildMoonSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        Vector2 cutC = c + new Vector2(size * 0.18f, 0f);
        float r = size * 0.32f;
        float cutR = size * 0.3f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, c);
                float a = Mathf.Clamp01((r - d) * 2f);
                float cutD = Vector2.Distance(p, cutC);
                float cutA = Mathf.Clamp01((cutR - cutD) * 2f);
                a = Mathf.Clamp01(a - cutA);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}

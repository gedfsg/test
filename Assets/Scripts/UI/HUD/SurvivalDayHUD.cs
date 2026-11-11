using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상단 중앙 생존 날짜/시간 HUD (레퍼런스 UI 섹션 2).
///
///   ──────  생존 1일차  ──────
///   ①──②──③──④──⑤──⑥──⑦        (현재 날짜만 노란 원, 나머지 회색 테두리)
///   ☀ 12:51  │  낮  │  생존 목표 · 7일
///
/// Start에서 코드로 전부 생성하고, DayNightCycle.Instance를 매 프레임 읽기만 함.
/// 위치/크기는 레퍼런스 이미지(1681x936) 픽셀 기준 — Canvas Scaler가 같은 해상도 기준이라 그대로 맞음.
/// 이 컴포넌트는 Canvas 밑 빈 오브젝트(TopCenterHUD, 상단 중앙 앵커)에 붙임 → Tools/HUD/Apply Layout/2.
/// </summary>
public class SurvivalDayHUD : MonoBehaviour
{
    [Header("색상 (레퍼런스 샘플링)")]
    public Color currentDayFill = new Color32(0xFE, 0xBD, 0x51, 0xFF);
    public Color currentDayNumber = new Color32(30, 35, 26, 255);
    public Color dayBorder = new Color32(95, 90, 87, 255);
    public Color dayFill = new Color32(30, 29, 30, 200);
    public Color dayNumber = new Color32(185, 184, 184, 255);
    public Color connector = new Color32(82, 77, 75, 255);
    public Color titleColor = new Color32(240, 235, 225, 255);
    public Color clockColor = new Color32(0xF2, 0xE9, 0xCD, 0xFF);
    public Color dayPhaseColor = new Color32(0xF6, 0xC1, 0x56, 0xFF);
    public Color nightPhaseColor = new Color32(0x8F, 0xB6, 0xFF, 0xFF);
    [Tooltip("건물 안에서는 해/달 아이콘·낮/밤 글자·날씨 아이콘을 숨기고 '실내'로 표시 (밖이 안 보이니 밤낮도 모르게 → 긴장감)")]
    public bool hideSkyIndoors = true;
    public Color indoorPhaseColor = new Color(1f, 1f, 1f, 0.55f);
    public Color sunColor = new Color32(0xFD, 0xB8, 0x4C, 0xFF);
    public Color dividerColor = new Color32(227, 215, 193, 200);

    // 레퍼런스에서 이 블록의 중심은 화면 중앙보다 25px 왼쪽 (x≈815.5 / 1681)
    // 아래 좌표들은 전부 그 블록 중심(=이 오브젝트) 기준
    // const float CircleY = -35f, TitleY = 0f, BottomY = -81f;   // (이전) 테두리 없는 배치
    // const float CircleSize = 30f, CircleSpacing = 55f;
    // 2026-11 ui.jpg: 금색 테두리 패널 (위 가운데 "생존 N일차" 탭 / 날짜 원 / 해·달 + 시각)
    const float CircleY = -40f, TitleY = 0f, BottomY = -84f;
    const float CircleSize = 28f, CircleSpacing = 44f;
    const float FrameW = 360f, FrameH = 112f, FrameY = -58f;
    static readonly Color FrameFill = new Color(0.06f, 0.07f, 0.09f, 0.84f);
    static readonly Color FrameGold = new Color32(0xC9, 0xA2, 0x55, 0xE6);

    TextMeshProUGUI titleText, clockText, phaseText, goalText;
    TextMeshProUGUI escapeFailText; // 7일 초과 시 "탈출 실패 횟수: N"
    Image phaseIcon;
    Image phaseDivider; // 해/달 아이콘과 시각 사이 세로선 (실내에서 아이콘과 같이 숨김)
    Image weatherIcon; // 낮/밤 글자 옆 날씨 아이콘 (WeatherManager)
    WeatherManager.Weather shownWeather = (WeatherManager.Weather)(-1);
    Image[] dayFills, dayBorders;
    TextMeshProUGUI[] dayNumbers;
    int shownDay = -1;
    int totalDays = 7;
    Image titleLineL, titleLineR;
    ZombieSpawner spawner;
    DayNightCycle.Phase shownPhase;
    bool shownIndoor;

    void Start()
    {
        var dnc = DayNightCycle.Instance;
        totalDays = dnc != null ? dnc.totalDays : 7;
        spawner = FindFirstObjectByType<ZombieSpawner>();
        Build(totalDays);
        Refresh(force: true);
    }

    void Update()
    {
        Refresh(force: false);
    }

    void Build(int totalDays)
    {
        Transform root = transform;

        // (이전) 글자 뒤 은은한 어두운 그림자 (레퍼런스의 상단 어두운 그라데이션)
        // HudStyle.NewImage(root, "Shadow", HudStyle.SoftShadow, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -42f), new Vector2(560f, 170f));

        // ── 금색 테두리 패널 (모서리 깎임, 미션 패널과 같은 스타일) ──
        Sprite panelSprite = HudStyle.ChamferPanel != null ? HudStyle.ChamferPanel : HudStyle.RoundedRect;
        Sprite frameSprite = HudStyle.ChamferFrame != null ? HudStyle.ChamferFrame : HudStyle.RoundedFrame;
        // 9-slice 배율 1.6 = 테두리를 레퍼런스처럼 얇게 (MissionHUD와 같은 값)
        HudStyle.NewImage(root, "Frame", panelSprite, FrameFill, new Vector2(0f, FrameY), new Vector2(FrameW, FrameH)).pixelsPerUnitMultiplier = 1.6f;
        HudStyle.NewImage(root, "FrameBorder", frameSprite, FrameGold, new Vector2(0f, FrameY), new Vector2(FrameW, FrameH)).pixelsPerUnitMultiplier = 1.6f;
        // 위 가운데 제목 탭 (테두리 윗변 위에 걸침)
        HudStyle.NewImage(root, "TitleTab", panelSprite, FrameFill, new Vector2(0f, TitleY), new Vector2(170f, 30f)).pixelsPerUnitMultiplier = 1.6f;
        HudStyle.NewImage(root, "TitleTabBorder", frameSprite, FrameGold, new Vector2(0f, TitleY), new Vector2(170f, 30f)).pixelsPerUnitMultiplier = 1.6f;

        // ── 제목 ──
        titleText = HudStyle.NewText(root, "Title", "생존 1일차", HudStyle.SemiBold, 17f, titleColor,
            TextAlignmentOptions.Center, new Vector2(0f, TitleY), new Vector2(200f, 30f));
        // 7일 초과: 테두리 아래에 "탈출 실패 횟수: N" (2026-11-12)
        escapeFailText = HudStyle.NewText(root, "EscapeFail", "", HudStyle.SemiBold, 14f, new Color32(0xFF, 0x6B, 0x5E, 0xFF),
            TextAlignmentOptions.Center, new Vector2(0f, FrameY - FrameH / 2f - 14f), new Vector2(260f, 22f));
        escapeFailText.gameObject.SetActive(false);
        // (이전) 제목 양옆 가는 선 → 탭 테두리로 대체
        // var lineColor = new Color32(200, 196, 192, 200);
        // titleLineL = HudStyle.NewImage(root, "TitleLineL", null, lineColor, new Vector2(-123f, TitleY + 0.5f), new Vector2(115f, 2f));
        // titleLineR = HudStyle.NewImage(root, "TitleLineR", null, lineColor, new Vector2(122f, TitleY + 0.5f), new Vector2(115f, 2f));

        // ── 날짜 원 1~N + 연결선 ──
        float firstX = -(totalDays - 1) * CircleSpacing / 2f;
        for (int i = 0; i < totalDays - 1; i++)
        {
            float midX = firstX + CircleSpacing * (i + 0.5f);
            HudStyle.NewImage(root, $"Connector{i + 1}", null, connector, new Vector2(midX, CircleY), new Vector2(CircleSpacing - CircleSize, 2f));
        }

        dayBorders = new Image[totalDays];
        dayFills = new Image[totalDays];
        dayNumbers = new TextMeshProUGUI[totalDays];
        for (int i = 0; i < totalDays; i++)
        {
            Vector2 c = new Vector2(firstX + CircleSpacing * i, CircleY);
            dayBorders[i] = HudStyle.NewImage(root, $"Day{i + 1}", HudStyle.Circle, dayBorder, c, new Vector2(CircleSize, CircleSize));
            dayFills[i] = HudStyle.NewImage(dayBorders[i].transform, "Fill", HudStyle.Circle, dayFill, Vector2.zero, new Vector2(CircleSize - 5f, CircleSize - 5f));
            dayNumbers[i] = HudStyle.NewText(dayBorders[i].transform, "Num", (i + 1).ToString(), HudStyle.SemiBold, 17f, dayNumber,
                TextAlignmentOptions.Center, new Vector2(0f, 0.5f), new Vector2(CircleSize, CircleSize));
        }

        // (이전) 아래 줄: ☀ 12:51 │ 낮 │ 생존 목표 · 7일
        // phaseIcon = HudStyle.NewImage(root, "PhaseIcon", HudStyle.Sun, sunColor, new Vector2(-181.5f, BottomY), new Vector2(36f, 36f));
        // clockText = HudStyle.NewText(root, "Clock", "12:51", HudStyle.Bold, 36f, clockColor,
        //     TextAlignmentOptions.Left, new Vector2(-138.5f + 50f, BottomY), new Vector2(100f, 40f));
        // HudStyle.NewImage(root, "Divider1", null, dividerColor, new Vector2(-26.5f, BottomY), new Vector2(1.5f, 30f));
        // phaseText = HudStyle.NewText(root, "Phase", "낮", HudStyle.SemiBold, 22f, dayPhaseColor,
        //     TextAlignmentOptions.Center, new Vector2(4f, BottomY), new Vector2(50f, 34f));
        // weatherIcon = HudStyle.NewImage(root, "WeatherIcon", Resources.Load<Sprite>("HUD/Sprites/Weather_Clear"), Color.white,
        //     new Vector2(36f, BottomY), new Vector2(24f, 24f));
        // weatherIcon.preserveAspect = true;
        // HudStyle.NewImage(root, "Divider2", null, dividerColor, new Vector2(50.5f, BottomY), new Vector2(1.5f, 30f));
        // goalText = HudStyle.NewText(root, "Goal", $"생존 목표 · {totalDays}일", HudStyle.Regular, 17f, Color.white,
        //     TextAlignmentOptions.Left, new Vector2(75.5f + 80f, BottomY), new Vector2(160f, 30f));

        // ── 아래 줄 (ui.jpg): ☀ │ 06:16  + 날씨 아이콘 (낮/밤 글자·생존 목표는 뺌) ──
        phaseIcon = HudStyle.NewImage(root, "PhaseIcon", HudStyle.Sun, sunColor, new Vector2(-62f, BottomY), new Vector2(30f, 30f));
        phaseDivider = HudStyle.NewImage(root, "Divider1", null, dividerColor, new Vector2(-38f, BottomY), new Vector2(1.5f, 24f));
        clockText = HudStyle.NewText(root, "Clock", "12:51", HudStyle.Bold, 32f, clockColor,
            TextAlignmentOptions.Left, new Vector2(-28f + 50f, BottomY), new Vector2(100f, 40f));
        weatherIcon = HudStyle.NewImage(root, "WeatherIcon", Resources.Load<Sprite>("HUD/Sprites/Weather_Clear"), Color.white,
            new Vector2(84f, BottomY), new Vector2(22f, 22f));
        weatherIcon.preserveAspect = true;
    }

    void Refresh(bool force)
    {
        var dnc = DayNightCycle.Instance;
        if (dnc == null || clockText == null) return;

        if (weatherIcon != null && (force || WeatherManager.Current != shownWeather))
        {
            shownWeather = WeatherManager.Current;
            weatherIcon.sprite = Resources.Load<Sprite>("HUD/Sprites/Weather_" + shownWeather);
        }

        var (hour, minute) = dnc.GetVirtualClock();
        clockText.text = $"{hour:00}:{minute:00}";

        int realDay = dnc.CurrentDay;
        int day = Mathf.Clamp(realDay, 1, dayFills.Length); // 7일 넘어가면 마지막 원 유지
        if (force || realDay != shownDay)
        {
            shownDay = realDay;
            bool extended = realDay > totalDays;
            // titleText.text = extended ? $"생존 {realDay}일차 (연장)" : $"생존 {realDay}일차"; // (이전)
            // 2026-11-12: 7일 넘으면 "생존 7일+N일차" + 아래에 탈출 실패 횟수 (7일차 이후 하루 지날 때마다 1회)
            titleText.text = extended ? $"생존 {totalDays}일+{realDay - totalDays}일차" : $"생존 {realDay}일차";
            if (escapeFailText != null)
            {
                escapeFailText.gameObject.SetActive(extended);
                escapeFailText.text = $"탈출 실패 횟수: {realDay - totalDays}";
            }
            if (titleLineL != null) titleLineL.enabled = titleLineR.enabled = !extended; // 긴 제목에 양옆 선이 겹침
            if (goalText != null) goalText.text = extended && spawner != null ? $"오늘 좀비 {spawner.TodayZombieCap}마리" : $"생존 목표 · {totalDays}일";
            for (int i = 0; i < dayFills.Length; i++)
            {
                bool current = i + 1 == day;
                dayBorders[i].color = current ? currentDayFill : dayBorder;
                dayFills[i].color = current ? currentDayFill : dayFill;
                dayNumbers[i].color = current ? currentDayNumber : dayNumber;
                dayNumbers[i].font = current ? HudStyle.Bold : HudStyle.SemiBold;
            }
        }

        // 실내: 하늘 관련 표시 숨김 (BuildingInterior 트리거 안에 있는 동안)
        bool indoor = hideSkyIndoors && BuildingInterior.IsPlayerEnclosed;
        if (force || indoor != shownIndoor)
        {
            shownIndoor = indoor;
            phaseIcon.enabled = !indoor;
            if (phaseDivider != null) phaseDivider.enabled = !indoor;
            if (weatherIcon != null) weatherIcon.enabled = !indoor;
            force = true; // 실외로 나오면 아래에서 낮/밤 글자 다시 채움
        }
        if (indoor)
        {
            if (phaseText != null) { phaseText.text = "실내"; phaseText.color = indoorPhaseColor; } // (ui.jpg 레이아웃엔 낮/밤 글자 없음 - 아이콘만 숨김)
            return;
        }

        if (force || dnc.CurrentPhase != shownPhase)
        {
            shownPhase = dnc.CurrentPhase;
            bool dark = shownPhase == DayNightCycle.Phase.Night;
            if (phaseText != null)
            {
                phaseText.text = shownPhase switch
                {
                    DayNightCycle.Phase.Day => "낮",
                    DayNightCycle.Phase.Dusk => "노을",
                    DayNightCycle.Phase.Night => "밤",
                    _ => "새벽",
                };
                phaseText.color = dark ? nightPhaseColor : dayPhaseColor;
            }
            phaseIcon.sprite = dark ? HudStyle.Moon : HudStyle.Sun;
            phaseIcon.color = dark ? nightPhaseColor : sunColor;
        }
    }
}

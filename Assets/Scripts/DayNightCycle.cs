using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// 낮(8분) → 노을(1분) → 밤(5분) → 새벽(1분) → 반복
/// 7일 생존이 목표
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    public enum Phase { Day, Dusk, Night, Dawn }

    [Header("시간 설정 (초) - 실제 밸런스는 하루 15분(낮8+노을1+밤5+새벽1).")]
    [Tooltip("지금은 테스트하기 쉽게 하루 2분(낮1+노을15초+밤30초+새벽15초)으로 맞춰둠. 실제 플레이용 15분으로 쓰려면 480/60/300/60으로 바꿀 것.")]
    public float dayDuration   = 150f;  // 하루 300초(5분): 낮 150 + 노을 37.5 + 밤 75 + 새벽 37.5 (기존 120초와 같은 비율)
    public float duskDuration  = 37.5f;
    public float nightDuration = 75f;
    public float dawnDuration  = 37.5f;
    public int   totalDays     = 7;

    [Header("라이팅")]
    public Light directionalLight;

    public Color  dayLightColor      = new Color(1f, 0.95f, 0.8f);
    public float  dayLightIntensity  = 1.1f;
    public float  dayLightAngle      = 50f;

    public Color  duskColor          = new Color(1f, 0.45f, 0.1f);
    public float  duskLightIntensity = 0.5f;

    public Color  nightLightColor    = new Color(0.1f, 0.15f, 0.35f);
    public float  nightLightIntensity = 0.08f;
    public float  nightLightAngle    = -30f;

    [Header("환경광 (Ambient) - 낮은 밝게, 밤은 기존 값 유지")]
    public bool  controlAmbient = true;
    public Color dayAmbient   = new Color(0.45f, 0.47f, 0.52f);
    public Color nightAmbient = new Color(0.188f, 0.204f, 0.243f); // 예전 씬 값 (밤 분위기 유지)

    [Tooltip("날씨(흐림/비)가 조명 강도에 곱하는 값 (WeatherManager가 설정)")]
    [HideInInspector] public float weatherLightMultiplier = 1f;

    [Header("안개 (실내에 있는 동안은 BuildingInterior가 안개를 따로 관리하니 건드리지 않음)")]
    public bool  controlFog = true;
    public Color dayFogColor   = new Color(0.75f, 0.8f, 0.85f);
    public float dayFogStart   = 150f;
    public float dayFogEnd     = 600f;
    public Color nightFogColor = new Color(0.03f, 0.04f, 0.09f);
    public float nightFogStart = 20f;
    public float nightFogEnd   = 180f;

    [Header("손전등 (T키)")]
    public Light flashlight;
    public TextMeshProUGUI flashlightStatusText;
    [Tooltip("예전 텍스트 표시(FlashlightStatusText)를 자동 생성할지. 새 HUD(FlashlightHUD)를 쓰면 끔")]
    public bool buildLegacyFlashlightText = true;

    [Header("손전등 배터리")]
    [Tooltip("켜둔 상태로 100% → 0%까지 걸리는 시간(초)")]
    public float batteryDrainSeconds = 240f;
    [Tooltip("꺼둔 상태로 0% → 100%까지 충전되는 시간(초)")]
    public float batteryRechargeSeconds = 480f;
    [Tooltip("이 잔량(0~1) 이상 충전돼야 다시 켤 수 있음. 방전 직후 켰다 꺼졌다 깜빡이는 것 방지")]
    [Range(0f, 1f)] public float minBatteryToTurnOn = 0.1f;

    /// <summary>배터리 잔량 0~1. 0이 되면 손전등이 자동으로 꺼지고 충전될 때까지 못 켬.</summary>
    public float Battery01 { get; private set; } = 1f;
    public bool FlashlightOn => flashlightOn;

    [Header("UI")]
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI timeText;

    [HideInInspector] public UnityEvent      onDayStart   = new UnityEvent();
    [HideInInspector] public UnityEvent      onNightStart = new UnityEvent();
    [HideInInspector] public UnityEvent<int> onNewDay     = new UnityEvent<int>();

    public Phase CurrentPhase  { get; private set; } = Phase.Day;
    public int   CurrentDay    { get; private set; } = 1;
    public bool  IsDay         => CurrentPhase == Phase.Day;
    public bool  IsNight       => CurrentPhase == Phase.Night;

    private float phaseTimer   = 0f;
    private bool  gameCleared  = false;
    private bool  flashlightOn = false; // 게임 시작 시 손전등 꺼진 상태 (T키로 켬)
    private float debugLogTimer = 0f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (flashlight == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                flashlight = player.GetComponentInChildren<Light>();
        }
        if (flashlightStatusText == null && buildLegacyFlashlightText) BuildFlashlightUI();
        if (flashlight != null) flashlight.enabled = flashlightOn; // 씬에서 켜져 있어도 시작은 꺼짐
        Debug.Log($"[Flashlight] 초기화: flashlight={(flashlight != null ? flashlight.name : "NULL! 못 찾음")}" +
                   (flashlight != null ? $" localPos={flashlight.transform.localPosition} enabled={flashlight.enabled}" : ""));

        ApplyLighting(Phase.Day, 0f);
        ApplyFog(Phase.Day, 0f);
        UpdateUI();
        UpdateFlashlightUI();
        Debug.Log($"[DayNightCycle] 시작: Day {CurrentDay} / {CurrentPhase} (낮 {dayDuration}s, 노을 {duskDuration}s, 밤 {nightDuration}s, 새벽 {dawnDuration}s)");
    }

    void Update()
    {
        if (gameCleared) return;

        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame)
        {
            Debug.Log($"[Flashlight] T키 입력 받음 (flashlight={(flashlight != null ? "있음" : "NULL!")})");
            // 배터리가 minBatteryToTurnOn 미만이면 켜지지 않음 (끄는 건 항상 가능)
            if (flashlight != null && (flashlightOn || Battery01 >= minBatteryToTurnOn))
            {
                flashlightOn = !flashlightOn;
                flashlight.enabled = flashlightOn;
                UpdateFlashlightUI();
                Debug.Log($"[Flashlight] 토글됨 -> {(flashlightOn ? "ON" : "OFF")}");
            }
        }

        UpdateBattery();

        phaseTimer += Time.deltaTime;
        float duration = PhaseDuration(CurrentPhase);

        if (phaseTimer >= duration)
        {
            phaseTimer = 0f;
            AdvancePhase();
        }
        else
        {
            float t = phaseTimer / duration;
            ApplyLighting(CurrentPhase, t);
            ApplyFog(CurrentPhase, t);
        }

        UpdateUI();

        debugLogTimer += Time.deltaTime;
        if (debugLogTimer >= 5f)
        {
            debugLogTimer = 0f;
            Debug.Log($"[DayNightCycle] Day {CurrentDay} | {CurrentPhase} 진행도 {GetPhaseProgress() * 100f:F0}% | 시간 흐름 확인용 Time.time={Time.time:F1}");
        }
    }

    void AdvancePhase()
    {
        switch (CurrentPhase)
        {
            case Phase.Day:
                SetPhase(Phase.Dusk);
                break;
            case Phase.Dusk:
                SetPhase(Phase.Night);
                onNightStart?.Invoke();
                break;
            case Phase.Night:
                SetPhase(Phase.Dawn);
                break;
            case Phase.Dawn:
                CurrentDay++;
                if (CurrentDay > totalDays)
                {
                    gameCleared = true;
                    // 미션 시스템이 있으면 7일이 끝나는 순간 = 탈출 못 함 → 미션 실패(게임오버). 클리어는 탈출 지점 도달로만
                    if (MissionManager.Instance != null)
                    {
                        CurrentDay = totalDays; // HUD가 8일차로 안 보이게
                        MissionManager.Instance.OnTimeUp();
                        return;
                    }
                    if (dayText  != null) dayText.text  = "7일 생존 성공!";
                    if (timeText != null) timeText.text = "";
                    Time.timeScale = 0f;
                    Debug.Log("[DayNightCycle] 7일 생존 성공, 게임 클리어");
                    return;
                }
                SetPhase(Phase.Day);
                onDayStart?.Invoke();
                onNewDay?.Invoke(CurrentDay);
                break;
        }
        Debug.Log($"[DayNightCycle] 페이즈 전환 -> Day {CurrentDay} / {CurrentPhase}");
    }

    void SetPhase(Phase next)
    {
        CurrentPhase = next;
        phaseTimer   = 0f;
        ApplyLighting(next, 0f);
        ApplyFog(next, 0f);
    }

    void ApplyLighting(Phase phase, float t)
    {
        if (directionalLight == null) return;

        Color targetColor;
        float targetIntensity;
        float targetAngle;

        switch (phase)
        {
            case Phase.Day:
                targetColor     = dayLightColor;
                targetIntensity = dayLightIntensity;
                targetAngle     = dayLightAngle;
                break;
            case Phase.Dusk:
                targetIntensity = Mathf.Lerp(dayLightIntensity, nightLightIntensity, t);
                targetAngle     = Mathf.Lerp(dayLightAngle, nightLightAngle, t);
                targetColor     = t < 0.5f
                    ? Color.Lerp(dayLightColor, duskColor, t * 2f)
                    : Color.Lerp(duskColor, nightLightColor, (t - 0.5f) * 2f);
                break;
            case Phase.Night:
                targetColor     = nightLightColor;
                targetIntensity = nightLightIntensity;
                targetAngle     = nightLightAngle;
                break;
            case Phase.Dawn:
                targetColor     = Color.Lerp(nightLightColor, dayLightColor, t);
                targetIntensity = Mathf.Lerp(nightLightIntensity, dayLightIntensity, t);
                targetAngle     = Mathf.Lerp(nightLightAngle, dayLightAngle, t);
                break;
            default: return;
        }

        directionalLight.color     = targetColor;
        directionalLight.intensity = targetIntensity * weatherLightMultiplier;

        // 환경광: 해 밝기 비율(밤 0 ~ 낮 1)로 밤/낮 색을 섞음
        if (controlAmbient)
        {
            float day01 = Mathf.InverseLerp(nightLightIntensity, dayLightIntensity, targetIntensity);
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, day01) * Mathf.Lerp(1f, weatherLightMultiplier, day01);
        }
        directionalLight.transform.rotation = Quaternion.Euler(targetAngle, -30f, 0f);
    }

    // 실내에 있는 동안은 BuildingInterior가 안개를 좀보이드 스타일로 따로 관리하므로
    // 거기에 끼어들지 않고 건너뜀 (둘이 RenderSettings.fog를 동시에 쓰면 서로 덮어씀).
    void ApplyFog(Phase phase, float t)
    {
        if (!controlFog) return;
        if (BuildingInterior.IsAnyoneIndoors) return;

        Color targetColor;
        float targetStart, targetEnd;

        switch (phase)
        {
            case Phase.Day:
                targetColor = dayFogColor; targetStart = dayFogStart; targetEnd = dayFogEnd;
                break;
            case Phase.Dusk:
                targetColor = Color.Lerp(dayFogColor, nightFogColor, t);
                targetStart = Mathf.Lerp(dayFogStart, nightFogStart, t);
                targetEnd   = Mathf.Lerp(dayFogEnd, nightFogEnd, t);
                break;
            case Phase.Night:
                targetColor = nightFogColor; targetStart = nightFogStart; targetEnd = nightFogEnd;
                break;
            case Phase.Dawn:
                targetColor = Color.Lerp(nightFogColor, dayFogColor, t);
                targetStart = Mathf.Lerp(nightFogStart, dayFogStart, t);
                targetEnd   = Mathf.Lerp(nightFogEnd, dayFogEnd, t);
                break;
            default: return;
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = targetColor;
        RenderSettings.fogStartDistance = targetStart;
        RenderSettings.fogEndDistance = targetEnd;
    }

    // 켜져 있으면 닳고, 꺼져 있으면 천천히 충전. 0이 되면 자동으로 꺼짐.
    void UpdateBattery()
    {
        if (flashlightOn)
        {
            Battery01 = Mathf.Max(0f, Battery01 - Time.deltaTime / Mathf.Max(batteryDrainSeconds, 0.01f));
            if (Battery01 <= 0f && flashlight != null)
            {
                flashlightOn = false;
                flashlight.enabled = false;
                UpdateFlashlightUI();
                Debug.Log("[Flashlight] 배터리 방전 → 자동 OFF");
            }
        }
        else
        {
            Battery01 = Mathf.Min(1f, Battery01 + Time.deltaTime / Mathf.Max(batteryRechargeSeconds, 0.01f));
        }
    }

    void BuildFlashlightUI()
    {
        var canvasGo = GameObject.Find("Canvas");
        if (canvasGo == null) return;

        var go = new GameObject("FlashlightStatusText", typeof(RectTransform));
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-20f, 90f);
        rt.sizeDelta = new Vector2(200f, 24f);

        flashlightStatusText = go.AddComponent<TextMeshProUGUI>();
        flashlightStatusText.alignment = TextAlignmentOptions.Right;
        flashlightStatusText.fontSize = 16;
        flashlightStatusText.fontStyle = FontStyles.Bold;
        flashlightStatusText.raycastTarget = false;
    }

    void UpdateFlashlightUI()
    {
        if (flashlightStatusText == null) return;
        flashlightStatusText.text = flashlightOn ? "손전등 ON (T)" : "손전등 OFF (T)";
        flashlightStatusText.color = flashlightOn ? Color.yellow : new Color(0.6f, 0.6f, 0.6f);
    }

    void UpdateUI()
    {
        if (dayText != null)
        {
            string label = CurrentPhase switch
            {
                Phase.Day   => "낮",
                Phase.Dusk  => "노을",
                Phase.Night => "밤",
                Phase.Dawn  => "새벽",
                _           => ""
            };
            dayText.text = $"Day {CurrentDay}  {label}";
        }

        if (timeText != null)
        {
            float remaining = PhaseDuration(CurrentPhase) - phaseTimer;
            int mm = Mathf.FloorToInt(remaining / 60f);
            int ss = Mathf.FloorToInt(remaining % 60f);
            timeText.text = $"{mm:00}:{ss:00}";
        }
    }

    float PhaseDuration(Phase phase) => phase switch
    {
        Phase.Day   => dayDuration,
        Phase.Dusk  => duskDuration,
        Phase.Night => nightDuration,
        Phase.Dawn  => dawnDuration,
        _           => dayDuration
    };

    public float GetPhaseProgress() => phaseTimer / PhaseDuration(CurrentPhase);

    public float GetTotalCycleDuration() => dayDuration + duskDuration + nightDuration + dawnDuration;

    // 하루 전체(낮→노을→밤→새벽) 기준 0~1 진행률. UI의 이동 마커/가상 시계에 사용.
    public float GetCycleProgress01()
    {
        float elapsed = CurrentPhase switch
        {
            Phase.Day   => phaseTimer,
            Phase.Dusk  => dayDuration + phaseTimer,
            Phase.Night => dayDuration + duskDuration + phaseTimer,
            Phase.Dawn  => dayDuration + duskDuration + nightDuration + phaseTimer,
            _ => 0f
        };
        return elapsed / GetTotalCycleDuration();
    }

    // 사이클 진행률을 24시간 가상 시계(0~24)로 환산. 낮 시작=06:00에 매핑.
    public (int hour, int minute) GetVirtualClock()
    {
        float virtualHour = (GetCycleProgress01() * 24f + 6f) % 24f;
        int hour = Mathf.FloorToInt(virtualHour);
        int minute = Mathf.FloorToInt((virtualHour - hour) * 60f);
        return (hour, minute);
    }
}

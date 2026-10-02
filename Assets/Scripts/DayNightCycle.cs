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
    public float dayDuration   = 60f;
    public float duskDuration  = 15f;
    public float nightDuration = 30f;
    public float dawnDuration  = 15f;
    public int   totalDays     = 7;

    [Header("라이팅")]
    public Light directionalLight;

    public Color  dayLightColor      = new Color(1f, 0.95f, 0.8f);
    public float  dayLightIntensity  = 1.2f;
    public float  dayLightAngle      = 50f;

    public Color  duskColor          = new Color(1f, 0.45f, 0.1f);
    public float  duskLightIntensity = 0.6f;

    public Color  nightLightColor    = new Color(0.1f, 0.15f, 0.35f);
    public float  nightLightIntensity = 0.08f;
    public float  nightLightAngle    = -30f;

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
    private bool  flashlightOn = true;
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
        if (flashlightStatusText == null) BuildFlashlightUI();
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
            if (flashlight != null)
            {
                flashlightOn = !flashlightOn;
                flashlight.enabled = flashlightOn;
                UpdateFlashlightUI();
                Debug.Log($"[Flashlight] 토글됨 -> {(flashlightOn ? "ON" : "OFF")}");
            }
        }

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
        directionalLight.intensity = targetIntensity;
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

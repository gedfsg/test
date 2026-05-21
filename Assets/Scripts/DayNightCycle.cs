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

    [Header("시간 설정 (초)")]
    public float dayDuration   = 480f;
    public float duskDuration  = 60f;
    public float nightDuration = 300f;
    public float dawnDuration  = 60f;
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

    [Header("손전등 (Q키)")]
    public Light flashlight;

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
        ApplyLighting(Phase.Day, 0f);
        UpdateUI();
    }

    void Update()
    {
        if (gameCleared) return;

        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame &&
            flashlight != null)
        {
            flashlightOn = !flashlightOn;
            flashlight.enabled = flashlightOn;
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
            ApplyLighting(CurrentPhase, phaseTimer / duration);
        }

        UpdateUI();
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
                    return;
                }
                SetPhase(Phase.Day);
                onDayStart?.Invoke();
                onNewDay?.Invoke(CurrentDay);
                break;
        }
    }

    void SetPhase(Phase next)
    {
        CurrentPhase = next;
        phaseTimer   = 0f;
        ApplyLighting(next, 0f);
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
}

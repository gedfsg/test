using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 날씨 4종: 맑음 / 흐림 / 비 / 안개
///  - 하루가 바뀔 때(DayNightCycle.onNewDay) 랜덤 변경. 1일차는 맑음 고정 (쉬운 시작)
///  - 흐림: 조명 60%
///  - 비: 조명 60% + 빗줄기 파티클(플레이어 위를 따라다님) + 좀비 감지 거리 20% 감소 (ZombieController가 DetectionMultiplier 사용)
///  - 안개: 밝은 회색 Linear 안개 (분위기 전용). 카메라가 플레이어에서 40~55m 떨어진 탑뷰라
///          "플레이어 기준 fogStartFromPlayer ~ fogEndFromPlayer"가 되도록 카메라 거리를 더해서 설정
///  - 실내: 비 파티클 끔, 안개는 BuildingInterior(실내 안개)에 맡김
/// 씬에 따로 둘 필요 없음 - 씬이 로드될 때마다 자동 생성. HUD 아이콘은 SurvivalDayHUD가 Current를 읽어서 표시.
/// </summary>
public class WeatherManager : MonoBehaviour
{
    public enum Weather { Clear, Cloudy, Rain, Fog }

    public static WeatherManager Instance { get; private set; }
    public static Weather Current => Instance != null ? Instance.current : Weather.Clear;
    /// <summary>좀비 감지 거리 배율 (비 0.8)</summary>
    public static float DetectionMultiplier => Current == Weather.Rain ? 0.8f : 1f;

    [Header("흐림 / 비")]
    [Range(0f, 1f)] public float overcastLight = 0.6f;

    [Header("안개 (분위기 전용, 약하게)")]
    public Color fogColor = new Color(0.75f, 0.75f, 0.75f);
    public float fogStartFromPlayer = 20f;
    public float fogEndFromPlayer = 50f;

    [Header("비")]
    public int rainPerSecond = 1500;
    public Vector3 rainArea = new Vector3(45f, 1f, 45f);
    public float rainHeight = 14f;

    Weather current = Weather.Clear;
    ParticleSystem rain;
    Transform player;
    bool dncFogWasOn = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindFirstObjectByType<DayNightCycle>() == null) return; // 게임 씬에서만
        if (FindFirstObjectByType<WeatherManager>() != null) return;
        new GameObject("WeatherManager").AddComponent<WeatherManager>();
    }

    void Awake() => Instance = this;

    void Start()
    {
        player = GameObject.FindWithTag("Player")?.transform;
        var dnc = DayNightCycle.Instance;
        if (dnc != null)
        {
            dncFogWasOn = dnc.controlFog;
            dnc.onNewDay.AddListener(OnNewDay);
        }
        rain = BuildRain();
        SetWeather(Weather.Clear); // 1일차 맑음
    }

    void OnNewDay(int day)
    {
        if (day <= 1) { SetWeather(Weather.Clear); return; }
        SetWeather((Weather)Random.Range(0, 4)); // 2일차부터 4종 랜덤
    }

    public void SetWeather(Weather w)
    {
        current = w;
        var dnc = DayNightCycle.Instance;
        if (dnc != null)
        {
            dnc.weatherLightMultiplier = (w == Weather.Cloudy || w == Weather.Rain) ? overcastLight : 1f;
            dnc.controlFog = w == Weather.Fog ? false : dncFogWasOn; // 안개 날씨면 여기서 안개를 관리
        }
        Debug.Log($"[Weather] 날씨 → {w}");
    }

    void LateUpdate()
    {
        bool indoors = BuildingInterior.IsAnyoneIndoors;

        // 비: 플레이어 위를 따라다님, 실내에선 끔
        if (rain != null)
        {
            bool raining = current == Weather.Rain && !indoors;
            if (player != null) rain.transform.position = player.position + Vector3.up * rainHeight;
            if (raining && !rain.isPlaying) rain.Play();
            else if (!raining && rain.isPlaying) rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 안개: 실내는 BuildingInterior가 관리하므로 건드리지 않음
        if (current == Weather.Fog && !indoors)
        {
            var cam = Camera.main;
            float camDist = cam != null && player != null ? Vector3.Distance(cam.transform.position, player.position) : 40f;
            var dnc = DayNightCycle.Instance;
            // 밤에는 안개도 어둡게 (해 밝기 비율)
            float day01 = dnc != null && dnc.directionalLight != null
                ? Mathf.InverseLerp(dnc.nightLightIntensity, dnc.dayLightIntensity * dnc.weatherLightMultiplier, dnc.directionalLight.intensity) : 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(dnc != null ? dnc.nightFogColor : Color.black, fogColor, day01);
            RenderSettings.fogStartDistance = camDist + fogStartFromPlayer * 0.5f;
            RenderSettings.fogEndDistance = camDist + fogEndFromPlayer;
        }
    }

    ParticleSystem BuildRain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.0f;
        main.startSpeed = 28f;
        main.startSize = 0.025f; // 카메라 가까이 지나가도 막대처럼 안 보이게 가늘게
        main.startColor = new Color(0.8f, 0.85f, 0.95f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4000;

        var em = ps.emission; em.rateOverTime = rainPerSecond;
        // 박스에서 로컬 +Z 방향으로 나감 → X축 90° 돌려서 아래로. 그러면 수평면 = 로컬 X-Y 평면
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(rainArea.x, rainArea.z, rainArea.y);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 1f; r.velocityScale = 0.04f;
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Rain(Runtime)" };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 날씨 3종 (논문 4.2절): 맑음 / 비 / 안개. 새 사이클(DayNightCycle 새벽→낮) 시작마다 가중치 랜덤 (50 / 30 / 20). 1일차는 맑음.
///  - 비: 빗방울 파티클(플레이어 머리 위 10m, 따라다님) + 땅에 튀는 물방울(옵션) + 빗소리 루프(코드 생성) + 조명 60%
///        소음 반경 ×0.5 (NoiseMultiplier → NoiseSystem), 구르기 거리 ×1.3 (RollDistanceMultiplier → Locomotion)
///  - 안개: 회색(100,100,100) ExponentialSquared 안개 + 카메라 가시거리 축소 + 손전등 range 70%
///          좀비 감지 거리 ×0.8 (DetectionMultiplier → ZombieController)
///          탑뷰 카메라가 플레이어에서 40~80m 떨어져 있어서 밀도/가시거리는 "플레이어 기준"이 되게 카메라 거리로 보정
///  - 전환: transitionTime(1.5초) 동안 안개 밀도/파티클 양/소리 크기/배수들을 같이 Lerp
///  - 실내: 비 파티클 끔(소리는 작게), 안개는 BuildingInterior(실내 안개)가 우선 → 나오면 여기서 다시 날씨 안개 적용
///  - 디버그: Ctrl+Shift+W = 맑음→비→안개 강제 전환 (에디터/개발 빌드만. Shift+W는 달리기(Shift)+앞(W)과 겹쳐서 Ctrl 추가)
/// 씬에 따로 둘 필요 없음 - 씬이 로드될 때마다 자동 생성. HUD 아이콘은 SurvivalDayHUD가 Current를 읽어서 표시.
/// </summary>
public class WeatherManager : MonoBehaviour
{
    // Cloudy는 예전 4종 시절 값 (HUD 스프라이트 Weather_Cloudy 이름과 맞춰 둠). 지금은 뽑지 않음
    public enum Weather { Clear, Cloudy, Rain, Fog }

    public static WeatherManager Instance { get; private set; }
    public static Weather Current => Instance != null ? Instance.current : Weather.Clear;

    // ── 다른 시스템이 매 프레임 읽는 전역 배수 (전환 중엔 부드럽게 변함) ──
    /// <summary>좀비 감지 거리 배수 (안개 0.8). ZombieController가 사용</summary>
    public static float DetectionMultiplier => Instance != null ? Mathf.Lerp(1f, Instance.fogDetectionMultiplier, Instance.fogAmount) : 1f;
    /// <summary>소음 반경 배수 (비 0.5). NoiseSystem이 사용</summary>
    public static float NoiseMultiplier => Instance != null ? Mathf.Lerp(1f, Instance.rainNoiseMultiplier, Instance.rainAmount) : 1f;
    /// <summary>구르기 거리 배수 (비 1.3). Locomotion이 사용</summary>
    public static float RollDistanceMultiplier => Instance != null ? Mathf.Lerp(1f, Instance.rainRollMultiplier, Instance.rainAmount) : 1f;

    [Header("가중치 (사이클 시작마다 랜덤)")]
    public float clearWeight = 50f;
    public float rainWeight = 30f;
    public float fogWeight = 20f;
    [Tooltip("날씨 바뀔 때 안개/파티클/소리/배수가 넘어가는 시간 (초)")]
    public float transitionTime = 1.5f;

    [Header("흐림 / 비")]
    [Range(0f, 1f)] public float overcastLight = 0.6f;
    public float rainNoiseMultiplier = 0.5f;
    public float rainRollMultiplier = 1.3f;

    // [Header("안개 (분위기 전용, 약하게)")]   // (이전) 밝은 회색 Linear 안개
    // public Color fogColor = new Color(0.75f, 0.75f, 0.75f);
    // public float fogStartFromPlayer = 20f;
    // public float fogEndFromPlayer = 50f;
    [Header("안개 (ExponentialSquared)")]
    public Color fogColor = new Color32(100, 100, 100, 255);
    [Tooltip("플레이어 기준 밀도 (스펙 0.02). 카메라가 플레이어에서 떨어진 만큼 보정해서 적용")]
    public float fogDensity = 0.02f;
    [Tooltip("플레이어 기준 가시거리 (m). 카메라 far = 카메라→플레이어 거리 + 이 값 (평상시 far는 원래 값으로 복원)")]
    public float fogVisibility = 60f;
    public float fogDetectionMultiplier = 0.8f;
    [Range(0f, 1f)] public float fogFlashlightRange = 0.7f;

    [Header("비")]
    public int rainPerSecond = 1500;
    public Vector3 rainArea = new Vector3(45f, 1f, 45f);
    // public float rainHeight = 14f; // (이전)
    public float rainHeight = 10f;     // 플레이어 머리 위 10m
    // [Range(0f, 1f)] public float rainVolume = 0.35f; // (이전)
    [Range(0f, 1f)] public float rainVolume = AudioManager.Vol.Rain; // 0.4 (사운드 볼륨 표)
    [Tooltip("실내에선 빗소리를 이만큼 줄임 (지붕 너머로 들리는 느낌)")]
    [Range(0f, 1f)] public float indoorRainVolume = 0.35f;
    [Tooltip("땅에 물 튀는 작은 파티클 (옵션)")]
    public bool rainSplashes = true;
    public int splashPerSecond = 250;
    public float splashArea = 30f;

    [Header("디버그")]
    public bool debugKey = true;

    Weather current = Weather.Clear;
    ParticleSystem rain, splash;
    AudioSource rainAudio;
    Transform player;
    bool dncFogWasOn = true;

    // 전환 정도 (0~1): 목표 날씨 쪽으로 transitionTime에 걸쳐 이동
    float rainAmount, fogAmount;
    bool fogApplied;                 // 안개를 이 컴포넌트가 잡고 있는 중 (카메라 far/배경색/손전등 원래 값 복원용)
    float baseFar = 1000f;
    Color baseBackground;
    float baseFlashlightRange = -1f;

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
            // dnc.onNewDay.AddListener(OnNewDay); // (이전) 하루 바뀔 때 4종 랜덤 → 지금은 DayNightCycle이 새벽→낮에 RollWeather() 직접 호출
        }
        var cam = Camera.main;
        if (cam != null) { baseFar = cam.farClipPlane; baseBackground = cam.backgroundColor; }
        rain = BuildRain();
        splash = BuildSplash();
        rainAudio = BuildRainAudio();
        SetWeather(Weather.Clear); // 1일차 맑음
    }

    // void OnNewDay(int day)   // (이전)
    // {
    //     if (day <= 1) { SetWeather(Weather.Clear); return; }
    //     SetWeather((Weather)Random.Range(0, 4)); // 2일차부터 4종 랜덤
    // }

    /// <summary>가중치 랜덤 (맑음 50 / 비 30 / 안개 20). DayNightCycle이 새벽→낮 전환 때 호출</summary>
    public void RollWeather()
    {
        float total = clearWeight + rainWeight + fogWeight;
        float r = Random.Range(0f, total);
        Weather w = r < clearWeight ? Weather.Clear : r < clearWeight + rainWeight ? Weather.Rain : Weather.Fog;
        SetWeather(w);
    }

    public void SetWeather(Weather w)
    {
        current = w;
        Debug.Log($"[Weather] 날씨 → {w}");
    }

    void Update()
    {
        // 디버그: Ctrl+Shift+W → 맑음 → 비 → 안개 → 맑음
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (debugKey && (Application.isEditor || Debug.isDebugBuild) && kb != null && kb.wKey.wasPressedThisFrame
            && kb.shiftKey.isPressed && kb.ctrlKey.isPressed)
            SetWeather(current == Weather.Clear ? Weather.Rain : current == Weather.Rain ? Weather.Fog : Weather.Clear);

        // 전환: 목표 쪽으로 서서히
        float step = Time.deltaTime / Mathf.Max(0.01f, transitionTime);
        rainAmount = Mathf.MoveTowards(rainAmount, current == Weather.Rain ? 1f : 0f, step);
        fogAmount = Mathf.MoveTowards(fogAmount, current == Weather.Fog ? 1f : 0f, step);

        var dnc = DayNightCycle.Instance;
        if (dnc != null) dnc.weatherLightMultiplier = Mathf.Lerp(1f, overcastLight, rainAmount); // 비: 흐린 조명
    }

    void LateUpdate()
    {
        bool indoors = BuildingInterior.IsAnyoneIndoors;
        var cam = Camera.main;
        float camDist = cam != null && player != null ? Vector3.Distance(cam.transform.position, player.position) : 40f;

        // ── 비: 플레이어 위를 따라다님, 실내에선 끔. 양은 rainAmount로 서서히 ──
        if (rain != null)
        {
            bool raining = rainAmount > 0.001f && !indoors;
            if (player != null) rain.transform.position = player.position + Vector3.up * rainHeight;
            var em = rain.emission; em.rateOverTime = rainPerSecond * rainAmount;
            if (raining && !rain.isPlaying) rain.Play();
            else if (!raining && rain.isPlaying) rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (splash != null)
        {
            bool on = rainSplashes && rainAmount > 0.001f && !indoors;
            if (player != null) splash.transform.position = player.position + Vector3.up * 0.08f;
            var em = splash.emission; em.rateOverTime = splashPerSecond * rainAmount;
            if (on && !splash.isPlaying) splash.Play();
            else if (!on && splash.isPlaying) splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (rainAudio != null)
        {
            rainAudio.volume = rainVolume * rainAmount * (indoors ? indoorRainVolume : 1f);
            if (rainAudio.volume > 0.001f && !rainAudio.isPlaying) rainAudio.Play();
            else if (rainAudio.volume <= 0.001f && rainAudio.isPlaying) rainAudio.Stop();
        }

        // ── 안개 ──
        var dnc = DayNightCycle.Instance;
        if (fogAmount > 0.001f)
        {
            fogApplied = true;
            if (dnc != null) dnc.controlFog = false; // 안개 날씨 동안은 이 컴포넌트가 RenderSettings 안개를 관리

            // 밤에는 안개도 어둡게 (해 밝기 비율). 밤 색은 DayNightCycle 밤 안개색(보라) 쪽으로
            float day01 = dnc != null && dnc.directionalLight != null
                ? Mathf.InverseLerp(dnc.nightLightIntensity, dnc.dayLightIntensity * dnc.weatherLightMultiplier, dnc.directionalLight.intensity) : 1f;
            Color col = Color.Lerp(dnc != null ? dnc.nightFogColor : Color.black, fogColor, day01);

            // 실내: BuildingInterior 실내 안개가 우선 → RenderSettings는 안 건드림 (나오면 BuildingInterior가 저장해 둔 이 안개를 복원하고, 다음 프레임부터 다시 여기서 갱신)
            if (!indoors)
            {
                // ExponentialSquared는 시작 거리가 없고 "카메라로부터의 거리"라서, 0.02를 그대로 쓰면 카메라에서 40m 떨어진 플레이어부터 절반쯤 안개에 묻힘.
                // → 플레이어 너머 fogVisibility(60m) 지점의 안개 정도가 "플레이어 기준 0.02"와 같아지도록 밀도를 카메라 거리만큼 낮춤
                float density = fogDensity * fogVisibility / (camDist + fogVisibility);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = Mathf.Lerp(0f, density, fogAmount);
                RenderSettings.fogColor = col;
            }
            if (cam != null)
            {
                cam.farClipPlane = Mathf.Lerp(baseFar, camDist + fogVisibility, fogAmount);
                cam.backgroundColor = Color.Lerp(baseBackground, col, fogAmount); // far 너머(잘린 곳)가 안개색으로 이어지게
            }
            var fl = dnc != null ? dnc.flashlight : null;
            if (fl != null)
            {
                if (baseFlashlightRange < 0f) baseFlashlightRange = fl.range;
                fl.range = baseFlashlightRange * Mathf.Lerp(1f, fogFlashlightRange, fogAmount); // 안개에 빛이 흩어지는 느낌
            }
        }
        else if (fogApplied)
        {
            // 안개 끝: 원래대로 (안개는 DayNightCycle에 돌려줌 → 다음 Update부터 원래 Linear 안개)
            fogApplied = false;
            if (dnc != null) dnc.controlFog = dncFogWasOn;
            if (cam != null) { cam.farClipPlane = baseFar; cam.backgroundColor = baseBackground; }
            if (dnc != null && dnc.flashlight != null && baseFlashlightRange >= 0f) dnc.flashlight.range = baseFlashlightRange;
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
        main.startLifetime = 0.8f; // 10m 높이 / 28m/s ≈ 0.36초면 땅에 닿음 + 비스듬히 보이는 여유
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

    // 땅에 물 튀는 파티클: 충돌 계산 없이 플레이어 주변 바닥 높이에 작은 고리가 퍼졌다 사라짐 (성능 부담 적게)
    ParticleSystem BuildSplash()
    {
        var go = new GameObject("RainSplash");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = 0.25f;
        main.startSpeed = 0f;
        main.startSize = 0.18f;
        main.startColor = new Color(0.85f, 0.9f, 1f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        var em = ps.emission; em.rateOverTime = splashPerSecond;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(splashArea, 0f, splashArea);

        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.6f)); // 퍼짐
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // 바닥에 눕혀서
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "RainSplash(Runtime)", mainTexture = RingTexture() };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static Texture2D RingTexture()
    {
        const int N = 32;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f - 0.5f, N / 2f - 0.5f)) / (N / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) / 0.2f); // 반지름 0.75 근처만 밝은 고리
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
    }

    // 빗소리: 녹음 파일이 없어서 코드로 생성 (ZombieHitEffect 타격음과 같은 방식). 걸러낸 노이즈(쏴아) + 가끔 똑똑 떨어지는 소리
    // rainClip에 실제 녹음 파일을 넣으면 그걸 사용
    public AudioClip rainClip;

    AudioSource BuildRainAudio()
    {
        var src = gameObject.AddComponent<AudioSource>();
        // 우선순위: 인스펙터 rainClip → SoundLibrary의 rain_loop (녹음) → 코드 생성음
        AudioClip clip = rainClip;
        if (clip == null && AudioManager.Library != null) clip = AudioManager.Library.rainLoop;
        src.clip = clip != null ? clip : GenerateRainClip();
        src.outputAudioMixerGroup = AudioManager.GetGroup(AudioManager.Channel.Ambient);
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f; // 전역 환경음
        src.volume = 0f;
        return src;
    }

    static AudioClip GenerateRainClip()
    {
        const int rate = 44100;
        const float seconds = 4f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(1234);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += 0.25f * (white - lp);       // 저역 통과 → 쏴아 소리 (거친 고음 제거)
            lp2 += 0.02f * (white - lp2);     // 더 낮은 웅웅 성분
            data[i] = lp * 0.5f + lp2 * 0.6f;
        }
        // 가끔 똑 떨어지는 소리 (짧게 감쇠하는 고음 톡)
        for (int k = 0; k < 120; k++)
        {
            int start = rng.Next(0, n - 2000);
            float amp = 0.15f + (float)rng.NextDouble() * 0.2f;
            float freq = 1800f + (float)rng.NextDouble() * 2200f;
            for (int j = 0; j < 1500; j++)
                data[start + j] += amp * Mathf.Exp(-j / 180f) * Mathf.Sin(2f * Mathf.PI * freq * j / rate);
        }
        // 이음새 없이 반복되게 끝 0.25초를 앞부분과 겹쳐 섞음
        int fade = rate / 4;
        for (int i = 0; i < fade; i++)
        {
            float t = (float)i / fade;
            data[i] = data[i] * t + data[n - fade + i] * (1f - t);
        }
        System.Array.Resize(ref data, n - fade);
        float peak = 0f;
        foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= 0.8f / peak;

        var clip = AudioClip.Create("RainLoop(Generated)", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

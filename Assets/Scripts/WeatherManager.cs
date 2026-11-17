using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

/// <summary>
/// 날씨 3종 (논문 4.2절): 맑음 / 비 / 안개. 게임 시작과 새 사이클(DayNightCycle 새벽→낮)마다 가중치 랜덤 (34 / 36 / 30, 2026-11-13).
///  - 비가 오면 가끔 천둥 번개 (번개 = 별도 방향광 깜빡임, 천둥 = 코드 생성 소리, 거리에 따라 늦게·작게)
///  - 비: 빗방울 파티클(플레이어 머리 위 10m, 따라다님) + 땅에 튀는 물방울(옵션) + 빗소리 루프(코드 생성) + 조명 60%
///        소음 반경 ×0.5 (NoiseMultiplier → NoiseSystem), 구르기 거리 ×1.3 (RollDistanceMultiplier → Locomotion)
///  - 안개: 낮 회색(140,140,140) / 밤 보라(80,80,110) ExponentialSquared 안개(기본 줌에서 밀도 0.02, far 80m) + 손전등 range 70%
///          + 바닥 안개 파티클(플레이어 주변 30m, 1~3m 높이, 큰 소프트 파티클 25개) + 비네트 0.38→0.5
///          좀비 감지 거리 ×0.8 (DetectionMultiplier → ZombieController)
///          탑뷰 카메라가 플레이어에서 40~80m 떨어져 있어서 밀도/가시거리는 "플레이어 기준"이 되게 카메라 거리로 보정
///  - 전환: transitionTime(1.5초) 동안 안개 밀도/파티클 양/소리 크기/배수들을 같이 Lerp
///  - 실내: 비 파티클 끔(소리는 작게), 안개는 BuildingInterior(실내 안개)가 우선 → 나오면 여기서 다시 날씨 안개 적용
///  - 디버그: Ctrl+Shift+L = 번개 한 번 / Ctrl+Shift+W = 맑음→비→안개 강제 전환 (에디터/개발 빌드만. Shift+W는 달리기(Shift)+앞(W)과 겹쳐서 Ctrl 추가)
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
    // public float clearWeight = 50f; public float rainWeight = 30f; public float fogWeight = 20f; // (이전)
    // 2026-11-13 피드백: 비·안개가 더 자주 나오게 → 맑음 34 / 비 36 / 안개 30
    public float clearWeight = 34f;
    public float rainWeight = 36f;
    public float fogWeight = 30f;
    // [Header("⚠ 임시 (2026-11-12)")] public bool tempDay1Rain = true; // (삭제) 1일차 비 임시 → 1일차도 랜덤으로
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
    // public Color fogColor = new Color32(100, 100, 100, 255); // (이전) 낮 색 - 밤은 DayNightCycle.nightFogColor 사용
    [Tooltip("낮 안개 색 (밝은 회색)")]
    public Color fogColor = new Color32(140, 140, 140, 255);
    [Tooltip("밤 안개 색 (보라 톤). 해 밝기에 따라 낮 색과 섞임")]
    public Color fogNightColor = new Color32(80, 80, 110, 255);
    // [Tooltip("플레이어 기준 밀도 (스펙 0.02). 카메라가 플레이어에서 떨어진 만큼 보정해서 적용")]
    // (이전) 0.02 × 가시거리/(카메라거리+가시거리) → 기본 줌(40m)에서 실제 적용 0.012라 너무 옅었음
    [Tooltip("기본 줌(카메라 거리 40m)에서 실제 적용되는 밀도. 줌아웃하면 그만큼 낮춰서 플레이어 주변 농도 유지")]
    public float fogDensity = 0.02f;
    // public float fogVisibility = 60f; // (이전) 기본 줌에서 far 100m
    [Tooltip("플레이어 너머 가시거리 (m). 카메라 far = 카메라→플레이어 거리 + 이 값 → 기본 줌에서 80m (평상시 far는 원래 값으로 복원)")]
    public float fogVisibility = 40f;
    public float fogDetectionMultiplier = 0.8f;
    [Range(0f, 1f)] public float fogFlashlightRange = 0.7f;
    [Tooltip("안개 때 비네트 세기 (평상시 0.38 = Atmosphere_Profile 값). 가장자리를 더 어둡게")]
    [Range(0f, 1f)] public float fogVignetteIntensity = 0.5f;

    [Header("안개 - 바닥 안개 파티클 (플레이어 따라다님)")]
    public bool groundFog = true;
    [Tooltip("동시에 떠 있는 개수 (크게 + 적게 → 성능 부담 적음)")]
    public int groundFogCount = 25;
    public float groundFogRadius = 30f;
    [Tooltip("파티클 크기 범위 (m)")]
    public Vector2 groundFogSize = new Vector2(3f, 8f);
    [Tooltip("바닥에서의 높이 범위 (m)")]
    public Vector2 groundFogHeight = new Vector2(1f, 3f);
    [Tooltip("알파 범위 (흰색/연회색)")]
    public Vector2 groundFogAlpha = new Vector2(0.3f, 0.5f);
    [Tooltip("바람에 흘러가는 속도 (m/s, 월드)")]
    public Vector3 groundFogWind = new Vector3(0.6f, 0f, 0.25f);

    [Header("비")]
    public int rainPerSecond = 1500;
    public Vector3 rainArea = new Vector3(45f, 1f, 45f);
    // public float rainHeight = 14f; // (이전)
    public float rainHeight = 10f;     // 플레이어 머리 위 10m
    // 2026-11-12: "비가 안 내리고 소리만 들림" - 빗줄기 굵기 0.025m 고정이라 카메라(약 40m)에서 1픽셀도 안 됐음
    // → 카메라 거리에 비례한 굵기. 2026-11-13 "너무 굵음" → 0.0008 → 0.0005 (기본 줌 80m에서 0.04m, 40m에서 0.02m)
    [Tooltip("빗줄기 굵기 = 카메라 거리(m) × 이 값 (아래 최소/최대로 제한)")]
    public float rainWidthPerMeter = 0.0005f;
    public float rainWidthMin = 0.01f, rainWidthMax = 0.045f;
    [Tooltip("빗줄기 불투명도 (낮을수록 투명)")]
    [Range(0f, 1f)] public float rainAlpha = 0.28f;

    [Header("천둥 번개 (비 올 때, 2026-11-13)")]
    public bool thunderstorm = true;
    [Tooltip("번개 간격 (초, 최소~최대 랜덤)")]
    public Vector2 thunderInterval = new Vector2(15f, 40f);
    [Tooltip("번개 빛 세기 (별도 방향광 최대 밝기). 실내에선 indoorLightningScale만큼")]
    // public float lightningIntensity = 2.2f; // (이전) 화면이 하얗게 날아감
    public float lightningIntensity = 1.1f;
    public Color lightningColor = new Color(0.78f, 0.84f, 1f);
    [Range(0f, 1f)] public float indoorLightningScale = 0.3f;
    [Range(0f, 1f)] public float thunderVolume = 0.75f;
    [Tooltip("비워두면 코드로 만든 천둥소리 사용")]
    public AudioClip[] thunderClips;
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
    ParticleSystem rain, splash, groundFogPs;
    Material groundFogMat;
    AudioSource rainAudio;
    Vignette vignette;              // PostFX_Volume 프로필(런타임 복제본)의 비네트 - 안개 때만 세게
    float baseVignette = -1f;
    DepthTextureMode baseDepthMode;  // 소프트 파티클은 깊이 텍스처 필요 → 안개 동안만 켬
    Transform player;
    bool dncFogWasOn = true;

    // 전환 정도 (0~1): 목표 날씨 쪽으로 transitionTime에 걸쳐 이동
    float rainAmount, fogAmount;
    /// <summary>비 정도 0~1 / 안개 정도 0~1 (헬기 로터 바람 파티클이 읽음)</summary>
    public static float RainAmount => Instance != null ? Instance.rainAmount : 0f;
    public static float FogAmount => Instance != null ? Instance.fogAmount : 0f;
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
        groundFogPs = BuildGroundFog();
        rainAudio = BuildRainAudio();
        if (cam != null) baseDepthMode = cam.depthTextureMode;
        // 비네트: 씬의 PostFX_Volume(Tools/FX/Setup Atmosphere). .profile은 런타임 복제본이라 에셋은 안 바뀜
        var volume = FindFirstObjectByType<PostProcessVolume>();
        if (volume != null && volume.profile != null && volume.profile.TryGetSettings(out vignette))
            baseVignette = vignette.intensity.value;
        // SetWeather(Weather.Clear); // 1일차 맑음 (원래)
        // SetWeather(tempDay1Rain ? Weather.Rain : Weather.Clear); // (이전 임시) 1일차 비
        RollWeather(); // 2026-11-13: 1일차도 가중치 랜덤
        BuildLightning();
    }

    // void OnNewDay(int day)   // (이전)
    // {
    //     if (day <= 1) { SetWeather(Weather.Clear); return; }
    //     SetWeather((Weather)Random.Range(0, 4)); // 2일차부터 4종 랜덤
    // }

    /// <summary>가중치 랜덤 (맑음 34 / 비 36 / 안개 30). 게임 시작(1일차)과 DayNightCycle 새벽→낮 전환 때 호출</summary>
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
        // 디버그: Ctrl+Shift+L → 번개 한 번 (가까이). T는 손전등이라 L(Lightning)
        if (debugKey && (Application.isEditor || Debug.isDebugBuild) && kb != null && kb.lKey.wasPressedThisFrame
            && kb.shiftKey.isPressed && kb.ctrlKey.isPressed)
            Strike(Random.Range(0f, 0.4f));

        // 전환: 목표 쪽으로 서서히
        float step = Time.deltaTime / Mathf.Max(0.01f, transitionTime);
        rainAmount = Mathf.MoveTowards(rainAmount, current == Weather.Rain ? 1f : 0f, step);
        fogAmount = Mathf.MoveTowards(fogAmount, current == Weather.Fog ? 1f : 0f, step);

        var dnc = DayNightCycle.Instance;
        if (dnc != null) dnc.weatherLightMultiplier = Mathf.Lerp(1f, overcastLight, rainAmount); // 비: 흐린 조명

        UpdateThunder();
    }

    // ── 천둥 번개 (2026-11-13) ─────────────────────────
    // 비가 다 내린 상태(rainAmount ≥ 0.95)에서 thunderInterval 간격으로 랜덤.
    // 번개: DayNightCycle이 해 밝기를 매 프레임 덮어쓰므로 해 대신 "번개 전용 방향광"을 잠깐 켬 (2~3번 깜빡, 약 0.5초)
    // 천둥: 번개가 멀수록(0~1) 소리가 늦고(0.3~2.8초) 작고, 가까우면 '쾅' 하는 앞부분이 있는 소리
    Light lightning;
    AudioSource thunderAudio;
    AudioClip[] generatedThunder;
    float nextThunderAt = -1f;
    Coroutine strikeRoutine;

    /// <summary>번개 횟수 (테스트용)</summary>
    public int StrikeCount { get; private set; }

    void BuildLightning()
    {
        var go = new GameObject("Lightning");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(65f, 140f, 0f);
        lightning = go.AddComponent<Light>();
        lightning.type = LightType.Directional;
        lightning.color = lightningColor;
        lightning.intensity = 0f;
        lightning.shadows = LightShadows.None;
        lightning.enabled = false;

        thunderAudio = gameObject.AddComponent<AudioSource>();
        thunderAudio.outputAudioMixerGroup = AudioManager.GetGroup(AudioManager.Channel.Ambient);
        thunderAudio.playOnAwake = false;
        thunderAudio.spatialBlend = 0f; // 하늘 전체에서 울림
    }

    void UpdateThunder()
    {
        if (!thunderstorm || lightning == null) return;
        if (rainAmount < 0.95f) { nextThunderAt = -1f; return; }
        // 비가 막 시작되면 첫 번개는 조금 기다렸다가
        if (nextThunderAt < 0f) { nextThunderAt = Time.time + Random.Range(6f, thunderInterval.y * 0.6f); return; }
        if (Time.time < nextThunderAt) return;
        nextThunderAt = Time.time + Random.Range(thunderInterval.x, thunderInterval.y);
        Strike(Random.value);
    }

    /// <summary>번개 + 천둥 (distance01 0 = 바로 근처, 1 = 멀리). 디버그·테스트에서도 호출 가능</summary>
    public void Strike(float distance01)
    {
        if (lightning == null) BuildLightning();
        if (strikeRoutine != null) StopCoroutine(strikeRoutine);
        strikeRoutine = StartCoroutine(StrikeRoutine(Mathf.Clamp01(distance01)));
        StrikeCount++;
    }

    System.Collections.IEnumerator StrikeRoutine(float dist)
    {
        bool indoors = BuildingInterior.IsAnyoneIndoors;
        float peak = lightningIntensity * Mathf.Lerp(1f, 0.45f, dist) * (indoors ? indoorLightningScale : 1f);
        lightning.color = lightningColor;
        lightning.enabled = true;

        // 소리는 빛보다 늦게 (거리만큼)
        float soundAt = Time.time + Mathf.Lerp(0.3f, 2.8f, dist);

        // 깜빡임 2~3번: (켜지는 세기 비율, 유지 시간, 꺼진 틈)
        int flashes = Random.value < 0.6f ? 3 : 2;
        for (int i = 0; i < flashes; i++)
        {
            float k = i == 0 ? 1f : Random.Range(0.45f, 0.9f);
            float on = Random.Range(0.04f, 0.09f);
            for (float t = 0f; t < on; t += Time.deltaTime) { lightning.intensity = peak * k; yield return null; }
            // 잔광이 빠르게 사라짐
            float fade = Random.Range(0.06f, 0.14f);
            for (float t = 0f; t < fade; t += Time.deltaTime) { lightning.intensity = peak * k * (1f - t / fade); yield return null; }
            lightning.intensity = 0f;
            float gap = Random.Range(0.04f, 0.12f);
            for (float t = 0f; t < gap; t += Time.deltaTime) yield return null;
        }
        lightning.enabled = false;

        while (Time.time < soundAt) yield return null;
        if (thunderAudio != null)
        {
            var clip = PickThunderClip(dist);
            thunderAudio.pitch = Random.Range(0.9f, 1.08f);
            float vol = thunderVolume * Mathf.Lerp(1f, 0.55f, dist) * (indoors ? 0.6f : 1f);
            if (clip != null) thunderAudio.PlayOneShot(clip, vol);
        }
        strikeRoutine = null;
    }

    AudioClip PickThunderClip(float dist)
    {
        if (thunderClips != null && thunderClips.Length > 0)
        {
            var c = thunderClips[Random.Range(0, thunderClips.Length)];
            if (c != null) return c;
        }
        if (generatedThunder == null)
            generatedThunder = new[] { GenerateThunderClip(11, true), GenerateThunderClip(23, true), GenerateThunderClip(37, false), GenerateThunderClip(41, false) };
        // 가까우면 '쾅'이 있는 소리, 멀면 우르릉만
        int baseIdx = dist < 0.45f ? 0 : 2;
        return generatedThunder[baseIdx + Random.Range(0, 2)];
    }

    /// <summary>
    /// 천둥소리 생성 (5.5초, 44.1kHz). 우르릉 = 저역 통과한 잡음을 두 번 거른 낮은 소리에 느린 굴곡(여러 번 굴러가는 느낌),
    /// crack = 앞 0.3초에 짧고 날카로운 잡음 터짐 (가까운 번개)
    /// </summary>
    static AudioClip GenerateThunderClip(int seed, bool crack)
    {
        const int rate = 44100;
        const float seconds = 5.5f;
        int n = (int)(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(seed);
        float lp1 = 0f, lp2 = 0f, lp3 = 0f, cr = 0f;
        // 느린 굴곡: 0.15~0.6초마다 바뀌는 랜덤 세기 사이를 부드럽게
        float rollA = 1f, rollB = (float)rng.NextDouble(), rollT = 0f, rollLen = 0.3f;
        float peak = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp1 += 0.06f * (white - lp1);
            lp2 += 0.03f * (lp1 - lp2);
            lp3 += 0.012f * (lp2 - lp3);
            float rumble = lp2 * 1.6f + lp3 * 3.2f;

            rollT += 1f / rate;
            if (rollT >= rollLen) { rollT = 0f; rollA = rollB; rollB = 0.35f + (float)rng.NextDouble() * 0.65f; rollLen = 0.15f + (float)rng.NextDouble() * 0.45f; }
            float roll = Mathf.Lerp(rollA, rollB, Mathf.SmoothStep(0f, 1f, rollT / rollLen));

            float attack = Mathf.Clamp01(t / (crack ? 0.03f : 0.35f));
            float env = attack * Mathf.Exp(-t / (crack ? 1.5f : 1.9f)) * roll;
            float s = rumble * env;

            if (crack && t < 0.35f)
            {
                cr += 0.45f * (white - cr); // 밝은 잡음
                float ce = Mathf.Exp(-t / 0.07f) + 0.5f * Mathf.Exp(-Mathf.Abs(t - 0.12f) / 0.03f);
                s += cr * ce * 0.9f;
            }
            data[i] = s;
            peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        // 끝 0.4초 부드럽게 0으로 + 최대 0.9로 정규화
        int tail = (int)(rate * 0.4f);
        float norm = peak > 0f ? 0.9f / peak : 1f;
        for (int i = 0; i < n; i++)
        {
            float f = i > n - tail ? (n - i) / (float)tail : 1f;
            data[i] *= norm * f;
        }
        var clip = AudioClip.Create(crack ? "Thunder_Near" : "Thunder_Far", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void LateUpdate()
    {
        bool indoors = BuildingInterior.IsAnyoneIndoors;
        var cam = Camera.main;
        float camDist = cam != null && player != null ? Vector3.Distance(cam.transform.position, player.position) : 40f;

        // 비·물 튀김·바닥 안개를 붙일 기준 위치 (2026-11-12)
        // 평소 = 플레이어. 탈출 헬기 컷신 중엔 플레이어가 헬기에 타고 하늘로 올라가서(비가 화면에서 사라졌음) → 헬리패드 기준
        Vector3? anchor = HelicopterCutscene.WeatherAnchor;
        if (anchor == null && player != null) anchor = player.position;
        if (HelicopterCutscene.WeatherAnchor != null && cam != null)
            camDist = Vector3.Distance(cam.transform.position, HelicopterCutscene.WeatherAnchor.Value); // 안개 밀도도 패드 기준

        // ── 비: 플레이어 위를 따라다님, 실내에선 끔. 양은 rainAmount로 서서히 ──
        if (rain != null)
        {
            bool raining = rainAmount > 0.001f && !indoors;
            // if (player != null) rain.transform.position = player.position + Vector3.up * rainHeight; // (이전)
            if (anchor != null) rain.transform.position = anchor.Value + Vector3.up * rainHeight;
            var em = rain.emission; em.rateOverTime = rainPerSecond * rainAmount;
            var rm = rain.main; rm.startSize = Mathf.Clamp(camDist * rainWidthPerMeter, rainWidthMin, rainWidthMax); // 카메라 거리에 맞춘 굵기
            if (raining && !rain.isPlaying) rain.Play();
            else if (!raining && rain.isPlaying) rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (splash != null)
        {
            bool on = rainSplashes && rainAmount > 0.001f && !indoors;
            if (anchor != null) splash.transform.position = anchor.Value + Vector3.up * 0.08f; // (이전) player.position 기준
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
            // Color col = Color.Lerp(dnc != null ? dnc.nightFogColor : Color.black, fogColor, day01); // (이전) 밤 = DayNightCycle 밤 안개색
            Color col = Color.Lerp(fogNightColor, fogColor, day01);

            // 실내: BuildingInterior 실내 안개가 우선 → RenderSettings는 안 건드림 (나오면 BuildingInterior가 저장해 둔 이 안개를 복원하고, 다음 프레임부터 다시 여기서 갱신)
            if (!indoors)
            {
                // ExponentialSquared는 시작 거리가 없고 "카메라로부터의 거리"라서 카메라가 멀어질수록 플레이어도 묻힘
                // → 기본 줌(40m)에서 정확히 fogDensity, 줌아웃하면 (40+가시거리)/(카메라거리+가시거리) 비율로 낮춤
                // float density = fogDensity * fogVisibility / (camDist + fogVisibility); // (이전) 기본 줌에서 0.012
                float density = fogDensity * (DefaultCamDist + fogVisibility) / (camDist + fogVisibility);
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

            // 비네트: 안개 정도만큼 0.38 → 0.5
            if (vignette != null && baseVignette >= 0f)
                vignette.intensity.value = Mathf.Lerp(baseVignette, fogVignetteIntensity, fogAmount);

            // 바닥 안개: 플레이어 따라다님(입자는 월드에 남아 바람에 흘러감), 실내에선 끔. 진하기 = fogAmount, 색 = 낮 흰색 ↔ 밤 보라
            if (groundFogPs != null)
            {
                bool on = groundFog && !indoors;
                if (anchor != null) groundFogPs.transform.position = anchor.Value + Vector3.up * (groundFogHeight.x + groundFogHeight.y) * 0.5f; // (이전) player.position 기준
                if (on && !groundFogPs.isPlaying)
                {
                    if (cam != null) cam.depthTextureMode |= DepthTextureMode.Depth; // 소프트 파티클용
                    groundFogPs.Play();
                }
                else if (!on && groundFogPs.isPlaying) groundFogPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                SetGroundFogTint(Color.Lerp(fogNightColor, Color.white, day01), fogAmount);
            }
        }
        else if (fogApplied)
        {
            // 안개 끝: 원래대로 (안개는 DayNightCycle에 돌려줌 → 다음 Update부터 원래 Linear 안개)
            fogApplied = false;
            if (vignette != null && baseVignette >= 0f) vignette.intensity.value = baseVignette;
            if (groundFogPs != null && groundFogPs.isPlaying) groundFogPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (cam != null) cam.depthTextureMode = baseDepthMode;
            if (dnc != null) dnc.controlFog = dncFogWasOn;
            if (cam != null) { cam.farClipPlane = baseFar; cam.backgroundColor = baseBackground; }
            if (dnc != null && dnc.flashlight != null && baseFlashlightRange >= 0f) dnc.flashlight.range = baseFlashlightRange;
        }
    }

    const float DefaultCamDist = 40f; // CameraZoom 기본 거리 - 안개 밀도 기준

    /// <summary>
    /// 바닥 안개: 크고 흐린 소프트 파티클이 1~3m 높이에서 천천히 흘러감.
    /// 원반(반경 groundFogRadius) 위에 뿌리고 ±1m 랜덤 위치로 높이를 흩뜨림. 입자는 월드 공간 → 플레이어가 움직여도 제자리에서 흘러감
    /// 재질: Resources/WorldDecor/GroundFog (Particles/Standard Unlit, Fade + Soft Particles). 없으면 SmokeAlpha(소프트 X)
    /// </summary>
    ParticleSystem BuildGroundFog()
    {
        var go = new GameObject("GroundFog");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float lifeMin = 14f, lifeMax = 20f;
        var main = ps.main;
        main.loop = true;
        main.duration = lifeMax;
        main.prewarm = true; // 안개가 시작되자마자 이미 깔려 있게
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(groundFogSize.x, groundFogSize.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, groundFogAlpha.x), new Color(0.85f, 0.85f, 0.88f, groundFogAlpha.y));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = groundFogCount + 10;

        var em = ps.emission; em.rateOverTime = groundFogCount / ((lifeMin + lifeMax) * 0.5f); // 동시 개수 ≈ groundFogCount

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = groundFogRadius;
        shape.radiusThickness = 1f;                 // 원 안 전체
        shape.rotation = new Vector3(90f, 0f, 0f);  // 바닥과 나란히
        shape.randomPositionAmount = (groundFogHeight.y - groundFogHeight.x) * 0.5f; // 높이 ±1m

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(groundFogWind.x * 0.6f, groundFogWind.x * 1.4f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        vel.z = new ParticleSystem.MinMaxCurve(groundFogWind.z * 0.6f, groundFogWind.z * 1.4f);

        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); // 천천히 일렁임

        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g; // 생겨날 때/사라질 때 서서히

        var src = Resources.Load<Material>("WorldDecor/GroundFog");
        if (src == null) src = Resources.Load<Material>("WorldDecor/SmokeAlpha");
        groundFogMat = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
        groundFogMat.name = "GroundFog(Runtime)";
        groundFogMat.mainTexture = WorldDecorTextures.SoftDot("FX_GroundFog", true); // 가장자리 흐린 얼룩 (불/연기와 같은 텍스처 생성기)

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = groundFogMat;
        r.maxParticleSize = 3f; // 기본 0.5(화면 절반)면 가까이 있는 큰 입자가 잘려 작아짐
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// <summary>바닥 안개 색/진하기 (재질 색 × 입자 색). Legacy 파티클 셰이더는 _TintColor(×2)</summary>
    void SetGroundFogTint(Color tint, float amount)
    {
        if (groundFogMat == null) return;
        tint.a = amount;
        if (groundFogMat.HasProperty("_Color")) groundFogMat.SetColor("_Color", tint);
        else if (groundFogMat.HasProperty("_TintColor")) groundFogMat.SetColor("_TintColor", tint * 0.5f);
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
        // main.startColor = new Color(0.8f, 0.85f, 0.95f, 0.35f); // (이전) 너무 옅어서 안 보였음
        // main.startColor = new Color(0.82f, 0.87f, 0.97f, 0.5f); // (이전) 너무 진했음
        main.startColor = new Color(0.85f, 0.9f, 1f, rainAlpha);
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // 화면 밖 판정으로 멈췄다 재개될 때 빈 구간 없게
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4000;

        var em = ps.emission; em.rateOverTime = rainPerSecond;
        // 박스에서 로컬 +Z 방향으로 나감 → X축 90° 돌려서 아래로. 그러면 수평면 = 로컬 X-Y 평면
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(rainArea.x, rainArea.z, rainArea.y);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        // r.lengthScale = 1f; r.velocityScale = 0.04f; // (이전) 위에서 내려다보면 짧은 점으로 보였음
        // r.lengthScale = 1f; r.velocityScale = 0.055f; // (이전) 길이 1.5m
        r.lengthScale = 1f; r.velocityScale = 0.07f; // 가늘고 긴 빗줄기 (약 2m)
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

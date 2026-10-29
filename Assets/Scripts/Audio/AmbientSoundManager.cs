using UnityEngine;

/// <summary>
/// 낮밤 사이클 연동 배경음 (씬 배치 불필요 - 게임 시작 시 자동 생성, DontDestroyOnLoad).
///  - 밤(DayNightCycle.IsNight): night_loop 0.3 / 그 외(낮·황혼·새벽): day_loop 0.2 → 2초 크로스페이드
///  - 낮(Day)에만 30~60초 간격으로 까마귀 울음 (플레이어 주변 15~30m 랜덤 위치, 3D)
/// 볼륨은 AudioManager.Vol, 출력은 Ambient 믹서 그룹 (설정 메뉴에서 따로 조절 가능).
/// </summary>
public class AmbientSoundManager : MonoBehaviour
{
    [Tooltip("낮↔밤 배경음 전환 시간(초)")]
    public float crossfadeTime = 2f;
    public float crowIntervalMin = 30f, crowIntervalMax = 60f;

    static AmbientSoundManager instance;
    AudioSource day, night;
    float nextCrowTime;

    /// <summary>현재 상태 (검증/디버그용)</summary>
    public static float DayVolume => instance != null && instance.day != null ? instance.day.volume : 0f;
    public static float NightVolume => instance != null && instance.night != null ? instance.night.volume : 0f;
    public static float NextCrowTime => instance != null ? instance.nextCrowTime : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("AmbientSoundManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AmbientSoundManager>();
    }

    void Start()
    {
        var lib = AudioManager.Library;
        if (lib == null) { enabled = false; return; }
        // 볼륨 0에서 시작 → Update가 현재 시간대 쪽으로 올림
        day = AudioManager.PlayLoop(lib.dayAmbientLoop, 0f);
        night = AudioManager.PlayLoop(lib.nightAmbientLoop, 0f);
        nextCrowTime = Time.time + Random.Range(crowIntervalMin, crowIntervalMax);
    }

    void Update()
    {
        var dnc = DayNightCycle.Instance;
        bool hasCycle = dnc != null;            // 메뉴 씬 등 낮밤이 없으면 배경음 끔
        bool isNight = hasCycle && dnc.IsNight;

        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, crossfadeTime);
        Fade(day, hasCycle && !isNight ? AudioManager.Vol.DayAmbient : 0f, AudioManager.Vol.DayAmbient * step);
        Fade(night, isNight ? AudioManager.Vol.NightAmbient : 0f, AudioManager.Vol.NightAmbient * step);

        // 까마귀: 낮에만. 낮이 아니면 타이머만 밀어둠 (아침이 되자마자 바로 울지 않게)
        if (!hasCycle || !dnc.IsDay) { nextCrowTime = Mathf.Max(nextCrowTime, Time.time + crowIntervalMin * 0.5f); return; }
        if (Time.time < nextCrowTime) return;
        nextCrowTime = Time.time + Random.Range(crowIntervalMin, crowIntervalMax);
        Vector2 off = Random.insideUnitCircle.normalized * Random.Range(15f, 30f);
        AudioManager.PlaySFX(SoundLibrary.Pick(AudioManager.Library.crows),
                             AudioManager.ListenerPosition + new Vector3(off.x, 8f, off.y), // 나무/지붕 위쯤
                             AudioManager.Vol.Crows, AudioManager.Channel.Ambient, 60f);
    }

    static void Fade(AudioSource src, float target, float maxStep)
    {
        if (src == null) return;
        src.volume = Mathf.MoveTowards(src.volume, target, maxStep);
    }
}

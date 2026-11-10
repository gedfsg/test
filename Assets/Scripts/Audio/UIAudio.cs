using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 메뉴 UI 사운드 + BGM (게임 시작 시 자동 생성, DontDestroyOnLoad).
/// 매핑(클립·볼륨·피치)은 Resources/Audio/UIAudio.prefab의 이 컴포넌트에 있음 - Tools → Audio → Setup UI Audio가 채움.
///  - UI 소리 → AudioMixer UI 그룹(UIVolume) / BGM·죽음 스팅어 → BGM 그룹(BGMVolume) → 설정 슬라이더로 실시간 조절
///  - 메인 메뉴 BGM: MainMenu 씬에 들어오면 작은 음량(0.3)으로 서서히 커지며 반복, 씬을 나가면 서서히 꺼짐
///  - 죽음 스팅어: 생존 종료 화면이 뜰 때 한 번 (0.7)
/// 일시정지(timeScale 0) 중에도 재생됨 (AudioSource는 시간 배율과 무관)
/// </summary>
public class UIAudio : MonoBehaviour
{
    public enum Sound { None, Hover, Click, Back, Toggle, Slider, PanelOpen, PanelClose, PauseOn, PauseOff, Apply, Defaults }

    [System.Serializable]
    public class Entry
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.55f;
        [Range(0.5f, 2f)] public float pitch = 1f;
    }

    [Header("UI (UI 그룹, 기본 0.5~0.6)")]
    public Entry hover = new Entry();
    public Entry click = new Entry();
    public Entry back = new Entry();
    public Entry toggle = new Entry();
    [Tooltip("슬라이더를 움직일 때 미세한 틱 (값에 따라 피치가 살짝 바뀜)")]
    public Entry slider = new Entry();
    public Entry panelOpen = new Entry();
    public Entry panelClose = new Entry();
    public Entry pauseOn = new Entry();
    public Entry pauseOff = new Entry();
    public Entry apply = new Entry();
    public Entry defaults = new Entry();

    [Header("BGM (BGM 그룹)")]
    public AudioClip menuBgm;
    [Range(0f, 1f)] public float menuBgmVolume = 0.3f; // 원본이 커서 낮춤
    public float bgmFadeIn = 2.5f;
    public float bgmFadeOut = 0.8f;
    public AudioClip deathStinger;
    [Range(0f, 1f)] public float deathVolume = 0.7f;

    static UIAudio instance;
    AudioSource[] uiSources;
    int nextSource;
    AudioSource bgm, stinger;
    float bgmTarget, bgmFadeSpeed;
    float lastHover, lastSlider;

    // ── 생성 ───────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var prefab = Resources.Load<GameObject>("Audio/UIAudio");
        var go = prefab != null ? Instantiate(prefab) : new GameObject();
        go.name = "UIAudio";
        DontDestroyOnLoad(go);
        if (go.GetComponent<UIAudio>() == null) go.AddComponent<UIAudio>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        uiSources = new AudioSource[6]; // 피치가 다른 소리가 겹쳐도 서로 끊지 않게 돌려 씀
        for (int i = 0; i < uiSources.Length; i++) uiSources[i] = NewSource(false);
        bgm = NewSource(true);
        stinger = NewSource(false);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (instance == this) { instance = null; SceneManager.sceneLoaded -= OnSceneLoaded; }
    }

    AudioSource NewSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true;
        return s;
    }

    // AudioManager의 믹서 그룹은 Awake 이후에 생기므로 재생할 때마다 연결 (없으면 null = 믹서 안 거침)
    void Route()
    {
        var uiGroup = AudioManager.GetGroup(AudioManager.Channel.UI);
        foreach (var s in uiSources) s.outputAudioMixerGroup = uiGroup;
        var bgmGroup = AudioManager.GetGroup(AudioManager.Channel.BGM);
        bgm.outputAudioMixerGroup = bgmGroup;
        stinger.outputAudioMixerGroup = bgmGroup;
    }

    // ── 메인 메뉴 BGM ──────────────────────────────
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Route();
        stinger.Stop(); // 다시 시작/메인 메뉴로 가면 죽음 스팅어 끊음
        if (scene.name == MenuUI.MainMenuScene && menuBgm != null)
        {
            if (bgm.clip != menuBgm || !bgm.isPlaying)
            {
                bgm.clip = menuBgm;
                bgm.volume = 0f;
                bgm.Play();
            }
            bgmTarget = menuBgmVolume;
            bgmFadeSpeed = menuBgmVolume / Mathf.Max(0.05f, bgmFadeIn);
        }
        else
        {
            bgmTarget = 0f;
            bgmFadeSpeed = Mathf.Max(bgm.volume, 0.01f) / Mathf.Max(0.05f, bgmFadeOut);
        }
    }

    void Update()
    {
        if (bgm == null || !bgm.isPlaying) return;
        bgm.volume = Mathf.MoveTowards(bgm.volume, bgmTarget, bgmFadeSpeed * Time.unscaledDeltaTime);
        if (bgmTarget <= 0f && bgm.volume <= 0.0001f) bgm.Stop();
    }

    // ── 재생 API ───────────────────────────────────
    public static void Play(Sound sound)
    {
        if (instance == null || sound == Sound.None) return;
        if (sound == Sound.Hover)
        {
            if (Time.unscaledTime - instance.lastHover < 0.04f) return; // 버튼 사이를 빠르게 지나갈 때 겹침 방지
            instance.lastHover = Time.unscaledTime;
        }
        instance.PlayEntry(instance.Get(sound), 1f);
    }

    /// <summary>슬라이더 미세 틱. t01 = 슬라이더 위치 (0~1, 피치에 반영). 0.06초에 한 번까지</summary>
    public static void SliderTick(float t01)
    {
        if (instance == null || Time.unscaledTime - instance.lastSlider < 0.06f) return;
        instance.lastSlider = Time.unscaledTime;
        instance.PlayEntry(instance.slider, Mathf.Lerp(0.9f, 1.25f, Mathf.Clamp01(t01)));
    }

    /// <summary>생존 종료 화면 스팅어 (BGM 그룹)</summary>
    public static void PlayDeathStinger()
    {
        if (instance == null || instance.deathStinger == null) return;
        instance.Route();
        instance.bgmTarget = 0f;
        instance.bgmFadeSpeed = 1f;
        instance.stinger.clip = instance.deathStinger;
        instance.stinger.volume = instance.deathVolume;
        instance.stinger.Play();
    }

    Entry Get(Sound s)
    {
        switch (s)
        {
            case Sound.Hover: return hover;
            case Sound.Click: return click;
            case Sound.Back: return back;
            case Sound.Toggle: return toggle;
            case Sound.Slider: return slider;
            case Sound.PanelOpen: return panelOpen;
            case Sound.PanelClose: return panelClose;
            case Sound.PauseOn: return pauseOn;
            case Sound.PauseOff: return pauseOff;
            case Sound.Apply: return apply;
            case Sound.Defaults: return defaults;
            default: return null;
        }
    }

    void PlayEntry(Entry e, float pitchScale)
    {
        if (e == null || e.clip == null) return;
        Route();
        var src = uiSources[nextSource];
        nextSource = (nextSource + 1) % uiSources.Length;
        src.pitch = e.pitch * pitchScale;
        src.PlayOneShot(e.clip, e.volume);
    }
}

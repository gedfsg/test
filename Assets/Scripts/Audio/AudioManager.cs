using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 전역 사운드 관리 (씬에 따로 배치할 필요 없음 - 게임 시작 시 자동 생성, DontDestroyOnLoad).
///  - PlaySFX(clip, 위치, 볼륨)  : 3D 효과음 (총/좀비/피격). 오디오 소스 풀 재사용
///  - PlayUI(clip, 볼륨)         : 2D 효과음 (UI/심장소리/발소리)
///  - PlayLoop(clip, 볼륨)       : 반복 환경음 (반환된 AudioSource로 볼륨 조절/정지)
///  - SetVolume(채널, 0~1)       : 설정 메뉴용 볼륨 (AudioMixer 노출 파라미터, PlayerPrefs 저장)
///
/// 리스너 위치: 카메라가 플레이어 위 약 39m에 있어서 카메라에 리스너가 있으면 3D 소리가 전부 멀게 들림.
///   → 카메라의 AudioListener를 끄고, 플레이어 머리 위치를 따라다니는 리스너를 씀 (방향은 카메라 화면 기준 좌우).
///
/// 데이터: Resources/Audio/SoundLibrary (클립), Resources/Audio/GameMixer (믹서) - Tools → Audio → Setup Sound System 으로 생성.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public enum Channel { SFX, Zombie, UI, Ambient }

    /// <summary>
    /// 카테고리별 기본 볼륨 (DayZ / Project Zomboid / PUBG 참고).
    /// 클립은 전처리로 전부 -20 LUFS로 맞췄으므로 여기 값이 곧 실제 크기 순서: 총소리 > 좀비/피격 > UI > 발소리
    /// </summary>
    public static class Vol
    {
        // public const float Gunshot = 1.0f;   // (이전) 플레이테스트: 너무 커서 낮춤
        public const float Gunshot = 0.8f;      // 긴장감, 원거리 식별
        public const float GunshotShotgun = 0.9f; // 샷건은 여전히 가장 크게
        public const float ZombieScream = 0.9f; // 위협 표현 (추적/공격/사망)
        public const float PlayerHurt = 0.9f;   // 위기감 (피격/사망)
        // public const float ZombieGroan = 0.6f; // (이전) 좀비가 많으면 시끄러워서 낮춤
        public const float ZombieGroan = 0.4f;  // 배경 분위기
        public const float Notification = 0.5f; // 서브 미션 활성화/완료, 점수, 실패/에러 알림
        public const float DayAmbient = 0.2f;   // 낮/평시 배경 루프
        public const float NightAmbient = 0.3f; // 밤 배경 루프
        public const float Crows = 0.35f;       // 낮 까마귀 (가끔)
        public const float OutOfBreath = 0.6f;  // 스태미나 소진 숨소리
        public const float Impact = 0.7f;       // 타격감
        public const float Pickup = 0.5f;       // 작은 피드백
        public const float UI = 0.4f;           // 클릭/문/인벤토리 - 과하지 않게
        public const float Footstep = 0.3f;     // 반복되므로 낮게
        public const float Rain = 0.4f;         // 배경 유지
        public const float Heartbeat = 1.2f;    // 긴장감 극대화 (1 초과 = 증폭)
    }

    // 믹서 노출 파라미터 이름 (Setup Sound System이 같은 이름으로 만듦)
    public const string MasterParam = "MasterVolume";
    static readonly string[] ChannelParams = { "SFXVolume", "ZombieVolume", "UIVolume", "AmbientVolume" };

    [Tooltip("3D 효과음 동시 재생 최대 수 (꽉 차면 가장 오래된 소리를 끊고 재사용)")]
    public int poolSize = 32;
    [Tooltip("리스너 높이 (플레이어 발 기준, 머리쯤)")]
    public float listenerHeight = 1.6f;

    static AudioManager instance;
    public static SoundLibrary Library { get; private set; }
    public static AudioMixer Mixer { get; private set; }

    readonly AudioMixerGroup[] groups = new AudioMixerGroup[4];
    readonly List<AudioSource> pool = new List<AudioSource>();
    readonly Dictionary<AudioSource, float> startTimes = new Dictionary<AudioSource, float>();
    AudioSource uiSource;
    Transform listener;
    Transform player;
    float nextListenerCheck;

    // ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("AudioManager");
        DontDestroyOnLoad(go);
        go.AddComponent<AudioManager>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        Library = Resources.Load<SoundLibrary>("Audio/SoundLibrary");
        Mixer = Resources.Load<AudioMixer>("Audio/GameMixer");
        if (Library == null) Debug.LogWarning("[Audio] Resources/Audio/SoundLibrary 없음 - Tools → Audio → Setup Sound System 실행 필요");
        if (Mixer != null)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                var found = Mixer.FindMatchingGroups(((Channel)i).ToString());
                groups[i] = found.Length > 0 ? found[0] : null;
            }
        }

        uiSource = gameObject.AddComponent<AudioSource>();
        uiSource.playOnAwake = false;
        uiSource.spatialBlend = 0f;
        uiSource.outputAudioMixerGroup = groups[(int)Channel.UI];

        var l = new GameObject("PlayerListener");
        l.transform.SetParent(transform, false);
        l.AddComponent<AudioListener>();
        listener = l.transform;
    }

    void Start()
    {
        // 저장된 볼륨 복원 (믹서 SetFloat는 Awake에서는 적용 안 될 때가 있어 Start에서)
        SetVolume(MasterParam, PlayerPrefs.GetFloat(MasterParam, 1f));
        foreach (var p in ChannelParams) SetVolume(p, PlayerPrefs.GetFloat(p, 1f));
    }

    void LateUpdate()
    {
        UpdateListener();
        DetectButtonClick();
    }

    // ── 리스너 ─────────────────────────────────────
    void UpdateListener()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        // 다른 리스너(카메라)는 끔 - 리스너가 2개면 Unity 경고 + 소리 위치 꼬임 (1초마다 확인)
        if (Time.unscaledTime >= nextListenerCheck)
        {
            nextListenerCheck = Time.unscaledTime + 1f;
            foreach (var other in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (other.transform != listener && other.enabled) other.enabled = false;
        }

        var cam = Camera.main;
        if (player != null) listener.position = player.position + Vector3.up * listenerHeight;
        else if (cam != null) listener.position = cam.transform.position;
        // 화면 기준 좌우 = 소리 좌우 (Q/E로 카메라를 돌려도 맞게 카메라 Y회전만 따라감)
        if (cam != null) listener.rotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
    }

    /// <summary>현재 소리를 듣는 위치 (플레이어 머리). 좀비 목소리 거리 판정용</summary>
    public static Vector3 ListenerPosition => instance != null ? instance.listener.position : Vector3.zero;

    // ── 버튼 클릭음 ─────────────────────────────────
    // 버튼마다 따로 연결하지 않아도 되게: 클릭한 UI가 활성 Button/Toggle이면 클릭음
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    void DetectButtonClick()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || EventSystem.current == null) return;
        var data = new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() };
        uiHits.Clear();
        EventSystem.current.RaycastAll(data, uiHits);
        if (uiHits.Count == 0) return;
        var sel = uiHits[0].gameObject.GetComponentInParent<Selectable>();
        if (sel != null && sel.IsInteractable() && (sel is Button || sel is Toggle))
            PlayUI(SoundLibrary.Pick(Library != null ? Library.click : null), Vol.UI);
    }

    // ── 재생 API ───────────────────────────────────
    /// <summary>3D 효과음. maxDistance 밖에서는 안 들림 (선형 감쇠)</summary>
    public static void PlaySFX(AudioClip clip, Vector3 position, float volume = 1f,
                               Channel channel = Channel.SFX, float maxDistance = 40f, float pitch = 1f)
    {
        if (clip == null || instance == null) return;
        var src = instance.GetPooledSource();
        src.transform.position = position;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = pitch;
        src.maxDistance = maxDistance;
        src.outputAudioMixerGroup = instance.groups[(int)channel];
        src.Play();
        instance.startTimes[src] = Time.unscaledTime;
    }

    /// <summary>2D 효과음 (위치 상관없이 같은 크기). volume 1 초과 = 증폭</summary>
    public static void PlayUI(AudioClip clip, float volume = 1f)
    {
        if (clip == null || instance == null) return;
        instance.uiSource.PlayOneShot(clip, volume);
        UIPlayed?.Invoke(clip, volume);
    }

    /// <summary>2D 소리가 재생될 때마다 (PlayOneShot은 재생 중인 클립을 조회할 수 없어서 - 검증/디버그용)</summary>
    public static event System.Action<AudioClip, float> UIPlayed;

    /// <summary>반복 재생 (2D). 반환된 소스로 volume 조절 / Stop()</summary>
    public static AudioSource PlayLoop(AudioClip clip, float volume = 1f, Channel channel = Channel.Ambient)
    {
        if (clip == null || instance == null) return null;
        var src = instance.gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = volume;
        src.outputAudioMixerGroup = instance.groups[(int)channel];
        src.Play();
        return src;
    }

    /// <summary>채널 믹서 그룹 (외부에서 직접 만든 AudioSource 연결용, 믹서 없으면 null)</summary>
    public static AudioMixerGroup GetGroup(Channel channel) => instance != null ? instance.groups[(int)channel] : null;

    AudioSource GetPooledSource()
    {
        // 쉬는 소스 재사용
        foreach (var s in pool) if (!s.isPlaying) return s;
        // 풀이 덜 찼으면 새로
        if (pool.Count < poolSize)
        {
            var go = new GameObject("SFX_" + pool.Count);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;               // 3D
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f;
            s.dopplerLevel = 0f;               // 탑뷰에서 도플러는 어색함
            pool.Add(s);
            return s;
        }
        // 꽉 참: 가장 오래 재생 중인 소리를 끊고 재사용
        AudioSource oldest = pool[0];
        foreach (var s in pool)
            if (startTimes.TryGetValue(s, out float t) && t < startTimes[oldest]) oldest = s;
        oldest.Stop();
        return oldest;
    }

    // ── 볼륨 설정 (설정 메뉴용) ───────────────────────
    /// <summary>전체 볼륨 0~1</summary>
    public static void SetMasterVolume(float linear) => SetVolume(MasterParam, linear);
    /// <summary>채널 볼륨 0~1</summary>
    public static void SetChannelVolume(Channel channel, float linear) => SetVolume(ChannelParams[(int)channel], linear);
    public static float GetMasterVolume() => PlayerPrefs.GetFloat(MasterParam, 1f);
    public static float GetChannelVolume(Channel channel) => PlayerPrefs.GetFloat(ChannelParams[(int)channel], 1f);

    static void SetVolume(string param, float linear)
    {
        linear = Mathf.Clamp01(linear);
        PlayerPrefs.SetFloat(param, linear);
        if (Mixer != null)
            Mixer.SetFloat(param, linear > 0.0001f ? 20f * Mathf.Log10(linear) : -80f); // 선형 → dB
        else if (param == MasterParam)
            AudioListener.volume = linear; // 믹서가 없으면 전체 볼륨만
    }
}

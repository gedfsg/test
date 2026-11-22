using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 특수 무기 소리 (2026-11-22). 플레이어 루트에 하나 (씬이 열릴 때 / Weapon이 처음 쏠 때 자동으로 붙음).
/// 사운드 파일: Assets/Audio/weapon/sp/ → WeaponData.fireSound(1회) / WeaponData.loopSound(반복)
///
/// (2차) 피드백 "화염방사기에 총소리도 같이 남 / RPG·레이저 총소리 말고 제대로 / 전기톱이 전기톱 소리가 안 남"
///  원본 파일들은 한 파일에 여러 소리가 이어져 있어서(파형 측정) 통째로 반복·재생하면 이상하게 들렸음:
///   - 화염방사기: 0.9~1.3초·4.0~4.4초에 점화 "펑" → 쏘기 시작할 때마다·6.7초마다 총소리처럼 들림
///   - 전기톱: 0~0.6초 무음 → 공회전 → 3~4초 거의 무음 → 4.1~9초 고속 회전 → 끝 감쇠 → 반복하면 끊겼다 다시 시동
///   - 미니건: 회전 시작 → 연사 → 감쇠가 통째로 반복
///   - RPG: 0.2초 발사음 + 1.4초 정적 + 1.7~2.9초 폭발음 → 쏘자마자 1.5초 뒤 엉뚱한 곳에서 폭발음,
///          착탄은 따로 산탄총 소리를 낮게 재생(Bullet.Explode)해서 총소리처럼 들림
///  → 필요한 구간만 잘라 씀 (Segment). 반복 구간은 끝을 처음과 섞어(crossfade) 이음매 없이 반복
///   - 화염방사기: 1.4~3.9초 반복 (쏘는 동안, 떼면 0.08초 페이드)
///   - 미니건: 1.5~3.2초 반복 + 피치 0.6 → 1.0 (spin up), 떼면 피치 내려가며 0.5초 페이드 (spin down)
///   - 전기톱: 공회전 1.4~2.7초 / 갈아낼 때 5.4~8.9초 두 소리를 0.15초에 걸쳐 섞어 바꿈, 내려놓으면 0.3초 페이드
///   - RPG: 발사 0~0.7초 1회, 착탄 폭발 1.6~3.5초 (Weapon이 탄에 넣어 줌 → Bullet.Explode)
///   - 레이저: 0~0.45초 1회 (연사 시 겹침 허용)
/// 모든 소리: 3D (SpatialBlend 1), Logarithmic 감쇠, 최대 25m, AudioMixer SFX 그룹 (설정 화면 효과음 볼륨 적용)
/// </summary>
[DisallowMultipleComponent]
public class SpecialWeaponAudio : MonoBehaviour
{
    [Header("볼륨")]
    public float flamethrowerVolume = 0.6f;
    public float chainsawVolume = 0.6f;
    public float rpgVolume = 0.9f;
    public float minigunVolume = 0.6f;
    public float laserVolume = 0.5f;

    [Header("3D")]
    public float minDistance = 2f;
    public float maxDistance = 25f;

    [Header("미니건 피치")]
    public float minigunSpinPitchMin = 0.6f;

    [Header("전기톱")]
    [Tooltip("공회전 ↔ 갈아내기 소리 전환 시간 (초)")]
    public float chainsawBlendTime = 0.15f;

    // 파일별 사용 구간 (초) - Assets/Audio/weapon/sp 파일 파형을 재서 정함. 파일을 바꾸면 같이 바꿀 것
    public static readonly Vector2 FlameLoopSeg = new Vector2(1.4f, 3.9f);
    public static readonly Vector2 MinigunLoopSeg = new Vector2(1.5f, 3.2f);
    public static readonly Vector2 SawIdleSeg = new Vector2(1.4f, 2.7f);
    public static readonly Vector2 SawCutSeg = new Vector2(5.4f, 8.9f);
    public static readonly Vector2 RpgLaunchSeg = new Vector2(0f, 0.7f);
    public static readonly Vector2 RpgImpactSeg = new Vector2(1.6f, 3.5f);
    public static readonly Vector2 LaserSeg = new Vector2(0f, 0.45f);
    const float LoopCrossfade = 0.25f;

    AudioSource loopSrc, shotSrc, sawIdle, sawCut;
    SpecialWeaponKind loopKind = SpecialWeaponKind.None;
    float lastShotTime = -99f;
    float fadeOutSpeed;           // 0이면 페이드 중 아님
    float baseVolume;
    Weapon minigunWeapon;         // 예열 시간을 읽을 무기
    PlayerController pc;
    float sawCutMix, sawFade = 1f; bool sawOn;

    /// <summary>c가 속한 플레이어(루트)의 컴포넌트. 없으면 붙임</summary>
    public static SpecialWeaponAudio For(Component c)
    {
        var owner = c.GetComponentInParent<PlayerController>();
        var root = owner != null ? owner.gameObject : c.transform.root.gameObject;
        var a = root.GetComponent<SpecialWeaponAudio>();
        return a != null ? a : root.AddComponent<SpecialWeaponAudio>();
    }

    // 전기톱은 쏘지 않으므로(근접) 씬이 열릴 때 미리 붙여 둬야 들고 있을 때 소리가 남 (메인 메뉴 → 게임 씬 전환마다)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null && p.GetComponent<SpecialWeaponAudio>() == null) p.AddComponent<SpecialWeaponAudio>();
    }

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        loopSrc = NewSource("SpecialWeaponLoop", true);
        shotSrc = NewSource("SpecialWeaponShot", false);
        sawIdle = NewSource("ChainsawIdle", true);
        sawCut = NewSource("ChainsawCut", true);
    }

    AudioSource NewSource(string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.2f; // 손·총 높이쯤
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = minDistance;
        s.maxDistance = maxDistance;
        s.dopplerLevel = 0f;
        s.outputAudioMixerGroup = AudioManager.GetGroup(AudioManager.Channel.SFX);
        return s;
    }

    void EnsureMixer()
    {
        if (shotSrc.outputAudioMixerGroup != null) return;
        var g = AudioManager.GetGroup(AudioManager.Channel.SFX);
        shotSrc.outputAudioMixerGroup = loopSrc.outputAudioMixerGroup = sawIdle.outputAudioMixerGroup = sawCut.outputAudioMixerGroup = g;
    }

    /// <summary>Weapon.Shoot에서 매 발 호출. 이 무기 소리를 처리했으면 true (기본 총소리 생략)</summary>
    public bool OnShot(WeaponData d, Weapon weapon)
    {
        if (d == null) return false;
        EnsureMixer();
        switch (d.specialKind)
        {
            case SpecialWeaponKind.Flamethrower:
                if (d.loopSound == null) return false;
                lastShotTime = Time.time;
                StartLoop(SpecialWeaponKind.Flamethrower, Segment(d.loopSound, FlameLoopSeg, true), flamethrowerVolume, 1f);
                return true;
            case SpecialWeaponKind.Minigun:
                if (d.loopSound == null) return false;
                OnSpinning(d, weapon);
                return true;
            case SpecialWeaponKind.RPG:
                if (d.fireSound == null) return false;
                shotSrc.PlayOneShot(Segment(d.fireSound, RpgLaunchSeg, false), rpgVolume);
                return true;
            case SpecialWeaponKind.Laser:
                if (d.fireSound == null) return false;
                shotSrc.PlayOneShot(Segment(d.fireSound, LaserSeg, false), laserVolume);
                return true;
        }
        return false;
    }

    /// <summary>미니건 예열 시작 (방아쇠를 누르는 동안 매 프레임) - 첫 발 전부터 회전음</summary>
    public void OnSpinning(WeaponData d, Weapon weapon)
    {
        if (d == null || d.loopSound == null) return;
        EnsureMixer();
        minigunWeapon = weapon;
        lastShotTime = Time.time;
        StartLoop(SpecialWeaponKind.Minigun, Segment(d.loopSound, MinigunLoopSeg, true), minigunVolume,
                  loopKind == SpecialWeaponKind.Minigun ? loopSrc.pitch : minigunSpinPitchMin);
    }

    /// <summary>RPG 착탄 폭발음 (Weapon.SpawnBullet이 탄에 넣어 줌). rpg.mp3의 폭발 구간</summary>
    public static AudioClip RpgImpactClip(WeaponData d) => d != null && d.fireSound != null ? Segment(d.fireSound, RpgImpactSeg, false) : null;

    void StartLoop(SpecialWeaponKind kind, AudioClip clip, float volume, float pitch)
    {
        fadeOutSpeed = 0f;
        baseVolume = volume;
        if (loopKind == kind && loopSrc.isPlaying && loopSrc.clip == clip) { loopSrc.volume = volume; return; }
        loopKind = kind;
        loopSrc.clip = clip;
        loopSrc.volume = volume;
        loopSrc.pitch = pitch;
        loopSrc.Play();
    }

    void FadeOut(float seconds)
    {
        if (!loopSrc.isPlaying || fadeOutSpeed > 0f) return;
        fadeOutSpeed = 1f / Mathf.Max(0.01f, seconds);
    }

    void Update()
    {
        UpdateChainsaw();

        // 화염방사기: 발사가 끊기면 멈춤
        if (loopKind == SpecialWeaponKind.Flamethrower && Time.time - lastShotTime > 0.15f) FadeOut(0.08f);

        // 미니건: 방아쇠 누르는 동안 피치 올라감, 떼면 내려가며 사라짐
        if (loopKind == SpecialWeaponKind.Minigun)
        {
            bool spinning = Time.time - lastShotTime <= 0.15f;
            float spinUp = minigunWeapon != null && minigunWeapon.weaponData != null ? Mathf.Max(0.1f, minigunWeapon.weaponData.spinUpTime) : 1f;
            float rate = (1f - minigunSpinPitchMin) / spinUp;
            loopSrc.pitch = Mathf.MoveTowards(loopSrc.pitch, spinning ? 1f : minigunSpinPitchMin * 0.8f, rate * Time.deltaTime);
            if (!spinning) FadeOut(0.5f);
        }

        // 페이드 아웃 → 0이면 정지
        if (fadeOutSpeed > 0f)
        {
            loopSrc.volume = Mathf.MoveTowards(loopSrc.volume, 0f, baseVolume * fadeOutSpeed * Time.deltaTime);
            if (loopSrc.volume <= 0.001f)
            {
                loopSrc.Stop();
                loopSrc.pitch = 1f;
                loopKind = SpecialWeaponKind.None;
                fadeOutSpeed = 0f;
            }
        }
    }

    /// <summary>전기톱: 들고 있으면 공회전, 갈아내는 동안(PlayerController.IsChainsawCutting) 고속 회전 소리로 섞어 바꿈</summary>
    void UpdateChainsaw()
    {
        var held = WeaponHotbarUI.Instance != null ? WeaponHotbarUI.Instance.GetActiveItem() as WeaponData : null;
        bool sawHeld = held != null && held.specialKind == SpecialWeaponKind.Chainsaw && held.loopSound != null && Time.timeScale > 0f;
        if (sawHeld)
        {
            EnsureMixer();
            var idleClip = Segment(held.loopSound, SawIdleSeg, true);
            var cutClip = Segment(held.loopSound, SawCutSeg, true);
            if (sawIdle.clip != idleClip) sawIdle.clip = idleClip;
            if (sawCut.clip != cutClip) sawCut.clip = cutClip;
            if (!sawIdle.isPlaying) sawIdle.Play();
            if (!sawCut.isPlaying) sawCut.Play();
            sawOn = true;
            sawFade = Mathf.MoveTowards(sawFade, 1f, Time.deltaTime / 0.1f);
            bool cutting = pc != null && (pc.IsChainsawCutting || pc.IsMeleeSwinging);
            sawCutMix = Mathf.MoveTowards(sawCutMix, cutting ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, chainsawBlendTime));
        }
        else if (sawOn)
        {
            sawFade = Mathf.MoveTowards(sawFade, 0f, Time.deltaTime / 0.3f); // 내려놓으면 0.3초 페이드
            sawCutMix = Mathf.MoveTowards(sawCutMix, 0f, Time.deltaTime / 0.3f);
            if (sawFade <= 0f) { sawIdle.Stop(); sawCut.Stop(); sawOn = false; }
        }
        if (sawOn)
        {
            sawIdle.volume = chainsawVolume * sawFade * (1f - sawCutMix);
            sawCut.volume = chainsawVolume * sawFade * sawCutMix;
        }
    }

    // ── 구간 자르기 ──
    static readonly Dictionary<(AudioClip, float, float, bool), AudioClip> segCache = new Dictionary<(AudioClip, float, float, bool), AudioClip>();

    /// <summary>
    /// src의 [seg.x, seg.y]초 구간만 새 클립으로. loopable이면 끝 LoopCrossfade초를 처음과 섞어 이음매 없이 반복되게,
    /// 아니면 앞 5ms·끝 60ms 페이드 (딸깍 방지). 샘플을 못 읽으면 원본 그대로
    /// </summary>
    public static AudioClip Segment(AudioClip src, Vector2 seg, bool loopable)
    {
        if (src == null) return null;
        var key = (src, seg.x, seg.y, loopable);
        if (segCache.TryGetValue(key, out var cached) && cached != null) return cached;

        AudioClip result = src;
        int ch = src.channels, hz = src.frequency;
        int s0 = Mathf.Clamp(Mathf.RoundToInt(seg.x * hz), 0, src.samples - 1);
        int s1 = Mathf.Clamp(Mathf.RoundToInt(seg.y * hz), s0 + 1, src.samples);
        int n = s1 - s0;
        if (src.loadState != AudioDataLoadState.Loaded) src.LoadAudioData();
        var data = new float[n * ch];
        if (n > 0 && src.GetData(data, s0))
        {
            float[] outData;
            if (loopable)
            {
                int x = Mathf.Min(Mathf.RoundToInt(LoopCrossfade * hz), n / 3);
                int m = n - x; // 결과 길이: 꼬리 x샘플을 머리에 섞어 넣음 → out 끝 다음 = out 처음과 이어짐
                outData = new float[m * ch];
                for (int i = 0; i < m; i++)
                {
                    float w = i < x ? (float)i / x : 1f; // 머리는 0 → 1, 섞을 꼬리는 1 → 0
                    for (int c = 0; c < ch; c++)
                    {
                        float v = data[i * ch + c] * w;
                        if (i < x) v += data[(m + i) * ch + c] * (1f - w);
                        outData[i * ch + c] = v;
                    }
                }
                n = m;
            }
            else
            {
                outData = data;
                int fin = Mathf.Min(Mathf.RoundToInt(0.005f * hz), n / 4), fout = Mathf.Min(Mathf.RoundToInt(0.06f * hz), n / 4);
                for (int i = 0; i < n; i++)
                {
                    float w = 1f;
                    if (i < fin) w = (float)i / Mathf.Max(1, fin);
                    if (i >= n - fout) w = Mathf.Min(w, (float)(n - 1 - i) / Mathf.Max(1, fout));
                    for (int c = 0; c < ch; c++) outData[i * ch + c] *= w;
                }
            }
            result = AudioClip.Create($"{src.name}_{seg.x:0.##}-{seg.y:0.##}{(loopable ? "_loop" : "")}", n, ch, hz, false);
            result.SetData(outData, 0);
        }
        segCache[key] = result;
        return result;
    }
}

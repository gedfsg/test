using UnityEngine;

/// <summary>
/// 특수 무기 소리 (2026-11-22). 플레이어 루트에 하나 (Weapon이 처음 쏠 때 / 전기톱을 들 때 자동으로 붙음).
/// 사운드 파일: Assets/Audio/weapon/sp/ → WeaponData.fireSound(1회) / WeaponData.loopSound(반복)
///  - 화염방사기: 쏘는 동안 loopSound 반복, 방아쇠 떼면(발사가 0.15초 끊기면) 멈춤 (0.08초 페이드로 딸깍 소리 방지)
///  - 미니건: 방아쇠를 누르면(예열 시작) loopSound 재생, 피치 0.6 → 1.0으로 올라감 (spin up),
///            떼면 피치 내려가며 0.5초에 걸쳐 사라짐 (spin down)
///  - 전기톱: 들고 있는 동안 loopSound 공회전 (피치 1.0), 휘두르는 동안 피치 1.3 (회전 가속), 내려놓으면 0.3초 페이드
///  - RPG·레이저: 쏠 때마다 fireSound 1회 (PlayOneShot → 연사 시 겹침 허용). RPG 착탄 폭발음은 기존 Bullet 그대로
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

    [Header("피치")]
    public float chainsawAttackPitch = 1.3f;
    public float minigunSpinPitchMin = 0.6f;

    AudioSource loopSrc, shotSrc;
    SpecialWeaponKind loopKind = SpecialWeaponKind.None;
    float lastShotTime = -99f;
    float fadeOutSpeed;           // 0이면 페이드 중 아님 (초당 볼륨 감소량 비율)
    float baseVolume;
    Weapon minigunWeapon;         // 예열 상태를 읽을 무기
    PlayerController pc;

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

    /// <summary>Weapon.Shoot에서 매 발 호출. 이 무기 소리를 처리했으면 true (기본 총소리 생략)</summary>
    public bool OnShot(WeaponData d, Weapon weapon)
    {
        if (d == null) return false;
        if (shotSrc.outputAudioMixerGroup == null) shotSrc.outputAudioMixerGroup = loopSrc.outputAudioMixerGroup = AudioManager.GetGroup(AudioManager.Channel.SFX);
        switch (d.specialKind)
        {
            case SpecialWeaponKind.Flamethrower:
                if (d.loopSound == null) return false;
                lastShotTime = Time.time;
                StartLoop(SpecialWeaponKind.Flamethrower, d.loopSound, flamethrowerVolume, 1f);
                return true;
            case SpecialWeaponKind.Minigun:
                if (d.loopSound == null) return false;
                lastShotTime = Time.time;
                minigunWeapon = weapon;
                StartLoop(SpecialWeaponKind.Minigun, d.loopSound, minigunVolume, loopKind == SpecialWeaponKind.Minigun ? loopSrc.pitch : minigunSpinPitchMin);
                return true;
            case SpecialWeaponKind.RPG:
                if (d.fireSound == null) return false;
                shotSrc.PlayOneShot(d.fireSound, rpgVolume);
                return true;
            case SpecialWeaponKind.Laser:
                if (d.fireSound == null) return false;
                shotSrc.PlayOneShot(d.fireSound, laserVolume);
                return true;
        }
        return false;
    }

    /// <summary>미니건 예열 시작 (방아쇠를 누르는 동안 매 프레임) - 첫 발 전부터 회전음</summary>
    public void OnSpinning(WeaponData d, Weapon weapon)
    {
        if (d == null || d.loopSound == null) return;
        minigunWeapon = weapon;
        lastShotTime = Time.time;
        StartLoop(SpecialWeaponKind.Minigun, d.loopSound, minigunVolume, loopKind == SpecialWeaponKind.Minigun ? loopSrc.pitch : minigunSpinPitchMin);
    }

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
        // 전기톱: 핫바에서 들고 있으면 공회전, 휘두르는 중이면 피치 상승
        var held = WeaponHotbarUI.Instance != null ? WeaponHotbarUI.Instance.GetActiveItem() as WeaponData : null;
        bool chainsawHeld = held != null && held.specialKind == SpecialWeaponKind.Chainsaw && held.loopSound != null && Time.timeScale > 0f;
        if (chainsawHeld)
        {
            StartLoop(SpecialWeaponKind.Chainsaw, held.loopSound, chainsawVolume, loopKind == SpecialWeaponKind.Chainsaw ? loopSrc.pitch : 1f);
            float target = pc != null && pc.IsMeleeSwinging ? chainsawAttackPitch : 1f;
            loopSrc.pitch = Mathf.MoveTowards(loopSrc.pitch, target, 2f * Time.deltaTime); // 0.15초쯤에 1.0 ↔ 1.3
        }
        else if (loopKind == SpecialWeaponKind.Chainsaw) FadeOut(0.3f); // 내려놓음

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
}

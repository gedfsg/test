using UnityEngine;

/// <summary>
/// 게임에서 쓰는 사운드 클립 모음 (Assets/Resources/Audio/SoundLibrary.asset).
/// Tools → Audio → Setup Sound System 이 Assets/Audio/_Processed 의 파일 이름(용도_번호.wav)을 보고 자동으로 채움.
/// 배열인 항목은 재생할 때마다 랜덤으로 하나 고름 (같은 소리 반복 방지).
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Audio/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [Header("좀비")]
    public AudioClip[] zombieGroan;
    public AudioClip[] zombieScream;
    public AudioClip[] zombieDeath;

    [Header("무기 (WeaponData.fireSound/reloadSound가 비어 있을 때 종류별 기본값)")]
    public AudioClip[] firePistol;
    public AudioClip[] fireSmg;
    public AudioClip[] fireRifle;
    public AudioClip[] fireShotgun;
    public AudioClip[] fireSniper;
    public AudioClip[] reloadPistol;
    public AudioClip[] reloadRifle;

    [Header("피격/타격")]
    public AudioClip[] hitFlesh;
    public AudioClip[] hitMelee;
    public AudioClip[] bloodSplatter;
    public AudioClip[] bodyFall;

    [Header("플레이어")]
    public AudioClip[] footstep;
    public AudioClip[] playerHurt;
    public AudioClip[] playerDeath;
    public AudioClip[] roll;

    [Header("UI")]
    public AudioClip[] inventoryOpen;
    public AudioClip[] inventoryClose;
    public AudioClip[] pickup;
    public AudioClip[] doorOpen;
    public AudioClip[] doorClose;
    public AudioClip[] crateOpen;
    public AudioClip[] click;
    public AudioClip[] heartbeat;

    [Header("반복음")]
    public AudioClip rainLoop;

    /// <summary>배열에서 랜덤 하나 (비어 있으면 null)</summary>
    public static AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    /// <summary>총 종류별 기본 발사음</summary>
    public AudioClip[] FireFor(WeaponType type) => type switch
    {
        WeaponType.Pistol => firePistol,
        WeaponType.Shotgun => fireShotgun,
        WeaponType.Sniper => fireSniper,
        _ => fireRifle, // AR / Ranged
    };

    /// <summary>총 종류별 기본 재장전음 (권총은 슬라이드 당기는 소리)</summary>
    public AudioClip[] ReloadFor(WeaponType type) => type == WeaponType.Pistol ? reloadPistol : reloadRifle;
}

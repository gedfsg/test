using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Inventory/Weapon Data")]
public class WeaponData : ItemData
{
    [Header("Weapon Info")]
    public GameObject weaponPrefab;
    public WeaponType type;

    [Header("Weapon Stats")]
    public float damage;
    public float attackRate;
    public int maxAmmo;
    public float reloadTime;

    [Header("Projectile Stats")]
    public float bulletSpeed;
    public float recoil;
    public float effectiveRange;

    [Header("손 그립 (2026-11-12) - WeaponHandAttacher 오른손 IK 목표")]
    [Tooltip("오른손(방아쇠 손) 위치: 가슴 뼈 기준 (x = 오른쪽, y = 위, z = 앞) m. (0,0,0)이면 종류별 기본값. " +
             "권총 = 가슴 중앙 양손 / AR·소총·샷건 = 오른쪽 어깨 견착 + 왼손은 무기의 LeftHandPoint(앞 손잡이)")]
    public Vector3 handGripOffset = Vector3.zero;

    [Header("그립 보정 (2026-11-20) - 무기별 손 쥐는 자리")]
    [Tooltip("오른손(Grip): 손 뼈 로컬 기준으로 총을 더 옮기는 양(m). Hand Offset의 positionOffset에 더해짐 → 손바닥이 손잡이 옆면을 감싸게 미세 조정")]
    public Vector3 gripOffset = Vector3.zero;
    [Tooltip("왼손(ForeGrip): 무기 프리팹의 LeftHandPoint 기준(무기 로컬) 왼손 목표를 옮기는 양(m). 총열 아래 지지 위치를 무기마다 맞춤")]
    public Vector3 foreGripOffset = Vector3.zero;

    [Header("Shotgun Only")]
    public int pelletCount = 8;
    public float spreadAngle = 15f;

    [Header("Sniper Only")]
    public bool penetrating = false;

    [Header("관통 (2026-11-16) - 스나이퍼·레이저")]
    [Tooltip("총알이 뚫고 지나갈 수 있는 좀비 수 (0 = 관통 없음). 통과해도 풀 데미지 유지. " +
             "예: Mosin 2 / L118a1 3 / 레이저 5. 0이고 penetrating이 켜져 있으면 예전처럼 무제한 관통")]
    public int penetrationCount = 0;

    [Header("특수 무기 (2026-11-16) - 상점 교환 전용 5종")]
    public SpecialWeaponKind specialKind = SpecialWeaponKind.None;
    [Tooltip("RPG: 맞은 곳 반경 안 좀비에게 주는 광역 데미지 (직격 데미지는 damage)")]
    public float splashDamage = 0f;
    public float splashRadius = 0f;
    [Tooltip("RPG: 광역 피해 때 밀어내는 힘 (ZombieHitEffect 넉백 배수)")]
    public float knockback = 0f;
    [Tooltip("화염방사기: 맞은 좀비에 붙는 점화 DoT (초당 데미지 / 지속 초)")]
    public float burnDps = 0f;
    public float burnDuration = 0f;
    [Tooltip("미니건: 방아쇠를 누르고 이 시간(초)이 지나야 발사 시작 (예열)")]
    public float spinUpTime = 0f;
    [Tooltip("전기톱: 연료 용량 / 초당 소모 (공격할 때만 소모. 0이면 연료 없음)")]
    public float fuelCapacity = 0f;
    public float fuelPerSecond = 0f;

    [Header("총구 불빛 (2026-11-16) - MuzzleLight")]
    [Tooltip("총구 상시 불빛 색 (일반 총 #FFD070, 화염 #FF5500, RPG #FF8800, 레이저 #00EEFF)")]
    public Color lightColor = new Color32(0xFF, 0xD0, 0x70, 0xFF);
    [Tooltip("조준등 밝기. 2026-11-17부터 MuzzleLight 기본 1.75보다 클 때만 씀 (화염방사기 2.0)")]
    public float lightIntensity = 0.8f;
    [Tooltip("(2026-11-17부터 안 씀) 예전 Point Light 범위. 조준등은 MuzzleLight.spotRange 6m 고정 - 바닥에 빛 원이 안 생기게")]
    public float lightRange = 2.5f;

    [Header("소음")]
    [Tooltip("발사 시 좀비가 듣는 반경(m). 0으로 두면 무기 종류별 기본값을 쓴다.")]
    public float noiseRadius = 0f;

    [Header("Fire Mode")]
    public bool autoFire = false;

    /// <summary>
    /// 좀비가 듣는 소음 반경 (날씨 반영 전).
    /// noiseRadius가 0이면 총 종류별 기본값(NoiseSystem.RadiusFor —— 팀원이 플레이테스트로 맞춘 값)을 쓴다.
    /// 개별 조정이 필요한 무기만 인스펙터에 값을 넣으면 그게 우선한다 (예: 소음기 SMG = 8).
    /// </summary>
    public float GetNoiseRadius()
    {
        if (noiseRadius > 0f) return noiseRadius;
        return NoiseSystem.RadiusFor(type);
    }

    [Header("Sound (비우면 SoundLibrary의 총 종류별 기본 소리)")]
    public AudioClip fireSound;
    public AudioClip reloadSound;
    [Tooltip("(2026-11-22) 반복 소리 - 화염방사기·미니건(쏘는 동안), 전기톱(들고 있는 동안). SpecialWeaponAudio가 재생")]
    public AudioClip loopSound;

    [Header("Hand Offset")]
    public Vector3 positionOffset = new Vector3(0.01f, 0.06f, -0.2f);
    public Vector3 rotationOffset = new Vector3(30f, 200f, -20f);

    [Header("근접 무기: 들고 다닐 때 손 회전 (휘두를 땐 rotationOffset). 0이면 rotationOffset 그대로")]
    [Tooltip("가만히 서 있을 때 (팔을 늘어뜨린 자세)")]
    public Vector3 carryRotationOffset = Vector3.zero;
    [Tooltip("움직일 때 (팔을 굽혀 흔드는 자세). 0이면 carryRotationOffset 그대로. 이동 속도에 따라 서 있을 때 값과 섞음")]
    public Vector3 carryMoveRotationOffset = Vector3.zero;
    [Tooltip("휘두를 때 손 위치 (손 뼈 로컬). 손 뼈 원점은 손목이라, 손바닥 중심 + 손잡이 중간쯤을 쥐도록 옮김")]
    public Vector3 swingPositionOffset = Vector3.zero;
    [Tooltip("근접 무기 한 번 휘두를 때 드는 스테미나 (0이면 안 듦). 모자라면 못 휘두름 → 무한 휘두르기 방지")]
    public float staminaCost = 0f;
    [Tooltip("켜면 들고 다닐 때도 휘두를 때와 같은 쥐는 방식(rotationOffset + swingPositionOffset)으로 고정. 이동 속도/공격에 따라 손 안에서 도끼가 돌지 않음 (carry 값은 무시)")]
    public bool rigidGrip = false;

    [Header("Back Holster (등에 멜 때 회전, 캐릭터 기준. 0이면 기본 대각선)")]
    public Vector3 holsterRotation = Vector3.zero;
}

public enum WeaponType
{
    Melee,
    Pistol,
    AR,
    Shotgun,
    Sniper,
    Ranged, // 하위 호환용
    SMG     // ⚠ 새 값은 반드시 맨 뒤에 추가할 것.
            //   중간에 끼워넣으면 enum 정수값이 밀려서
            //   기존 .asset 8개의 type이 전부 엉뚱한 무기로 바뀐다.
}

/// <summary>상점 교환 전용 특수 무기 종류 (2026-11-16). 새 값은 맨 뒤에 추가 (에셋에 int로 저장됨)</summary>
public enum SpecialWeaponKind { None, Flamethrower, Chainsaw, RPG, Minigun, Laser }
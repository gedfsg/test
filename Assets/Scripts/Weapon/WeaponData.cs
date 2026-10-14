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

    [Header("Shotgun Only")]
    public int pelletCount = 8;
    public float spreadAngle = 15f;

    [Header("Sniper Only")]
    public bool penetrating = false;

    [Header("소음")]
    [Tooltip("발사 시 좀비가 듣는 반경(m). 0으로 두면 무기 종류별 기본값을 쓴다.")]
    public float noiseRadius = 0f;

    [Header("Fire Mode")]
    public bool autoFire = false;

    /// <summary>
    /// 좀비가 듣는 소음 반경.
    /// noiseRadius가 0이면 무기 타입별 기본값으로 대체한다 —— 필드를 새로 추가하면
    /// 기존 에셋 8개가 전부 0으로 들어오므로, 그대로 쓰면 모든 총이 무음이 되어버린다.
    /// 개별 조정이 필요한 무기만 인스펙터에 값을 넣으면 그게 우선한다 (예: 소음기 SMG = 8).
    /// </summary>
    public float GetNoiseRadius()
    {
        if (noiseRadius > 0f) return noiseRadius;

        switch (type)
        {
            case WeaponType.Melee: return 4f;    // 거의 조용함
            case WeaponType.Pistol: return 25f;
            case WeaponType.SMG: return 30f;
            case WeaponType.AR: return 40f;
            case WeaponType.Shotgun: return 55f;   // 제일 시끄러움
            case WeaponType.Sniper: return 70f;   // 강하지만 온 동네가 안다
            default: return 30f;
        }
    }

    [Header("Hand Offset")]
    public Vector3 positionOffset = new Vector3(0.01f, 0.06f, -0.2f);
    public Vector3 rotationOffset = new Vector3(30f, 200f, -20f);
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
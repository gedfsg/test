using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Inventory/Weapon Data")]
public class WeaponData : ItemData
{
    [Header("Weapon Info")]
    public GameObject weaponPrefab;
    public WeaponType type; // 기존의 Melee/Ranged 구분용

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
    public int pelletCount = 8;      // 산탄 수
    public float spreadAngle = 15f;  // 퍼짐 각도(도)

    [Header("Sniper Only")]
    public bool penetrating = false; // 관통탄 여부

    [Header("Fire Mode")]
    public bool autoFire = false;  // true면 자동연사, false면 반자동(1클릭 1발)

    [Header("Hand Offset")]
    public Vector3 positionOffset = new Vector3(0.01f, 0.06f, -0.2f);
    public Vector3 rotationOffset = new Vector3(30f, 200f, -20f);

}

public enum WeaponType
{
    Melee,
    Ranged,
    AR,
    Shotgun,
    Sniper
}
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

    [Header("Fire Mode")]
    public bool autoFire = false;

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
    Ranged  // 하위 호환용
}
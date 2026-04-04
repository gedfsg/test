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
}

public enum WeaponType
{
    Melee,
    Ranged
}
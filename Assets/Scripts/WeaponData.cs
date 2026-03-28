using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Inventory/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Weapon Info")]
    public string weaponName;
    public GameObject weaponPrefab;

    public WeaponType type;

    [Header("Weapon Stats")]
    public float damage;
    public float attackRate;
    public int maxAmmo;
    public float reloadTime;

    // 신규 추가된 투사체 관련 데이터임.
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
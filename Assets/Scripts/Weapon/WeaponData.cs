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

    [Header("근접 무기: 들고 다닐 때 손 회전 (휘두를 땐 rotationOffset). 0이면 rotationOffset 그대로")]
    [Tooltip("가만히 서 있을 때 (팔을 늘어뜨린 자세)")]
    public Vector3 carryRotationOffset = Vector3.zero;
    [Tooltip("움직일 때 (팔을 굽혀 흔드는 자세). 0이면 carryRotationOffset 그대로. 이동 속도에 따라 서 있을 때 값과 섞음")]
    public Vector3 carryMoveRotationOffset = Vector3.zero;

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
    Ranged  // 하위 호환용
}
using UnityEngine;

/// <summary>
/// 탄약 아이템 데이터 (권총탄 / 소총탄 / 샷건탄 / 저격탄)
/// WeaponType과 매핑해서 해당 무기에 탄을 보충한다.
/// </summary>
[CreateAssetMenu(fileName = "New Ammo Data", menuName = "Inventory/Ammo Data")]
public class AmmoData : ItemData
{
    [Header("Ammo Settings")]
    [Tooltip("이 탄약이 보충할 무기 타입")]
    public WeaponType weaponType;

    [Tooltip("한 스택이 보충하는 탄수")]
    public int ammoAmount = 30;
}
using UnityEngine;
using TMPro;

public class AmmoUI : MonoBehaviour
{
    public static AmmoUI Instance { get; private set; }

    [Header("UI References")]
    public TextMeshProUGUI ammoText;
    public Weapon targetRangedWeapon;

    void Awake() => Instance = this;

    void Update() => Refresh();

    public void Refresh()
    {
        if (targetRangedWeapon == null || !targetRangedWeapon.gameObject.activeInHierarchy)
        {
            ammoText.text = "";
            return;
        }

        WeaponType type = targetRangedWeapon.weaponData != null
            ? targetRangedWeapon.weaponData.type
            : WeaponType.Pistol;

        int reserve = AmmoInventory.Instance != null
            ? AmmoInventory.Instance.GetAmmo(type)
            : 0;

        ammoText.text = $"{targetRangedWeapon.GetCurrentAmmo()} / {reserve}";
    }
}
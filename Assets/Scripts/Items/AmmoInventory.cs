using System.Collections.Generic;
using UnityEngine;

public class AmmoInventory : MonoBehaviour
{
    public static AmmoInventory Instance { get; private set; }

    [Header("최대 소지량")]
    public int maxPistolAmmo = 120;
    public int maxARAmmo = 200;
    public int maxShotgunAmmo = 60;
    public int maxSniperAmmo = 30;

    private Dictionary<WeaponType, int> ammo = new Dictionary<WeaponType, int>();

    private InventoryUI inventoryUI;

    void Awake()
    {
        Instance = this;
        ammo[WeaponType.Pistol] = 0;
        ammo[WeaponType.AR] = 0;
        ammo[WeaponType.Shotgun] = 0;
        ammo[WeaponType.Sniper] = 0;
    }

    void Start()
    {
        inventoryUI = FindAnyObjectByType<InventoryUI>();
    }

    private void RefreshInventoryAmmoPanel()
    {
        if (inventoryUI != null && inventoryUI.isInventoryOpen)
            inventoryUI.UpdateAmmoPanel();
    }

    public void AddAmmo(WeaponType type, int amount)
    {
        if (!ammo.ContainsKey(type)) return;
        ammo[type] = Mathf.Min(ammo[type] + amount, GetMax(type));
        AmmoUI.Instance?.Refresh();
        RefreshInventoryAmmoPanel();
    }

    public int ConsumeAmmo(WeaponType type, int amount)
    {
        if (!ammo.ContainsKey(type)) return 0;
        int consumed = Mathf.Min(ammo[type], amount);
        ammo[type] -= consumed;
        AmmoUI.Instance?.Refresh();
        RefreshInventoryAmmoPanel();
        return consumed;
    }

    public int GetAmmo(WeaponType type)
        => ammo.ContainsKey(type) ? ammo[type] : 0;

    public int GetMax(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Pistol: return maxPistolAmmo;
            case WeaponType.AR: return maxARAmmo;
            case WeaponType.Shotgun: return maxShotgunAmmo;
            case WeaponType.Sniper: return maxSniperAmmo;
            default: return 0;
        }
    }
}
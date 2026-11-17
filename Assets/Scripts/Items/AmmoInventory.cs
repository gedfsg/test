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
    [Tooltip("특수 무기(화염방사기·RPG·미니건·레이저, WeaponType.Ranged) 공용 탄약/연료/에너지셀. 상점 시스템이 AddAmmo(WeaponType.Ranged, n)로 보충 (2026-11-16)")]
    public int maxSpecialAmmo = 400;

    private Dictionary<WeaponType, int> ammo = new Dictionary<WeaponType, int>();

    private InventoryUI inventoryUI;

    void Awake()
    {
        Instance = this;
        ammo[WeaponType.Pistol] = 0;
        ammo[WeaponType.AR] = 0;
        ammo[WeaponType.Shotgun] = 0;
        ammo[WeaponType.Sniper] = 0;
        ammo[WeaponType.Ranged] = 0; // 특수 무기 공용 (2026-11-16)
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
            case WeaponType.Ranged: return maxSpecialAmmo;
            default: return 0;
        }
    }
}
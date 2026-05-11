using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public Transform weaponSocket;
    private GameObject currentWeaponInstance;
    public WeaponData currentWeaponData;

    public void EquipWeapon(WeaponData newWeapon)
    {
        if (currentWeaponInstance != null)
            Destroy(currentWeaponInstance);

        currentWeaponData = newWeapon;

        currentWeaponInstance = Instantiate(newWeapon.weaponPrefab, weaponSocket);
        currentWeaponInstance.transform.localPosition = Vector3.zero;
        currentWeaponInstance.transform.localRotation = Quaternion.identity;

        Weapon weaponScript = currentWeaponInstance.GetComponent<Weapon>();
        if (weaponScript != null)
            weaponScript.ChangeWeaponData(newWeapon);
    }

    public Weapon GetCurrentWeapon()
    {
        return currentWeaponInstance != null
            ? currentWeaponInstance.GetComponent<Weapon>()
            : null;
    }
}
using UnityEngine;
using TMPro;

public class AmmoUI : MonoBehaviour
{
    public Weapon playerWeapon;
    public TextMeshProUGUI ammoDisplay;
    void Update()
    {
        if (playerWeapon != null && ammoDisplay != null)
        {
            ammoDisplay.text = string.Format("{0} / {1}",
                playerWeapon.GetCurrentAmmo(),
                playerWeapon.GetMaxAmmo());
        }
    }
}

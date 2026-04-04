using UnityEngine;
using TMPro;

public class AmmoUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI ammoText;

    [Header("Weapon Reference")]
    // 탄약을 추적할 대상 원거리 무기 스크립트를 연결함.
    public Weapon targetRangedWeapon; 

    void Update()
    {
        // 대상 무기가 존재하고, 해당 무기 오브젝트가 현재 활성화 상태인지 확인함.
        if (targetRangedWeapon != null && targetRangedWeapon.gameObject.activeInHierarchy)
        {
            // 원거리 무기를 들고 있을 경우 현재 탄약과 최대 탄약을 UI에 갱신함.
            ammoText.text = targetRangedWeapon.GetCurrentAmmo() + " / " + targetRangedWeapon.GetMaxAmmo();
        }
        else
        {
            // 근접 무기를 들고 있거나 원거리 무기가 비활성화된 경우 UI 텍스트를 공란으로 비움.
            ammoText.text = "";
        }
    }
}
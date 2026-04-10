using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public Transform weaponSocket; 
    private GameObject currentWeaponInstance;
    public WeaponData currentWeaponData;

    // 인벤토리에서 무기를 클릭했을 때 호출될 함수
    public void EquipWeapon(WeaponData newWeapon)
    {
        // 1. 이미 무기를 들고 있다면 삭제
        if (currentWeaponInstance != null)
        {
            Destroy(currentWeaponInstance);
        }

        currentWeaponData = newWeapon;

        // 2. 새로운 무기 프리팹을 소켓 위치에 생성
        currentWeaponInstance = Instantiate(newWeapon.weaponPrefab, weaponSocket);
        
        // 3. 위치와 회전값을 소켓에 딱 맞게 초기화
        currentWeaponInstance.transform.localPosition = Vector3.zero;
        currentWeaponInstance.transform.localRotation = Quaternion.identity;

        // 4. Weapon.cs 스크립트가 프리팹에 있다면, 데이터 세팅
        Weapon weaponScript = currentWeaponInstance.GetComponent<Weapon>();
        if (weaponScript != null)
        {
            // Weapon 스크립트에서 WeaponData의 능력치를 받아 쓰도록 연결
            // 예: weaponScript.Initialize(newWeapon);
        }
    }
}
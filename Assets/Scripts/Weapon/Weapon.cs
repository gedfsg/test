using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class Weapon : MonoBehaviour
{
    [Header("Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public string shooterTag;

    // 추가된 데이터 연동 변수임.
    [Header("Weapon Data")]
    public WeaponData weaponData;

    [Header("UI Events")]
    public UnityEvent<float> onReloadStart;
    public UnityEvent onReloadComplete;

    private int currentAmmo;
    private float lastFireTime;
    private bool isReloading = false;

    void Start()
    {
        // 데이터 파일이 할당되어 있다면 해당 데이터의 탄약 수치로 초기화함.
        if (weaponData != null)
        {
            currentAmmo = weaponData.maxAmmo;
        }
        else
        {
            currentAmmo = 30; 
        }
    }

    public void TryFire()
    {
        if(isReloading) return;
        if(currentAmmo <= 0)
        {
            StartCoroutine(Reload());
            return;
        }

        // 무기 데이터의 연사 속도를 참조함.
        float currentFireRate = (weaponData != null) ? weaponData.attackRate : 0.2f;

        if(Time.time >= lastFireTime + currentFireRate)
        {
            Shoot();
            lastFireTime = Time.time;
        }
    }

    void Shoot()
    {
        currentAmmo--;

        // 무기 데이터에서 반동 값을 참조함. 데이터가 없을 경우 0으로 처리함.
        float currentRecoil = (weaponData != null) ? weaponData.recoil : 0f;

        // Y축을 기준으로 지정된 반동 범위 내에서 무작위 회전값을 생성함.
        Quaternion randomRecoilRotation = Quaternion.Euler(0, Random.Range(-currentRecoil, currentRecoil), 0);
        
        // 발사 지점의 기본 방향에 무작위 회전값을 적용하여 최종 발사 각도를 산출함.
        Quaternion finalFireRotation = firePoint.rotation * randomRecoilRotation;

        GameObject bulletObj = Instantiate(bulletPrefab, firePoint.position, finalFireRotation);
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        
        if(bullet != null)
        {
            bullet.shooterTag = shooterTag;
            
            // 생성된 총알 객체에 무기 데이터의 수치들을 전달함.
            if (weaponData != null)
            {
                bullet.damage = weaponData.damage;
                bullet.speed = weaponData.bulletSpeed;
                bullet.effectiveRange = weaponData.effectiveRange;
            }
        }
    }

    public IEnumerator Reload()
    {
        if(isReloading) yield break;

        isReloading = true;
        
        // 무기 데이터의 장전 소요 시간을 참조함.
        float currentReloadTime = (weaponData != null) ? weaponData.reloadTime : 2.0f;

        onReloadStart?.Invoke(currentReloadTime);
        Debug.Log(shooterTag + " 장전 중...");
        yield return new WaitForSeconds(currentReloadTime);

        currentAmmo = (weaponData != null) ? weaponData.maxAmmo : 30;
        isReloading = false;
        onReloadComplete?.Invoke();
        Debug.Log(shooterTag + " 장전 완료!");
    }

    public void TryReload()
    {
        int max = (weaponData != null) ? weaponData.maxAmmo : 30;
        if(!isReloading && currentAmmo < max)
        {
            StartCoroutine(Reload());
        }
    }

    public int GetCurrentAmmo()
    {
        return currentAmmo;
    }

    public int GetMaxAmmo()
    {
        return (weaponData != null) ? weaponData.maxAmmo : 30;
    }

    // 외부에서 새로운 무기 데이터를 덮어씌울 때 호출하는 함수
    public void ChangeWeaponData(WeaponData newData)
    {
        weaponData = newData;
        // 새 데이터의 최대 탄약수로 즉시 장전
        currentAmmo = (weaponData != null) ? weaponData.maxAmmo : 30;
        Debug.Log("원거리 무기가 [" + newData.itemName + "]으로 교체됨!");
    }
}
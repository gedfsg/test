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
        GameObject bulletObj = Instantiate(bulletPrefab, firePoint.position, transform.rotation);
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        if(bullet != null)
        {
            bullet.shooterTag = shooterTag;
            
            // 무기 데이터의 공격력을 생성된 총알 객체에 전달함.
            if (weaponData != null)
            {
                bullet.damage = weaponData.damage;
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
}
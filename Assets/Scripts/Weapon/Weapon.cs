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

    [Header("Muzzle Flash")]
    public Light muzzleFlashLight;
    public float muzzleFlashDuration = 0.05f;
    public GameObject muzzleFlashPrefab;

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

        if (muzzleFlashLight != null)
            muzzleFlashLight.enabled = false;
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

    if (muzzleFlashLight != null)
        StartCoroutine(MuzzleFlash());

    if (muzzleFlashPrefab != null)
    {
        GameObject flash = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation);
        Destroy(flash, 0.1f);
    }

    float currentRecoil = (weaponData != null) ? weaponData.recoil : 0f;

    Vector3 direction = firePoint.forward;
    Quaternion baseRotation = Quaternion.LookRotation(direction);

    // 반동 적용
    Quaternion randomRecoilRotation = Quaternion.Euler(0, Random.Range(-currentRecoil, currentRecoil), 0);
    Quaternion finalFireRotation = baseRotation * randomRecoilRotation;

    GameObject bulletObj = Instantiate(bulletPrefab, firePoint.position, finalFireRotation);
    Bullet bullet = bulletObj.GetComponent<Bullet>();

    if (bullet != null)
    {
        bullet.shooterTag = shooterTag;
        if (weaponData != null)
        {
            bullet.damage = weaponData.damage;
            bullet.speed = weaponData.bulletSpeed;
            bullet.effectiveRange = weaponData.effectiveRange;
        }
    }
}

    private IEnumerator MuzzleFlash()
    {
        muzzleFlashLight.enabled = true;
        yield return new WaitForSeconds(muzzleFlashDuration);
        muzzleFlashLight.enabled = false;
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
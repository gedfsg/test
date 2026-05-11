using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class Weapon : MonoBehaviour
{
    [Header("Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public string shooterTag;

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
        if (weaponData != null)
            currentAmmo = weaponData.maxAmmo;
        else
            currentAmmo = 30;

        if (muzzleFlashLight != null)
            muzzleFlashLight.enabled = false;
    }

    public void TryFire()
    {
        if (isReloading) return;
        if (currentAmmo <= 0) { StartCoroutine(Reload()); return; }

        bool auto = (weaponData != null) && weaponData.autoFire;
        if (!auto) return;

        float currentFireRate = (weaponData != null) ? weaponData.attackRate : 0.2f;
        if (Time.time >= lastFireTime + currentFireRate)
        {
            Shoot();
            lastFireTime = Time.time;
        }
    }

    public void TryFireSingle()
    {
        if (isReloading) return;
        if (currentAmmo <= 0) { StartCoroutine(Reload()); return; }

        float currentFireRate = (weaponData != null) ? weaponData.attackRate : 0.5f;
        if (Time.time >= lastFireTime + currentFireRate)
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

        Vector3 targetPoint;
        bool hasTarget = GameUtils.GetMouseWorldPosition(Camera.main, firePoint.position.y, out targetPoint);
        Vector3 baseDirection = hasTarget
            ? (targetPoint - firePoint.position).normalized
            : firePoint.forward;

        WeaponType wType = (weaponData != null) ? weaponData.type : WeaponType.Ranged;

        if (wType == WeaponType.Shotgun)
        {
            int pellets = (weaponData != null) ? weaponData.pelletCount : 8;
            float spread = (weaponData != null) ? weaponData.spreadAngle : 15f;

            for (int i = 0; i < pellets; i++)
                SpawnBullet(ApplySpread(baseDirection, spread));
        }
        else
        {
            float recoil = (weaponData != null) ? weaponData.recoil : 0f;
            SpawnBullet(ApplySpread(baseDirection, recoil));
        }
    }

    void SpawnBullet(Vector3 direction)
    {
        Quaternion rotation = Quaternion.LookRotation(direction);
        GameObject bulletObj = Instantiate(bulletPrefab, firePoint.position, rotation);
        Bullet bullet = bulletObj.GetComponent<Bullet>();

        if (bullet != null)
        {
            bullet.shooterTag = shooterTag;
            if (weaponData != null)
            {
                bullet.damage = weaponData.damage;
                bullet.speed = weaponData.bulletSpeed;
                bullet.effectiveRange = weaponData.effectiveRange;
                bullet.penetrating = (weaponData.type == WeaponType.Sniper) && weaponData.penetrating;
            }
        }
    }

    Vector3 ApplySpread(Vector3 baseDir, float maxAngle)
    {
        if (maxAngle <= 0f) return baseDir;
        float yaw = Random.Range(-maxAngle, maxAngle);
        float pitch = Random.Range(-maxAngle, maxAngle);
        return Quaternion.Euler(pitch, yaw, 0f) * baseDir;
    }

    private IEnumerator MuzzleFlash()
    {
        muzzleFlashLight.enabled = true;
        yield return new WaitForSeconds(muzzleFlashDuration);
        muzzleFlashLight.enabled = false;
    }

    public IEnumerator Reload()
    {
        if (isReloading) yield break;

        isReloading = true;
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
        if (!isReloading && currentAmmo < max)
            StartCoroutine(Reload());
    }

    public int GetCurrentAmmo() => currentAmmo;
    public int GetMaxAmmo() => (weaponData != null) ? weaponData.maxAmmo : 30;

    // savedAmmo가 -1이면 처음 줍는 무기 → maxAmmo로 초기화
    // savedAmmo가 0 이상이면 저장된 탄수 복원
    public void ChangeWeaponData(WeaponData newData)
    {
        weaponData = newData;
        currentAmmo = (weaponData != null) ? weaponData.maxAmmo : 30;
        Debug.Log("원거리 무기가 [" + newData.itemName + "]으로 교체됨!");
    }
}
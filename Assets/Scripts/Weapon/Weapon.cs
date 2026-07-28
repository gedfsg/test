using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class Weapon : MonoBehaviour
{
    [Header("Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public string shooterTag;

    [Header("조준 (PlayerController와 동일한 지점 참조)")]
    [Tooltip("PlayerController의 Aim Target과 정확히 같은 오브젝트를 연결할 것. 이 값을 그대로 참조해서 방향을 계산하므로, 캐릭터가 보는 방향과 총알이 나가는 방향이 항상 100% 일치한다.")]
    public Transform aimTarget;

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

        // 자체적으로 다시 계산하지 않고, PlayerController가 이미 이번 프레임에 계산해둔
        // aimTarget(정확한 마우스 월드 좌표)을 그대로 참조한다. 계산 로직이 하나로
        // 통일되어 있으므로, 캐릭터가 보는 방향과 총알이 나가는 방향이 절대 어긋나지 않는다.
        Vector3 baseDirection;
        if (aimTarget != null)
        {
            Vector3 toTarget = aimTarget.position - firePoint.position;
            toTarget.y = 0f; // 총은 항상 수평으로 발사 (WeaponHandAttacher의 시각적 조준과 일치)
            baseDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : firePoint.forward;
        }
        else
        {
            baseDirection = firePoint.forward;
        }

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

        WeaponType wType = weaponData != null ? weaponData.type : WeaponType.Pistol;
        int reserve = AmmoInventory.Instance != null
            ? AmmoInventory.Instance.GetAmmo(wType)
            : 0;

        if (reserve <= 0)
        {
            Debug.Log("[재장전] 소지 탄약 없음!");
            yield break;
        }

        isReloading = true;
        float currentReloadTime = weaponData != null ? weaponData.reloadTime : 2.0f;

        onReloadStart?.Invoke(currentReloadTime);
        yield return new WaitForSeconds(currentReloadTime);

        int max = weaponData != null ? weaponData.maxAmmo : 30;
        int needed = max - currentAmmo;
        int consumed = AmmoInventory.Instance != null
            ? AmmoInventory.Instance.ConsumeAmmo(wType, needed)
            : needed;

        currentAmmo += consumed;
        isReloading = false;
        onReloadComplete?.Invoke();

        Debug.Log($"[재장전] {consumed}발 보충 → 현재 {currentAmmo}/{max}");
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
    public void ChangeWeaponData(WeaponData newData, int savedAmmo = -1)
    {
        // 장전 중이면 취소
        if (isReloading)
        {
            StopAllCoroutines();
            isReloading = false;
        }

        weaponData = newData;
        currentAmmo = (savedAmmo >= 0) ? savedAmmo : newData.maxAmmo;
    }
}
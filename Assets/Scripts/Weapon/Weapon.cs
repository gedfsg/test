using UnityEngine;
using System.Collections;
using UnityEngine.Events;
using UnityEngine.InputSystem;

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

    float currentRecoil = (weaponData != null) ? weaponData.recoil : 0f;

    // 마우스 월드 좌표 구하기
    Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
    Plane plane = new Plane(Vector3.up, firePoint.position);
    float dist;
    Vector3 mouseWorldPos = firePoint.position; // 기본값

    if (plane.Raycast(ray, out dist))
    {
        mouseWorldPos = ray.GetPoint(dist);
    }

    // 마우스 방향으로 발사 방향 계산
    Vector3 direction = (mouseWorldPos - firePoint.position).normalized;
    direction.y = 0;
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
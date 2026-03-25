using UnityEngine;
using System.Collections;
using UnityEngine.Events;

public class Weapon : MonoBehaviour
{
    [Header("Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public string shooterTag;

    [Header("Stats")]
    public float fireRate = 0.2f;
    public int maxAmmo = 30;
    public float reloadTime = 2.0f;

    [Header("UI Events")]
    public UnityEvent<float> onReloadStart;
    public UnityEvent onReloadComplete;

    private int currentAmmo;
    private float lastFireTime;
    private bool isReloading = false;

    void Start()
    {
        currentAmmo = maxAmmo;
    }

    public void TryFire()
    {
        if(isReloading) return;
        if(currentAmmo <= 0)
        {
            StartCoroutine(Reload());
            return;
        }

        if(Time.time >= lastFireTime + fireRate)
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
        }
    }

    public IEnumerator Reload()
    {
        if(isReloading) yield break;

        isReloading = true;
        onReloadStart?.Invoke(reloadTime);
        Debug.Log(shooterTag + " 장전 중...");
        yield return new WaitForSeconds(reloadTime);

        currentAmmo = maxAmmo;
        isReloading = false;
        onReloadComplete?.Invoke();
        Debug.Log(shooterTag + " 장전 완료!");
    }

    public void TryReload()
    {
        if(!isReloading && currentAmmo < maxAmmo)
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
        return maxAmmo;
    }
}

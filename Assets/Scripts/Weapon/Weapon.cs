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

    [Header("총구 불빛 (2026-11-16) - 손에 든 무기 모델의 MuzzleLight. 비워두면 WeaponHandAttacher의 현재 모델에서 자동으로 찾음")]
    public MuzzleLight muzzleLight;
    private WeaponHandAttacher handAttacher;
    private Transform lightVisual; // muzzleLight를 찾은 무기 모델 (모델이 바뀌면 다시 찾음)

    [Header("UI Events")]
    public UnityEvent<float> onReloadStart;
    public UnityEvent onReloadComplete;

    private int currentAmmo;
    private float lastFireTime;
    private bool isReloading = false;

    // 미니건 예열 (2026-11-16): 방아쇠를 누른 채 spinUpTime이 지나야 발사
    private float spinStartTime = -1f;
    private float lastTriggerTime = -99f;


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

        // 미니건 예열: 매 프레임 불리는 TryFire가 0.15초 넘게 끊기면 방아쇠를 뗀 것 → 예열 다시
        if (weaponData.spinUpTime > 0f)
        {
            if (Time.time - lastTriggerTime > 0.15f) spinStartTime = Time.time;
            lastTriggerTime = Time.time;
            if (weaponData.specialKind == SpecialWeaponKind.Minigun) SpecialWeaponAudio.For(this).OnSpinning(weaponData, this); // (2026-11-22) 예열 중에도 회전음
            if (Time.time < spinStartTime + weaponData.spinUpTime) return;
        }

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
        SpawnProtection.Cancel(); // 플레이어가 먼저 공격 → 스폰 보호 해제
        currentAmmo--;

        // 소음: 플레이어 총소리는 주변 좀비를 부름.
        // 반경 = 무기 에셋의 noiseRadius(있으면) 아니면 총 종류별 기본값(NoiseSystem.RadiusFor), 비 오면 절반(NoiseSystem 안에서)
        if (shooterTag == "Player")
            NoiseSystem.Emit(firePoint != null ? firePoint.position : transform.position,
                             weaponData != null ? weaponData.GetNoiseRadius() : NoiseSystem.RadiusFor(WeaponType.Ranged));

        PlayWeaponSound(true);

        if (muzzleFlashLight != null)
            StartCoroutine(MuzzleFlash());

        // 총구 불빛 번쩍 (0.1초간 밝기 ×3, 범위 ×1.5)
        ResolveMuzzleLight()?.Flash();

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

        // (2026-11-22) 화염방사기: 탄 궤적 선 대신 총구에서 불길 파티클 분사 (탄은 데미지·점화 판정만)
        if (weaponData != null && weaponData.specialKind == SpecialWeaponKind.Flamethrower && firePoint != null)
        {
            var body = GetComponentInParent<Rigidbody>();
            FlameStream.Emit(firePoint.position, baseDirection, body != null ? body.linearVelocity : Vector3.zero);
        }

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
                // 2026-11-16: 관통 횟수 + 특수 탄 (RPG 폭발 / 화염 점화) + 궤적 색
                bullet.penetrationCount = weaponData.penetrationCount;
                bullet.splashDamage = weaponData.splashDamage;
                bullet.splashRadius = weaponData.splashRadius;
                bullet.knockback = weaponData.knockback;
                bullet.burnDps = weaponData.burnDps;
                bullet.burnDuration = weaponData.burnDuration;
                switch (weaponData.specialKind)
                {
                    // case SpecialWeaponKind.Flamethrower: bullet.trailColor = new Color(1f, 0.4f, 0.05f); bullet.trailWidth = 0.35f; break; // (이전) 주황 선
                    case SpecialWeaponKind.Flamethrower: // (2026-11-22) 불길은 FlameStream이 그림 → 탄은 안 보이게
                        bullet.trailWidth = 0f;
                        foreach (var rr in bulletObj.GetComponentsInChildren<Renderer>()) rr.enabled = false;
                        break;
                    case SpecialWeaponKind.RPG: bullet.trailColor = new Color(1f, 0.55f, 0.2f); bullet.trailWidth = 0.12f;
                        bullet.explodeSound = SpecialWeaponAudio.RpgImpactClip(weaponData); break; // (2026-11-22) 착탄 = RPG 폭발음
                    case SpecialWeaponKind.Laser: bullet.trailColor = new Color(0f, 0.93f, 1f); bullet.trailWidth = 0.08f; break;
                }
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

    // 발사음/재장전음 (3D). WeaponData에 지정된 소리 우선, 없으면 총 종류별 기본 소리
    void PlayWeaponSound(bool fire)
    {
        // (2026-11-22) 특수 무기 소리 (화염방사기·미니건 반복 / RPG·레이저 1회) → 처리했으면 기본 총소리 생략
        if (fire && weaponData != null && weaponData.specialKind != SpecialWeaponKind.None && SpecialWeaponAudio.For(this).OnShot(weaponData, this)) return;
        AudioClip clip = weaponData != null ? (fire ? weaponData.fireSound : weaponData.reloadSound) : null;
        if (clip == null && AudioManager.Library != null)
        {
            WeaponType wType = weaponData != null ? weaponData.type : WeaponType.Pistol;
            var lib = AudioManager.Library;
            clip = SoundLibrary.Pick(fire ? lib.FireFor(wType) : lib.ReloadFor(wType));
        }
        Vector3 pos = firePoint != null ? firePoint.position : transform.position;
        bool shotgun = weaponData != null && weaponData.type == WeaponType.Shotgun;
        if (fire) AudioManager.PlaySFX(clip, pos, shotgun ? AudioManager.Vol.GunshotShotgun : AudioManager.Vol.Gunshot,
                                       AudioManager.Channel.SFX, 60f, Random.Range(0.96f, 1.04f));
        else AudioManager.PlaySFX(clip, pos, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 20f);
    }

    private IEnumerator MuzzleFlash()
    {
        muzzleFlashLight.enabled = true;
        yield return new WaitForSeconds(muzzleFlashDuration);
        muzzleFlashLight.enabled = false;
    }

    // ── 총구 불빛 (2026-11-16) ──
    // 손에 든 무기 모델이 바뀌면(핫바 전환) 새 모델의 MuzzleLight를 찾아 켜고, 이전 것은 끔
    void Update()
    {
        if (shooterTag == "Player") ResolveMuzzleLight();
    }

    void OnDisable()
    {
        // (2026-11-21) 재장전 중에 근접 무기로 바꾸면 이 오브젝트가 꺼지면서 코루틴만 멈춰
        // 게이지가 안 사라지고 isReloading이 true로 남아 총을 다시 들어도 못 쐈음 → 취소 처리
        CancelReload();
        // 근접 무기로 바꾸면 이 오브젝트가 꺼짐 → 불빛도 끔
        if (muzzleLight != null) muzzleLight.Deactivate();
        muzzleLight = null; lightVisual = null;
    }

    MuzzleLight ResolveMuzzleLight()
    {
        if (handAttacher == null) handAttacher = FindAnyObjectByType<WeaponHandAttacher>();
        Transform visual = handAttacher != null ? handAttacher.CurrentVisual : null;
        bool showing = visual != null && visual.gameObject.activeInHierarchy;
        if (visual != lightVisual || (muzzleLight != null && !showing))
        {
            if (muzzleLight != null) muzzleLight.Deactivate();
            muzzleLight = null;
            lightVisual = visual;
            if (showing)
            {
                muzzleLight = MuzzleLight.FindIn(visual);
                if (muzzleLight != null) muzzleLight.Activate(weaponData);
            }
        }
        else if (muzzleLight == null && showing)
        {
            // 구르기 동안 숨겼다가 다시 보이게 된 경우
            muzzleLight = MuzzleLight.FindIn(visual);
            if (muzzleLight != null) muzzleLight.Activate(weaponData);
        }
        return muzzleLight;
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
            PickupFeedbackUI.Instance?.ShowWarning("탄약 없음!");
            yield break;
        }

        isReloading = true;
        float currentReloadTime = weaponData != null ? weaponData.reloadTime : 2.0f;

        onReloadStart?.Invoke(currentReloadTime);
        PlayWeaponSound(false);
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
    /// <summary>
    /// (2026-11-21) 재장전 취소. onReloadComplete를 불러서 재장전 게이지(ReloadUI)·플레이어 재장전 자세·
    /// 좀비 AI의 "재장전 중" 정보가 같이 풀리게 함 (예전엔 총을 바꾸면 게이지가 화면에 남았음)
    /// </summary>
    void CancelReload()
    {
        if (!isReloading) return;
        StopAllCoroutines();
        isReloading = false;
        onReloadComplete?.Invoke();
    }

    public void ChangeWeaponData(WeaponData newData, int savedAmmo = -1)
    {
        // 장전 중이면 취소 (2026-11-21: 재장전 게이지도 같이 닫도록 CancelReload로)
        CancelReload();

        weaponData = newData;
        currentAmmo = (savedAmmo >= 0) ? savedAmmo : newData.maxAmmo;
        spinStartTime = -1f; // 미니건 예열 초기화
        lightVisual = null;  // 다음 Update에서 새 모델의 총구 불빛 + 새 색으로 다시 켬
    }
}
using UnityEngine.Events;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;
    private bool dead; // 사망 처리 한 번만 (죽은 뒤 추가 피해로 onDeath가 반복 호출되던 것 방지)

    [Tooltip("켜져 있는 동안 피해 무시 (플레이어 구르기 무적 시간)")]
    public bool isInvincible;

    [Tooltip("플레이어 사망 후 게임을 멈출 때까지 기다리는 시간(초, 실제 시간). 사망 애니메이션이 보이게")]
    public float playerDeathFreezeDelay = 4.5f; // Dying 애니메이션(4.4초)이 끝까지 보이게

    public UnityEvent onDeath;
    public UnityEvent onHurt;

    // 데미지 수치를 표시할 UI 프리팹 객체임.
    [Header("Damage UI")]
    public GameObject damageTextPrefab;

    [Header("사운드")]
    [Tooltip("플레이어 신음 최소 간격(초) - 여러 마리에게 연달아 맞아도 신음이 겹치지 않게")]
    public float hurtSoundCooldown = 0.5f;
    [Tooltip("(2026-11-22) 좀비 살 맞는 소리 최소 간격 (초). 화염방사기·미니건·전기톱처럼 초당 10~20번 맞으면 '퍽퍽' 총소리처럼 들렸음")]
    public float zombieHitSoundCooldown = 0.15f;
    [Tooltip("(2026-11-22) 이보다 작은 피해(화염방사기 한 발 1, 화상 지속 피해)는 살 맞는 소리 없음")]
    public float zombieHitSoundMinDamage = 3f;
    private float lastZombieHitSoundTime = -99f;
    [Tooltip("이 체력 비율 이하면 심장소리 시작 (플레이어만)")]
    public float heartbeatStartRatio = 0.3f;
    [Tooltip("이 체력 비율 이하면 심장소리가 빨라짐")]
    public float heartbeatFastRatio = 0.1f;
    [Tooltip("이 체력 비율 이상 회복하면 심장소리 멈춤")]
    public float heartbeatStopRatio = 0.5f;
    public float heartbeatInterval = 3f;
    public float heartbeatFastInterval = 1.5f;

    private bool isPlayer;
    private bool isZombie;
    private float lastHurtSoundTime = -99f;
    private bool heartbeatOn;
    private float nextHeartbeatTime;

    void Awake()
    {
        currentHealth = maxHealth;
        isPlayer = CompareTag("Player");
        isZombie = GetComponent<ZombieController>() != null;
    }

    void Update()
    {
        if (isPlayer) UpdateHeartbeat();
    }

    // 저체력 심장소리 (2D): 30% 이하 3초 간격 → 10% 이하 1.5초 간격 → 50% 이상 회복하면 멈춤
    private void UpdateHeartbeat()
    {
        float ratio = maxHealth > 0f ? currentHealth / maxHealth : 0f;
        if (dead) heartbeatOn = false;
        else if (!heartbeatOn && ratio <= heartbeatStartRatio) { heartbeatOn = true; nextHeartbeatTime = Time.time; }
        else if (heartbeatOn && ratio >= heartbeatStopRatio) heartbeatOn = false;

        if (!heartbeatOn || Time.time < nextHeartbeatTime || AudioManager.Library == null) return;
        AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.heartbeat), AudioManager.Vol.Heartbeat);
        nextHeartbeatTime = Time.time + (ratio <= heartbeatFastRatio ? heartbeatFastInterval : heartbeatInterval);
    }

    // 피격음: 플레이어 = 신음 (2D, 쿨타임), 좀비 = 살 맞는 소리 랜덤 (3D)
    // private void PlayHurtSound() // (이전) 피해마다 소리
    private void PlayHurtSound(float amount)
    {
        var lib = AudioManager.Library;
        if (lib == null) return;
        if (isPlayer)
        {
            if (Time.time < lastHurtSoundTime + hurtSoundCooldown) return;
            lastHurtSoundTime = Time.time;
            AudioManager.PlayUI(SoundLibrary.Pick(lib.playerHurt), AudioManager.Vol.PlayerHurt);
        }
        else if (isZombie)
        {
            // (2026-11-22) 작은 피해(불길·화상)는 소리 없음 + 0.15초에 한 번까지
            if (silentHit || amount < zombieHitSoundMinDamage || Time.time < lastZombieHitSoundTime + zombieHitSoundCooldown) return;
            lastZombieHitSoundTime = Time.time;
            AudioManager.PlaySFX(SoundLibrary.Pick(lib.hitFlesh), transform.position + Vector3.up,
                                 AudioManager.Vol.Impact, AudioManager.Channel.SFX, 25f, UnityEngine.Random.Range(0.9f, 1.1f));
        }
    }

    /// <summary>
    /// (2026-11-21) 지금 처리 중인 피해가 좀비 공격인지. onHurt 리스너(회복템 사용 취소 등)가 읽음.
    /// 갈증·배고픔·불길 같은 피해로는 먹고 마시기가 끊기지 않게 하려고 구분함
    /// </summary>
    public bool IsZombieHit { get; private set; }

    // (2026-11-22) 소리 없는 피해 (화상 지속 피해 등): 불에 타는 좀비가 0.5초마다 '퍽' 살 맞는 소리를 내던 것 방지
    private bool silentHit;
    /// <summary>살 맞는 소리 없이 피해 (BurnDoT). 나머지는 TakeDamage와 같음</summary>
    public void TakeDamageSilent(float amount)
    {
        silentHit = true;
        try { TakeDamage(amount); }
        finally { silentHit = false; }
    }

    /// <summary>좀비 공격 피해 (ZombieController가 호출). 나머지는 TakeDamage와 같음</summary>
    public void TakeZombieHit(float amount)
    {
        IsZombieHit = true;
        try { TakeDamage(amount); }
        finally { IsZombieHit = false; }
    }

    public void TakeDamage(float amount, bool isCritical = false)
    {
        if (isInvincible) return; // 무적 (구르기): 피해/데미지 숫자/피격 반응 전부 없음
        currentHealth -= amount;

        // 데미지 텍스트를 생성하는 함수를 호출함.
        // (2026-11-21) 이미 죽은 몸(시체)을 때릴 때는 숫자를 띄우지 않음
        if (!dead) ShowDamageText(amount, isCritical);

        if (dead) return;
        if (currentHealth > 0f) PlayHurtSound(amount); // 죽는 타격은 사망음으로 대신
        onHurt?.Invoke();
        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    // 인스턴스화 과정을 처리하는 내부 함수임.
    private void ShowDamageText(float amount, bool isCritical)
    {
        if (damageTextPrefab != null)
        {
            // 모호한 참조 에러 방지를 위해 UnityEngine.Random을 명시적으로 호출함.
            float randomX = UnityEngine.Random.Range(-0.5f, 0.5f);
            float randomZ = UnityEngine.Random.Range(-0.5f, 0.5f);
            Vector3 randomOffset = new Vector3(randomX, 1f, randomZ);

            GameObject textObj = Instantiate(damageTextPrefab, transform.position + randomOffset, Quaternion.identity);

            DamageText damageText = textObj.GetComponent<DamageText>();
            if (damageText != null)
            {
                damageText.Setup(amount, isCritical);
            }
        }
    }

    public void Heal(float amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
        if (currentHealth > 0f) dead = false;
    }

    // 스폰 시점에 maxHealth를 바꿔 끼울 때 사용 (currentHealth도 꽉 채워서 같이 맞춤).
    // Heal(0)으로는 currentHealth가 새 maxHealth까지 안 올라가서 별도로 뺌.
    public void SetMaxHealth(float newMax)
    {
        maxHealth = newMax;
        currentHealth = maxHealth;
        dead = false;
    }

    void Die()
    {
        dead = true;
        onDeath?.Invoke();

        // 플레이어 사망 비명 (2D). 좀비 사망음은 ZombieController.Die (목소리 풀 + 피/쓰러짐)
        if (isPlayer && AudioManager.Library != null)
            AudioManager.PlayUI(SoundLibrary.Pick(AudioManager.Library.playerDeath), AudioManager.Vol.PlayerHurt);

        // 플레이어가 죽으면 사망 애니메이션을 보여준 뒤 게임 정지
        if (CompareTag("Player"))
            StartCoroutine(FreezeAfterDeath());
    }

    System.Collections.IEnumerator FreezeAfterDeath()
    {
        yield return new WaitForSecondsRealtime(playerDeathFreezeDelay);
        Time.timeScale = 0f;
    }

    public void SelfDestroy()
    {
        Destroy(gameObject);
    }

    public float GetCurrentHealth()
    {
        return currentHealth;
    }

    public float GetMaxHealth()
    {
        return maxHealth;
    }
}
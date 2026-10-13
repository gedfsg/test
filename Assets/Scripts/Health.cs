using UnityEngine.Events;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;
    private bool dead; // 사망 처리 한 번만 (죽은 뒤 추가 피해로 onDeath가 반복 호출되던 것 방지)

    [Tooltip("플레이어 사망 후 게임을 멈출 때까지 기다리는 시간(초, 실제 시간). 사망 애니메이션이 보이게")]
    public float playerDeathFreezeDelay = 4.5f; // Dying 애니메이션(4.4초)이 끝까지 보이게

    public UnityEvent onDeath;
    public UnityEvent onHurt;

    // 데미지 수치를 표시할 UI 프리팹 객체임.
    [Header("Damage UI")]
    public GameObject damageTextPrefab;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount, bool isCritical = false)
    {
        currentHealth -= amount;

        // 데미지 텍스트를 생성하는 함수를 호출함.
        ShowDamageText(amount, isCritical);

        if (dead) return;
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
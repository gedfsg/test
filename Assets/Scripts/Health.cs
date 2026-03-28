using UnityEngine.Events;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    public UnityEvent onDeath;
    public UnityEvent onHurt;

    // 데미지 수치를 표시할 UI 프리팹 객체임.
    [Header("Damage UI")]
    public GameObject damageTextPrefab;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        
        // 데미지 텍스트를 생성하는 함수를 호출함.
        ShowDamageText(amount);
        
        onHurt?.Invoke();
        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    // 인스턴스화 과정을 처리하는 내부 함수임.
    private void ShowDamageText(float amount)
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
                damageText.Setup(amount);
            }
        }
    }

    void Die()
    {
        onDeath?.Invoke();
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
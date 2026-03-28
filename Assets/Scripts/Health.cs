using UnityEngine.Events;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    public UnityEvent onDeath;
    public UnityEvent onHurt;

    [Header("Damage UI")]
    public GameObject damageTextPrefab;
    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        ShowDamageText(damage);
        onHurt?.Invoke();
        if (currentHealth <= 0f)
        {
            Die();
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

    private void ShowDamageText(float damage)
    {
        if(damageTextPrefab != null)
        {
            Vector3 randomOffset = new Vector3(Random.Range(-0.5f, 0.5f), 1f, Random.Range(-0.5f, 0.5f));
            GameObject textObj = Instantiate(damageTextPrefab, transform.position + randomOffset, Quaternion.identity);

            DamageText damageText = textObj.GetComponent<DamageText>();
            if (damageText != null)            {
                damageText.Setup(damage);
            }
        }
    }
}

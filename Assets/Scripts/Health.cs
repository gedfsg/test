using UnityEngine.Events;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    public UnityEvent onDeath;
    public UnityEvent onHurt;
    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
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
}

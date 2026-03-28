using System;
using UnityEngine;

public class DestructibleObstacle : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Damage UI")]
    public GameObject damageTextPrefab;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        ShowDamageText(damage);

        if (currentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }

    private void ShowDamageText(float amount)
    {
        if(damageTextPrefab != null)
        {
            Vector3 randomOffset = new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1f, UnityEngine.Random.Range(-0.5f, 0.5f));
            GameObject textObj = Instantiate(damageTextPrefab, transform.position + randomOffset, Quaternion.identity);

            DamageText damageText = textObj.GetComponent<DamageText>();
            if (damageText != null)            {
                damageText.Setup(amount);
            }
        }
    }
}

using UnityEngine;

public class DestructibleObstacle : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    // 데미지 수치를 표시할 UI 프리팹 객체임.
    [Header("Damage UI")]
    public GameObject damageTextPrefab;

    [Header("Sound")]
    [Tooltip("부서질 때 유리 깨지는 소리 (끄면 나무 부서지는 소리). 이름에 Glass/Window가 들어가면 자동으로 켜짐")]
    public bool isGlass;

    void Start()
    {
        currentHealth = maxHealth;
        string n = name.ToLower();
        if (n.Contains("glass") || n.Contains("window")) isGlass = true;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        // 데미지 텍스트를 생성하는 함수를 호출함.
        ShowDamageText(damage);

        if (currentHealth <= 0)
        {
            if (AudioManager.Library != null)
                AudioManager.PlaySFX(SoundLibrary.Pick(isGlass ? AudioManager.Library.glassBreak : AudioManager.Library.woodBreak),
                                     transform.position, AudioManager.Vol.Impact, AudioManager.Channel.SFX, 30f);
            Destroy(gameObject);
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
}
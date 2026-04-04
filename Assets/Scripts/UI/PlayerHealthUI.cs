using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthUI : MonoBehaviour
{
    public Health playerHealth;
    public Slider hpSlider;

    void Start()
    {
        if(playerHealth != null && hpSlider != null)
        {
            hpSlider.maxValue = playerHealth.GetMaxHealth();
        
            hpSlider.value = playerHealth.GetCurrentHealth();
        
            Debug.Log("체력 바 초기화 완료: " + hpSlider.value);
        }
    }

    public void UpdateHealthUI()
    {
        if(playerHealth != null && hpSlider != null)
        {
            hpSlider.value = playerHealth.GetCurrentHealth();
        }
    }
}

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
            hpSlider.minValue = 0f;
            hpSlider.maxValue = 1f;
            hpSlider.value = 1f;
        }
    }

    public void UpdateHealthUI()
    {
        if(playerHealth != null && hpSlider != null)
        {
            hpSlider.value = playerHealth.GetCurrentHealth() / playerHealth.GetMaxHealth();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

public class PlayerStaminaUI : MonoBehaviour
{
    public Locomotion playerLocomotion;
    public Image staminaFillImage;

    void Update()
    {
        if(playerLocomotion != null && staminaFillImage != null)
        {
            staminaFillImage.fillAmount = playerLocomotion.GetStaminaNormalized();
        }
    }
}

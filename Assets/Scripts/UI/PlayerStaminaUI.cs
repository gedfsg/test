using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 스태미나바 UI. 씬(또는 HUD 프리팹)에 배치된 Fill 이미지를 갱신한다.
/// </summary>
public class PlayerStaminaUI : MonoBehaviour
{
    [Header("References")]
    public Locomotion playerLocomotion;
    public Image staminaFillImage;

    [Tooltip("색상 팔레트. 비워두면 staminaFillImage의 기존 색을 그대로 사용한다.")]
    public UITheme theme;

    void Start()
    {
        if (playerLocomotion == null)
            playerLocomotion = GameObject.FindWithTag("Player")?.GetComponent<Locomotion>();

        if (theme != null && staminaFillImage != null)
            staminaFillImage.color = theme.staminaFill;
    }

    void Update()
    {
        if (playerLocomotion != null && staminaFillImage != null)
            staminaFillImage.fillAmount = playerLocomotion.GetStaminaNormalized();
    }
}

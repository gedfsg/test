using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체력바 UI. 씬(또는 HUD 프리팹)에 배치된 Image를 갱신한다.
/// 색상은 UITheme에서 가져오므로 디자인 변경 시 코드 수정이 필요 없다.
///
/// 이중 바(지연 잔상) 구조:
///   HealthBarRoot (배경)
///     ├─ DelayedFill   (잔상 - 피격 시 잠시 멈췄다 따라옴)   ← delayedFillImage
///     └─ HpFill        (실제 체력 - 즉시 반영)              ← hpFillImage
/// 두 이미지 모두 Image Type을 Filled / Horizontal / Left로 설정한다.
/// DelayedFill이 HpFill보다 자식 순서가 앞이어야 뒤에 깔린다.
/// </summary>
public class PlayerHealthUI : MonoBehaviour
{
    [Header("References")]
    public Health playerHealth;

    [Tooltip("실제 체력을 즉시 반영하는 Fill 이미지. Image Type = Filled.")]
    public Image hpFillImage;

    [Tooltip("피격 시 뒤늦게 따라오는 잔상 Fill 이미지. 비워두면 이중 바 연출이 꺼진다.")]
    public Image delayedFillImage;

    [Tooltip("\"100 / 100\" 형태로 수치를 보여줄 텍스트. 선택사항.")]
    public TextMeshProUGUI hpText;

    [Tooltip("색상 팔레트. 비워두면 기본 빨강/초록 보간을 사용한다.")]
    public UITheme theme;

    [Header("잔상 연출")]
    [Tooltip("피격 후 잔상이 따라오기 시작할 때까지의 대기 시간(초).")]
    public float delayBeforeCatchUp = 0.4f;

    [Tooltip("잔상이 따라오는 속도. 클수록 빠르게 붙는다.")]
    public float catchUpSpeed = 1.2f;

    [Tooltip("잔상 바의 색. 깎인 구간이 이 색으로 보인다.")]
    public Color delayedColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    private float displayedRatio = 1f;   // 잔상이 현재 보여주는 값
    private float delayTimer;            // 남은 대기 시간
    private float lastRatio = 1f;        // 직전 프레임 체력 비율 (피격 감지용)

    void Start()
    {
        if (playerHealth == null)
            playerHealth = GameObject.FindWithTag("Player")?.GetComponent<Health>();

        displayedRatio = GetRatio();
        lastRatio = displayedRatio;

        if (delayedFillImage != null)
        {
            delayedFillImage.color = delayedColor;
            delayedFillImage.fillAmount = displayedRatio;
        }
    }

    void Update() => UpdateHealthUI();

    public void UpdateHealthUI()
    {
        if (playerHealth == null) return;

        float ratio = GetRatio();

        if (hpFillImage != null)
        {
            hpFillImage.fillAmount = ratio;
            hpFillImage.color = theme != null
                ? theme.GetHealthColor(ratio)
                : Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.15f, 0.8f, 0.2f), ratio);
        }

        UpdateDelayedFill(ratio);
        lastRatio = ratio;

        if (hpText != null)
            hpText.text = $"{Mathf.CeilToInt(playerHealth.GetCurrentHealth())} / {Mathf.CeilToInt(playerHealth.GetMaxHealth())}";
    }

    /// <summary>
    /// 잔상 바 갱신. 체력이 줄면 잠시 멈췄다가 서서히 따라오고,
    /// 체력이 늘면(회복) 기다리지 않고 바로 맞춘다.
    /// </summary>
    private void UpdateDelayedFill(float ratio)
    {
        if (delayedFillImage == null) return;

        if (ratio > displayedRatio)
        {
            // 회복 — 잔상이 뒤에 남을 이유가 없으므로 즉시 동기화
            displayedRatio = ratio;
            delayTimer = 0f;
        }
        else if (ratio < displayedRatio)
        {
            // 이번 프레임에 새로 깎였다면 대기 타이머를 다시 채운다
            if (ratio < lastRatio) delayTimer = delayBeforeCatchUp;

            if (delayTimer > 0f)
            {
                delayTimer -= Time.deltaTime;
            }
            else
            {
                displayedRatio = Mathf.MoveTowards(displayedRatio, ratio, catchUpSpeed * Time.deltaTime);
            }
        }

        delayedFillImage.fillAmount = displayedRatio;
    }

    /// <summary>체력이 깎인 순간 호출하면 잔상이 잠시 멈춘다. Health 쪽에서 불러도 되고, 안 불러도 동작한다.</summary>
    public void OnDamaged() => delayTimer = delayBeforeCatchUp;

    private float GetRatio()
    {
        if (playerHealth == null) return 1f;
        float max = playerHealth.GetMaxHealth();
        return max <= 0f ? 0f : Mathf.Clamp01(playerHealth.GetCurrentHealth() / max);
    }
}
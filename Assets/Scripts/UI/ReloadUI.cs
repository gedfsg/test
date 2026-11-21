using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 재장전 진행 게이지 UI.
/// </summary>
public class ReloadUI : MonoBehaviour
{
    [Header("References")]
    public Image gaugeImage;

    private float reloadDuration;
    private float currentReloadTime;
    private bool isReloadingUI = false;

    void Awake()
    {
        // (2026-11-21) 게이지를 월드 고정 위치가 아니라 화면 기준 '머리 옆'에 띄움.
        // 예전엔 캔버스(FloatingUI)가 월드 방향 오프셋을 써서 카메라를 돌리면 엉뚱한 곳에 나왔음
        var follow = GetComponentInParent<FloatingUI>(true);
        if (follow != null)
        {
            follow.cameraRelative = true;
            gaugeImage.rectTransform.anchoredPosition = Vector2.zero;   // 캔버스 한가운데 = 머리 옆 지점
        }
        gaugeImage.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!isReloadingUI) return;

        currentReloadTime += Time.deltaTime;
        gaugeImage.fillAmount = currentReloadTime / reloadDuration;
    }

    public void StartReloadUI(float duration)
    {
        reloadDuration = duration;
        currentReloadTime = 0f;
        isReloadingUI = true;

        gaugeImage.fillAmount = 0f;
        gaugeImage.gameObject.SetActive(true);
    }

    public void CompleteReloadUI()
    {
        isReloadingUI = false;
        gaugeImage.gameObject.SetActive(false);
    }
}

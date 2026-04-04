using UnityEngine;
using UnityEngine.UI;

public class ReloadUI : MonoBehaviour
{
    public Image gaugeImage;

    private float reloadDuration;
    private float currentReloadTime;
    private bool isReloadingUI = false;

    void Awake()
    {
        gaugeImage.gameObject.SetActive(false);
    }

    void Update()
    {
        if (isReloadingUI)
        {
            currentReloadTime += Time.deltaTime;

            gaugeImage.fillAmount = currentReloadTime / reloadDuration;
        }
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

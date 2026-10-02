using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좀비 머리 위에 떠다니는 체력바. 월드스페이스 Canvas를 코드로 생성하고
/// 데미지를 받을 때(Health.onHurt)만 갱신, 평소엔 카메라를 향해 빌보드 회전만 함.
/// </summary>
[RequireComponent(typeof(Health))]
public class ZombieHealthBar : MonoBehaviour
{
    public float heightOffset = 2.3f;
    public float barWidth = 1f;
    public float barHeight = 0.12f;

    private Health health;
    private Transform barRoot;
    private Image fillImage;

    void Start()
    {
        health = GetComponent<Health>();
        BuildBar();
        health.onHurt.AddListener(UpdateFill);
        health.onDeath.AddListener(() => { if (barRoot != null) barRoot.gameObject.SetActive(false); });
        UpdateFill();
    }

    void BuildBar()
    {
        var canvasGo = new GameObject("HealthBarCanvas");
        barRoot = canvasGo.transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = Vector3.up * heightOffset;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<CanvasScaler>();
        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(barWidth, barHeight);
        canvasGo.transform.localScale = Vector3.one * 0.01f; // 월드스페이스 캔버스는 보통 작게 스케일

        var bgGo = new GameObject("BG");
        bgGo.transform.SetParent(barRoot, false);
        var bgImage = bgGo.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.6f);
        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(barWidth * 100f, barHeight * 100f);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barRoot, false);
        fillImage = fillGo.AddComponent<Image>();
        fillImage.color = new Color(0.9f, 0.1f, 0.1f, 1f);
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        var fillRect = fillGo.GetComponent<RectTransform>();
        fillRect.sizeDelta = new Vector2(barWidth * 100f, barHeight * 100f);
    }

    void UpdateFill()
    {
        if (fillImage == null) return;
        fillImage.fillAmount = Mathf.Clamp01(health.GetCurrentHealth() / health.GetMaxHealth());
    }

    void LateUpdate()
    {
        if (barRoot == null) return;
        barRoot.position = transform.position + Vector3.up * heightOffset;
        if (Camera.main != null)
            barRoot.rotation = Camera.main.transform.rotation;
    }
}

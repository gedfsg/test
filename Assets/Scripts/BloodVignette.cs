using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어가 데미지를 받으면(Health.onHurt) 화면 가장자리가 빨갛게 번쩍였다가 페이드아웃.
/// 체력이 낮을수록 평소에도 더 진하게 깔림. 전체화면 Image + 코드로 생성한 방사형 그라디언트 텍스처 사용.
/// </summary>
public class BloodVignette : MonoBehaviour
{
    public float flashAlpha = 0.6f;
    public float fadeSpeed = 1.2f;      // 번쩍인 뒤 초당 알파 감소량
    public float lowHealthThreshold = 0.3f; // 이 비율 이하부터 상시 비네트 시작
    public float lowHealthMaxAlpha = 0.45f;
    public Color vignetteColor = Color.red;

    private Health playerHealth;
    private Image image;
    private float flashTimer;

    void Awake()
    {
        var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

        var go = new GameObject("BloodVignette");
        go.transform.SetParent(canvas.transform, false);
        image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.sprite = BuildVignetteSprite();
        image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.transform.SetAsLastSibling();
    }

    void Start()
    {
        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo != null) playerHealth = playerGo.GetComponent<Health>();
        if (playerHealth != null) playerHealth.onHurt.AddListener(Flash);
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.onHurt.RemoveListener(Flash);
    }

    public void Flash()
    {
        flashTimer = flashAlpha;
    }

    void Update()
    {
        flashTimer = Mathf.Max(0f, flashTimer - fadeSpeed * Time.deltaTime);

        float lowHealthAlpha = 0f;
        if (playerHealth != null)
        {
            float ratio = playerHealth.GetCurrentHealth() / playerHealth.GetMaxHealth();
            if (ratio < lowHealthThreshold)
                lowHealthAlpha = Mathf.Lerp(lowHealthMaxAlpha, 0f, ratio / lowHealthThreshold);
        }

        float alpha = Mathf.Max(flashTimer, lowHealthAlpha);
        image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, alpha);
    }

    // 중심은 완전 투명, 가장자리로 갈수록 불투명해지는 방사형 텍스처
    static Sprite BuildVignetteSprite()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.Alpha8, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxDist = center.magnitude;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float a = Mathf.Clamp01((dist - 0.35f) / 0.65f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}

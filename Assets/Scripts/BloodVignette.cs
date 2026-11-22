using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어가 데미지를 받으면(Health.onHurt) 화면 가장자리가 빨갛게 번쩍였다가 페이드아웃.
/// 체력이 낮을수록 평소에도 더 진하게 깔림. 전체화면 Image + 코드로 생성한 방사형 그라디언트 텍스처 사용.
///
/// (2026-11-22) 아포칼립스 HUD 리디자인 - 상시 피 튀김(HudBloodOverlay, 어두운 적갈색·정적)과 구분되게 (사용자 지정):
///  - 색: 선명한 빨강 #D01010, 최대 알파 0.75 (0.6~0.8)
///  - 짧은 펄스: 0.06초에 확 뜨고 0.2초까지 유지 → 0.5초에 사라짐
///  - 펄스 동안 깜빡임 + 흔들림 (위치 ±8px)
///  - 화면 전체 비네트: 안쪽 시작 반경 0.35 → 0.2 (상시 피 튀김보다 넓게)
///  - 레이어: HudBloodOverlay 캔버스(order 50)의 마지막 자식 = HUD·상시 피 튀김 위
/// </summary>
public class BloodVignette : MonoBehaviour
{
    public float flashAlpha = 0.6f;
    public float fadeSpeed = 1.2f;      // 번쩍인 뒤 초당 알파 감소량 (2026-11-22부터 펄스 곡선 사용, 안 씀)
    public float lowHealthThreshold = 0.3f; // 이 비율 이하부터 상시 비네트 시작
    public float lowHealthMaxAlpha = 0.45f;
    public Color vignetteColor = Color.red;

    // (2026-11-22) 피격 펄스 (씬에 저장된 위 값과 상관없이 아래 값 사용)
    static readonly Color HitColor = new Color(0xD0 / 255f, 0x10 / 255f, 0x10 / 255f, 1f);
    const float PulsePeak = 0.75f, PulseRise = 0.06f, PulseHold = 0.2f, PulseEnd = 0.5f;
    const float ShakePx = 8f;

    private Health playerHealth;
    private Image image;
    private RectTransform rect;
    private float flashTimer;
    private float pulseTime = 999f; // (2026-11-22) 마지막 피격 후 경과 시간

    void Awake()
    {
        // var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>(); // (이전) 메인 HUD 캔버스(order 0) → 미션 패널(order 5) 아래에 깔렸음
        // if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

        var go = new GameObject("BloodVignette");
        // go.transform.SetParent(canvas.transform, false); // (이전)
        go.transform.SetParent(HudBloodOverlay.EnsureCanvas(), false); // (2026-11-22) HUD·상시 피 튀김 위
        image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.sprite = BuildVignetteSprite();
        // image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f); // (이전)
        image.color = new Color(HitColor.r, HitColor.g, HitColor.b, 0f);

        rect = go.GetComponent<RectTransform>();
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
        pulseTime = 0f; // (2026-11-22)
    }

    void Update()
    {
        // (이전) flashTimer = Mathf.Max(0f, flashTimer - fadeSpeed * Time.deltaTime);
        // (2026-11-22) 펄스 곡선: 확 뜨고(0.06s) → 0.2s 유지 → 0.5s에 0, 그동안 깜빡임
        pulseTime += Time.deltaTime;
        float pulse = 0f;
        if (pulseTime < PulseRise) pulse = pulseTime / PulseRise;
        else if (pulseTime < PulseHold) pulse = 1f;
        else if (pulseTime < PulseEnd) pulse = 1f - (pulseTime - PulseHold) / (PulseEnd - PulseHold);
        if (pulse > 0f) pulse *= 0.8f + 0.2f * Mathf.Abs(Mathf.Sin(pulseTime * 70f)); // 깜빡임
        flashTimer = pulse * PulsePeak;

        // 흔들림 (펄스 동안만)
        if (rect != null)
        {
            Vector2 off = pulse > 0f ? Random.insideUnitCircle * ShakePx * pulse : Vector2.zero;
            rect.offsetMin = off; rect.offsetMax = off;
        }

        float lowHealthAlpha = 0f;
        if (playerHealth != null)
        {
            float ratio = playerHealth.GetCurrentHealth() / playerHealth.GetMaxHealth();
            if (ratio < lowHealthThreshold)
                lowHealthAlpha = Mathf.Lerp(lowHealthMaxAlpha, 0f, ratio / lowHealthThreshold);
        }

        float alpha = Mathf.Max(flashTimer, lowHealthAlpha);
        // image.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, alpha); // (이전)
        image.color = new Color(HitColor.r, HitColor.g, HitColor.b, alpha);
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
                // float a = Mathf.Clamp01((dist - 0.35f) / 0.65f); // (이전)
                float a = Mathf.Clamp01((dist - 0.2f) / 0.8f);      // (2026-11-22) 상시 피 튀김보다 넓게 (화면 전체 비네트)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}

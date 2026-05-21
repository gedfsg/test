using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 화면 고정 HUD — 체력바 + 스태미나바 (왼쪽 하단)
/// 씬에 아무 오브젝트에나 붙이면 자동으로 UI 생성됨
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    // 외부 참조 (없으면 자동 탐색)
    public Health      playerHealth;
    public Locomotion  playerLocomotion;

    private Image hpFill;
    private Image stFill;
    private TextMeshProUGUI hpText;

    void Start()
    {
        // 자동 탐색
        if (playerHealth    == null) playerHealth    = GameObject.FindWithTag("Player")?.GetComponent<Health>();
        if (playerLocomotion == null) playerLocomotion = GameObject.FindWithTag("Player")?.GetComponent<Locomotion>();

        BuildHUD();
    }

    void Update()
    {
        // 체력바
        if (hpFill != null && playerHealth != null)
        {
            float ratio = playerHealth.GetCurrentHealth() / playerHealth.GetMaxHealth();
            hpFill.fillAmount = ratio;
            if (hpText != null)
                hpText.text = $"{Mathf.CeilToInt(playerHealth.GetCurrentHealth())} / {Mathf.CeilToInt(playerHealth.GetMaxHealth())}";

            // 체력 낮으면 빨간색, 보통은 초록색
            hpFill.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.15f, 0.8f, 0.2f), ratio);
        }

        // 스태미나바
        if (stFill != null && playerLocomotion != null)
            stFill.fillAmount = playerLocomotion.GetStaminaNormalized();
    }

    // ── UI 생성 ───────────────────────────────────

    void BuildHUD()
    {
        // 캔버스
        GameObject canvasObj = new GameObject("PlayerHUDCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 패널 (왼쪽 하단)
        GameObject panel = new GameObject("HUDPanel");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0f, 0f);
        prt.anchorMax = new Vector2(0f, 0f);
        prt.pivot     = new Vector2(0f, 0f);
        prt.anchoredPosition = new Vector2(20f, 20f);
        prt.sizeDelta = new Vector2(220f, 64f);

        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0f, 0f, 0f, 0.45f);

        // 체력바
        hpFill = CreateBar(panel.transform,
            new Vector2(8f, 36f), new Vector2(204f, 22f),
            new Color(0.15f, 0.8f, 0.2f), "HPBar");

        // 체력 숫자
        hpText = CreateLabel(panel.transform,
            new Vector2(8f, 36f), new Vector2(204f, 22f), "100 / 100");

        // 스태미나바
        stFill = CreateBar(panel.transform,
            new Vector2(8f, 8f), new Vector2(204f, 16f),
            new Color(0.9f, 0.75f, 0.1f), "STBar");

        // HP 아이콘 레이블
        CreateIconLabel(panel.transform, new Vector2(8f, 56f), "❤ HP");
    }

    Image CreateBar(Transform parent, Vector2 pos, Vector2 size, Color fillColor, string name)
    {
        // 배경
        GameObject bg = new GameObject(name + "_BG");
        bg.transform.SetParent(parent, false);
        RectTransform bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = bgRt.pivot = Vector2.zero;
        bgRt.anchoredPosition = pos;
        bgRt.sizeDelta = size;
        bg.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

        // 채우기
        GameObject fill = new GameObject(name + "_Fill");
        fill.transform.SetParent(bg.transform, false);
        RectTransform frt = fill.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;

        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = fillColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 1f;

        return fillImg;
    }

    TextMeshProUGUI CreateLabel(Transform parent, Vector2 pos, Vector2 size, string text)
    {
        GameObject obj = new GameObject("HPLabel");
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 11f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineRight;
        return tmp;
    }

    void CreateIconLabel(Transform parent, Vector2 pos, string text)
    {
        GameObject obj = new GameObject("IconLabel");
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(60f, 14f);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 10f;
        tmp.color = new Color(0.9f, 0.9f, 0.9f, 0.8f);
    }
}

using UnityEngine;
using TMPro;

/// <summary>
/// 화면 중하단에 짧게 뜨고 사라지는 피드백 텍스트 ("+10 AR탄", "탄약 없음!" 등).
/// Show()만 호출하면 되고, 코드로 생성되므로 씬에 미리 배치할 필요 없음.
/// </summary>
public class PickupFeedbackUI : MonoBehaviour
{
    public static PickupFeedbackUI Instance { get; private set; }

    public float displayDuration = 2f;
    public float fadeDuration = 0.4f;
    public Color defaultColor = Color.white;
    public Color warningColor = new Color(1f, 0.35f, 0.3f);

    private TextMeshProUGUI text;
    private float timer;

    void Awake()
    {
        Instance = this;
        Build();
    }

    void Build()
    {
        var canvasGo = GameObject.Find("Canvas");
        var canvas = canvasGo != null ? canvasGo.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();

        var go = new GameObject("PickupFeedbackText", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.32f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(500f, 44f);

        text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 24;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(defaultColor.r, defaultColor.g, defaultColor.b, 0f);
        text.raycastTarget = false;
        text.enableWordWrapping = false;
    }

    public void Show(string message)
    {
        ShowInternal(message, defaultColor);
    }

    public void ShowWarning(string message)
    {
        ShowInternal(message, warningColor);
    }

    void ShowInternal(string message, Color color)
    {
        text.text = message;
        text.color = color;
        timer = displayDuration;
        text.transform.localScale = Vector3.one * 1.1f;
    }

    void Update()
    {
        if (timer <= 0f) return;
        timer -= Time.deltaTime;

        var c = text.color;
        c.a = timer < fadeDuration ? Mathf.Clamp01(timer / fadeDuration) : 1f;
        text.color = c;

        text.transform.localScale = Vector3.Lerp(text.transform.localScale, Vector3.one, Time.deltaTime * 10f);
    }
}

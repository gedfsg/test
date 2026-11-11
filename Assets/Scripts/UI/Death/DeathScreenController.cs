using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 생존 종료 화면 (DeathScreen.prefab). 게임 씬이 뜰 때 MenuUI가 전용 캔버스에 꺼내 둠 (내용은 꺼진 상태).
///  플레이어 Health.onDeath → 사망 순간 화면을 흐리게 찍어 둠 → 3초 동안 배경이 서서히 어두워짐(사망 모션이 그 사이 보임)
///  → 패널 표시 + Time.timeScale = 0
///  통계: GameStats (도달 일차 / 생존 시간 / 완료한 미션) + 1~7일 도트 (지난 날 주황, 사망한 날 붉게 강조)
///  다시 시작 = 현재 씬 다시 로드 / 메인 메뉴
/// </summary>
public class DeathScreenController : MonoBehaviour
{
    public CanvasGroup backdrop;   // 흐린 화면 + 검은 막 (3초 페이드)
    public RawImage blurImage;
    public CanvasGroup panel;      // 가운데 패널 (배경이 다 어두워진 뒤 짧게 페이드)
    public float fadeDuration = 3f;
    public float panelFade = 0.4f;

    [Header("통계")]
    public TextMeshProUGUI dayValue;
    public TextMeshProUGUI timeValue;
    public TextMeshProUGUI missionValue;

    [Header("일차 도트 (1~7)")]
    public Image[] dayDots;
    public Image[] dayDotFrames;
    public TextMeshProUGUI[] dayDotLabels;
    public Color pastFill = new Color32(0x3A, 0x2F, 0x1E, 0xFF), pastFrame = new Color32(0x9A, 0x74, 0x38, 0xFF), pastText = new Color32(0xF0, 0xC8, 0x80, 0xFF);
    public Color reachedFill = new Color32(0x6E, 0x33, 0x36, 0xFF), reachedFrame = new Color32(0xC0, 0x62, 0x66, 0xFF), reachedText = Color.white;
    public Color futureFill = new Color32(0x22, 0x24, 0x2A, 0xFF), futureFrame = new Color32(0x4A, 0x4D, 0x55, 0xFF), futureText = new Color32(0x9C, 0xA3, 0xAF, 0xFF);

    [Header("버튼")]
    public Button restartButton;
    public Button mainMenuButton;

    bool shown;

    void Awake()
    {
        Localization.Register(gameObject); // 메뉴 글자 언어 (설정 → 언어)
        if (restartButton != null) restartButton.onClick.AddListener(MenuUI.RestartGame);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(MenuUI.LoadMainMenu);
        if (backdrop != null) backdrop.gameObject.SetActive(false);
        if (panel != null) panel.gameObject.SetActive(false);
    }

    void Start()
    {
        var player = GameObject.FindWithTag("Player");
        var health = player != null ? player.GetComponent<Health>() : null;
        if (health == null) return;
        if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
        health.onDeath.AddListener(OnPlayerDied);
    }

    void OnPlayerDied()
    {
        if (shown) return;
        shown = true;
        MenuUI.DeathShown = true;
        if (GameStats.Instance != null) GameStats.Instance.Freeze();
        // 일시정지 중에 죽는 일은 없지만(시간 정지), 혹시 열려 있으면 닫음
        var pause = FindFirstObjectByType<PauseMenuController>();
        if (pause != null && pause.IsOpen) pause.Close();
        MenuUI.RefreshGameplayInput(); // DeathShown·PauseOpen을 먼저 켠 뒤 → 조작 막힘
        StartCoroutine(Sequence());
    }

    IEnumerator Sequence()
    {
        yield return MenuUI.CaptureBlur(blurImage); // 사망 순간 화면 (HUD 포함)
        FillStats();

        backdrop.gameObject.SetActive(true);
        backdrop.alpha = 0f;
        UIAudio.PlayDeathStinger(); // 화면이 어두워지기 시작할 때 드라마틱 스팅어
        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            backdrop.alpha = Mathf.SmoothStep(0f, 1f, t / fadeDuration);
            yield return null;
        }
        backdrop.alpha = 1f;
        Time.timeScale = 0f; // Health도 사망 4.5초 뒤 멈추지만 화면이 뜨는 순간 확실히 정지

        panel.gameObject.SetActive(true);
        panel.alpha = 0f;
        for (float t = 0f; t < panelFade; t += Time.unscaledDeltaTime)
        {
            panel.alpha = t / panelFade;
            yield return null;
        }
        panel.alpha = 1f;
    }

    void FillStats()
    {
        var s = GameStats.Instance;
        int day = s != null ? s.CurrentDay : 1;
        int total = s != null ? s.TotalDays : 7;
        if (dayValue != null) dayValue.text = $"{day}<size=55%><color=#C9CDD4> / {total}{(Localization.IsEnglish ? " days" : "일")}</color></size>";
        if (timeValue != null) timeValue.text = GameStats.FormatTime(s != null ? s.PlayTime : 0f);
        if (missionValue != null) missionValue.text = $"{(s != null ? s.CompletedMissions : 0)}<size=55%><color=#C9CDD4>{(Localization.IsEnglish ? "" : " 개")}</color></size>";

        if (dayDots == null) return;
        for (int i = 0; i < dayDots.Length; i++)
        {
            int d = i + 1;
            Color fill = d < day ? pastFill : d == day ? reachedFill : futureFill;
            Color frame = d < day ? pastFrame : d == day ? reachedFrame : futureFrame;
            Color text = d < day ? pastText : d == day ? reachedText : futureText;
            if (dayDots[i] != null) dayDots[i].color = fill;
            if (dayDotFrames != null && i < dayDotFrames.Length && dayDotFrames[i] != null) dayDotFrames[i].color = frame;
            if (dayDotLabels != null && i < dayDotLabels.Length && dayDotLabels[i] != null) dayDotLabels[i].color = text;
        }
    }
}

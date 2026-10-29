using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 사망 화면: 플레이어 체력 0 → Dying 모션 → showDelay초 뒤
///  화면이 검게 fade in (fadeDuration) → "GAME OVER" 빨간 큰 글자 fade in (textFade) + 아래 "R - 다시 시작"
///  R키로 재시작 (씬 다시 로드)
/// Health가 사망 4.5초 뒤 Time.timeScale = 0으로 멈추므로 전부 실제 시간(unscaled) 기준으로 동작.
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성.
/// </summary>
public class DeathUI : MonoBehaviour
{
    [Tooltip("사망 후 화면이 어두워지기 시작할 때까지 (초, 실제 시간)")]
    public float showDelay = 3f;
    public float fadeDuration = 1.5f;
    public float textFade = 0.5f;

    Image black;
    TextMeshProUGUI title, hint;
    bool canRestart;

    // 씬이 로드될 때마다 자동 생성 (재시작 = 씬 다시 로드 때도). AfterSceneLoad만 쓰면 게임 첫 시작에 한 번만 실행됨
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AutoCreate();

    static void AutoCreate()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null || player.GetComponent<Health>() == null) return;
        if (FindFirstObjectByType<DeathUI>() != null) return;
        new GameObject("DeathUI").AddComponent<DeathUI>();
    }

    void Start()
    {
        Build();
        var health = GameObject.FindWithTag("Player")?.GetComponent<Health>();
        if (health != null)
        {
            if (health.onDeath == null) health.onDeath = new UnityEngine.Events.UnityEvent();
            health.onDeath.AddListener(() => StartCoroutine(Sequence()));
        }
    }

    void Build()
    {
        // 다른 HUD 위에 그려지도록 별도 캔버스 (HUD와 같은 기준 해상도)
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1681f, 936f);
        scaler.matchWidthOrHeight = 0.5f;

        var bg = new GameObject("Black", typeof(RectTransform), typeof(Image));
        var r = (RectTransform)bg.transform;
        r.SetParent(transform, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        black = bg.GetComponent<Image>();
        black.color = new Color(0f, 0f, 0f, 0f);
        black.raycastTarget = false;

        title = HudStyle.NewText(transform, "Title", "GAME OVER", HudStyle.Bold, 110f, new Color(0.85f, 0.1f, 0.1f, 0f),
                                 TextAlignmentOptions.Center, new Vector2(0f, 30f), new Vector2(1000f, 160f));
        title.characterSpacing = 4f;
        hint = HudStyle.NewText(transform, "Hint", "R - 다시 시작", HudStyle.Regular, 28f, new Color(1f, 1f, 1f, 0f),
                                TextAlignmentOptions.Center, new Vector2(0f, -80f), new Vector2(400f, 40f));
    }

    IEnumerator Sequence()
    {
        yield return new WaitForSecondsRealtime(showDelay);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / fadeDuration)
        {
            black.color = new Color(0f, 0f, 0f, t);
            yield return null;
        }
        black.color = Color.black;

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / textFade)
        {
            SetAlpha(title, t); SetAlpha(hint, t * 0.8f);
            yield return null;
        }
        SetAlpha(title, 1f); SetAlpha(hint, 0.8f);
        canRestart = true;
    }

    static void SetAlpha(TextMeshProUGUI t, float a) { var c = t.color; c.a = a; t.color = c; }

    bool rWasDown = true; // 사망 순간 이미 R(재장전)을 누르고 있었으면 한 번 떼야 재시작

    void Update()
    {
        if (Keyboard.current == null) return;
        // wasPressedThisFrame은 게임이 멈춘(timeScale 0) 동안 한 프레임 신호를 놓칠 때가 있어서 눌림 상태 변화로 직접 판단
        bool down = Keyboard.current.rKey.isPressed;
        bool pressed = down && !rWasDown;
        rWasDown = down;
        if (canRestart && pressed) Restart();
    }

    /// <summary>재시작: 시간 정상화 + static 값 초기화 + 씬 다시 로드</summary>
    public void Restart()
    {
        canRestart = false;
        Time.timeScale = 1f;
        BuildingInterior.ResetStatics(); // 실내 안개/숨김 카운터 등 static 값이 다음 판으로 넘어가지 않게
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}

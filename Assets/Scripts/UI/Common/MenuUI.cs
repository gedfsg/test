using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 메뉴 UI 공용 (메인 메뉴 / 일시정지 / 생존 종료 / 설정 / 조작 방법).
///  - 씬 이동: StartGame / RestartGame / LoadMainMenu / Quit (전부 timeScale을 1로 되돌린 뒤 이동)
///  - 게임 씬 자동 배치: 게임 씬이 뜨면 MenuUIPrefabs(Resources/UI)의 일시정지·생존 종료 프리팹을
///    전용 캔버스에 비활성 상태로 꺼내 둠 + GameStats 생성. main.unity(바이너리·LFS)는 건드리지 않음
///  - 배경 흐림: 현재 화면을 찍어 작게 줄인 뒤 늘려서 보여줌 (일시정지·생존 종료 뒤 배경)
/// </summary>
public static class MenuUI
{
    public const string MainMenuScene = "MainMenu";
    public const string GameScene = "main"; // Assets/Main Project/main.unity (Build 1번)

    /// <summary>일시정지 메뉴가 열려 있음</summary>
    public static bool PauseOpen;
    /// <summary>생존 종료 화면이 떠 있음</summary>
    public static bool DeathShown;
    /// <summary>게임 조작을 막는 메뉴가 떠 있음 (커서를 화살표로 - CrosshairUI)</summary>
    public static bool BlockingOpen => PauseOpen || DeathShown;
    /// <summary>인벤토리 창이 열려 있음 (InventoryUI가 알려줌)</summary>
    public static bool InventoryOpen;
    /// <summary>플레이어 조작(이동·사격·핫바·손전등·카메라)을 막아야 하는지: 메뉴 또는 인벤토리가 열림</summary>
    public static bool GameplayBlocked => BlockingOpen || InventoryOpen;

    // ── 씬 이동 ─────────────────────────────────────
    public static void StartGame() => Load(GameScene);
    public static void RestartGame() => Load(SceneManager.GetActiveScene().name);
    public static void LoadMainMenu() => Load(MainMenuScene);

    static void Load(string scene)
    {
        Time.timeScale = 1f;
        PauseOpen = DeathShown = InventoryOpen = false;
        SceneManager.LoadScene(scene);
    }

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── 게임 씬 자동 배치 ─────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PauseOpen = DeathShown = InventoryOpen = false;
        if (!WorldDecor.IsGameScene) return; // 학습장(더미 Player 태그)·메인 메뉴 제외
        var player = GameObject.FindWithTag("Player");
        if (player == null || player.GetComponent<Health>() == null) return;

        if (Object.FindFirstObjectByType<GameStats>() == null) new GameObject("GameStats").AddComponent<GameStats>();

        var prefabs = MenuUIPrefabs.Load();
        if (prefabs == null || Object.FindFirstObjectByType<PauseMenuController>() != null) return;

        var canvas = CreateOverlayCanvas("GameMenuCanvas", 1100); // 다른 HUD·옛 DeathUI(1000) 위
        if (prefabs.pauseMenu != null) Object.Instantiate(prefabs.pauseMenu, canvas.transform, false).name = "PauseMenu";
        if (prefabs.deathScreen != null) Object.Instantiate(prefabs.deathScreen, canvas.transform, false).name = "DeathScreen";
    }

    /// <summary>1920×1080 기준 Overlay 캔버스 (레퍼런스 픽셀 좌표 그대로 배치)</summary>
    public static Canvas CreateOverlayCanvas(string name, int order)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    // ── 게임 조작 막기 (일시정지·사망 중 클릭이 사격으로 새지 않게) ──
    // 상태 플래그(PauseOpen/DeathShown/InventoryOpen)로 매번 다시 계산 → 인벤토리를 연 채 일시정지했다 닫아도 조작이 잘못 켜지지 않음

    /// <summary>
    /// 지금 상태에 맞게 조작을 켜고 끔.
    ///  - 플레이어(이동·사격·재장전·달리기·구르기·상호작용·조준 회전)·카메라(휠/우클릭 줌·Q/E): 메뉴나 인벤토리가 열리면 끔
    ///  - 인벤토리 Tab: 메뉴(일시정지·사망)가 열렸을 때만 끔 (인벤토리를 열어둔 채로는 Tab으로 닫을 수 있어야 함)
    /// </summary>
    public static void RefreshGameplayInput()
    {
        bool play = !GameplayBlocked;
        var player = GameObject.FindWithTag("Player");
        var pc = player != null ? player.GetComponent<PlayerController>() : null;
        if (pc != null) pc.SetInputEnabled(play);
        var cam = Camera.main;
        if (cam != null)
        {
            var zoom = cam.GetComponent<CameraZoom>();
            if (zoom != null) zoom.enabled = play;
            var rot = cam.GetComponent<CameraRotate>();
            if (rot != null) rot.enabled = play;
        }
        foreach (var inv in Object.FindObjectsByType<InventoryUI>(FindObjectsSortMode.None)) inv.enabled = !BlockingOpen; // Tab
    }

    // ── 배경 흐림 ───────────────────────────────────
    /// <summary>
    /// 이번 프레임 화면을 찍어서 흐린 텍스처로 target에 넣음.
    /// 반드시 메뉴를 띄우기 전에 호출 (메뉴가 같이 찍히지 않게). 이전 텍스처는 정리
    /// (이전) GPU Blit으로 1/12까지 줄였다 늘림 → 네모난 블록이 그대로 보여서 CPU 평균 + 박스 블러로 교체
    /// </summary>
    public static IEnumerator CaptureBlur(RawImage target)
    {
        yield return new WaitForEndOfFrame();
        if (target == null) yield break;
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        const int F = 8; // 1/8 크기 (1920×1080 → 240×135)
        int w = Mathf.Max(8, shot.width / F), h = Mathf.Max(8, shot.height / F);
        var src = shot.GetPixels32();
        var buf = new Color[w * h];
        // 1) F×F 칸 평균으로 줄이기
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float r = 0f, g = 0f, b = 0f; int n = 0;
                for (int sy = y * F; sy < Mathf.Min(shot.height, y * F + F); sy += 2)
                    for (int sx = x * F; sx < Mathf.Min(shot.width, x * F + F); sx += 2)
                    {
                        var c = src[sy * shot.width + sx];
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                buf[y * w + x] = new Color(r / (255f * n), g / (255f * n), b / (255f * n), 1f);
            }
        Object.Destroy(shot);
        // 2) 가로·세로 박스 블러 2번 (반경 2) → 가우시안 비슷하게 부드러움
        var tmp = new Color[buf.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            BoxBlur(buf, tmp, w, h, 2, true);  // 레퍼런스처럼 HUD 윤곽이 흐릿하게 남을 정도
            BoxBlur(tmp, buf, w, h, 2, false);
        }
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false) { name = "MenuBlur", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(buf);
        tex.Apply();
        if (target.texture != null && target.texture.name == "MenuBlur") Object.Destroy(target.texture);
        target.texture = tex;
        target.color = Color.white;
    }

    static void BoxBlur(Color[] from, Color[] to, int w, int h, int r, bool horizontal)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color sum = Color.clear; int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int xx = horizontal ? Mathf.Clamp(x + k, 0, w - 1) : x;
                    int yy = horizontal ? y : Mathf.Clamp(y + k, 0, h - 1);
                    sum += from[yy * w + xx]; n++;
                }
                to[y * w + x] = sum / n;
            }
    }
}

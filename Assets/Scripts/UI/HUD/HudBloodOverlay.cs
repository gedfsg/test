using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// (2026-11-22) 아포칼립스 HUD 리디자인 - 화면 가장자리 피 튀김 + 미니맵 테두리 (Assets/UI/변경할 ui.png).
///
/// 레이어 순서 (아래 → 위, 사용자 결정 2026-11-22):
///   1. HUD 요소 (Canvas order 0, MissionHUD order 5)
///   2. 상시 피 튀김 vignette  ← 이 캔버스(order 50)의 첫 자식
///   3. 피격 펄스 (BloodVignette) ← 같은 캔버스 마지막 자식 (BloodVignette가 EnsureCanvas()로 붙음)
/// 구분: 상시 = 어두운 적갈색 #4A0A0A α0.4 정적 (말라붙은 피), 피격 = 선명한 빨강 #D01010 깜빡임·흔들림.
/// 피 튀김 텍스처는 코드로 생성 (가장자리 얼룩 + 방울 + 위에서 흐른 자국, 고정 시드라 매번 같은 모양).
/// 게임 씬(StatusPanelHUD가 있는 씬)이 로드되면 자동 생성. 메뉴(order 1100)보다는 아래.
/// 미니맵: 굵은 금속 링(MetalRing) → 얇은 어두운 빨강 링.
/// (2026-11-22 2차) 레퍼런스 "똑같이" 재작업:
///  - 피 튀김: 실제 스플래터 질감 (HudGrunge.BloodVignette - 큰 덩어리·방울·미세 점·흘러내림, 좌·하단·우상단 진하게, 색 농담 #2A0504~#C0101A)
///    Resources/HUD/Blood/blood_vignette.png(CC0 이미지)가 있으면 그걸 씀. 1차의 #4A0A0A α0.4 단색은 BuildSplatterSprite로 남김
///  - 미니맵 링: 흰 페인트가 벗겨진 어두운 빨강 링 (HudGrunge.PeeledRing), N 배지 검정 원
/// </summary>
public class HudBloodOverlay : MonoBehaviour
{
    public const int SortingOrder = 50;
    // (2026-11-22 2차) 레퍼런스처럼 상시 피 튀김은 HUD 패널 "아래" (패널이 피 위에 놓임). 피격 펄스는 그대로 맨 위(SortingOrder)
    public const int StaticSortingOrder = -10;
    static Canvas staticCanvas;
    public static readonly Color StaticColor = new Color(0x4A / 255f, 0x0A / 255f, 0x0A / 255f, 0.4f);
    static readonly Color MinimapRing = new Color(0.42f, 0.06f, 0.06f, 0.95f);

    static Canvas canvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (Object.FindFirstObjectByType<StatusPanelHUD>() == null) return; // 게임 씬만
        if (Object.FindFirstObjectByType<HudBloodOverlay>() != null) return;
        new GameObject("HudBloodOverlay").AddComponent<HudBloodOverlay>();
    }

    /// <summary>피 레이어 캔버스 (없으면 만듦). BloodVignette(피격 펄스)도 여기에 붙음</summary>
    public static Transform EnsureCanvas()
    {
        if (canvas != null) return canvas.transform;
        var go = new GameObject("HudBloodCanvas", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1681f, 936f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas.transform;
    }

    /// <summary>(2026-11-22 2차) 상시 피 튀김 전용 캔버스 - 메인 HUD(order 0)보다 아래</summary>
    static Transform EnsureStaticCanvas()
    {
        if (staticCanvas != null) return staticCanvas.transform;
        var go = new GameObject("HudBloodStaticCanvas", typeof(RectTransform));
        staticCanvas = go.AddComponent<Canvas>();
        staticCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        staticCanvas.sortingOrder = StaticSortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1681f, 936f);
        scaler.matchWidthOrHeight = 0.5f;
        return staticCanvas.transform;
    }

    void Start()
    {
        // ── 상시 피 튀김 (맨 아래) ──
        // var root = EnsureCanvas(); // (1차) 피격 펄스와 같은 캔버스(HUD 위)
        var root = EnsureStaticCanvas(); // (2차) HUD 아래 캔버스
        var go = new GameObject("StaticBloodSplatter", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(root, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        // img.sprite = BuildSplatterSprite(); // (2026-11-22 1차) 단색 얼룩
        // img.color = StaticColor;
        img.sprite = HudGrunge.BloodVignette();  // (2차) 색까지 구운 스플래터
        // img.color = new Color(1f, 1f, 1f, 0.88f); // (2026-11-22 2차)
        img.color = new Color(1f, 1f, 1f, 0.6f);         // (2026-11-22) 피드백 "테두리 피 조금 더 안 보이게"
        img.raycastTarget = false;
        rt.SetAsFirstSibling(); // 피격 펄스보다 아래

        RestyleMinimap();
    }

    static void RestyleMinimap()
    {
        var panel = GameObject.Find("MinimapPanel");
        if (panel == null) return;
        // (2026-11-22 2차) 레퍼런스: 지도 화면이 어둡고 탁한 톤 → 곱하기 틴트
        foreach (var raw in panel.GetComponentsInChildren<RawImage>(true))
            if (raw.name == "MinimapRawImage") raw.color = new Color(0.66f, 0.64f, 0.58f, 1f);
        foreach (var im in panel.GetComponentsInChildren<Image>(true))
        {
            if (im.name == "OuterRing")
            {
                // (이전) MetalRing 188px 굵은 금속 링 → (1차) 얇은 어두운 빨강 링 → (2차) 페인트 벗겨진 어두운 빨강 링
                // im.sprite = HudStyle.Ring != null ? HudStyle.Ring : im.sprite; im.color = MinimapRing; im.rectTransform.sizeDelta = new Vector2(180f, 180f);
                im.sprite = HudGrunge.PeeledRing();
                im.color = Color.white;
                im.rectTransform.sizeDelta = new Vector2(186f, 186f);
            }
            else if (im.name == "MinimapRawImage") { } // RawImage라 여기 안 옴 (아래에서 처리)
            else if (im.name == "NorthBadge") { im.sprite = HudGrunge.RoughCircle(); im.color = new Color32(0x2A, 0x06, 0x08, 0xFF); }
            else if (im.name == "Fill" && im.transform.parent != null && im.transform.parent.name == "NorthBadge") im.color = new Color32(0x0C, 0x09, 0x08, 0xFF);
        }
    }

    // 가장자리 피 튀김 알파 텍스처 (색은 Image.color)
    static Sprite BuildSplatterSprite()
    {
        const int W = 512, H = 288;
        var a = new float[W * H];
        var oldState = Random.state;
        Random.InitState(20261122);

        // 1) 가장자리 띠: 노이즈로 들쭉날쭉한 안쪽 경계
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u) * (W / (float)H), Mathf.Min(v, 1f - v)); // 가장 가까운 가장자리까지 (세로 기준)
                float n = Mathf.PerlinNoise(u * 9f, v * 9f) * 0.6f + Mathf.PerlinNoise(u * 31f + 7f, v * 31f + 3f) * 0.4f;
                float reach = 0.05f + 0.09f * n;
                a[y * W + x] = Mathf.Clamp01((reach - edge) / 0.035f) * (0.65f + 0.35f * n);
            }

        // 2) 가장자리 근처 큰 얼룩 + 주변 방울
        for (int i = 0; i < 70; i++)
        {
            Vector2 c = EdgePoint(0.10f);
            float r = Random.Range(6f, 22f);
            Blob(a, W, H, c, r, Random.Range(0.6f, 1f));
            int drops = Random.Range(3, 9);
            for (int d = 0; d < drops; d++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                Blob(a, W, H, c + dir * Random.Range(r, r * 3.2f), Random.Range(1.2f, 4.5f), Random.Range(0.5f, 1f));
            }
        }
        // 3) 위에서 흘러내린 자국
        for (int i = 0; i < 14; i++)
        {
            float x = Random.Range(0f, W), len = Random.Range(14f, 48f), w = Random.Range(1.5f, 3.5f);
            for (float t = 0; t < len; t += 0.5f) Blob(a, W, H, new Vector2(x + Mathf.Sin(t * 0.15f) * 1.2f, H - 1 - t), w * (1f - t / len * 0.6f), 0.85f);
        }
        Random.state = oldState;

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i]) * 255f));
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
    }

    // 네 가장자리 중 한 곳의 안쪽 margin 이내 임의 점 (텍스처 픽셀 좌표)
    static Vector2 EdgePoint(float margin)
    {
        const int W = 512, H = 288;
        int side = Random.Range(0, 4);
        float m = Random.Range(0f, margin);
        return side switch
        {
            0 => new Vector2(Random.Range(0f, W), m * H),
            1 => new Vector2(Random.Range(0f, W), H - m * H),
            2 => new Vector2(m * H, Random.Range(0f, H)),
            _ => new Vector2(W - m * H, Random.Range(0f, H)),
        };
    }

    static void Blob(float[] a, int W, int H, Vector2 c, float r, float strength)
    {
        int x0 = Mathf.Max(0, (int)(c.x - r - 1)), x1 = Mathf.Min(W - 1, (int)(c.x + r + 1));
        int y0 = Mathf.Max(0, (int)(c.y - r - 1)), y1 = Mathf.Min(H - 1, (int)(c.y + r + 1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / r;
                if (d >= 1f) continue;
                float v = Mathf.Clamp01((1f - d) / 0.25f) * strength; // 가장자리만 살짝 부드럽게
                int k = y * W + x;
                if (v > a[k]) a[k] = v;
            }
    }
}

using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 메뉴 UI 생성기 (Tools → UI → Build Menu UI). 재실행해도 안전 (전부 덮어씀).
///  1) 스프라이트 (모서리 깎은 패널/테두리, 아이콘, 마우스 그림, 그런지 질감) → Assets/Textures/UI/Menu
///  2) 프리팹 → Assets/Prefabs/UI : SettingsPanel / ControlsPanel / PauseMenu(설정·조작 방법 중첩) / DeathScreen
///  3) 프리팹 목록 → Assets/Resources/UI/MenuUIPrefabs.asset (게임 씬이 일시정지·생존 종료를 꺼내 씀)
///  4) 메인 메뉴 씬 → Assets/Scenes/MainMenu.unity + Build Settings (MainMenu 0번, main 1번, 학습 씬은 목록에 두고 끔)
/// 좌표는 전부 레퍼런스 이미지(1920×1080, Assets/Textures/UI/References) 픽셀 기준 - 왼쪽 위 원점.
/// 지금 열린 씬(main.unity)은 건드리지 않음 (임시 씬을 추가로 열어서 만들고 닫음)
/// </summary>
public static class MenuUIBuilder
{
    const string SpriteDir = "Assets/Textures/UI/Menu";
    const string PrefabDir = "Assets/Prefabs/UI";
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameScenePath = "Assets/Main Project/main.unity";
    const string RegistryPath = "Assets/Resources/UI/MenuUIPrefabs.asset";
    const string HudSprites = "Assets/Resources/HUD/Sprites/";

    // ── 색 (디자인 시스템) ───────────────────────────
    static readonly Color Orange = Hex("FFA500");
    static readonly Color OrangeLight = Hex("FFB347");
    static readonly Color Navy = Hex("0A1220");
    static readonly Color White = Color.white;
    static readonly Color Gray = Hex("9CA3AF");
    static readonly Color TextSoft = Hex("D1D5DB");
    static readonly Color PanelBg = Hex("12161DEB");
    static readonly Color PanelFrame = Hex("3B4049");
    static readonly Color Divider = Hex("2C3038");
    static readonly Color Track = Hex("3A3F48");
    static readonly Color FieldBg = Hex("181C23");

    static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString("#" + h, out Color c);
        return c;
    }

    // ── 에셋 ───────────────────────────────────────
    static TMP_FontAsset fRegular, fSemi, fBold, fTitle;
    static Material titleMat;
    static Sprite sPanel, sFrame, sBtnOrange, sRound, sRoundFrame, sCircle, sDiamond, sLeftShade, sHatch, sMouse;
    static Sprite iArrowR, iArrowL, iChevron, iCheck, iPlay, iGear, iKeyboard, iRestart, iHome, iExit, iSpeaker, iGamepad, iMonitor, iGlobe, iPulse, iCursor;

    [MenuItem("Tools/UI/Build Menu UI")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[MenuUIBuilder] Play 모드에서는 실행할 수 없어요"); return; }
        // MainMenu 씬이 열려 있으면 같은 경로로 새로 저장이 안 됨 → main 씬으로 바꿔 놓고 만듦
        var openMenu = SceneManager.GetSceneByPath(ScenePath);
        if (openMenu.isLoaded)
        {
            if (openMenu.isDirty && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { openMenu })) return;
            if (SceneManager.sceneCount == 1) EditorSceneManager.OpenScene(GameScenePath);
            else EditorSceneManager.CloseScene(openMenu, true);
        }
        BuildSprites();
        LoadAssets();
        var prefabs = BuildPrefabs();
        BuildMainMenuScene(prefabs);
        SetupBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[MenuUIBuilder] 메뉴 UI 생성 완료: 프리팹 4개 + MainMenu 씬 + Build Settings");
    }

    // ═════════════════════════════════════════════
    // 1) 스프라이트
    // ═════════════════════════════════════════════
    delegate float Cov(float x, float y);
    delegate Color ColorAt(float x, float y);

    [MenuItem("Tools/UI/Build Menu UI Sprites Only")]
    public static void BuildSprites()
    {
        Directory.CreateDirectory(SpriteDir);
        var b40 = new Vector4(40, 40, 40, 40);
        // 오른쪽 위 모서리만 45° 깎은 패널 (128px, 깎임 26px) - 채움 / 테두리(2.5px)
        WriteSprite("PanelTR", 128, 128, (x, y) => InChamferTR(x, y, 0f) ? 1f : 0f, b40);
        WriteSprite("FrameTR", 128, 128, (x, y) => InChamferTR(x, y, 0f) && !InChamferTR(x, y, 2.5f) ? 1f : 0f, b40);
        // 주황 버튼: 왼쪽 밝은 주황 → 오른쪽 진한 주황
        WriteColorSprite("BtnOrangeTR", 128, 128, (x, y) => InChamferTR(x, y, 0f)
            ? Color.Lerp(Hex("FFB650"), Hex("FF9F26"), x / 128f) : Color.clear, b40);
        // 왼쪽이 어둡고 오른쪽으로 사라지는 그늘 (메인 메뉴 왼쪽 / 설정 배경)
        WriteColorSprite("LeftShade", 512, 4, (x, y) => new Color(0.02f, 0.025f, 0.05f, Mathf.Pow(1f - x / 512f, 1.6f)), Vector4.zero);
        // 헤더 액센트 옆 빗금 "////"
        WriteSprite("Hatch", 64, 16, (x, y) =>
        {
            if (y < 2f || y > 14f) return 0f;
            for (int i = 0; i < 4; i++) if (Mathf.Abs((x - (8f + 16f * i)) - (y - 8f) * 0.7f) < 2.6f) return 1f;
            return 0f;
        }, Vector4.zero);

        // 아이콘 (흰색 64px → Image 색으로 틴트)
        WriteSprite("IconArrowRight", 64, 64, (x, y) => Stroke(x, y, 5f, 12, 32, 52, 32) || Stroke(x, y, 5f, 38, 46, 52, 32, 38, 18) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconArrowLeft", 64, 64, (x, y) => Stroke(64 - x, y, 5f, 12, 32, 52, 32) || Stroke(64 - x, y, 5f, 38, 46, 52, 32, 38, 18) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconChevron", 64, 64, (x, y) => Stroke(x, y, 5.5f, 16, 40, 32, 24, 48, 40) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconCheck", 64, 64, (x, y) => Stroke(x, y, 6f, 10, 33, 25, 18, 54, 47) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconPlay", 64, 64, (x, y) => InPoly(x, y, 18, 10, 18, 54, 52, 32) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconGear", 64, 64, GearCov, Vector4.zero);
        WriteSprite("IconKeyboard", 64, 64, KeyboardCov, Vector4.zero);
        WriteSprite("IconRestart", 64, 64, RestartCov, Vector4.zero);
        WriteSprite("IconHome", 64, 64, (x, y) => Stroke(x, y, 4.5f, 8, 30, 32, 52, 56, 30) || Stroke(x, y, 4.5f, 15, 34, 15, 10, 49, 10, 49, 34) || Stroke(x, y, 4f, 27, 10, 27, 23, 37, 23, 37, 10) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconExit", 64, 64, (x, y) => Stroke(x, y, 4.5f, 30, 52, 12, 52, 12, 12, 30, 12) || Stroke(x, y, 4.5f, 26, 32, 54, 32) || Stroke(x, y, 4.5f, 44, 42, 54, 32, 44, 22) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconSpeaker", 64, 64, SpeakerCov, Vector4.zero);
        WriteSprite("IconGamepad", 64, 64, GamepadCov, Vector4.zero);
        WriteSprite("IconMonitor", 64, 64, (x, y) => Stroke(x, y, 4f, 6, 22, 58, 22, 58, 54, 6, 54, 6, 22) || Stroke(x, y, 4f, 32, 22, 32, 12) || Stroke(x, y, 4f, 20, 10, 44, 10) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconGlobe", 64, 64, GlobeCov, Vector4.zero);
        WriteSprite("IconPulse", 64, 64, (x, y) => Stroke(x, y, 4.5f, 6, 32, 20, 32, 26, 44, 34, 18, 40, 32, 58, 32) ? 1f : 0f, Vector4.zero);
        WriteSprite("IconCursor", 64, 64, (x, y) => InPoly(x, y, 16, 58, 16, 12, 27, 23, 35, 6, 42, 9, 34, 26, 49, 27) ? 1f : 0f, Vector4.zero);

        WriteColorSprite("Mouse", 320, 560, MouseColor, Vector4.zero);
        WriteGrungeTexture();
        AssetDatabase.Refresh();
    }

    // 오른쪽 위 모서리를 c=26 깎은 사각형 (0~128), inset만큼 안쪽 (깎인 변도 수직 거리 inset만큼 안쪽)
    static bool InChamferTR(float x, float y, float inset)
    {
        const float S = 128f, C = 26f;
        if (x < inset || y < inset || x > S - inset || y > S - inset) return false;
        return x + y <= 2f * S - C - inset * 1.4142f;
    }

    static float GearCov(float x, float y)
    {
        float dx = x - 32f, dy = y - 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
        float th = Mathf.Atan2(dy, dx);
        bool tooth = Mathf.Cos(8f * th) > 0.3f;
        float r = tooth ? 24f : 18.5f;
        if (d > r) return 0f;
        if (d < 9f) return 0f;                 // 가운데 구멍
        if (d > 12.5f && d < 15f) return 0f;   // 안쪽 링 (윤곽선 느낌)
        return 1f;
    }

    static float KeyboardCov(float x, float y)
    {
        if (Stroke(x, y, 4f, 6, 16, 58, 16, 58, 48, 6, 48, 6, 16)) return 1f;
        for (int i = 0; i < 5; i++)
            for (int r = 0; r < 2; r++)
                if (Mathf.Abs(x - (16f + 8f * i)) < 2.5f && Mathf.Abs(y - (38f - 8f * r)) < 2.5f) return 1f;
        return Stroke(x, y, 4f, 20, 22, 44, 22) ? 1f : 0f;
    }

    static float RestartCov(float x, float y)
    {
        float dx = x - 32f, dy = y - 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // -180~180
        bool gap = ang > 70f && ang < 125f;
        if (!gap && d > 15f && d < 19.5f) return 1f;
        return InPoly(x, y, 12, 54, 30, 52, 20, 38) ? 1f : 0f; // 화살촉 (왼쪽 위)
    }

    static float SpeakerCov(float x, float y)
    {
        if (InPoly(x, y, 6, 24, 18, 24, 32, 12, 32, 52, 18, 40, 6, 40)) return 1f;
        float dx = x - 34f, dy = y - 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
        float ang = Mathf.Abs(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
        if (ang < 48f && ((d > 9f && d < 13f) || (d > 18f && d < 22f))) return 1f;
        return 0f;
    }

    static float GamepadCov(float x, float y)
    {
        float d = SegDist(x, y, 20, 30, 44, 30);
        if (d > 11.5f && d < 15.5f) return 1f;
        if (Stroke(x, y, 3.5f, 14, 30, 25, 30) || Stroke(x, y, 3.5f, 19.5f, 24.5f, 19.5f, 35.5f)) return 1f;
        if (Vector2.Distance(new Vector2(x, y), new Vector2(42, 33)) < 3f || Vector2.Distance(new Vector2(x, y), new Vector2(48, 27)) < 3f) return 1f;
        return 0f;
    }

    static float GlobeCov(float x, float y)
    {
        float dx = x - 32f, dy = y - 32f, d = Mathf.Sqrt(dx * dx + dy * dy);
        if (d > 21f && d < 25f) return 1f;
        if (d > 24f) return 0f;
        float e = Mathf.Sqrt((dx / 10f) * (dx / 10f) + (dy / 23f) * (dy / 23f));
        if (Mathf.Abs(e - 1f) * 10f < 2f) return 1f;
        if (Mathf.Abs(dy) < 1.8f) return 1f;
        if (Mathf.Abs(Mathf.Abs(dy) - 12f) < 1.5f) return 1f;
        return 0f;
    }

    // 레퍼런스 마우스 그림 (320×560 = UI 160×280의 2배): 몸통 + 좌클릭(빨강)·우클릭(초록) + 휠(주황)
    static Color MouseColor(float x, float y)
    {
        const float cx = 160f, r = 138f, top = 540f, bottom = 20f, split = 330f;
        float d = SegDist(x, y, cx, bottom + r, cx, top - r) - r; // 몸통 경계까지 (안쪽 음수)
        if (d > 0f) return Color.clear;
        Color outline = Hex("8C939C");
        if (d > -5f) return outline;

        // 휠
        bool inWheel = x > 145f && x < 175f && y > 378f && y < 472f;
        if (inWheel)
        {
            bool edge = x < 148f || x > 172f || y < 381f || y > 469f;
            if (edge) return Hex("E0A245");
            for (int i = 0; i < 5; i++) if (Mathf.Abs(y - (397f + 16f * i)) < 2.2f && x > 152f && x < 168f) return Hex("F0B04D");
            return Hex("1E2228");
        }

        if (y > split)
        {
            bool left = x < cx - 6f, right = x > cx + 6f;
            if (!left && !right) return Hex("1A1D23");
            if (Mathf.Abs(y - split) < 8f) return Hex("1A1D23");
            bool border = d > -10f || Mathf.Abs(y - (split + 8f)) < 2.5f || (left && x > cx - 8.5f) || (right && x < cx + 8.5f);
            // 연결점 (라벨 선이 닿는 곳)
            if (left && Vector2.Distance(new Vector2(x, y), new Vector2(100f, 400f)) < 7f) return Hex("F08088");
            if (right && Vector2.Distance(new Vector2(x, y), new Vector2(220f, 400f)) < 7f) return Hex("7FD29A");
            if (left) return border ? Hex("C0646A") : Hex("5A3236");
            return border ? Hex("5FAA74") : Hex("2E4A3B");
        }
        // 아래 몸통: 세로 가운데 선 + 안쪽 U자 선
        if (Mathf.Abs(x - cx) < 1.4f && y > 120f) return Hex("3C424B");
        if (Mathf.Abs(d + 34f) < 1.4f && y < split - 30f) return Hex("3C424B");
        return Color.Lerp(Hex("20242B"), Hex("2B3038"), (y - bottom) / (top - bottom));
    }

    // TRIGGER 타이틀 질감: 흰 바탕 + 투명한 긁힘/점 (TMP Face 텍스처로 곱해짐 → 글자에 금이 간 것처럼)
    static void WriteGrungeTexture()
    {
        const int W = 512, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        var rnd = new System.Random(7);
        for (int i = 0; i < px.Length; i++)
        {
            float n = 0.88f + (float)rnd.NextDouble() * 0.12f; // 미세한 얼룩
            px[i] = new Color(n, n * 0.985f, n * 0.96f, 1f);
        }
        void Hole(float fx, float fy, float rad, float alpha)
        {
            int x0 = Mathf.FloorToInt(fx - rad - 1), x1 = Mathf.CeilToInt(fx + rad + 1);
            int y0 = Mathf.FloorToInt(fy - rad - 1), y1 = Mathf.CeilToInt(fy + rad + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float dd = Mathf.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy));
                    if (dd > rad) continue;
                    var c = px[y * W + x];
                    c.a = Mathf.Min(c.a, alpha);
                    px[y * W + x] = c;
                }
        }
        // 긁힘: 주로 사선, 가늘게
        for (int s = 0; s < 38; s++)
        {
            float x = (float)rnd.NextDouble() * W, y = (float)rnd.NextDouble() * H;
            float ang = (float)(rnd.NextDouble() < 0.7 ? 0.5 + rnd.NextDouble() * 0.6 : rnd.NextDouble() * Mathf.PI);
            float len = 15f + (float)rnd.NextDouble() * 90f, w = 0.8f + (float)rnd.NextDouble() * 1.0f; // 글자가 커서(512px≈로고 폭) 1px대는 안 보임
            for (float t = 0; t < len; t += 0.5f)
            {
                float wob = Mathf.Sin(t * 0.15f + s) * 0.8f;
                Hole(x + Mathf.Cos(ang) * t + wob, y + Mathf.Sin(ang) * t, w * (1f - t / len * 0.6f), 0f);
            }
        }
        // 점 / 작은 이 빠짐
        for (int s = 0; s < 160; s++)
            Hole((float)rnd.NextDouble() * W, (float)rnd.NextDouble() * H, 0.7f + (float)rnd.NextDouble() * 1.4f, (float)rnd.NextDouble() < 0.5 ? 0f : 0.4f);
        tex.SetPixels(px);
        string path = $"{SpriteDir}/TitleGrunge.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // ── 그리기 헬퍼 ─────────────────────────────────
    static float SegDist(float x, float y, float ax, float ay, float bx, float by)
    {
        Vector2 p = new Vector2(x, y), a = new Vector2(ax, ay), b = new Vector2(bx, by);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
        return Vector2.Distance(p, a + (b - a) * t);
    }

    /// <summary>꺾은선(pts = x0,y0,x1,y1,...)에서 굵기 w 안인지 (끝은 둥글게)</summary>
    static bool Stroke(float x, float y, float w, params float[] pts)
    {
        for (int i = 0; i + 3 < pts.Length; i += 2)
            if (SegDist(x, y, pts[i], pts[i + 1], pts[i + 2], pts[i + 3]) <= w * 0.5f) return true;
        return false;
    }

    static bool InPoly(float x, float y, params float[] pts)
    {
        bool inside = false;
        int n = pts.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = pts[i * 2], yi = pts[i * 2 + 1], xj = pts[j * 2], yj = pts[j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    static void WriteSprite(string name, int w, int h, Cov cov, Vector4 border)
    {
        WriteColorSprite(name, w, h, (x, y) => new Color(1f, 1f, 1f, cov(x, y)), border);
    }

    // 4×4 슈퍼샘플 (프리멀티 평균 - 가장자리 색 번짐 방지)
    static void WriteColorSprite(string name, int w, int h, ColorAt colorAt, Vector4 border)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        Color c = colorAt(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                sum /= 16f;
                px[y * w + x] = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : new Color(1f, 1f, 1f, 0f);
            }
        tex.SetPixels(px);
        string path = $"{SpriteDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spriteBorder = border;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    static void LoadAssets()
    {
        fRegular = Resources.Load<TMP_FontAsset>("HUD/Fonts/Pretendard-Regular SDF");
        fSemi = Resources.Load<TMP_FontAsset>("HUD/Fonts/Pretendard-SemiBold SDF");
        fBold = Resources.Load<TMP_FontAsset>("HUD/Fonts/Pretendard-Bold SDF");
        fTitle = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Anton SDF.asset");
        if (fTitle == null) fTitle = fBold;

        S(ref sPanel, "PanelTR"); S(ref sFrame, "FrameTR"); S(ref sBtnOrange, "BtnOrangeTR"); S(ref sLeftShade, "LeftShade");
        S(ref sHatch, "Hatch"); S(ref sMouse, "Mouse");
        S(ref iArrowR, "IconArrowRight"); S(ref iArrowL, "IconArrowLeft"); S(ref iChevron, "IconChevron"); S(ref iCheck, "IconCheck");
        S(ref iPlay, "IconPlay"); S(ref iGear, "IconGear"); S(ref iKeyboard, "IconKeyboard"); S(ref iRestart, "IconRestart");
        S(ref iHome, "IconHome"); S(ref iExit, "IconExit"); S(ref iSpeaker, "IconSpeaker"); S(ref iGamepad, "IconGamepad");
        S(ref iMonitor, "IconMonitor"); S(ref iGlobe, "IconGlobe"); S(ref iPulse, "IconPulse"); S(ref iCursor, "IconCursor");
        sRound = AssetDatabase.LoadAssetAtPath<Sprite>(HudSprites + "RoundedRect.png");
        sRoundFrame = AssetDatabase.LoadAssetAtPath<Sprite>(HudSprites + "RoundedFrame.png");
        sCircle = AssetDatabase.LoadAssetAtPath<Sprite>(HudSprites + "Circle.png");
        sDiamond = AssetDatabase.LoadAssetAtPath<Sprite>(HudSprites + "Diamond.png");

        // TRIGGER 타이틀 머티리얼: Anton + 그런지 Face 텍스처 + 아래 그림자
        string matPath = $"{SpriteDir}/TriggerTitle.mat";
        titleMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (titleMat == null)
        {
            titleMat = new Material(fTitle.material);
            AssetDatabase.CreateAsset(titleMat, matPath);
        }
        titleMat.CopyPropertiesFromMaterial(fTitle.material);
        // titleMat.shader = fTitle.material.shader;  // (이전) Anton 기본은 Mobile 셰이더라 Face 텍스처가 없음 → 그런지가 안 보였음
        titleMat.shader = Shader.Find("TextMeshPro/Distance Field");
        titleMat.SetTexture("_FaceTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{SpriteDir}/TitleGrunge.png"));
        titleMat.EnableKeyword("UNDERLAY_ON");
        titleMat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.55f));
        titleMat.SetFloat("_UnderlayOffsetX", 0.6f);
        titleMat.SetFloat("_UnderlayOffsetY", -0.6f);
        titleMat.SetFloat("_UnderlaySoftness", 0.35f);
        EditorUtility.SetDirty(titleMat);
    }

    static void S(ref Sprite s, string name) => s = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{name}.png");

    // ═════════════════════════════════════════════
    // UI 헬퍼 (부모의 왼쪽 위 기준, y는 아래로 +)
    // ═════════════════════════════════════════════
    static RectTransform R(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static RectTransform Stretch(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>화면 가운데 1920×1080 기준 틀 (레퍼런스 좌표를 그대로 쓰기 위한 컨테이너)</summary>
    static RectTransform Frame1080(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1920f, 1080f);
        return rt;
    }

    static Image Img(Transform parent, string name, float x, float y, float w, float h, Color c, Sprite s = null, float ppm = 1f)
    {
        var img = R(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
        SetupImage(img, c, s, ppm);
        return img;
    }

    static Image StretchImg(Transform parent, string name, Color c, Sprite s = null, float ppm = 1f)
    {
        var img = Stretch(name, parent).gameObject.AddComponent<Image>();
        SetupImage(img, c, s, ppm);
        return img;
    }

    static void SetupImage(Image img, Color c, Sprite s, float ppm)
    {
        img.sprite = s;
        img.color = c;
        img.raycastTarget = false;
        if (s != null && s.border != Vector4.zero) { img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ppm; }
        else img.preserveAspect = false;
    }

    /// <summary>글자: (x, 세로 중심 cy) 기준. w는 정렬 상자 너비</summary>
    static TextMeshProUGUI Txt(Transform parent, string name, float x, float cy, float w, string text, float size, Color c,
        TMP_FontAsset font, TextAlignmentOptions align = TextAlignmentOptions.Left, float spacing = 0f)
    {
        float h = size * 1.6f;
        var t = R(name, parent, x, cy - h * 0.5f, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.characterSpacing = spacing;
        if (spacing > 0f) t.fontFeatures = new List<UnityEngine.TextCore.OTL_FeatureTag>(); // 자간을 벌린 영문 라벨: 커닝 끔 (GAM E P LAY처럼 벌어지는 것 방지)
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Image Line(Transform parent, float x, float y, float w, float h, Color c) => Img(parent, "Line", x, y, w, h, c);

    static void Breadcrumb(Transform parent, float x, float cy, float size, TextAlignmentOptions align, float w = 700f)
    {
        Txt(parent, "Breadcrumb", x, cy, w,
            $"<cspace=0.12em>TRIGGER</cspace>  <color=#FFA500><i>/</i></color>  <size=74%>일곱 번째 밤</size>", size, White, fBold, align);
    }

    static void TeamLabel(Transform parent, float x, float cy, bool line, TextAlignmentOptions align = TextAlignmentOptions.Left, float w = 400f, float size = 16f)
    {
        if (line) Line(parent, x, cy - 1f, 27f, 2f, Orange);
        Txt(parent, "TeamTrigger", line ? x + 40f : x, cy, w, "TEAM TRIGGER", size, Gray, fSemi, align, 25f);
    }

    /// <summary>메뉴 버튼: 배경(모서리 깎음) + 테두리 + 아이콘 + 글자 + 선택 시 → 화살표</summary>
    static UIButton Btn(Transform parent, string name, float x, float y, float w, float h, string text, float size,
        Sprite icon, float iconX, float iconSize, float textX, bool arrow, float arrowX, float arrowSize, string group, bool defaultSel, float ppm = 2.2f)
    {
        var rt = R(name, parent, x, y, w, h);
        var bg = rt.gameObject.AddComponent<Image>();
        SetupImage(bg, Hex("14161BE6"), sPanel, ppm);
        bg.raycastTarget = true;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        var frame = StretchImg(rt, "Frame", Hex("5A5D66"), sFrame, ppm);
        Image ic = null;
        if (icon != null) ic = Img(rt, "Icon", iconX, (h - iconSize) * 0.5f, iconSize, iconSize, White, icon);
        var label = Txt(rt, "Label", textX, h * 0.5f, w - textX - 10f, text, size, White, fBold);
        Image ar = null;
        if (arrow) ar = Img(rt, "Arrow", arrowX, (h - arrowSize) * 0.5f, arrowSize, arrowSize, White, iArrowR);
        var ub = rt.gameObject.AddComponent<UIButton>();
        ub.bg = bg; ub.frame = frame; ub.label = label; ub.icon = ic; ub.arrow = ar;
        ub.group = group; ub.defaultSelected = defaultSel;
        ub.selectedBgSprite = sBtnOrange;
        ub.selectedFrame = new Color(0f, 0f, 0f, 0f);
        return ub;
    }

    static UIButton BackButton(Transform parent, float x, float y)
    {
        var b = Btn(parent, "BackButton", x, y, 152f, 53f, "뒤로", 24f, iArrowL, 24f, 22f, 58f, false, 0f, 0f, "", false, 2f);
        b.normalBg = Hex("12151BE6");
        b.normalFrame = Hex("E89A2C");
        b.hoverFrame = OrangeLight;
        b.normalIcon = White;
        b.clickSound = UIAudio.Sound.Back;
        return b;
    }

    /// <summary>설정/조작 카드: 배경 + 테두리 + 왼쪽 위 주황 액센트·빗금 + 헤더(아이콘·제목·영문) + 구분선</summary>
    static RectTransform Card(Transform parent, string name, float x, float y, float w, float h, string title, string english, Sprite icon)
    {
        var rt = R(name, parent, x, y, w, h);
        StretchImg(rt, "Bg", PanelBg, sPanel, 1.6f);
        StretchImg(rt, "Frame", PanelFrame, sFrame, 1.6f);
        Img(rt, "Accent", 1f, -1f, 56f, 4f, Orange);
        Img(rt, "Hatch", 60f, -1f, 34f, 8f, new Color(Orange.r, Orange.g, Orange.b, 0.55f), sHatch);
        float tx = 30f;
        if (icon != null) { Img(rt, "Icon", 27f, 22f, 26f, 26f, OrangeLight, icon); tx = 66f; }
        Txt(rt, "Title", tx, 35f, 400f, title, 28f, White, fBold);
        Txt(rt, "English", w - 424f, 38f, 400f, english, 13f, Gray, fSemi, TextAlignmentOptions.Right, 30f);
        Line(rt, 24f, 62f, w - 48f, 1f, Hex("343943"));
        return rt;
    }

    static RectTransform FullBackdrop(Transform parent)
    {
        var bg = StretchImg(parent, "Backdrop", Hex("0A1220E0"));
        bg.raycastTarget = true; // 뒤 메뉴 클릭 막기
        var shade = Stretch("LeftShade", parent);
        var img = shade.gameObject.AddComponent<Image>();
        SetupImage(img, new Color(1f, 1f, 1f, 0.6f), sLeftShade, 1f);
        shade.anchorMax = new Vector2(0.75f, 1f);
        return shade;
    }

    /// <summary>설정·조작 공통 상단: 경로 + 큰 제목 + 주황 밑줄 + 회색 선 + 뒤로 버튼</summary>
    static UIButton PageHeader(Transform root, string title, float rightEdge, float backX)
    {
        Breadcrumb(root, 107f, 96f, 28f, TextAlignmentOptions.Left);
        Txt(root, "PageTitle", 103f, 156f, 700f, title, 66f, White, fBold);
        Line(root, 106f, 204f, 114f, 3f, Orange);
        Line(root, 220f, 205f, rightEdge - 220f, 1f, Hex("3A3F48"));
        return BackButton(root, backX, 127f);
    }

    // ── 설정 위젯 ───────────────────────────────────
    static SettingsPanel.SliderRow SliderRow(Transform card, float x, float cy, float w, float boxX, float boxW)
    {
        var rt = R("Slider", card, x, cy - 11f, w, 22f);
        var slider = rt.gameObject.AddComponent<Slider>();
        Img(rt, "Track", 0f, 9.5f, w, 3f, Track);
        var fillArea = Stretch("Fill Area", rt);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(0f, 3f);
        var fill = Stretch("Fill", fillArea).gameObject.AddComponent<Image>();
        SetupImage(fill, Hex("F5A53A"), null, 1f);
        var handleArea = Stretch("Handle Slide Area", rt);
        var handle = Stretch("Handle", handleArea);
        handle.sizeDelta = new Vector2(22f, 0f);
        var hImg = handle.gameObject.AddComponent<Image>();
        SetupImage(hImg, Hex("3A2A12"), sCircle, 1f);
        hImg.raycastTarget = true;
        var inner = Stretch("Knob", handle);
        inner.offsetMin = new Vector2(3f, 3f);
        inner.offsetMax = new Vector2(-3f, -3f);
        SetupImage(inner.gameObject.AddComponent<Image>(), OrangeLight, sCircle, 1f);
        // 트랙 아무 데나 눌러도 잡히게 (투명 히트 영역)
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        slider.targetGraphic = hImg;
        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;

        var box = R("ValueBox", card, boxX, cy - 16f, boxW, 32f);
        SetupImage(box.gameObject.AddComponent<Image>(), Hex("13161C"), sRound, 4f);
        StretchImg(box, "Frame", Track, sRoundFrame, 4f);
        var value = Txt(box, "Value", 0f, 16f, boxW, "0", 18f, White, fSemi, TextAlignmentOptions.Center);
        return new SettingsPanel.SliderRow { slider = slider, value = value };
    }

    static Toggle Switch(Transform card, float x, float cy, float labelX)
    {
        var rt = R("Switch", card, x, cy - 14f, 54f, 28f);
        var track = rt.gameObject.AddComponent<Image>();
        SetupImage(track, Hex("6E5526"), sRound, 1.15f);
        track.raycastTarget = true;
        var toggle = rt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = track;
        var knobRt = (RectTransform)new GameObject("Knob", typeof(RectTransform)) { layer = 5 }.transform;
        knobRt.SetParent(rt, false);
        knobRt.anchorMin = knobRt.anchorMax = knobRt.pivot = new Vector2(0.5f, 0.5f);
        knobRt.sizeDelta = new Vector2(20f, 20f);
        var knob = knobRt.gameObject.AddComponent<Image>();
        SetupImage(knob, OrangeLight, sCircle, 1f);
        var label = Txt(card, "SwitchLabel", labelX, cy, 80f, "ON", 18f, OrangeLight, fBold);
        var sw = rt.gameObject.AddComponent<UIToggleSwitch>();
        sw.track = track; sw.knob = knobRt; sw.knobImage = knob; sw.stateLabel = label; sw.knobTravel = 13f;
        return toggle;
    }

    static TMP_Dropdown Dropdown(Transform card, float x, float cy, float w)
    {
        var res = new TMP_DefaultControls.Resources { standard = sPanel, background = sPanel, dropdown = iChevron, checkmark = iCheck, mask = sRound, inputField = sPanel };
        var go = TMP_DefaultControls.CreateDropdown(res);
        go.name = "Dropdown";
        SetLayerRecursive(go, 5);
        var rt = (RectTransform)go.transform;
        rt.SetParent(card, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -(cy - 18f));
        rt.sizeDelta = new Vector2(w, 36f);
        var dd = go.GetComponent<TMP_Dropdown>();

        var bg = go.GetComponent<Image>();
        SetupImage(bg, FieldBg, sPanel, 2.4f);
        bg.raycastTarget = true;
        dd.transition = Selectable.Transition.ColorTint;
        var cb = dd.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f); cb.pressedColor = new Color(0.9f, 0.9f, 0.9f); cb.selectedColor = Color.white; dd.colors = cb;
        var frame = StretchImg(rt, "Frame", Track, sFrame, 2.4f);
        frame.transform.SetSiblingIndex(0);

        var label = dd.captionText as TextMeshProUGUI;
        if (label != null)
        {
            label.font = fSemi; label.fontSize = 18f; label.color = White;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var lr = label.rectTransform; lr.offsetMin = new Vector2(14f, 0f); lr.offsetMax = new Vector2(-36f, 0f);
        }
        var arrow = rt.Find("Arrow") as RectTransform;
        if (arrow != null)
        {
            var ai = arrow.GetComponent<Image>(); ai.sprite = iChevron; ai.color = Hex("C9CDD4");
            arrow.anchorMin = arrow.anchorMax = new Vector2(1f, 0.5f);
            arrow.sizeDelta = new Vector2(16f, 16f);
            arrow.anchoredPosition = new Vector2(-18f, 0f);
        }
        // 펼친 목록
        var template = dd.template;
        var tImg = template.GetComponent<Image>();
        SetupImage(tImg, Hex("1A1E25FA"), sRound, 4f);
        tImg.raycastTarget = true;
        var tFrame = StretchImg(template, "Frame", Hex("FFA50099"), sRoundFrame, 4f);
        tFrame.transform.SetAsLastSibling();
        template.sizeDelta = new Vector2(0f, 180f);
        var item = template.GetComponentInChildren<Toggle>(true);
        if (item != null)
        {
            var ir = (RectTransform)item.transform; ir.sizeDelta = new Vector2(0f, 32f);
            var ibg = item.targetGraphic as Image;
            if (ibg != null) { ibg.sprite = null; ibg.color = Color.white; }
            var ic = item.colors; ic.normalColor = new Color(1f, 1f, 1f, 0f); ic.highlightedColor = new Color(1f, 0.65f, 0f, 0.25f); ic.selectedColor = new Color(1f, 0.65f, 0f, 0.12f); ic.pressedColor = new Color(1f, 0.65f, 0f, 0.35f); item.colors = ic;
            if (item.graphic is Image check) { check.sprite = iCheck; check.color = OrangeLight; }
            var itemLabel = dd.itemText as TextMeshProUGUI;
            if (itemLabel != null) { itemLabel.font = fSemi; itemLabel.fontSize = 17f; itemLabel.color = Hex("E5E7EB"); }
        }
        var content = template.Find("Viewport/Content") as RectTransform;
        if (content != null) content.sizeDelta = new Vector2(0f, 34f);
        var scrollbar = template.GetComponentInChildren<Scrollbar>(true);
        if (scrollbar != null)
        {
            var sbImg = scrollbar.GetComponent<Image>(); if (sbImg != null) { sbImg.sprite = null; sbImg.color = Hex("22262E"); }
            if (scrollbar.handleRect != null) { var hi = scrollbar.handleRect.GetComponent<Image>(); hi.sprite = sRound; hi.type = Image.Type.Sliced; hi.pixelsPerUnitMultiplier = 6f; hi.color = Hex("5A5D66"); }
            ((RectTransform)scrollbar.transform).sizeDelta = new Vector2(8f, 0f);
        }
        return dd;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    static void RowLabel(Transform card, float cy, string text) => Txt(card, "Label", 27f, cy, 300f, text, 22f, Hex("E8EAED"), fSemi);

    static void RowSeparators(Transform card, float w, params float[] ys)
    {
        foreach (var y in ys) Line(card, 24f, y, w - 48f, 1f, Divider);
    }

    // ═════════════════════════════════════════════
    // 2) 프리팹
    // ═════════════════════════════════════════════
    class Built { public GameObject settings, controls, pause, death; }

    static Built BuildPrefabs()
    {
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath));
        var prev = SceneManager.GetActiveScene();
        var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(temp);
        var built = new Built();
        try
        {
            built.settings = Save(BuildSettingsPanel(), "SettingsPanel");
            built.controls = Save(BuildControlsPanel(), "ControlsPanel");
            built.pause = Save(BuildPauseMenu(built), "PauseMenu");
            built.death = Save(BuildDeathScreen(), "DeathScreen");
        }
        finally
        {
            SceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(temp, true);
        }

        var reg = AssetDatabase.LoadAssetAtPath<MenuUIPrefabs>(RegistryPath);
        if (reg == null) { reg = ScriptableObject.CreateInstance<MenuUIPrefabs>(); AssetDatabase.CreateAsset(reg, RegistryPath); }
        reg.settingsPanel = built.settings; reg.controlsPanel = built.controls; reg.pauseMenu = built.pause; reg.deathScreen = built.death;
        EditorUtility.SetDirty(reg);
        return built;
    }

    static GameObject Save(GameObject root, string name)
    {
        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject NewRoot(string name)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    static void StretchInstance(GameObject inst, Transform parent)
    {
        var rt = (RectTransform)inst.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ── 설정 (Trigger_Settings_UI.png) ───────────────
    static GameObject BuildSettingsPanel()
    {
        var root = NewRoot("SettingsPanel");
        FullBackdrop(root.transform);
        var f = Frame1080("Content", root.transform);
        var p = root.AddComponent<SettingsPanel>();
        p.backButton = PageHeader(f, "설정", 1615f, 1463f).GetComponent<Button>();

        // 사운드
        var snd = Card(f, "Sound", 106f, 234f, 745f, 308f, "사운드", "SOUND", iSpeaker);
        float[] sy = { 87f, 133f, 179f, 226f, 272f };
        string[] sl = { "마스터 볼륨", "BGM 볼륨", "효과음 볼륨", "좀비 사운드", "UI 사운드" };
        var rows = new SettingsPanel.SliderRow[5];
        for (int i = 0; i < 5; i++) { RowLabel(snd, sy[i], sl[i]); rows[i] = SliderRow(snd, 370f, sy[i], 272f, 661f, 58f); }
        RowSeparators(snd, 745f, 110f, 156f, 202f, 249f);
        p.master = rows[0]; p.bgm = rows[1]; p.sfx = rows[2]; p.zombie = rows[3]; p.ui = rows[4];

        // 게임플레이
        var gp = Card(f, "Gameplay", 871f, 234f, 744f, 308f, "게임플레이", "GAMEPLAY", iGamepad);
        RowLabel(gp, 102f, "마우스 민감도"); p.mouseSensitivity = SliderRow(gp, 370f, 102f, 271f, 662f, 57f);
        RowLabel(gp, 179f, "카메라 회전"); p.cameraRotation = SliderRow(gp, 370f, 179f, 271f, 662f, 57f);
        RowLabel(gp, 256f, "화면 흔들림"); p.screenShake = Switch(gp, 489f, 256f, 555f);
        RowSeparators(gp, 744f, 140f, 217f);

        // 그래픽
        var gr = Card(f, "Graphics", 106f, 561f, 745f, 308f, "그래픽", "GRAPHICS", iMonitor);
        RowLabel(gr, 87f, "해상도"); p.resolution = Dropdown(gr, 488f, 87f, 231f);
        RowLabel(gr, 133f, "화면 모드"); p.screenMode = Dropdown(gr, 488f, 133f, 231f);
        RowLabel(gr, 180f, "품질"); p.quality = Dropdown(gr, 488f, 180f, 231f);
        RowLabel(gr, 226f, "VSync"); p.vsync = Switch(gr, 489f, 226f, 555f);
        RowLabel(gr, 272f, "그림자 품질"); p.shadows = Dropdown(gr, 488f, 272f, 231f);
        RowSeparators(gr, 745f, 110f, 156f, 203f, 249f);

        // 접근성
        var ac = Card(f, "Accessibility", 871f, 561f, 744f, 308f, "접근성", "ACCESSIBILITY", iGlobe);
        RowLabel(ac, 92f, "언어"); p.language = Dropdown(ac, 488f, 92f, 231f);
        RowLabel(ac, 150f, "미니맵 크기"); p.minimapSize = SliderRow(ac, 370f, 150f, 271f, 662f, 57f);
        RowLabel(ac, 208f, "HUD 투명도"); p.hudOpacity = SliderRow(ac, 370f, 208f, 271f, 662f, 57f);
        RowLabel(ac, 266f, "색맹 모드"); p.colorBlind = Dropdown(ac, 488f, 266f, 231f);
        RowSeparators(ac, 744f, 121f, 179f, 237f);

        // 하단
        Line(f, 106f, 907f, 1509f, 1f, Hex("3A3F48"));
        TeamLabel(f, 106f, 953f, true);
        var def = Btn(f, "DefaultsButton", 1223f, 929f, 187f, 53f, "기본값 복원", 22f, null, 0f, 0f, 0f, false, 0f, 0f, "", false, 2f);
        def.label.alignment = TextAlignmentOptions.Center;
        def.label.rectTransform.sizeDelta = new Vector2(187f, def.label.rectTransform.sizeDelta.y);
        def.normalBg = Hex("1A1D23F0"); def.normalFrame = Hex("5A5D66");
        def.clickSound = UIAudio.Sound.Defaults;
        var apply = Btn(f, "ApplyButton", 1425f, 929f, 190f, 53f, "적용", 24f, null, 0f, 0f, 66f, true, 140f, 24f, "apply", true, 2f);
        apply.arrow.sprite = iCheck; // 적용 ✓ (항상 주황)
        apply.clickSound = UIAudio.Sound.Apply;
        p.defaultsButton = def.GetComponent<Button>();
        p.applyButton = apply.GetComponent<Button>();
        root.SetActive(false);
        return root;
    }

    // ── 조작 방법 (Trigger_Keyboard_Mouse_Controls.png) ─
    static readonly Color GreenFill = Hex("2E4A3A"), GreenFrame = Hex("4E8A63"), GreenText = Hex("A6E3BC");
    static readonly Color OrangeFill = Hex("5E4A2A"), OrangeFrame = Hex("C08F45"), OrangeText = Hex("F6D7A0");
    static readonly Color RedFill = Hex("5A2D31"), RedFrame = Hex("A44A50"), RedText = Hex("F2B1B4");
    static readonly Color BlueFill = Hex("2A4659"), BlueFrame = Hex("4F8DB3"), BlueText = Hex("B7DCF4");
    static readonly Color KeyFill = Hex("262A31"), KeyFrame = Hex("3A3F48"), KeyText = Hex("9CA3AF");

    // 키 색 분류 (레퍼런스 그림 기준 - 아래 범례 색과 같음): g 이동/시점, r 전투, o 아이템/핫바, b 상호작용/시스템
    static readonly Dictionary<string, char> KeyColors = new Dictionary<string, char>
    {
        { "W", 'g' }, { "A", 'g' }, { "S", 'g' }, { "D", 'g' }, { "Shift", 'g' }, { "Space", 'g' }, { "Q", 'g' }, { "E", 'g' },
        { "R", 'r' }, { "V", 'r' },
        { "1", 'o' }, { "2", 'o' }, { "3", 'o' }, { "4", 'o' }, { "5", 'o' }, { "G", 'o' }, { "Tab", 'o' },
        { "F", 'b' }, { "T", 'b' }, { "F5", 'b' },
    };

    static void Key(Transform parent, string label, float x, float cy, float w)
    {
        KeyColors.TryGetValue(label, out char k);
        Color fill = k == 'g' ? GreenFill : k == 'r' ? RedFill : k == 'o' ? OrangeFill : k == 'b' ? BlueFill : KeyFill;
        Color frame = k == 'g' ? GreenFrame : k == 'r' ? RedFrame : k == 'o' ? OrangeFrame : k == 'b' ? BlueFrame : KeyFrame;
        Color text = k == 'g' ? GreenText : k == 'r' ? RedText : k == 'o' ? OrangeText : k == 'b' ? BlueText : KeyText;
        var rt = R("Key_" + label, parent, x, cy - 18f, w, 36f);
        SetupImage(rt.gameObject.AddComponent<Image>(), fill, sRound, 3.2f);
        StretchImg(rt, "Frame", frame, sRoundFrame, 3.2f);
        Txt(rt, "Label", 0f, 18f, w, label, label.Length > 5 ? 13f : 14f, text, fSemi, TextAlignmentOptions.Center);
    }

    // (라벨, 시작 x, 너비) - 레퍼런스 픽셀 측정
    static void KeyRow(Transform parent, float cy, params object[] keys)
    {
        for (int i = 0; i + 2 < keys.Length; i += 3)
            Key(parent, (string)keys[i], System.Convert.ToSingle(keys[i + 1]), cy, System.Convert.ToSingle(keys[i + 2]));
    }

    static void LegendBlock(Transform parent, float x, float headerCy, string title, Color color, string[,] rows)
    {
        Img(parent, "Bar", x, headerCy - 9f, 3f, 18f, color);
        Txt(parent, "Header", x + 14f, headerCy, 300f, title, 20f, color, fBold);
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            float cy = headerCy + 27f + 21f * i;
            Txt(parent, "Key", x + 14f, cy, 100f, rows[i, 0], 15f, color, fBold, TextAlignmentOptions.Left, 8f);
            Txt(parent, "Desc", x + 123f, cy, 340f, rows[i, 1], 15f, TextSoft, fRegular);
        }
    }

    static GameObject BuildControlsPanel()
    {
        var root = NewRoot("ControlsPanel");
        FullBackdrop(root.transform);
        var f = Frame1080("Content", root.transform);
        var c = root.AddComponent<ControlsPanel>();
        c.backButton = PageHeader(f, "조작 방법", 1665f, 1513f).GetComponent<Button>();

        // 키보드 카드
        var kb = Card(f, "Keyboard", 106f, 230f, 1040f, 700f, "키보드", "KEYBOARD", null);
        var keys = R("Keys", f, 0f, 0f, 1920f, 1080f); // 키는 화면 좌표 그대로
        var board = R("Board", keys, 143f, 326f, 967f, 297f);
        SetupImage(board.gameObject.AddComponent<Image>(), Hex("15191F"), sRound, 2f);
        StretchImg(board, "Frame", Hex("3A3F48"), sRoundFrame, 2f);
        var inner = R("Inner", board, 6f, 6f, 955f, 285f);
        SetupImage(inner.gameObject.AddComponent<Image>(), new Color(0, 0, 0, 0), null, 1f);
        StretchImg(inner, "Frame", Hex("2A2E35"), sRoundFrame, 2.5f);

        KeyRow(keys, 362f, "Esc", 162, 49, "F1", 250, 49, "F2", 303, 49, "F3", 356, 49, "F4", 409, 49, "F5", 485, 49, "F6", 538, 49, "F7", 591, 49, "F8", 644, 49,
            "F9", 720, 49, "F10", 773, 49, "F11", 826, 49, "F12", 879, 49, "PrtSc", 940, 49, "ScrLk", 993, 49, "Pause", 1046, 49);
        KeyRow(keys, 412f, "~", 162, 49, "1", 215, 49, "2", 268, 49, "3", 321, 49, "4", 374, 49, "5", 427, 49, "6", 480, 49, "7", 533, 49, "8", 586, 49, "9", 639, 49,
            "0", 692, 49, "-", 745, 49, "=", 798, 49, "Backspace", 851, 78, "Ins", 940, 49, "Home", 993, 49, "PgUp", 1046, 49);
        KeyRow(keys, 456f, "Tab", 162, 73, "Q", 239, 49, "W", 292, 49, "E", 345, 49, "R", 398, 49, "T", 451, 49, "Y", 504, 49, "U", 557, 49, "I", 610, 49, "O", 663, 49,
            "P", 716, 49, "[", 769, 49, "]", 822, 49, "\\", 875, 54, "Del", 940, 49, "End", 993, 49, "PgDn", 1046, 49);
        KeyRow(keys, 500f, "Caps", 162, 85, "A", 251, 49, "S", 304, 49, "D", 357, 49, "F", 410, 49, "G", 463, 49, "H", 516, 49, "J", 569, 49, "K", 622, 49, "L", 675, 49,
            ";", 728, 49, "'", 781, 49, "Enter", 834, 99);
        KeyRow(keys, 544f, "Shift", 162, 109, "Z", 275, 49, "X", 328, 49, "C", 381, 49, "V", 434, 49, "B", 487, 49, "N", 540, 49, "M", 593, 49, ",", 646, 49, ".", 699, 49,
            "/", 752, 49, "Shift", 806, 127, "↑", 993, 49);
        KeyRow(keys, 588f, "Ctrl", 162, 62, "Win", 228, 62, "Alt", 294, 62, "Space", 360, 310, "Alt", 674, 62, "Win", 740, 62, "Menu", 806, 62, "Ctrl", 872, 62,
            "←", 940, 49, "↓", 993, 49, "→", 1046, 49);

        // 범례 4칸
        Line(keys, 143f, 650f, 474f, 1f, Divider); Line(keys, 636f, 650f, 474f, 1f, Divider);
        Line(keys, 143f, 782f, 474f, 1f, Divider); Line(keys, 636f, 782f, 474f, 1f, Divider);
        LegendBlock(keys, 143f, 670f, "이동 / 시점", GreenText, new[,] { { "WASD", "이동" }, { "Shift", "전력질주" }, { "Space", "구르기" }, { "Q / E", "카메라 좌·우 회전" } });
        LegendBlock(keys, 636f, 670f, "전투", RedText, new[,] { { "R", "재장전" }, { "1", "원거리 무기 장착" }, { "V", "근접 무기 장착" } });
        LegendBlock(keys, 143f, 802f, "아이템 / 핫바", OrangeText, new[,] { { "1 ~ 5", "핫바 슬롯 선택" }, { "G", "선택한 슬롯 아이템 버리기" }, { "Tab", "인벤토리 열기" } });
        LegendBlock(keys, 636f, 802f, "상호작용 / 시스템", BlueText, new[,] { { "F", "상호작용 · 짧게 / 길게 누르기" }, { "T", "플래시라이트 ON / OFF" }, { "F5", "재시작" } });

        // 마우스 카드
        Card(f, "Mouse", 1164f, 230f, 502f, 700f, "마우스", "MOUSE", null);
        var m = R("MouseArt", f, 0f, 0f, 1920f, 1080f);
        Img(m, "Mouse", 1335f, 403f, 160f, 280f, White, sMouse);
        Txt(m, "WheelTitle", 1215f, 343f, 400f, "마우스 휠 스크롤", 18f, OrangeLight, fBold, TextAlignmentOptions.Center);
        Txt(m, "WheelDesc", 1215f, 369f, 400f, "카메라 줌 인·아웃", 18f, White, fSemi, TextAlignmentOptions.Center);
        Line(m, 1414.5f, 384f, 1.5f, 52f, Hex("6B7280"));
        Txt(m, "LeftTitle", 1195f, 435f, 200f, "좌클릭", 18f, Hex("F08088"), fBold);
        Txt(m, "LeftDesc", 1195f, 468f, 200f, "공격 / 사격", 18f, White, fSemi);
        Line(m, 1290f, 452f, 36f, 1.5f, Hex("F08088"));
        Line(m, 1325f, 452f, 1.5f, 17f, Hex("F08088"));
        Line(m, 1325f, 468f, 40f, 1.5f, Hex("F08088"));
        Txt(m, "RightTitle", 1436f, 435f, 200f, "우클릭", 18f, Hex("7FD29A"), fBold, TextAlignmentOptions.Right);
        Txt(m, "RightDesc", 1436f, 468f, 200f, "조준", 18f, White, fSemi, TextAlignmentOptions.Right);
        Txt(m, "RightSub", 1436f, 494f, 200f, "Aim Zoom", 15f, Gray, fSemi, TextAlignmentOptions.Right);
        Line(m, 1465f, 468f, 40f, 1.5f, Hex("7FD29A"));
        Line(m, 1504f, 452f, 1.5f, 17f, Hex("7FD29A"));
        Line(m, 1504f, 452f, 28f, 1.5f, Hex("7FD29A"));
        Img(m, "MoveLeft", 1348f, 712f, 28f, 28f, Hex("9CA3AF"), iArrowL);
        Line(m, 1414.5f, 692f, 1.5f, 36f, Hex("6B7280"));
        Img(m, "MoveRight", 1454f, 712f, 28f, 28f, Hex("9CA3AF"), iArrowR);
        var box = R("MoveBox", m, 1195f, 760f, 440f, 110f);
        SetupImage(box.gameObject.AddComponent<Image>(), Hex("172030F0"), sPanel, 1.8f);
        StretchImg(box, "Frame", Hex("34465E"), sFrame, 1.8f);
        Img(box, "Cursor", 22f, 32f, 34f, 34f, Hex("8EC5F0"), iCursor);
        Txt(box, "MoveTitle", 67f, 37f, 340f, "마우스 이동", 22f, Hex("8EC5F0"), fBold);
        Txt(box, "MoveDesc", 67f, 70f, 340f, "조준 방향 · 커서 따라감", 20f, White, fSemi);

        TeamLabel(f, 106f, 980f, true);
        root.SetActive(false);
        return root;
    }

    // ── 일시정지 (Trigger_Pause_Menu_UI.png) ──────────
    static GameObject BuildPauseMenu(Built built)
    {
        var root = NewRoot("PauseMenu");
        var pm = root.AddComponent<PauseMenuController>();

        var backdrop = Stretch("Backdrop", root.transform);
        var blur = Stretch("Blur", backdrop).gameObject.AddComponent<RawImage>();
        blur.color = Color.black;
        blur.raycastTarget = true;
        StretchImg(backdrop, "Dim", Hex("0504089E"));
        pm.backdrop = backdrop.gameObject;
        pm.blurImage = blur;

        var panelRoot = Stretch("Panel", root.transform);
        var f = Frame1080("Content", panelRoot);
        var p = R("Card", f, 696f, 123f, 528f, 813f);
        StretchImg(p, "Bg", Hex("1B1C21F5"), sPanel, 1.6f);
        StretchImg(p, "Frame", Hex("6E5A3C"), sFrame, 1.6f);
        var innerFrame = R("InnerFrame", p, 8f, 8f, 512f, 797f);
        SetupImage(innerFrame.gameObject.AddComponent<Image>(), Hex("2E2F35"), sFrame, 1.6f);
        Img(p, "Accent", 1f, -1f, 112f, 4f, Orange);
        Img(p, "Hatch", 118f, -1f, 50f, 9f, new Color(Orange.r, Orange.g, Orange.b, 0.55f), sHatch);

        Txt(p, "Breadcrumb", 0f, 47f, 528f, "<cspace=0.12em>TRIGGER</cspace>  /  일곱 번째 밤", 17f, Hex("D8DCE2"), fSemi, TextAlignmentOptions.Center);
        Img(p, "PauseBarL", 246f, 85f, 10f, 23f, Orange);
        Img(p, "PauseBarR", 272f, 85f, 10f, 23f, Orange);
        Txt(p, "Title", 0f, 152f, 528f, "일시 정지", 64f, White, fBold, TextAlignmentOptions.Center);

        var btns = R("Buttons", p, 0f, 0f, 528f, 813f);
        UIButton B(string n, float y, string t, Sprite ic, bool def) => Btn(btns, n, 44f, y, 440f, 58f, t, 25f, ic, 23f, 22f, 66f, true, 386f, 28f, "pause", def);
        var resume = B("Resume", 207f, "계속하기", iPlay, true);
        var settings = B("Settings", 278f, "설정", iGear, false);
        var controls = B("Controls", 344f, "조작 방법", iKeyboard, false);
        Line(p, 44f, 426f, 440f, 1f, Hex("34353B"));
        var restart = B("Restart", 450f, "다시 시작", iRestart, false);
        var menu = B("MainMenu", 517f, "메인 메뉴로", iHome, false);
        var quit = B("Quit", 584f, "게임 종료", iExit, false);
        quit.normalBg = Hex("2A1C21F0"); quit.normalFrame = Hex("54353C"); quit.normalIcon = Hex("E0A3AA");
        Line(p, 44f, 672f, 440f, 1f, Hex("34353B"));
        var esc = R("EscKey", p, 153f, 697f, 56f, 33f);
        SetupImage(esc.gameObject.AddComponent<Image>(), Hex("2A2B30"), sRound, 4f);
        StretchImg(esc, "Frame", Hex("5A5C63"), sRoundFrame, 4f);
        Txt(esc, "Label", 0f, 16.5f, 56f, "ESC", 15f, White, fBold, TextAlignmentOptions.Center);
        Txt(p, "EscHint", 225f, 713f, 300f, "게임으로 돌아가기", 18f, Hex("E5E7EB"), fSemi);
        TeamLabel(p, 0f, 769f, false, TextAlignmentOptions.Center, 528f, 14f);

        pm.panel = panelRoot.gameObject;
        pm.resumeButton = resume.GetComponent<Button>();
        pm.settingsButton = settings.GetComponent<Button>();
        pm.controlsButton = controls.GetComponent<Button>();
        pm.restartButton = restart.GetComponent<Button>();
        pm.mainMenuButton = menu.GetComponent<Button>();
        pm.quitButton = quit.GetComponent<Button>();

        // 설정·조작 방법: 메인 메뉴와 같은 프리팹을 중첩
        var s = (GameObject)PrefabUtility.InstantiatePrefab(built.settings);
        StretchInstance(s, root.transform);
        s.SetActive(false);
        var c = (GameObject)PrefabUtility.InstantiatePrefab(built.controls);
        StretchInstance(c, root.transform);
        c.SetActive(false);
        pm.settingsPanel = s.GetComponent<SettingsPanel>();
        pm.controlsPanel = c.GetComponent<ControlsPanel>();

        backdrop.gameObject.SetActive(false);
        panelRoot.gameObject.SetActive(false);
        return root;
    }

    // ── 생존 종료 (Trigger_Death_Screen_UI.png) ────────
    static GameObject BuildDeathScreen()
    {
        var root = NewRoot("DeathScreen");
        var dc = root.AddComponent<DeathScreenController>();

        var backdrop = Stretch("Backdrop", root.transform);
        dc.backdrop = backdrop.gameObject.AddComponent<CanvasGroup>();
        var blur = Stretch("Blur", backdrop).gameObject.AddComponent<RawImage>();
        blur.color = Color.black;
        blur.raycastTarget = true;
        StretchImg(backdrop, "Dim", Hex("000000B4"));
        dc.blurImage = blur;

        var panelRoot = Stretch("Panel", root.transform);
        dc.panel = panelRoot.gameObject.AddComponent<CanvasGroup>();
        var f = Frame1080("Content", panelRoot);
        Breadcrumb(f, 0f, 163f, 22f, TextAlignmentOptions.Center, 1920f);
        var p = R("Card", f, 476f, 218f, 968f, 644f);
        StretchImg(p, "Bg", Hex("1A1B20F5"), sPanel, 1.6f);
        StretchImg(p, "Frame", Hex("5C4A48"), sFrame, 1.6f);
        Color bracket = Hex("C9786A");
        Img(p, "Accent", 1f, -1f, 58f, 3f, bracket);
        Img(p, "CornerBL_V", -1f, 600f, 2f, 45f, bracket);
        Img(p, "CornerBL_H", -1f, 643f, 46f, 2f, bracket);
        Img(p, "CornerBR_V", 967f, 600f, 2f, 45f, bracket);
        Img(p, "CornerBR_H", 922f, 643f, 46f, 2f, bracket);

        Img(p, "IconFrame", 459f, 33f, 50f, 50f, Hex("8A4A4E"), sDiamond);
        Img(p, "IconFill", 463f, 37f, 42f, 42f, Hex("3A2226"), sDiamond);
        Img(p, "IconPulse", 472f, 46f, 24f, 24f, Hex("E07A7A"), iPulse);
        Txt(p, "Title", 0f, 122f, 968f, "생존 종료", 60f, White, fBold, TextAlignmentOptions.Center);
        Txt(p, "Sub", 0f, 177f, 968f, "이번 생존 기록", 21f, Hex("C9CDD4"), fSemi, TextAlignmentOptions.Center);
        Line(p, 62f, 220f, 844f, 1f, Hex("34363D"));

        float[] cx = { 199f, 484f, 769f };
        string[] labels = { "도달 일차", "생존 시간", "완료한 미션" };
        var values = new TextMeshProUGUI[3];
        for (int i = 0; i < 3; i++)
        {
            Txt(p, "StatLabel", cx[i] - 150f, 259f, 300f, labels[i], 19f, TextSoft, fSemi, TextAlignmentOptions.Center);
            values[i] = Txt(p, "StatValue", cx[i] - 150f, 309f, 300f, "0", 42f, White, fBold, TextAlignmentOptions.Center);
        }
        Line(p, 341f, 250f, 1f, 92f, Hex("34363D"));
        Line(p, 627f, 250f, 1f, 92f, Hex("34363D"));
        dc.dayValue = values[0]; dc.timeValue = values[1]; dc.missionValue = values[2];

        var dots = new Image[7]; var frames = new Image[7]; var dotLabels = new TextMeshProUGUI[7];
        for (int i = 0; i < 7; i++)
        {
            float x = 313f + 57f * i;
            if (i < 6) Line(p, x + 15f, 404f, 42f, 1.5f, Hex("3A3D45"));
        }
        for (int i = 0; i < 7; i++)
        {
            float x = 313f + 57f * i;
            var d = R("Day" + (i + 1), p, x - 15f, 389f, 30f, 30f);
            dots[i] = d.gameObject.AddComponent<Image>();
            SetupImage(dots[i], Hex("22242A"), sRound, 4.5f);
            frames[i] = StretchImg(d, "Frame", Hex("4A4D55"), sRoundFrame, 4.5f);
            dotLabels[i] = Txt(d, "Label", 0f, 15f, 30f, (i + 1).ToString(), 15f, Gray, fSemi, TextAlignmentOptions.Center);
        }
        dc.dayDots = dots; dc.dayDotFrames = frames; dc.dayDotLabels = dotLabels;
        Line(p, 62f, 458f, 844f, 1f, Hex("34363D"));

        var restart = Btn(p, "Restart", 216f, 502f, 259f, 61f, "다시 시작", 24f, iRestart, 24f, 22f, 78f, true, 210f, 26f, "death", true);
        var menu = Btn(p, "MainMenu", 492f, 502f, 259f, 61f, "메인 메뉴로", 24f, iHome, 24f, 22f, 80f, true, 210f, 26f, "death", false);
        menu.normalBg = Hex("1E1F25F0");
        dc.restartButton = restart.GetComponent<Button>();
        dc.mainMenuButton = menu.GetComponent<Button>();
        TeamLabel(p, 0f, 594f, false, TextAlignmentOptions.Center, 968f, 14f);

        backdrop.gameObject.SetActive(false);
        panelRoot.gameObject.SetActive(false);
        return root;
    }

    // ═════════════════════════════════════════════
    // 3) 메인 메뉴 씬 (트리거_ 일곱 번째 밤.png)
    // ═════════════════════════════════════════════
    static void BuildMainMenuScene(Built built)
    {
        var prev = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0; // 화면은 전부 UI (영상 포함)

            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasGo = new GameObject("MainMenuCanvas", typeof(RectTransform)) { layer = 5 };
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var ct = canvasGo.transform;

            // 배경 영상 (화면을 꽉 채움)
            var video = Stretch("VideoBackground", ct);
            var raw = video.gameObject.AddComponent<RawImage>();
            raw.color = Color.black;
            raw.raycastTarget = false;
            var fitter = video.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;
            video.gameObject.AddComponent<MainMenuVideoBg>();

            // 왼쪽 그늘 (타이틀·버튼 가독성)
            var shade = Stretch("LeftShade", ct);
            SetupImage(shade.gameObject.AddComponent<Image>(), new Color(1f, 1f, 1f, 0.92f), sLeftShade, 1f);
            shade.anchorMax = new Vector2(0.62f, 1f);

            var menuRoot = Stretch("Menu", ct);
            var f = Frame1080("Content", menuRoot);
            var title = Txt(f, "Title", 116f, 184f, 900f, "TRIGGER", 154f, Hex("F3EADB"), fTitle, TextAlignmentOptions.Left, 2f);
            title.fontSharedMaterial = titleMat;
            title.fontStyle = FontStyles.Italic;
            title.horizontalMapping = TextureMappingOptions.Paragraph;
            title.verticalMapping = TextureMappingOptions.Paragraph;
            Img(f, "SubBar", 127f, 274f, 4f, 27f, Orange);
            Txt(f, "Subtitle", 158f, 287f, 500f, "일곱 번째 밤", 38f, White, fBold);

            var buttons = R("Buttons", f, 0f, 0f, 1920f, 1080f);
            UIButton B(string n, float y, string t, bool def) => Btn(buttons, n, 119f, y, 397f, 72f, t, 34f, null, 0f, 0f, 41f, true, 333f, 34f, "main", def, 2f);
            var start = B("Start", 369f, "게임 시작", true);
            var controls = B("Controls", 458f, "조작 방법", false);
            var settings = B("Settings", 547f, "설정", false);
            var quit = B("Quit", 636f, "게임 종료", false);
            foreach (var b in new[] { start, controls, settings, quit }) { b.normalBg = Hex("101116D9"); b.normalFrame = Hex("6A6C74"); }
            Txt(f, "TeamTrigger", 119f, 855f, 400f, "TEAM TRIGGER", 19f, Hex("A3A7AF"), fSemi, TextAlignmentOptions.Left, 12f);

            var s = (GameObject)PrefabUtility.InstantiatePrefab(built.settings);
            StretchInstance(s, ct);
            s.SetActive(false);
            var c = (GameObject)PrefabUtility.InstantiatePrefab(built.controls);
            StretchInstance(c, ct);
            c.SetActive(false);

            var mc = canvasGo.AddComponent<MainMenuController>();
            mc.menuRoot = menuRoot.gameObject;
            mc.startButton = start.GetComponent<Button>();
            mc.controlsButton = controls.GetComponent<Button>();
            mc.settingsButton = settings.GetComponent<Button>();
            mc.quitButton = quit.GetComponent<Button>();
            mc.settingsPanel = s.GetComponent<SettingsPanel>();
            mc.controlsPanel = c.GetComponent<ControlsPanel>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            SceneManager.SetActiveScene(prev);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ═════════════════════════════════════════════
    // 4) Build Settings: MainMenu 0번, main 1번, 나머지(학습 씬)는 목록에 두되 끔
    // ═════════════════════════════════════════════
    static void SetupBuildSettings()
    {
        var list = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true),
        };
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == ScenePath || s.path == GameScenePath) continue;
            list.Add(new EditorBuildSettingsScene(s.path, false)); // 학습 빌드할 때 체크만 바꾸면 됨
        }
        EditorBuildSettings.scenes = list.ToArray();
    }
}

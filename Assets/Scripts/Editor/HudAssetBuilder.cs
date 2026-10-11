using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// HUD 공용 에셋 생성기 (Tools → HUD → Build HUD Assets). 재실행해도 안전(덮어씀).
///  1) Pretendard(OFL) → TMP 동적 폰트 에셋 (Regular/SemiBold/Bold) → Assets/Resources/HUD/Fonts
///     + TMP Settings의 전역 폴백에 Regular 추가 → 기본 폰트(LiberationSans)로 된 기존 UI의 한글 □ 깨짐 해결
///  2) 9-slice 둥근 사각형 / 원 / 링 / 삼각형 스프라이트 → Assets/Resources/HUD/Sprites
/// 런타임에서는 HudStyle이 Resources.Load로 불러 씀.
/// </summary>
public static class HudAssetBuilder
{
    const string FontSrcDir = "Assets/Fonts/Pretendard";
    const string FontOutDir = "Assets/Resources/HUD/Fonts";
    const string SpriteOutDir = "Assets/Resources/HUD/Sprites";

    [MenuItem("Tools/HUD/Build HUD Assets")]
    public static void Build()
    {
        Directory.CreateDirectory(FontOutDir);
        Directory.CreateDirectory(SpriteOutDir);

        TMP_FontAsset regular = null;
        foreach (string weight in new[] { "Regular", "SemiBold", "Bold" })
        {
            var fa = BuildFont(weight);
            if (weight == "Regular") regular = fa;
        }
        AddGlobalFallback(regular);

        // 둥근 사각형: 64px, 반경 16px (9-slice 테두리 16) → UI에서 10~15px 모서리로 보임
        WriteSprite("RoundedRect", 64, 64, (x, y) => RoundedRectCoverage(x, y, 64, 64, 16f), new Vector4(16, 16, 16, 16));
        WriteSprite("Circle", 128, 128, (x, y) => CircleCoverage(x, y, 64f, 64f, 63f), Vector4.zero);
        WriteSprite("Ring", 128, 128, (x, y) => Mathf.Min(CircleCoverage(x, y, 64f, 64f, 63f), 1f - CircleCoverage(x, y, 64f, 64f, 57f)), Vector4.zero);
        WriteSprite("Triangle", 64, 64, TriangleCoverage, Vector4.zero);

        // 미니맵 (레퍼런스 픽셀 샘플링 기준 색상을 직접 구움 → Image 색은 흰색으로 씀)
        WriteColorSprite("MetalRing", 256, 256, MetalRingColor);
        WriteColorSprite("NavArrow", 64, 64, NavArrowColor);

        // 상단 중앙 (흰색 → Image 색으로 틴트)
        WriteSprite("Sun", 64, 64, SunCoverage, Vector4.zero);
        WriteSprite("Moon", 64, 64, (x, y) => Mathf.Min(CircleCoverage(x, y, 32f, 32f, 24f), 1f - CircleCoverage(x, y, 42f, 40f, 20f)), Vector4.zero);
        WriteColorSprite("SoftShadow", 128, 64, SoftShadowColor);

        // 좌하단 상태 패널 아이콘 (흰색 → 틴트)
        WriteSprite("Heart", 64, 64, HeartCoverage, Vector4.zero);
        WriteSprite("Bolt", 64, 64, (x, y) => InPolygon(new Vector2(x, y), BoltPoly) ? 1f : 0f, Vector4.zero);
        WriteSprite("Drop", 64, 64, DropCoverage, Vector4.zero);
        WriteSprite("ForkKnife", 64, 64, ForkKnifeCoverage, Vector4.zero);
        // 수분/포만감 원형 게이지: 지름 86px 기준 두께 7px → 128px 텍스처에서 약 10.5px
        WriteSprite("RingThick", 128, 128, (x, y) => Mathf.Min(CircleCoverage(x, y, 64f, 64f, 63f), 1f - CircleCoverage(x, y, 64f, 64f, 52.5f)), Vector4.zero);

        // 테두리만 있는 둥근 사각형 (9-slice): 핫바/패널 테두리. 얇은 것 4px, 두꺼운 것 6px
        // (Image.pixelsPerUnitMultiplier 2로 쓰면 각각 2px / 3px 테두리, 모서리 반경 8px)
        WriteSprite("RoundedFrame", 64, 64, (x, y) => FrameCoverage(x, y, 4f), new Vector4(16, 16, 16, 16));
        WriteSprite("RoundedFrameThick", 64, 64, (x, y) => FrameCoverage(x, y, 6f), new Vector4(16, 16, 16, 16));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HudAssetBuilder] ✅ Pretendard 폰트 3종 + HUD 스프라이트 16종 생성 완료");
    }

    // ── 폰트 ──
    static TMP_FontAsset BuildFont(string weight)
    {
        string src = $"{FontSrcDir}/Pretendard-{weight}.otf";
        Font font = AssetDatabase.LoadAssetAtPath<Font>(src);
        if (font == null) { Debug.LogError($"[HudAssetBuilder] 폰트 없음: {src}"); return null; }

        // 이미 있으면 재사용 (지우고 다시 만들면 GUID가 바뀌어 기존 참조가 깨짐)
        string outPath = $"{FontOutDir}/Pretendard-{weight} SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
        if (existing != null) return existing;

        // 동적 아틀라스: 쓰이는 글자만 그때그때 아틀라스에 추가 (한글 11,172자를 미리 굽지 않음)
        var fa = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);
        fa.name = $"Pretendard-{weight} SDF";
        AssetDatabase.CreateAsset(fa, outPath);
        fa.material.name = fa.name + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        foreach (var tex in fa.atlasTextures)
        {
            tex.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(tex, fa);
        }
        EditorUtility.SetDirty(fa);
        return fa;
    }

    static void AddGlobalFallback(TMP_FontAsset fa)
    {
        if (fa == null || TMP_Settings.instance == null) return;
        var list = TMP_Settings.fallbackFontAssets;
        list.RemoveAll(f => f == null || f.name == fa.name);
        list.Insert(0, fa);
        EditorUtility.SetDirty(TMP_Settings.instance);
    }

    // ── 스프라이트 (4x4 슈퍼샘플 안티앨리어싱, 흰색 + 알파) ──
    delegate float Coverage(float x, float y);

    static void WriteSprite(string name, int w, int h, Coverage cov, Vector4 border)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = 0f;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        a += cov(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f) > 0.5f ? 1f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a / 16f));
            }
        }
        string path = $"{SpriteOutDir}/{name}.png";
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
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // 색이 들어간 스프라이트 (4x4 슈퍼샘플 평균)
    delegate Color ColorAt(float x, float y);

    static void WriteColorSprite(string name, int w, int h, ColorAt colorAt)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        Color c = colorAt(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a); // 프리멀티 평균 (가장자리 색 번짐 방지)
                    }
                sum /= 16f;
                tex.SetPixel(x, y, sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear);
            }
        }
        string path = $"{SpriteOutDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // 레퍼런스 미니맵 링 (지름 188px 기준 두께 10px): 바깥부터
    // 어두운 테(0~2) → 밝은 웜그레이 하이라이트(2~7, 위쪽이 살짝 밝음) → 중간 회색(7~8) → 어두운 안쪽 선(8~10)
    static Color MetalRingColor(float x, float y)
    {
        const float size = 256f, refDiameter = 188f;
        float px = size / refDiameter;                 // 레퍼런스 1px = 텍스처 몇 px
        float r = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f));
        float fromOuter = (size / 2f - 0.5f - r) / px; // 바깥 테두리에서 안쪽으로 레퍼런스 px
        if (fromOuter < 0f || fromOuter > 10f) return Color.clear;

        float top = Mathf.Clamp01(y / size);           // 위쪽일수록 1 (빛이 위에서 오는 느낌)
        if (fromOuter < 2f) return new Color32(70, 72, 79, 255);
        if (fromOuter < 7f) return Color.Lerp(new Color32(150, 140, 131, 255), new Color32(178, 167, 158, 255), top);
        if (fromOuter < 8f) return new Color32(115, 108, 103, 255);
        return new Color32(56, 52, 48, 255);
    }

    // 내비게이션 화살표 (위쪽 방향, 아래 가운데가 오목): 흰색 채움 + 어두운 외곽선
    static readonly Vector2[] ArrowPoly = { new Vector2(32f, 60f), new Vector2(8f, 6f), new Vector2(32f, 20f), new Vector2(56f, 6f) };

    static Color NavArrowColor(float x, float y)
    {
        var p = new Vector2(x, y);
        if (InPolygon(p, ScalePoly(ArrowPoly, 0.78f))) return Color.white;
        if (InPolygon(p, ArrowPoly)) return new Color32(30, 30, 32, 255);
        return Color.clear;
    }

    static Vector2[] ScalePoly(Vector2[] poly, float s)
    {
        Vector2 c = Vector2.zero;
        foreach (var v in poly) c += v;
        c /= poly.Length;
        c.y += 4f; // 오목한 꼬리 쪽으로 중심이 쏠려 있어서 살짝 보정 (외곽선 두께를 균일하게)
        var result = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++) result[i] = c + (poly[i] - c) * s;
        return result;
    }

    // 짝수-홀수 규칙 다각형 내부 판정 (오목 다각형 OK)
    static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    // 해: 가운데 원 + 둥근 끝 광선 8개 (레퍼런스 ☀)
    static float SunCoverage(float x, float y)
    {
        Vector2 p = new Vector2(x - 32f, y - 32f);
        if (p.magnitude <= 13f) return 1f;
        for (int i = 0; i < 8; i++)
        {
            float ang = i * Mathf.PI / 4f;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 a = dir * 19f, b = dir * 29f;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
            if (Vector2.Distance(p, a + (b - a) * t) <= 2.4f) return 1f;
        }
        return 0f;
    }

    // 글자 뒤 은은한 그림자: 가장자리로 갈수록 투명해지는 검은 타원
    static Color SoftShadowColor(float x, float y)
    {
        float nx = (x - 64f) / 64f, ny = (y - 32f) / 32f;
        float d = Mathf.Sqrt(nx * nx + ny * ny);
        float a = Mathf.Clamp01(1f - d);
        return new Color(0f, 0f, 0f, a * a * (3f - 2f * a)); // smoothstep
    }

    // 하트: (x²+y²-1)³ - x²y³ ≤ 0
    static float HeartCoverage(float x, float y)
    {
        float u = (x - 32f) / 26f, v = (y - 30f) / 26f;
        float a = u * u + v * v - 1f;
        return a * a * a - u * u * v * v * v <= 0f ? 1f : 0f;
    }

    // 번개 (⚡): 오른쪽 위 → 왼쪽 아래로 꺾이는 모양
    static readonly Vector2[] BoltPoly =
    {
        new Vector2(40f, 63f), new Vector2(12f, 29f), new Vector2(29f, 29f),
        new Vector2(21f, 1f), new Vector2(52f, 37f), new Vector2(35f, 37f), new Vector2(46f, 63f)
    };

    // 물방울: 아래 원 + 위로 뾰족한 삼각형
    static float DropCoverage(float x, float y)
    {
        if (CircleCoverage(x, y, 32f, 23f, 18f) > 0f) return 1f;
        var tri = new[] { new Vector2(32f, 62f), new Vector2(15.2f, 29.5f), new Vector2(48.8f, 29.5f) };
        return InPolygon(new Vector2(x, y), tri) ? 1f : 0f;
    }

    // 포크 + 나이프
    static float ForkKnifeCoverage(float x, float y)
    {
        bool InRect(float x0, float y0, float x1, float y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
        // 포크: 손잡이 + 머리 + 갈래 3개
        if (InRect(17.5f, 4f, 23.5f, 36f)) return 1f;
        if (InRect(11f, 32f, 30f, 42f)) return 1f;
        if (InRect(11f, 42f, 14.5f, 60f) || InRect(18.8f, 42f, 22.2f, 60f) || InRect(26.5f, 42f, 30f, 60f)) return 1f;
        // 나이프: 손잡이 + 위가 둥근 칼날
        if (InRect(39f, 4f, 46f, 34f)) return 1f;
        if (InRect(39f, 30f, 50f, 52f)) return 1f;
        if (CircleCoverage(x, y, 44.5f, 52f, 5.5f) > 0f && x >= 39f) return 1f;
        return 0f;
    }

    // 테두리만 있는 둥근 사각형 (64px, 반경 16)
    static float FrameCoverage(float x, float y, float stroke)
    {
        if (RoundedRectCoverage(x, y, 64f, 64f, 16f) == 0f) return 0f;
        float ix = x - stroke, iy = y - stroke, iw = 64f - stroke * 2f;
        if (ix < 0f || iy < 0f || ix > iw || iy > iw) return 1f;
        return RoundedRectCoverage(ix, iy, iw, iw, 16f - stroke) > 0f ? 0f : 1f;
    }

    static float RoundedRectCoverage(float x, float y, float w, float h, float r)
    {
        float dx = Mathf.Max(r - x, 0f, x - (w - r));
        float dy = Mathf.Max(r - y, 0f, y - (h - r));
        return dx * dx + dy * dy <= r * r ? 1f : 0f;
    }

    static float CircleCoverage(float x, float y, float cx, float cy, float r)
    {
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= r * r ? 1f : 0f;
    }

    // 위쪽을 가리키는 삼각형 (미니맵 플레이어 마커)
    static float TriangleCoverage(float x, float y)
    {
        Vector2 a = new Vector2(32f, 60f), b = new Vector2(6f, 6f), c = new Vector2(58f, 6f);
        Vector2 p = new Vector2(x, y);
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return neg && pos ? 0f : 1f;
    }

    static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
}

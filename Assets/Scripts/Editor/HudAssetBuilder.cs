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

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HudAssetBuilder] ✅ Pretendard 폰트 3종 + HUD 스프라이트 4종 생성 완료");
    }

    // ── 폰트 ──
    static TMP_FontAsset BuildFont(string weight)
    {
        string src = $"{FontSrcDir}/Pretendard-{weight}.otf";
        Font font = AssetDatabase.LoadAssetAtPath<Font>(src);
        if (font == null) { Debug.LogError($"[HudAssetBuilder] 폰트 없음: {src}"); return null; }

        string outPath = $"{FontOutDir}/Pretendard-{weight} SDF.asset";
        AssetDatabase.DeleteAsset(outPath);

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

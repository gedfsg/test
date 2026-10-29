using UnityEngine;

/// <summary>
/// 폐허 꾸미기용 텍스처를 코드로 생성 (다운로드 없음).
/// 각 텍스처 = 2×2 아틀라스(변형 4개, 칸당 128px). 색은 텍스처에, 모양은 알파에.
/// 같은 시드면 같은 그림.
/// </summary>
public static class WorldDecorTextures
{
    // 칸 크기: 선이 또렷해야 하는 것(금/탄흔/유리)은 128, 부드러운 무늬는 64 (생성 시간 1/4)
    const int Sharp = 128, Soft = 64;
    static int Cell = Sharp; // 지금 만드는 아틀라스의 칸 크기 (RasterLines/MaskAt가 씀)

    // 한 번 만든 텍스처는 재시작(씬 다시 로드) 때 다시 안 만듦
    static readonly System.Collections.Generic.Dictionary<string, Texture2D> cache = new System.Collections.Generic.Dictionary<string, Texture2D>();

    // ── 공용 ───────────────────────────────────

    delegate Color PixelFn(float x, float y, int variant, System.Random rng); // x,y: 칸 안 -1~1

    static Texture2D Atlas(string name, PixelFn fn, int seed, int cell = Sharp)
    {
        string key = name + seed;
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        Cell = cell; int Size = cell * 2;
        var px = new Color32[Size * Size];
        for (int v = 0; v < 4; v++)
        {
            var rng = new System.Random(seed * 31 + v);
            int ox = (v % 2) * Cell, oy = (v / 2) * Cell;
            for (int j = 0; j < Cell; j++)
                for (int i = 0; i < Cell; i++)
                {
                    float x = (i + 0.5f) / Cell * 2f - 1f, y = (j + 0.5f) / Cell * 2f - 1f;
                    px[(oy + j) * Size + ox + i] = fn(x, y, v, rng);
                }
        }
        var tex = Make(name, px, Size);
        cache[key] = tex;
        return tex;
    }

    static Texture2D Make(string name, Color32[] px, int Size)
    {
        var t = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        t.SetPixels32(px); t.Apply(true, true); // 밉맵 생성 후 CPU 사본 버림
        return t;
    }

    static float Fbm(float x, float y, float seed, int oct = 4)
    {
        float sum = 0f, amp = 0.5f, f = 1f;
        for (int o = 0; o < oct; o++) { sum += amp * Mathf.PerlinNoise(x * f + seed, y * f + seed * 1.37f); f *= 2f; amp *= 0.5f; }
        return sum; // 대략 0~1
    }

    /// <summary>칸 가장자리로 갈수록 0 (아틀라스 옆 칸 번짐 방지)</summary>
    static float EdgeFade(float x, float y) => Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y))) * 12f);

    /// <summary>선분까지 거리</summary>
    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>갈라진 금(꺾이는 선 + 가지) 선분 목록</summary>
    static System.Collections.Generic.List<(Vector2 a, Vector2 b, float w)> CrackPaths(System.Random rng, Vector2 start, float len, int branches, float width)
    {
        var segs = new System.Collections.Generic.List<(Vector2, Vector2, float)>();
        void Walk(Vector2 p, float ang, float remain, float w, int depth)
        {
            while (remain > 0f)
            {
                float step = 0.06f + (float)rng.NextDouble() * 0.06f;
                ang += ((float)rng.NextDouble() - 0.5f) * 0.9f;
                Vector2 q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * step;
                segs.Add((p, q, w));
                if (depth < 2 && rng.NextDouble() < 0.12) Walk(q, ang + (rng.NextDouble() < 0.5 ? 0.8f : -0.8f), remain * 0.5f, w * 0.6f, depth + 1);
                p = q; remain -= step; w *= 0.97f;
            }
        }
        for (int i = 0; i < branches; i++) Walk(start, (float)rng.NextDouble() * Mathf.PI * 2f, len * (0.6f + (float)rng.NextDouble() * 0.4f), width, 0);
        return segs;
    }

    /// <summary>선분들을 칸(Cell×Cell) 알파 마스크로 그림. 각 선분 주변 픽셀만 계산해서 빠름 (픽셀마다 전체 선분을 도는 것보다 수십 배)</summary>
    static float[] RasterLines(System.Collections.Generic.List<(Vector2 a, Vector2 b, float w)> segs, float px)
    {
        var mask = new float[Cell * Cell];
        foreach (var s in segs)
        {
            float pad = s.w + px;
            int i0 = Mathf.Max(0, Mathf.FloorToInt(((Mathf.Min(s.a.x, s.b.x) - pad) + 1f) * 0.5f * Cell));
            int i1 = Mathf.Min(Cell - 1, Mathf.CeilToInt(((Mathf.Max(s.a.x, s.b.x) + pad) + 1f) * 0.5f * Cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt(((Mathf.Min(s.a.y, s.b.y) - pad) + 1f) * 0.5f * Cell));
            int j1 = Mathf.Min(Cell - 1, Mathf.CeilToInt(((Mathf.Max(s.a.y, s.b.y) + pad) + 1f) * 0.5f * Cell));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2((i + 0.5f) / Cell * 2f - 1f, (j + 0.5f) / Cell * 2f - 1f);
                    float a = Mathf.Clamp01(1f - (SegDist(p, s.a, s.b) - s.w) / px);
                    if (a > mask[j * Cell + i]) mask[j * Cell + i] = a;
                }
        }
        return mask;
    }

    static float MaskAt(float[] mask, float x, float y)
    {
        int i = Mathf.Clamp((int)((x + 1f) * 0.5f * Cell), 0, Cell - 1), j = Mathf.Clamp((int)((y + 1f) * 0.5f * Cell), 0, Cell - 1);
        return mask[j * Cell + i];
    }

    static Color32 C(float r, float g, float b, float a) => new Color(r, g, b, Mathf.Clamp01(a));

    // ── 도로 ───────────────────────────────────

    /// <summary>아스팔트 균열</summary>
    public static Texture2D Crack(int seed)
    {
        var cache = new float[4][];
        return Atlas("Decal_Crack", (x, y, v, rng) =>
        {
            if (cache[v] == null) cache[v] = RasterLines(CrackPaths(rng, new Vector2(((float)rng.NextDouble() - 0.5f) * 0.3f, ((float)rng.NextDouble() - 0.5f) * 0.3f), 1.3f, 3 + v % 2, 0.02f), 2.5f / Cell);
            float a = MaskAt(cache[v], x, y) * EdgeFade(x, y);
            return C(0.12f, 0.12f, 0.13f, a * 0.7f); // 짙은 회색 반투명 (까만 선은 나뭇가지처럼 보였음)
        }, seed);
    }

    /// <summary>포트홀 (움푹 파인 구멍 + 밝은 테두리 + 주변 잔금)</summary>
    public static Texture2D Pothole(int seed)
    {
        return Atlas("Decal_Pothole", (x, y, v, rng) =>
        {
            float ang = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float edge = 0.68f + 0.16f * (Fbm(Mathf.Cos(ang) * 1.5f + 3f, Mathf.Sin(ang) * 1.5f + 3f, seed + v * 7.1f) - 0.5f) * 2f;
            float n = Fbm(x * 4f, y * 4f, seed + v * 3.3f);
            if (r < edge)
            {
                float depth = 1f - r / edge; // 가운데가 더 어둡게
                float shade = Mathf.Lerp(0.16f, 0.06f, depth) * (0.8f + n * 0.4f);
                return C(shade, shade * 0.95f, shade * 0.9f, 0.95f);
            }
            float rim = Mathf.Clamp01(1f - (r - edge) / 0.12f);
            float g = 0.38f + n * 0.12f;
            return C(g, g, g * 0.97f, rim * 0.75f * EdgeFade(x, y));
        }, seed, Soft);
    }

    /// <summary>기름 얼룩 (검고 번진 + 살짝 무지갯빛 테두리)</summary>
    public static Texture2D Oil(int seed)
    {
        return Atlas("Decal_Oil", (x, y, v, rng) =>
        {
            float n = Fbm(x * 2.2f, y * 2.2f, seed + v * 5.7f);
            float r = Mathf.Sqrt(x * x + y * y) + (n - 0.5f) * 0.6f;
            float a = Mathf.Clamp01((0.7f - r) / 0.25f);
            float sheen = Mathf.Clamp01(1f - Mathf.Abs(r - 0.55f) / 0.08f) * 0.35f;
            Color c = Color.Lerp(new Color(0.04f, 0.04f, 0.05f), Color.HSVToRGB(Mathf.Repeat(n * 2f + v * 0.2f, 1f), 0.5f, 0.35f), sheen);
            c.a = a * 0.8f * EdgeFade(x, y);
            return c;
        }, seed, Soft);
    }

    /// <summary>타이어 자국 (세로로 두 줄, 끝으로 갈수록 흐려짐, 살짝 휘어짐)</summary>
    public static Texture2D TireMarks(int seed)
    {
        return Atlas("Decal_Tire", (x, y, v, rng) =>
        {
            float bend = Mathf.Sin((y + 1f) * 1.2f + v) * 0.08f * (v % 2 == 0 ? 1f : -1f);
            float tread = 0.6f + 0.4f * Mathf.PerlinNoise(y * 40f + seed, v * 9f);
            float a = 0f;
            foreach (float cx in new[] { -0.42f, 0.42f })
            {
                float d = Mathf.Abs(x - cx - bend);
                a = Mathf.Max(a, Mathf.Clamp01((0.13f - d) / 0.04f));
            }
            float along = Mathf.Clamp01((1f - Mathf.Abs(y)) * 3f) * (0.5f + 0.5f * Fbm(0.5f, y * 2f, seed + v));
            return C(0.03f, 0.03f, 0.03f, a * tread * along * 0.75f * EdgeFade(x, y));
        }, seed, Soft);
    }

    // ── 바닥/벽 ────────────────────────────────

    /// <summary>핏자국 (큰 얼룩 + 튄 방울, 변형 2개는 끌린 자국)</summary>
    public static Texture2D Blood(int seed)
    {
        var drops = new System.Collections.Generic.List<Vector3>[4];
        return Atlas("Decal_Blood", (x, y, v, rng) =>
        {
            if (drops[v] == null)
            {
                drops[v] = new System.Collections.Generic.List<Vector3>();
                for (int k = 0; k < 14; k++) { float ang = (float)rng.NextDouble() * 6.283f, dist = 0.45f + (float)rng.NextDouble() * 0.45f; drops[v].Add(new Vector3(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist, 0.02f + (float)rng.NextDouble() * 0.05f)); }
            }
            float n = Fbm(x * 3f, y * 3f, seed + v * 2.9f);
            float sx = v >= 2 ? x : x, sy = v >= 2 ? y * 0.35f : y; // 끌린 자국은 세로로 늘인 모양
            float r = Mathf.Sqrt(sx * sx + sy * sy) + (n - 0.5f) * 0.5f;
            float a = Mathf.Clamp01((0.42f - r) / 0.06f);
            foreach (var d in drops[v]) a = Mathf.Max(a, Mathf.Clamp01((d.z - Vector2.Distance(new Vector2(x, y), new Vector2(d.x, d.y))) / 0.015f));
            float dark = 0.7f + 0.3f * n;
            return C(0.32f * dark, 0.02f, 0.02f, a * 0.9f * EdgeFade(x, y));
        }, seed, Soft);
    }

    /// <summary>그을음 (검은 원형 번짐)</summary>
    public static Texture2D Scorch(int seed)
    {
        return Atlas("Decal_Scorch", (x, y, v, rng) =>
        {
            float n = Fbm(x * 2.5f, y * 2.5f, seed + v * 4.1f);
            float r = Mathf.Sqrt(x * x + y * y) + (n - 0.5f) * 0.5f;
            float a = Mathf.Clamp01(1f - r / 0.85f);
            a = a * a * (3f - 2f * a);
            float g = 0.03f + n * 0.05f;
            return C(g, g * 0.9f, g * 0.8f, a * 0.85f * EdgeFade(x, y));
        }, seed, Soft);
    }

    /// <summary>탄흔 (검은 구멍 + 깨진 회색 테두리 + 방사형 잔금)</summary>
    public static Texture2D BulletHole(int seed)
    {
        var cracks = new float[4][];
        return Atlas("Decal_BulletHole", (x, y, v, rng) =>
        {
            if (cracks[v] == null) cracks[v] = RasterLines(CrackPaths(rng, Vector2.zero, 0.55f, 4 + v % 3, 0.01f), 2f / Cell);
            float ang = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float hole = 0.16f + 0.04f * Mathf.Sin(ang * 5f + v);
            if (r < hole) return C(0.02f, 0.02f, 0.02f, 1f);
            float chip = 0.34f + 0.08f * (Fbm(Mathf.Cos(ang) * 2f + 5f, Mathf.Sin(ang) * 2f + 5f, seed + v) - 0.5f) * 2f;
            if (r < chip) { float g = 0.32f + 0.1f * Fbm(x * 8f, y * 8f, seed); return C(g, g * 0.97f, g * 0.93f, 0.85f); }
            float a = MaskAt(cracks[v], x, y);
            return C(0.08f, 0.08f, 0.08f, a * 0.8f * EdgeFade(x, y));
        }, seed);
    }

    /// <summary>폭발 흔적 (큰 그을음 + 방사형 줄기 + 가운데 패임)</summary>
    public static Texture2D Explosion(int seed)
    {
        return Atlas("Decal_Explosion", (x, y, v, rng) =>
        {
            float ang = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            // 불규칙한 줄기: 방향마다 길이가 다른 노이즈 (규칙적인 별 모양 방지)
            float spokes = Fbm(Mathf.Cos(ang) * 3f + v * 3.1f, Mathf.Sin(ang) * 3f + v * 1.7f, seed, 3);
            float fine = Fbm(Mathf.Cos(ang) * 9f + v, Mathf.Sin(ang) * 9f + v, seed + 4f, 2);
            float n = Fbm(x * 3f, y * 3f, seed + v * 6.3f);
            float reach = 0.38f + Mathf.Pow(spokes, 2f) * 0.9f + (fine - 0.5f) * 0.25f + (n - 0.5f) * 0.2f;
            float a = Mathf.Clamp01(1f - r / reach);
            a = Mathf.Sqrt(a);
            float crater = Mathf.Clamp01(1f - r / 0.22f);
            float g = Mathf.Lerp(0.05f + n * 0.04f, 0.015f, crater);
            return C(g, g * 0.9f, g * 0.85f, Mathf.Max(a * 0.85f, crater) * EdgeFade(x, y));
        }, seed, Soft);
    }

    // ── 유리 ───────────────────────────────────

    /// <summary>
    /// 깨진 유리 (칸 전체가 창문 한 장).
    ///  변형 0,1 = 금 간 유리 (충격점에서 방사형 + 동심원 금, 흰 선)
    ///  변형 2,3 = 완전히 깨진 창 (안은 어둡고 가장자리에 들쭉날쭉한 유리 조각만 남음)
    /// </summary>
    public static Texture2D GlassCrack(int seed)
    {
        var lines = new float[4][];
        return Atlas("Decal_GlassCrack", (x, y, v, rng) =>
        {
            if (v >= 2)
            {
                // 가장자리에서 안쪽으로 뾰족하게 남은 유리: 테두리까지 거리 < 들쭉날쭉한 깊이
                float edgeDist = 1f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
                float along = Mathf.Abs(x) > Mathf.Abs(y) ? y : x;
                // 불규칙한 조각: 넓은 덩어리(저주파) + 뾰족한 끝(고주파), 변마다 다르게 (규칙적인 톱니는 이빨처럼 보였음)
                float sideSeed = (Mathf.Abs(x) > Mathf.Abs(y) ? (x > 0 ? 1f : 2f) : (y > 0 ? 3f : 4f)) * 7.3f + v * 13f;
                float chunk = Mathf.Clamp01(Fbm(along * 1.6f + sideSeed, sideSeed, seed, 3) * 1.8f - 0.45f);
                float spike = Mathf.Pow(Mathf.Clamp01(Fbm(along * 7f + sideSeed, sideSeed * 0.5f, seed + 3f, 2) * 1.6f - 0.3f), 2f);
                float teeth = 0.05f + chunk * 0.45f + spike * 0.25f;
                if (edgeDist < teeth) return C(0.72f, 0.82f, 0.9f, 0.75f * EdgeFade(x, y)); // 남은 유리 조각
                return C(0.04f, 0.045f, 0.05f, 0.88f * EdgeFade(x, y));                    // 뚫린 창 안쪽 (어두움)
            }
            if (lines[v] == null)
            {
                var c = new Vector2(((float)rng.NextDouble() - 0.5f) * 0.8f, ((float)rng.NextDouble() - 0.5f) * 0.8f);
                var segs = CrackPaths(rng, c, 1.6f, 7 + v, 0.008f);
                // 동심원 금 (방사형 사이를 잇는 짧은 선)
                for (int ring = 1; ring <= 2; ring++)
                {
                    float rr = 0.18f * ring + (float)rng.NextDouble() * 0.05f; int n = 9 + ring * 2;
                    for (int k = 0; k < n; k++)
                    {
                        if (rng.NextDouble() < 0.3) continue;
                        float a0 = k * 6.283f / n, a1 = (k + 1) * 6.283f / n;
                        segs.Add((c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * rr, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * rr * 1.05f, 0.006f));
                    }
                }
                lines[v] = RasterLines(segs, 1.6f / Cell);
            }
            float a = MaskAt(lines[v], x, y);
            // 금 + 아주 옅은 먼지 낀 유리 (깨진 창은 뿌옇게)
            float haze = 0.18f * Fbm(x * 2f, y * 2f, seed + v);
            return C(Mathf.Lerp(0.55f, 0.92f, a), Mathf.Lerp(0.55f, 0.94f, a), Mathf.Lerp(0.52f, 0.95f, a), Mathf.Max(a * 0.9f, haze) * EdgeFade(x, y));
        }, seed);
    }

    /// <summary>불꽃 한 장 (아래가 넓고 위로 가늘어지는 물방울 + 일렁임, 가운데 더 밝음). 색은 파티클 색으로</summary>
    public static Texture2D Flame()
    {
        const int S = 64; var px = new Color32[S * S];
        for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
            {
                float x = (i + 0.5f) / S * 2f - 1f, y = (j + 0.5f) / S * 2f - 1f; // y: 아래 -1 → 위 +1
                float h = (y + 1f) * 0.5f;
                float wobble = (Fbm(h * 3f, 0.5f, 21f, 3) - 0.5f) * 0.35f * h;
                float width = 0.62f * Mathf.Pow(1f - h, 0.8f) * Mathf.Clamp01(h * 6f + 0.3f);
                float d = Mathf.Abs(x - wobble);
                float a = Mathf.Clamp01((width - d) / Mathf.Max(0.05f, width * 0.6f));
                a *= Mathf.Clamp01((1f - h) * 4f);
                float core = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, width * 0.5f)) * (1f - h);
                float v = Mathf.Clamp01(0.7f + core * 0.6f);
                px[j * S + i] = new Color(v, v, v, a);
            }
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "FX_FlameShape", wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }

    /// <summary>파티클용 부드러운 동그라미 (가운데 밝고 가장자리 투명)</summary>
    public static Texture2D SoftDot(string name, bool noisy)
    {
        const int S = 64; var px = new Color32[S * S];
        for (int j = 0; j < S; j++)
            for (int i = 0; i < S; i++)
            {
                float x = (i + 0.5f) / S * 2f - 1f, y = (j + 0.5f) / S * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Clamp01(1f - r); a *= a;
                if (noisy) a *= 0.6f + 0.4f * Fbm(x * 2.5f + 3f, y * 2.5f + 3f, 11f);
                px[j * S + i] = new Color(1f, 1f, 1f, a);
            }
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply(false, true);
        return t;
    }
}

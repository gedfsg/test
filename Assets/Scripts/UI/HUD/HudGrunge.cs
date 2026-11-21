using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// (2026-11-22 2차) HUD 레퍼런스(Assets/UI/변경할 ui.png) 질감을 코드로 생성 - 외부 에셋 없음, 고정 시드라 매번 같은 모양.
///  - Panel   : 거의 불투명한 검붉은 배경 + 얼룩·먼지·긁힘 (미션·탄약·손전등 패널)
///  - Frame   : 긁히고 군데군데 끊긴 회색 금속 테두리 + 모서리 각진 보강 (9-slice)
///  - Brush   : 붓으로 거칠게 칠한 검정 띠, 좌우 끝이 찢긴 모양 (시간 패널, 상태 바 뒤)
///  - BarFill : 스텐실처럼 거친 가장자리 + 군데군데 벗겨진 바 (색은 Image.color)
///  - Slot    : 흙먼지·긁힘 있는 핫바 칸
///  - RoughCircle : 가장자리가 거친 원 (진행률 ! 아이콘)
///  - PeeledRing  : 흰 페인트가 벗겨진 어두운 빨강 링 (미니맵)
/// 모든 텍스처는 흰색 기반(RGB 1) + 알파/밝기 변화만 담고, 실제 색은 Image.color로 곱함 (Panel·Slot·Ring은 색까지 구움).
/// </summary>
public static class HudGrunge
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    static float Noise(float x, float y, float s, float ox = 0f, float oy = 0f) => Mathf.PerlinNoise(x * s + ox, y * s + oy);
    static float Fbm(float x, float y, float s, float o = 0f) => Noise(x, y, s, o, o) * 0.55f + Noise(x, y, s * 2.1f, o + 3f, o + 7f) * 0.3f + Noise(x, y, s * 4.7f, o + 11f, o + 5f) * 0.15f;

    static Sprite Make(string key, int w, int h, System.Func<int, int, Color> px, Vector4 border = default)
    {
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HudGrunge_" + key };
        var arr = new Color32[w * h];
        var old = Random.state;
        Random.InitState(key.GetHashCode());
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) arr[y * w + x] = px(x, y);
        Random.state = old;
        tex.SetPixels32(arr);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        cache[key] = s;
        return s;
    }

    // 긁힘 선 목록 (텍스처 좌표 0~1)
    static List<(Vector2 a, Vector2 b, float w, float v)> Scratches(int n, float maxLen, int seed)
    {
        var old = Random.state; Random.InitState(seed);
        var l = new List<(Vector2, Vector2, float, float)>();
        for (int i = 0; i < n; i++)
        {
            var a = new Vector2(Random.value, Random.value);
            float ang = Random.Range(-0.5f, 0.5f) + (Random.value < 0.5f ? 0f : Mathf.PI * 0.5f);
            var b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.03f, maxLen);
            l.Add((a, b, Random.Range(0.0015f, 0.004f), Random.Range(0.25f, 0.7f)));
        }
        Random.state = old;
        return l;
    }
    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>패널 배경: base 색(검붉은)에 얼룩·먼지·긁힘. 9-slice 아님 (늘려 씀)</summary>
    public static Sprite Panel(Color baseColor, float alpha, string variant = "a")
    {
        const int W = 256, H = 256;
        // var scr = Scratches(26, 0.25f, ("panel" + variant).GetHashCode()); // (1회차) 흰 긁힘이 레퍼런스보다 두드러짐
        var scr = Scratches(10, 0.2f, ("panel" + variant).GetHashCode());
        return Make("panel_" + variant + ColorUtility.ToHtmlStringRGBA(baseColor) + alpha, W, H, (x, y) =>
        {
            float u = x / (float)W, v = y / (float)H;
            float mott = Fbm(u, v, 5f, variant.GetHashCode() % 50);         // 얼룩
            Color c = baseColor * (0.75f + 0.5f * mott);
            float dust = Noise(u, v, 90f, 3f, 9f);
            if (dust > 0.8f) c = Color.Lerp(c, new Color(0.45f, 0.4f, 0.36f), (dust - 0.8f) * 1.8f); // 먼지 점
            float red = Fbm(u, v, 3f, 41f);
            if (red > 0.62f) c = Color.Lerp(c, new Color(0.32f, 0.03f, 0.03f), (red - 0.62f) * 1.6f); // 말라붙은 피 얼룩
            var p = new Vector2(u, v);
            foreach (var s in scr) { float d = SegDist(p, s.a, s.b); if (d < s.w) c = Color.Lerp(c, new Color(0.55f, 0.52f, 0.5f), s.v * (1f - d / s.w) * 0.3f); }
            c.a = alpha * (0.93f + 0.07f * mott);
            return c;
        });
    }

    /// <summary>긁힌 회색 금속 테두리 (9-slice, 테두리 두께 thickness px, 모서리 각진 보강)</summary>
    public static Sprite Frame(float thickness = 3f)
    {
        const int S = 96; int b = 16;
        return Make("frame" + thickness, S, S, (x, y) =>
        {
            float dx = Mathf.Min(x, S - 1 - x), dy = Mathf.Min(y, S - 1 - y);
            float d = Mathf.Min(dx, dy);
            bool corner = dx < 14 && dy < 14;
            float th = corner ? thickness + 2f : thickness;   // 모서리 보강
            float a = Mathf.Clamp01(th - d + 0.5f);
            if (corner && dx < 3 && dy < 3) a = 0f;             // 각진 모서리 (깎임)
            float n = Noise(x, y, 0.35f, 5f, 2f);
            a *= n < 0.28f ? 0.25f : 1f;                        // 군데군데 끊김
            float shade = 0.45f + 0.4f * Noise(x, y, 0.9f, 13f, 1f);
            var c = new Color(shade, shade * 0.96f, shade * 0.93f, a);
            return c;
        }, new Vector4(b, b, b, b));
    }

    /// <summary>붓질 검정 띠 (좌우 끝 찢김, 위아래 거침). 흰색 기반 → Image.color로 검정</summary>
    public static Sprite Brush(string variant = "a")
    {
        const int W = 512, H = 96;
        int o = variant.GetHashCode() % 97;
        return Make("brush_" + variant, W, H, (x, y) =>
        {
            float u = x / (float)W, v = y / (float)H;
            float edgeL = 0.03f + 0.06f * Fbm(0f, v, 7f, o);         // 왼쪽 찢긴 끝
            float edgeR = 0.97f - 0.07f * Fbm(1f, v, 7f, o + 20);
            float top = 0.92f - 0.1f * Fbm(u, 0f, 9f, o + 40), bot = 0.08f + 0.1f * Fbm(u, 1f, 9f, o + 60);
            float a = Mathf.Clamp01((u - edgeL) * 40f) * Mathf.Clamp01((edgeR - u) * 40f) * Mathf.Clamp01((top - v) * 25f) * Mathf.Clamp01((v - bot) * 25f);
            float streak = Noise(u * 0.3f, v, 18f, o + 5f, 0f);       // 붓결 (가로 줄무늬)
            // a *= 0.78f + 0.22f * streak; // (1회차) 띠가 반투명해 뒤 피 번짐이 비쳐 갈색으로 보였음
            a *= 0.92f + 0.08f * streak;
            float holes = Noise(u, v, 40f, o + 9f, 4f);
            if (holes > 0.78f && (u < edgeL + 0.12f || u > edgeR - 0.12f)) a *= 0.3f; // 끝부분 듬성듬성
            return new Color(1f, 1f, 1f, a);
        });
    }

    /// <summary>스텐실 바: 거친 위아래 가장자리 + 벗겨진 점. 흰색 기반 (Image.color로 색), Filled로 씀</summary>
    public static Sprite BarFill()
    {
        const int W = 512, H = 32;
        return Make("barfill", W, H, (x, y) =>
        {
            float u = x / (float)W, v = y / (float)H;
            float top = 0.9f - 0.12f * Noise(u, 0f, 60f, 1f, 0f), bot = 0.1f + 0.12f * Noise(u, 1f, 60f, 7f, 0f);
            float a = Mathf.Clamp01((top - v) * 30f) * Mathf.Clamp01((v - bot) * 30f);
            float chip = Noise(u, v, 70f, 3f, 8f);
            if (chip > 0.74f) a *= 0.15f;                    // 벗겨진 자리
            float tone = 0.82f + 0.18f * Noise(u, v, 25f, 9f, 1f);
            return new Color(tone, tone, tone, a);
        });
    }

    /// <summary>빈 바 홈: 아주 어두운 바탕 + 흐린 점선 윤곽 (흰색 기반)</summary>
    public static Sprite BarTrack()
    {
        const int W = 512, H = 32;
        return Make("bartrack", W, H, (x, y) =>
        {
            float u = x / (float)W, v = y / (float)H;
            bool edgeY = y <= 4 || y >= H - 5, edgeX = x <= 2 || x >= W - 3;
            float a = 0.85f;
            float rgb = 0.03f;
            if ((edgeY && (x / 6) % 2 == 0 && y > 2 && y < H - 3) || (edgeX && y > 3 && y < H - 4)) { rgb = 1f; a = 0.5f; } // 점선 윤곽 (색은 Image.color가 곱해짐)
            if (y < 3 || y > H - 4) a = 0f;
            return new Color(rgb, rgb, rgb, a);
        });
    }

    /// <summary>핫바 칸: 흙먼지·긁힘 + 위쪽 닳은 밝은 가장자리</summary>
    public static Sprite Slot(bool active)
    {
        const int S = 128;
        var scr = Scratches(14, 0.35f, active ? 77 : 55);
        Color baseC = active ? new Color32(0x64, 0x63, 0x63, 0xFF) : new Color32(0x0E, 0x0A, 0x04, 0xFF);
        return Make(active ? "slot_on" : "slot_off", S, S, (x, y) =>
        {
            float u = x / (float)S, v = y / (float)S;
            float mott = Fbm(u, v, 4f, active ? 3f : 8f);
            Color c = baseC * (0.8f + 0.4f * mott);
            if (active) c = Color.Lerp(c, new Color(0.75f, 0.75f, 0.76f), Mathf.Clamp01(v - 0.5f) * 0.3f); // 금속 광택
            float dust = Noise(u, v, 70f, 1f, 4f);
            if (!active && dust > 0.78f) c = Color.Lerp(c, new Color(0.5f, 0.45f, 0.4f), (dust - 0.78f) * 2f);
            var p = new Vector2(u, v);
            foreach (var s in scr) { float d = SegDist(p, s.a, s.b); if (d < s.w) c = Color.Lerp(c, new Color(0.6f, 0.58f, 0.56f), s.v * (1f - d / s.w) * 0.7f); }
            // 위쪽 닳은 가장자리 (레퍼런스: 칸 윗변이 밝게 해짐)
            float edge = Mathf.Min(Mathf.Min(x, S - 1 - x), Mathf.Min(y, S - 1 - y));
            if (edge < 3) c = Color.Lerp(c, active ? new Color(0.85f, 0.85f, 0.86f) : new Color(0.38f, 0.33f, 0.3f), (active ? 0.9f : 0.45f) * Noise(u, v, 20f, 2f, 2f) + (active ? 0.1f : 0f));
            if (edge < 1) c.a = 0.6f; else c.a = active ? 0.97f : 0.9f;
            // 모서리 둥글게 (6px)
            float cx = Mathf.Max(0, 6 - Mathf.Min(x, S - 1 - x)), cy = Mathf.Max(0, 6 - Mathf.Min(y, S - 1 - y));
            if (cx * cx + cy * cy > 36) c.a = 0f;
            return c;
        });
    }

    /// <summary>가장자리 거친 원 (흰색 기반, ! 아이콘·N 배지)</summary>
    public static Sprite RoughCircle()
    {
        const int S = 64;
        return Make("roughcircle", S, S, (x, y) =>
        {
            Vector2 p = new Vector2(x + 0.5f - S / 2f, y + 0.5f - S / 2f);
            float ang = Mathf.Atan2(p.y, p.x);
            float r = (S / 2f - 3f) * (0.93f + 0.07f * Noise(Mathf.Cos(ang), Mathf.Sin(ang), 3f, 4f, 4f));
            float a = Mathf.Clamp01(r - p.magnitude + 0.5f);
            if (Noise(x, y, 0.5f, 9f, 9f) > 0.82f && p.magnitude > r - 4f) a *= 0.4f;
            return new Color(1f, 1f, 1f, a);
        });
    }

    /// <summary>미니맵 링: 어두운 빨강(#320509) 두께 약 6px + 흰 페인트 벗겨짐 (색까지 구움)</summary>
    public static Sprite PeeledRing()
    {
        const int S = 256;
        return Make("peeledring", S, S, (x, y) =>
        {
            Vector2 p = new Vector2(x + 0.5f - S / 2f, y + 0.5f - S / 2f);
            float d = p.magnitude, ang = Mathf.Atan2(p.y, p.x);
            // float outer = S / 2f - 2f, thick = 9f + 2f * Noise(...); // (1회차) 레퍼런스보다 얇음
            float outer = S / 2f - 2f, thick = 15f + 3f * Noise(Mathf.Cos(ang) * 3f, Mathf.Sin(ang) * 3f, 1f, 2f, 2f);
            float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - (outer - thick) + 0.5f);
            Color c = new Color32(0x3A, 0x06, 0x0A, 0xFF);
            c *= 0.8f + 0.4f * Noise(x, y, 0.15f, 1f, 1f);
            // 흰 페인트 벗겨짐: 왼쪽 위·왼쪽에 몰림 (레퍼런스)
            float peel = Noise(x, y, 0.08f, 6f, 3f) * (0.6f + 0.6f * Mathf.Clamp01(-Mathf.Cos(ang - 2.4f)));
            if (peel > 0.62f) c = Color.Lerp(c, new Color(0.82f, 0.8f, 0.78f), Mathf.Clamp01((peel - 0.62f) * 5f));
            c.a = a;
            return c;
        });
    }

    /// <summary>
    /// 화면 가장자리 피 튀김 (색까지 구움, 알파 포함). Resources/HUD/Blood/blood_vignette.png가 있으면 그걸 우선 사용 (CC0 이미지 교체용).
    /// 코드 생성: 좌·하단·우상단 모서리에 진하게, 큰 얼룩 + 방울 + 미세 점 + 흘러내림, 아래쪽 더 진하게, 색 농담 (#3C0A06 ~ #C0101A)
    /// </summary>
    public static Sprite BloodVignette()
    {
        var tex = Resources.Load<Texture2D>("HUD/Blood/blood_vignette");
        if (tex != null) return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        const int W = 640, H = 360;
        if (cache.TryGetValue("blood", out var cs) && cs != null) return cs;
        var a = new float[W * H]; var shade = new float[W * H];
        var old = Random.state; Random.InitState(1122);
        // 모서리별 밀도 (레퍼런스: 왼쪽·좌하단 가장 진함, 우상단·아래쪽 중간, 위쪽 가운데 약함)
        float Density(float u, float v)
        {
            // (2회차) 띠를 얇게 - 레퍼런스는 가장자리 가는 띠 + 모서리 덩어리
            float left = Mathf.Clamp01(1f - u / 0.11f), right = Mathf.Clamp01(1f - (1f - u) / 0.08f);
            float bottom = Mathf.Clamp01(1f - v / 0.12f), top = Mathf.Clamp01(1f - (1f - v) / 0.06f);
            float tr = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(1f, 1f)) / 0.28f);
            float bl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0f, 0f)) / 0.35f);
            // return Mathf.Max(Mathf.Max(left * 1.0f, bottom * 0.85f), ...); // (2회차) 아래쪽 띠가 뿌연 안개처럼 보였음
            return Mathf.Max(Mathf.Max(left * 1.0f, bottom * 0.62f), Mathf.Max(Mathf.Max(right * 0.6f, top * 0.45f), Mathf.Max(tr * 1.0f, bl * 1.1f)));
        }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                float den = Density(u, v);
                float n = Fbm(u * 1.7f, v, 6f, 2f);
                // float body = Mathf.Clamp01((den * 1.25f + (n - 0.5f) * 0.9f - 0.45f) * 3.2f); // (1회차) 경계가 흐려 안개처럼 보였음
                // float body = Mathf.Clamp01((den * 1.25f + (n - 0.5f) * 0.9f - 0.5f) * 9f); // (2회차)
                float body = Mathf.Clamp01((den * 1.25f + (n - 0.5f) * 1.1f - 0.55f) * 16f);   // (3회차) 경계 더 선명, 덩어리 더 들쭉날쭉
                float breakup = Noise(u * 1.8f, v, 55f, 8f, 3f);                                   // 덩어리 안을 듬성듬성 (스플래터 질감)
                body *= breakup > 0.42f ? 1f : Mathf.Lerp(0.25f, 1f, breakup / 0.42f);
                a[y * W + x] = body;
                shade[y * W + x] = Fbm(u, v, 14f, 30f);
            }
        void Splat(Vector2 c, float r, float s)
        {
            int x0 = Mathf.Max(0, (int)(c.x - r - 2)), x1 = Mathf.Min(W - 1, (int)(c.x + r + 2)), y0 = Mathf.Max(0, (int)(c.y - r - 2)), y1 = Mathf.Min(H - 1, (int)(c.y + r + 2));
            float wob = Random.value * 10f;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x, y) - c; float ang = Mathf.Atan2(p.y, p.x);
                    float rr = r * (0.75f + 0.35f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.5f + wob, Mathf.Sin(ang) * 1.5f + wob));
                    float d = p.magnitude / rr; if (d >= 1f) continue;
                    float val = Mathf.Clamp01((1f - d) * 6f) * s; int k = y * W + x; if (val > a[k]) a[k] = val;
                }
        }
        // 방울·미세 점: 밀도 높은 곳 근처일수록 많이
        for (int i = 0; i < 6000; i++) // (2회차) 2600 → 6000 미세 점
        {
            float u = Random.value, v = Random.value;
            float den = Density(u, v);
            // if (Random.value > den * 0.9f + 0.02f) continue; // (1회차) 화면 가운데까지 큰 방울이 흩어졌음
            if (den < 0.12f || Random.value > den) continue;              // 가장자리 근처에만
            float r = (Random.value < 0.04f && den > 0.45f) ? Random.Range(3f, 7f) : Random.Range(0.6f, 2.0f);
            Splat(new Vector2(u * W, v * H), r, Random.Range(0.55f, 1f));
        }
        // 흘러내림 (위·왼쪽 가장자리에서 아래로)
        for (int i = 0; i < 18; i++)
        {
            bool fromTop = Random.value < 0.6f;
            float x = fromTop ? Random.Range(0f, W) : Random.Range(0f, W * 0.1f);
            float y = fromTop ? H - 1 : Random.Range(H * 0.3f, H * 0.95f);
            float len = Random.Range(12f, 60f), w = Random.Range(1f, 2.4f);
            for (float t = 0; t < len; t += 0.5f) Splat(new Vector2(x + Mathf.Sin(t * 0.12f) * 1.5f, y - t), w * (1f - 0.5f * t / len), 0.9f);
            Splat(new Vector2(x, y - len), w * 1.8f, 0.9f); // 끝 방울
        }
        Random.state = old;
        var t2 = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "HudGrunge_blood" };
        var px = new Color32[W * H];
        // Color dark = new Color32(0x2A, 0x05, 0x04, 0xFF), mid = new Color32(0x7A, 0x0A, 0x0C, 0xFF), bright = new Color32(0xC0, 0x10, 0x1A, 0xFF); // (1회차)
        Color dark = new Color32(0x16, 0x03, 0x02, 0xFF), mid = new Color32(0x58, 0x08, 0x08, 0xFF), bright = new Color32(0xB0, 0x0E, 0x16, 0xFF); // (2회차) 더 어둡게
        for (int i = 0; i < px.Length; i++)
        {
            float s = shade[i];
            // Color c = s < 0.5f ? Color.Lerp(dark, mid, s * 2f) : Color.Lerp(mid, bright, (s - 0.5f) * 2f); // (1회차) 너무 밝음
            // 대부분 말라붙은 어두운 피, 밝은 빨강은 일부만 (레퍼런스 왼쪽 #3C0A06 ~ 얼룩 하이라이트)
            // Color c = s < 0.62f ? ... // (2회차)
            Color c = s < 0.7f ? Color.Lerp(dark, mid, s / 0.7f) : Color.Lerp(mid, bright, (s - 0.7f) / 0.3f); // (3회차) 밝은 빨강 더 드물게
            c.a = Mathf.Clamp01(a[i]);
            px[i] = c;
        }
        t2.SetPixels32(px); t2.Apply();
        var sp = Sprite.Create(t2, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
        cache["blood"] = sp;
        return sp;
    }
}

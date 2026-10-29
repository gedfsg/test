using TMPro;
using UnityEngine;

/// <summary>
/// HUD 공용 스타일 (레퍼런스 UI 기준 팔레트 + 폰트 + 스프라이트).
/// 에셋은 Tools → HUD → Build HUD Assets 로 생성되어 Assets/Resources/HUD 에 있음.
/// </summary>
public static class HudStyle
{
    // ── 팔레트 ──
    public static readonly Color PanelBg   = new Color(0f, 0f, 0f, 0.7f);              // #000000 α0.7
    public static readonly Color Text      = Color.white;                               // #FFFFFF
    public static readonly Color TextDim   = new Color(1f, 1f, 1f, 0.6f);
    public static readonly Color Highlight = new Color32(0xFF, 0xD7, 0x00, 0xFF);       // #FFD700
    public static readonly Color Health    = new Color32(0xFF, 0x44, 0x44, 0xFF);       // #FF4444
    public static readonly Color Energy    = new Color32(0xFF, 0xA5, 0x00, 0xFF);       // #FFA500
    public static readonly Color Water     = new Color32(0x4A, 0x9E, 0xFF, 0xFF);       // #4A9EFF
    public static readonly Color Food      = new Color32(0x4A, 0xDE, 0x80, 0xFF);       // #4ADE80
    public static readonly Color Inactive  = new Color(1f, 1f, 1f, 0.35f);              // 지난/미래 날짜, 빈 슬롯 테두리
    public static readonly Color BarTrack  = new Color(1f, 1f, 1f, 0.12f);              // 바 배경 홈

    // ── 폰트 (Pretendard) ──
    static TMP_FontAsset regular, semiBold, bold;
    public static TMP_FontAsset Regular  => regular  != null ? regular  : regular  = Resources.Load<TMP_FontAsset>("HUD/Fonts/Pretendard-Regular SDF");
    public static TMP_FontAsset SemiBold => semiBold != null ? semiBold : semiBold = Resources.Load<TMP_FontAsset>("HUD/Fonts/Pretendard-SemiBold SDF");
    public static TMP_FontAsset Bold     => bold     != null ? bold     : bold     = Resources.Load<TMP_FontAsset>("HUD/Fonts/Pretendard-Bold SDF");

    // ── 스프라이트 ──
    static Sprite roundedRect, circle, ring, triangle, metalRing, navArrow, sun, moon, softShadow;
    static Sprite heart, bolt, drop, forkKnife, ringThick, roundedFrame, roundedFrameThick;
    public static Sprite Heart => Load(ref heart, "Heart");
    public static Sprite Bolt => Load(ref bolt, "Bolt");
    public static Sprite Drop => Load(ref drop, "Drop");
    public static Sprite ForkKnife => Load(ref forkKnife, "ForkKnife");
    public static Sprite RingThick => Load(ref ringThick, "RingThick");
    public static Sprite RoundedFrame => Load(ref roundedFrame, "RoundedFrame");
    public static Sprite RoundedFrameThick => Load(ref roundedFrameThick, "RoundedFrameThick");
    static Sprite bulletP, bulletAR, shellSG, bulletSR, flashlight;
    public static Sprite BulletP => Load(ref bulletP, "BulletP");
    public static Sprite BulletAR => Load(ref bulletAR, "BulletAR");
    public static Sprite ShellSG => Load(ref shellSG, "ShellSG");
    public static Sprite BulletSR => Load(ref bulletSR, "BulletSR");
    public static Sprite Flashlight => Load(ref flashlight, "Flashlight");
    static Sprite Load(ref Sprite cache, string name) => cache != null ? cache : cache = Resources.Load<Sprite>("HUD/Sprites/" + name);
    public static Sprite Sun         => sun         != null ? sun         : sun         = Resources.Load<Sprite>("HUD/Sprites/Sun");
    public static Sprite Moon        => moon        != null ? moon        : moon        = Resources.Load<Sprite>("HUD/Sprites/Moon");
    public static Sprite SoftShadow  => softShadow  != null ? softShadow  : softShadow  = Resources.Load<Sprite>("HUD/Sprites/SoftShadow");
    public static Sprite MetalRing   => metalRing   != null ? metalRing   : metalRing   = Resources.Load<Sprite>("HUD/Sprites/MetalRing");
    public static Sprite NavArrow    => navArrow    != null ? navArrow    : navArrow    = Resources.Load<Sprite>("HUD/Sprites/NavArrow");
    public static Sprite RoundedRect => roundedRect != null ? roundedRect : roundedRect = Resources.Load<Sprite>("HUD/Sprites/RoundedRect");
    public static Sprite Circle      => circle      != null ? circle      : circle      = Resources.Load<Sprite>("HUD/Sprites/Circle");
    public static Sprite Ring        => ring        != null ? ring        : ring        = Resources.Load<Sprite>("HUD/Sprites/Ring");
    public static Sprite Triangle    => triangle    != null ? triangle    : triangle    = Resources.Load<Sprite>("HUD/Sprites/Triangle");

    // ── 코드로 HUD 만들 때 쓰는 헬퍼 (위치/크기는 레퍼런스 px, 부모 가운데 기준) ──

    public static RectTransform NewRect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static UnityEngine.UI.Image NewImage(Transform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
    {
        var rt = NewRect(parent, name, pos, size);
        var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
        return img;
    }

    public static TextMeshProUGUI NewText(Transform parent, string name, string text, TMP_FontAsset font, float fontSize,
        Color color, TextAlignmentOptions align, Vector2 pos, Vector2 size)
    {
        var rt = NewRect(parent, name, pos, size);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.text = text;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }
}

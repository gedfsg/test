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
    static Sprite roundedRect, circle, ring, triangle, metalRing, navArrow;
    public static Sprite MetalRing   => metalRing   != null ? metalRing   : metalRing   = Resources.Load<Sprite>("HUD/Sprites/MetalRing");
    public static Sprite NavArrow    => navArrow    != null ? navArrow    : navArrow    = Resources.Load<Sprite>("HUD/Sprites/NavArrow");
    public static Sprite RoundedRect => roundedRect != null ? roundedRect : roundedRect = Resources.Load<Sprite>("HUD/Sprites/RoundedRect");
    public static Sprite Circle      => circle      != null ? circle      : circle      = Resources.Load<Sprite>("HUD/Sprites/Circle");
    public static Sprite Ring        => ring        != null ? ring        : ring        = Resources.Load<Sprite>("HUD/Sprites/Ring");
    public static Sprite Triangle    => triangle    != null ? triangle    : triangle    = Resources.Load<Sprite>("HUD/Sprites/Triangle");
}

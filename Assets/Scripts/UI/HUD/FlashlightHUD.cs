using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 우하단 손전등 패널 (레퍼런스 UI 섹션 6):  🔦  ON [T]  ▮▮▮▮
/// DayNightCycle의 손전등 상태(FlashlightOn)와 배터리(Battery01)를 읽기만 함.
/// 좌표는 레퍼런스(1681x936) 패널 중심 (1544, 785.5) 기준. → Tools/HUD/Apply Layout/6.
/// </summary>
public class FlashlightHUD : MonoBehaviour
{
    [Header("색상 (레퍼런스 샘플링)")]
    public Color panelBg = new Color32(32, 34, 38, 225);
    public Color panelBorder = new Color32(100, 98, 96, 255);
    public Color iconColor = new Color32(0xF8, 0xE9, 0xC1, 0xFF);
    public Color onText = Color.white;
    public Color offText = new Color(1f, 1f, 1f, 0.45f);
    public Color batteryOutline = new Color32(170, 165, 160, 255);
    public Color barColor = new Color32(0xFB, 0xB4, 0x3C, 0xFF);
    public Color barLowColor = new Color32(0xFC, 0x62, 0x60, 0xFF);
    public Color barEmptyColor = new Color(1f, 1f, 1f, 0.08f);

    // 레퍼런스 배터리 칸 중심 x (패널 중심 기준)
    static readonly float[] BarX = { 39f, 51f, 63.5f, 76f };

    TextMeshProUGUI stateText;
    Image icon;
    Image[] bars;

    void Start()
    {
        Transform root = transform;
        var bg = HudStyle.NewImage(root, "Bg", HudStyle.RoundedRect, panelBg, Vector2.zero, new Vector2(222f, 45f));
        bg.pixelsPerUnitMultiplier = 2f;
        var border = HudStyle.NewImage(root, "Border", HudStyle.RoundedFrame, panelBorder, Vector2.zero, new Vector2(222f, 45f));
        border.pixelsPerUnitMultiplier = 2f;

        icon = HudStyle.NewImage(root, "Icon", HudStyle.Flashlight, iconColor, new Vector2(-85.5f, -1.5f), new Vector2(30f, 32f));
        stateText = HudStyle.NewText(root, "State", "ON  [T]", HudStyle.Regular, 18f, onText,
            TextAlignmentOptions.Left, new Vector2(-56f + 45f, -0.5f), new Vector2(90f, 26f));

        // 배터리: 외곽선(67x25) + 오른쪽 꼭지 + 4칸
        var outline = HudStyle.NewImage(root, "Battery", HudStyle.RoundedFrame, batteryOutline, new Vector2(63.5f, 0f), new Vector2(67f, 25f));
        outline.pixelsPerUnitMultiplier = 3f;
        HudStyle.NewImage(root, "BatteryNub", null, batteryOutline, new Vector2(100f, 0f), new Vector2(4f, 10f));
        bars = new Image[BarX.Length];
        for (int i = 0; i < BarX.Length; i++)
            bars[i] = HudStyle.NewImage(root, $"Bar{i + 1}", null, barColor, new Vector2(BarX[i], -0.5f), new Vector2(9f, 16f));
    }

    void Update()
    {
        var dnc = DayNightCycle.Instance;
        if (dnc == null || bars == null) return;

        bool on = dnc.FlashlightOn;
        stateText.text = on ? "ON  [T]" : "OFF [T]";
        stateText.color = on ? onText : offText;
        icon.color = on ? iconColor : offText;

        // 0~25% = 1칸 … 75~100% = 4칸, 1칸 이하일 때 빨간색
        int lit = Mathf.CeilToInt(dnc.Battery01 * bars.Length);
        for (int i = 0; i < bars.Length; i++)
            bars[i].color = i < lit ? (lit <= 1 ? barLowColor : barColor) : barEmptyColor;
    }
}

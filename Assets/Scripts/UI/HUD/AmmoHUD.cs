using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 우하단 현재 무기 탄약 패널 (레퍼런스 UI 섹션 6):
///   🔫  24 / 0
///   ──────────
///     돌격소총
/// 무기는 AmmoUI.Instance.targetRangedWeapon(팀원 AmmoUI가 관리)을 읽고, 예비 탄은 AmmoInventory에서 읽음.
/// 기존 AmmoHud는 AmmoUI 싱글톤(크로스헤어도 사용)이 계속 돌도록 오브젝트는 켜두고 투명하게만 함.
/// 좌표는 레퍼런스(1681x936) 패널 중심 (1544, 863.5) 기준. → Tools/HUD/Apply Layout/6.
/// </summary>
public class AmmoHUD : MonoBehaviour
{
    [Header("색상 (레퍼런스 샘플링)")]
    public Color panelBg = new Color32(32, 34, 38, 225);
    public Color panelBorder = new Color32(100, 98, 96, 255);
    public Color loadedColor = Color.white;
    public Color loadedEmptyColor = new Color32(0xFC, 0x62, 0x60, 0xFF);
    public Color reserveColor = new Color(1f, 1f, 1f, 0.75f);
    public Color separatorColor = new Color32(150, 150, 150, 255);

    TextMeshProUGUI loadedText, reserveText, nameText;
    Image icon;

    void Start()
    {
        Transform root = transform;
        var bg = HudStyle.NewImage(root, "Bg", HudStyle.RoundedRect, panelBg, Vector2.zero, new Vector2(222f, 97f));
        bg.pixelsPerUnitMultiplier = 2f;
        var border = HudStyle.NewImage(root, "Border", HudStyle.RoundedFrame, panelBorder, Vector2.zero, new Vector2(222f, 97f));
        border.pixelsPerUnitMultiplier = 2f;

        // 탄 아이콘 스프라이트에 2색이 구워져 있으므로 틴트는 흰색
        icon = HudStyle.NewImage(root, "Icon", HudStyle.BulletAR, Color.white, new Vector2(-77.5f, 16.5f), new Vector2(41f, 47f));
        // 장전 탄: 오른쪽 끝이 레퍼런스 x 1582 → +38
        loadedText = HudStyle.NewText(root, "Loaded", "24", HudStyle.Bold, 46f, loadedColor,
            TextAlignmentOptions.Right, new Vector2(38f - 50f, 17f), new Vector2(100f, 50f));
        reserveText = HudStyle.NewText(root, "Reserve", "/ 0", HudStyle.Regular, 26f, reserveColor,
            TextAlignmentOptions.Left, new Vector2(48f + 30f, 12f), new Vector2(60f, 32f));
        HudStyle.NewImage(root, "Separator", null, separatorColor, new Vector2(-0.5f, -12.5f), new Vector2(187f, 1.5f));
        nameText = HudStyle.NewText(root, "WeaponName", "돌격소총", HudStyle.Regular, 19f, Color.white,
            TextAlignmentOptions.Center, new Vector2(-1f, -31.5f), new Vector2(200f, 26f));
    }

    void Update()
    {
        if (loadedText == null) return;

        Weapon weapon = AmmoUI.Instance != null ? AmmoUI.Instance.targetRangedWeapon : null;
        if (weapon == null || !weapon.gameObject.activeInHierarchy || weapon.weaponData == null)
        {
            loadedText.text = "-";
            reserveText.text = "/ -";
            nameText.text = "무기 없음";
            loadedText.color = loadedColor;
            return;
        }

        WeaponType type = weapon.weaponData.type;
        int loaded = weapon.GetCurrentAmmo();
        int reserve = AmmoInventory.Instance != null ? AmmoInventory.Instance.GetAmmo(type) : 0;

        loadedText.text = loaded.ToString();
        loadedText.color = loaded > 0 ? loadedColor : loadedEmptyColor;
        reserveText.text = $"/ {reserve}";
        nameText.text = KoreanName(type);
        icon.sprite = TypeIcon(type);
    }

    static string KoreanName(WeaponType type) => type switch
    {
        WeaponType.Pistol => "권총",
        WeaponType.AR => "돌격소총",
        WeaponType.Shotgun => "산탄총",
        WeaponType.Sniper => "저격소총",
        WeaponType.Melee => "근접무기",
        _ => "총기",
    };

    static Sprite TypeIcon(WeaponType type) => type switch
    {
        WeaponType.Pistol => HudStyle.BulletP,
        WeaponType.Shotgun => HudStyle.ShellSG,
        WeaponType.Sniper => HudStyle.BulletSR,
        _ => HudStyle.BulletAR,
    };
}

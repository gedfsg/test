using TMPro;
using UnityEngine;

/// <summary>
/// 현재 장착 무기의 탄약(장전/예비) 텍스트 표시. 싱글톤.
/// 장전된 탄이 0이면 경고색으로 바뀐다.
/// </summary>
public class AmmoUI : MonoBehaviour
{
    public static AmmoUI Instance { get; private set; }

    [Header("References")]
    public TextMeshProUGUI ammoText;
    public Weapon targetRangedWeapon;

    [Tooltip("색상 팔레트. 비워두면 아래 기본값을 사용한다.")]
    public UITheme theme;

    [Header("색상")]
    [Tooltip("장전된 탄이 남아 있을 때의 색.")]
    public Color normalColor = Color.white;

    [Tooltip("장전된 탄이 0일 때의 색.")]
    public Color emptyColor = new Color(0.85f, 0.25f, 0.20f, 1f);

    [Tooltip("켜면 예비 탄까지 0일 때만 경고색을 쓴다.")]
    public bool warnOnlyWhenReserveEmpty = false;

    void Awake() => Instance = this;

    void Update() => Refresh();

    public void Refresh()
    {
        if (ammoText == null) return;

        if (targetRangedWeapon == null || !targetRangedWeapon.gameObject.activeInHierarchy)
        {
            ammoText.text = "";
            return;
        }

        WeaponType type = targetRangedWeapon.weaponData != null
            ? targetRangedWeapon.weaponData.type
            : WeaponType.Pistol;

        int loaded = targetRangedWeapon.GetCurrentAmmo();
        int reserve = AmmoInventory.Instance != null
            ? AmmoInventory.Instance.GetAmmo(type)
            : 0;

        ammoText.text = $"{loaded} / {reserve}";

        bool isEmpty = warnOnlyWhenReserveEmpty
            ? (loaded <= 0 && reserve <= 0)
            : loaded <= 0;

        ammoText.color = isEmpty
            ? emptyColor
            : (theme != null ? theme.textAmmoAvailable : normalColor);
    }
}
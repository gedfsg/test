using UnityEngine;

/// <summary>
/// UI 전역 색상/스타일 팔레트. 모든 UI 스크립트는 색상을 직접 하드코딩하지 않고
/// 이 에셋을 참조한다. 디자인을 바꿀 땐 코드가 아니라 이 에셋(Inspector)만 수정하면 된다.
///
/// 에셋 생성: Project 창 우클릭 → Create → UI → UI Theme
/// 적용: 각 UI 프리팹/컴포넌트의 "theme" 필드에 드래그해서 연결.
///
/// 현재 팔레트: 탑뷰 좀비 서바이벌 톤 (카키/올리브 + 녹슨 오렌지, 저채도 다크 배경)
/// 이미 만든 에셋에 새 팔레트를 한번에 적용하려면, 이 컴포넌트 우클릭(⋮) →
/// "Apply Survival Preset" 클릭.
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = "UI/UI Theme")]
public class UITheme : ScriptableObject
{
    [Header("Slot - 상태별 배경색")]
    public Color slotEmpty = new Color(0.07f, 0.08f, 0.06f, 0.90f);
    public Color slotFilled = new Color(0.14f, 0.15f, 0.11f, 0.95f);
    public Color slotActive = new Color(0.55f, 0.42f, 0.12f, 0.95f);
    public Color slotOutlineDefault = new Color(0.35f, 0.36f, 0.28f, 0.8f);

    [Header("Slot - 아이템 타입별 배경/테두리")]
    public Color consumableBg = new Color(0.13f, 0.18f, 0.10f, 1f);
    public Color consumableOutline = new Color(0.42f, 0.55f, 0.25f, 0.8f);
    public Color weaponBg = new Color(0.20f, 0.13f, 0.09f, 1f);
    public Color weaponOutline = new Color(0.65f, 0.32f, 0.14f, 0.85f);
    public Color materialBg = new Color(0.17f, 0.15f, 0.09f, 1f);
    public Color materialOutline = new Color(0.60f, 0.50f, 0.22f, 0.8f);
    public Color defaultItemBg = new Color(0.15f, 0.16f, 0.13f, 1f);
    public Color defaultItemOutline = new Color(0.38f, 0.39f, 0.32f, 0.8f);

    [Header("Text")]
    public Color textPrimary = new Color(0.92f, 0.93f, 0.88f, 1f);
    public Color textSecondary = new Color(0.70f, 0.71f, 0.64f, 1f);
    public Color textMuted = new Color(0.50f, 0.51f, 0.45f, 1f);
    public Color textAmount = new Color(0.85f, 0.70f, 0.30f, 1f);
    [Tooltip("탄약 패널의 종류 라벨(P, AR, SG, SR) 색")]
    public Color textAmmoLabel = new Color(0.85f, 0.72f, 0.32f, 1f);
    [Tooltip("탄약이 1발 이상 있을 때의 수치 색")]
    public Color textAmmoAvailable = Color.white;
    public Color textAmmoEmpty = new Color(0.42f, 0.42f, 0.38f, 1f);

    [Header("HUD - 체력/스태미나")]
    public Color healthHigh = new Color(0.45f, 0.60f, 0.20f, 1f);   // 올리브 그린
    public Color healthLow = new Color(0.70f, 0.20f, 0.12f, 1f);    // 탁한 적색
    public Color staminaFill = new Color(0.75f, 0.60f, 0.20f, 1f);  // 머스타드/카키 옐로우

    [Header("Context Menu 버튼")]
    public Color buttonNormal = new Color(0.13f, 0.14f, 0.11f, 1f);
    public Color buttonHover = new Color(0.45f, 0.36f, 0.14f, 1f);
    public Color buttonText = new Color(0.90f, 0.90f, 0.84f, 1f);
    public Color panelBackground = new Color(0.08f, 0.09f, 0.07f, 0.97f);
    public Color panelOutline = new Color(0.45f, 0.40f, 0.20f, 0.85f);

    /// <summary>아이템 타입에 맞는 (배경색, 테두리색) 조합을 반환한다.</summary>
    public (Color bg, Color outline) GetItemTypeColors(ItemType type)
    {
        switch (type)
        {
            case ItemType.Consumable: return (consumableBg, consumableOutline);
            case ItemType.Weapon: return (weaponBg, weaponOutline);
            case ItemType.Material: return (materialBg, materialOutline);
            default: return (defaultItemBg, defaultItemOutline);
        }
    }

    /// <summary>체력 비율(0~1)에 따라 healthLow~healthHigh 사이를 보간한 색을 반환한다.</summary>
    public Color GetHealthColor(float normalizedHealth) => Color.Lerp(healthLow, healthHigh, normalizedHealth);

#if UNITY_EDITOR
    [ContextMenu("Apply Survival Preset")]
    private void ApplySurvivalPreset()
    {
        slotEmpty = new Color(0.07f, 0.08f, 0.06f, 0.90f);
        slotFilled = new Color(0.14f, 0.15f, 0.11f, 0.95f);
        slotActive = new Color(0.55f, 0.42f, 0.12f, 0.95f);
        slotOutlineDefault = new Color(0.35f, 0.36f, 0.28f, 0.8f);

        consumableBg = new Color(0.13f, 0.18f, 0.10f, 1f);
        consumableOutline = new Color(0.42f, 0.55f, 0.25f, 0.8f);
        weaponBg = new Color(0.20f, 0.13f, 0.09f, 1f);
        weaponOutline = new Color(0.65f, 0.32f, 0.14f, 0.85f);
        materialBg = new Color(0.17f, 0.15f, 0.09f, 1f);
        materialOutline = new Color(0.60f, 0.50f, 0.22f, 0.8f);
        defaultItemBg = new Color(0.15f, 0.16f, 0.13f, 1f);
        defaultItemOutline = new Color(0.38f, 0.39f, 0.32f, 0.8f);

        textPrimary = new Color(0.92f, 0.93f, 0.88f, 1f);
        textSecondary = new Color(0.70f, 0.71f, 0.64f, 1f);
        textMuted = new Color(0.50f, 0.51f, 0.45f, 1f);
        textAmount = new Color(0.85f, 0.70f, 0.30f, 1f);
        textAmmoLabel = new Color(0.85f, 0.72f, 0.32f, 1f);
        textAmmoAvailable = Color.white;
        textAmmoEmpty = new Color(0.42f, 0.42f, 0.38f, 1f);

        healthHigh = new Color(0.45f, 0.60f, 0.20f, 1f);
        healthLow = new Color(0.70f, 0.20f, 0.12f, 1f);
        staminaFill = new Color(0.75f, 0.60f, 0.20f, 1f);

        buttonNormal = new Color(0.13f, 0.14f, 0.11f, 1f);
        buttonHover = new Color(0.45f, 0.36f, 0.14f, 1f);
        buttonText = new Color(0.90f, 0.90f, 0.84f, 1f);
        panelBackground = new Color(0.08f, 0.09f, 0.07f, 0.97f);
        panelOutline = new Color(0.45f, 0.40f, 0.20f, 0.85f);

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[UITheme] 서바이벌 프리셋 적용 완료.");
    }

    [ContextMenu("Apply Navy Preset")]
    private void ApplyNavyPreset()
    {
        // 패널보다 한 단계 밝은 슬롯. 알파는 불투명으로 둬야 층이 안 뭉개진다.
        slotEmpty = new Color(0.125f, 0.157f, 0.227f, 1f);
        slotFilled = new Color(0.157f, 0.196f, 0.278f, 1f);
        slotActive = new Color(0.25f, 0.52f, 0.85f, 1f);
        slotOutlineDefault = new Color(0.24f, 0.29f, 0.40f, 0.8f);

        // 남색 배경에서 구분되도록 타입별 색도 채도를 낮추고 어둡게 잡았다.
        consumableBg = new Color(0.11f, 0.20f, 0.18f, 1f);
        consumableOutline = new Color(0.30f, 0.60f, 0.50f, 0.8f);
        weaponBg = new Color(0.22f, 0.15f, 0.18f, 1f);
        weaponOutline = new Color(0.70f, 0.35f, 0.40f, 0.85f);
        materialBg = new Color(0.20f, 0.18f, 0.13f, 1f);
        materialOutline = new Color(0.60f, 0.52f, 0.30f, 0.8f);
        defaultItemBg = new Color(0.145f, 0.180f, 0.255f, 1f);
        defaultItemOutline = new Color(0.28f, 0.34f, 0.46f, 0.8f);

        textPrimary = new Color(0.90f, 0.93f, 0.97f, 1f);
        textSecondary = new Color(0.66f, 0.71f, 0.80f, 1f);
        textMuted = new Color(0.45f, 0.50f, 0.60f, 1f);
        textAmount = new Color(0.80f, 0.85f, 0.95f, 1f);
        textAmmoLabel = new Color(0.85f, 0.72f, 0.32f, 1f);
        textAmmoAvailable = Color.white;
        textAmmoEmpty = new Color(0.40f, 0.44f, 0.52f, 1f);

        healthHigh = new Color(0.30f, 0.62f, 0.45f, 1f);
        healthLow = new Color(0.70f, 0.22f, 0.25f, 1f);
        staminaFill = new Color(0.40f, 0.60f, 0.85f, 1f);

        buttonNormal = new Color(0.125f, 0.157f, 0.227f, 1f);
        buttonHover = new Color(0.22f, 0.35f, 0.55f, 1f);
        buttonText = new Color(0.90f, 0.93f, 0.97f, 1f);
        panelBackground = new Color(0.071f, 0.094f, 0.149f, 0.85f);
        panelOutline = new Color(0.30f, 0.40f, 0.58f, 0.85f);

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[UITheme] 남색 프리셋 적용 완료.");
    }
#endif
}
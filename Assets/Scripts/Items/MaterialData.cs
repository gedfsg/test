using UnityEngine;

/// <summary>
/// 재료 아이템 데이터 (고철, 천, 배터리, 알코올 등)
/// 현재는 인벤토리에 보관하는 용도. 추후 크래프팅 시스템에 활용 가능.
/// </summary>
[CreateAssetMenu(fileName = "New Material Data", menuName = "Inventory/Material Data")]
public class MaterialData : ItemData
{
    [Header("Material Settings")]
    [Tooltip("재료 설명 (인게임 툴팁용)")]
    [TextArea]
    public string description;
}
using UnityEngine;

/// <summary>
/// 가방 아이템 (작은 15칸 / 중간 24칸 / 큰 40칸).
/// 인벤토리 우클릭 → Equip으로 장착하면 인벤토리 칸 수가 바뀌고 캐릭터 등에 모델이 붙음 (BackpackManager).
/// 에셋은 Tools/Items/Build Backpacks로 자동 생성 (Resources/Backpacks).
/// </summary>
[CreateAssetMenu(fileName = "New Backpack Data", menuName = "Inventory/Backpack Data")]
public class BackpackData : ItemData
{
    [Header("Backpack")]
    [Tooltip("장착 시 인벤토리 칸 수")]
    public int capacity = 24;
    [Tooltip("등에 붙일 가방 모델 (높이 1m로 정규화된 프리팹, 끈 쪽 = +Z)")]
    public GameObject model;
    [Tooltip("등에 붙였을 때 가방 높이(m)")]
    public float visualHeight = 0.62f;
}

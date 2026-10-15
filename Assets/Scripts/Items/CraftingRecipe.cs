using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제작 레시피 (재료 N개 → 결과 M개). Resources/Crafting 폴더에 두면 자동으로 읽힘.
/// 인벤토리에서 재료 아이템 우클릭 → "제작하기" 로 사용 (InventoryUI.Craft).
/// 예: 넝마 2개 → 붕대 1개 (Resources/Crafting/Rag_To_Bandage.asset)
/// </summary>
[CreateAssetMenu(fileName = "New Crafting Recipe", menuName = "Inventory/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    public ItemData ingredient;
    [Min(1)] public int ingredientCount = 2;
    public ItemData result;
    [Min(1)] public int resultCount = 1;

    static CraftingRecipe[] all;

    /// <summary>Resources/Crafting 의 모든 레시피 (처음 한 번만 로드)</summary>
    public static IReadOnlyList<CraftingRecipe> All => all ??= Resources.LoadAll<CraftingRecipe>("Crafting");

    /// <summary>이 아이템을 재료로 쓰는 첫 레시피 (없으면 null)</summary>
    public static CraftingRecipe FindByIngredient(ItemData item)
    {
        if (item == null) return null;
        foreach (var r in All) if (r != null && r.ingredient == item && r.result != null) return r;
        return null;
    }
}

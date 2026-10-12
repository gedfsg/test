using UnityEditor;
using UnityEngine;

/// <summary>
/// 소비 아이템 10종 ConsumableData 자동 생성 (이모지 아이콘).
/// 메뉴: Tools/Items/Build Consumables (다시 실행하면 값만 갱신)
/// 아이콘 PNG는 Assets/Items/Consumables/Icons/icon_*.png (Windows Segoe UI Emoji로 렌더링해둔 것)
/// </summary>
public static class ConsumableItemBuilder
{
    const string Dir = "Assets/Items/Consumables";

    struct Def
    {
        public string file, name; public float heal, hps, hpsDur, water, food;
        public Def(string file, string name, float heal = 0, float hps = 0, float hpsDur = 0, float water = 0, float food = 0)
        { this.file = file; this.name = name; this.heal = heal; this.hps = hps; this.hpsDur = hpsDur; this.water = water; this.food = food; }
    }

    static readonly Def[] Defs =
    {
        // 의료
        new Def("Bandage", "붕대", heal: 20),
        new Def("MedKit", "의료키트", heal: 50),
        new Def("Painkiller", "진통제", hps: 8, hpsDur: 10),      // HP+80을 10초에 걸쳐 천천히
        // 음료
        new Def("Water", "생수", water: 30),
        new Def("Cola", "콜라", water: 20, food: 5),
        new Def("Coffee", "커피", water: 15),
        // 음식
        new Def("Apple", "사과", food: 15),
        new Def("Bread", "빵", food: 25),
        new Def("CannedFood", "통조림", food: 35),
        new Def("Ramen", "라면", food: 40, water: 10),
    };

    [MenuItem("Tools/Items/Build Consumables")]
    public static void BuildAll()
    {
        var log = new System.Text.StringBuilder();
        foreach (var d in Defs)
        {
            string iconPath = $"{Dir}/Icons/icon_{d.file}.png";
            var imp = AssetImporter.GetAtPath(iconPath) as TextureImporter;
            // 프로젝트 기본값이 Sprite(Multiple)라 슬라이스 없으면 스프라이트가 안 생김 → Single로
            if (imp != null && (imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single))
            {
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }

            string path = $"{Dir}/{d.file}.asset";
            var c = AssetDatabase.LoadAssetAtPath<ConsumableData>(path);
            bool isNew = c == null;
            if (isNew) c = ScriptableObject.CreateInstance<ConsumableData>();
            c.itemName = d.name;
            c.icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            c.itemType = ItemType.Consumable;
            c.rarity = Rarity.Common;
            c.isStackable = true; c.maxStackSize = 10;
            c.healAmount = d.heal; c.healPerSecond = d.hps; c.healDuration = d.hpsDur;
            c.waterAmount = d.water; c.foodAmount = d.food;
            if (isNew) AssetDatabase.CreateAsset(c, path); else EditorUtility.SetDirty(c);
            log.AppendLine($"  {d.name}: HP+{d.heal}{(d.hps > 0 ? $" (+{d.hps}/s x{d.hpsDur}s)" : "")} 수분+{d.water} 포만감+{d.food} icon={(c.icon != null)}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ConsumableItemBuilder] 완료\n" + log);
    }
}

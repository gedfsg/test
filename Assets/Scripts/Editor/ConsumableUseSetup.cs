using UnityEditor;
using UnityEngine;

/// <summary>
/// 회복템 사용 시간 + 사용 소리 + MedKit 등급 일괄 적용 (2026-11-16).
/// 메뉴: Tools/Items/Apply Consumable Use Times (다시 실행해도 같은 값)
///  붕대 2.0 / 진통제 2.5 / 구급상자 3.0 / 커피 2.0 / 생수·콜라 1.5 / 음식(통조림·빵·사과·라면) 2.0 초
///  소리 (Assets/Audio/consume): 붕대 bandage_use, 진통제 pill_use, 구급상자 medkit_use, 생수·콜라·커피 drink, 음식 eat
///  MedKit 등급 = 희귀 (파랑 테두리)
/// </summary>
public static class ConsumableUseSetup
{
    const string SoundDir = "Assets/Audio/consume/";

    // 에셋 파일 이름 → (사용 시간, 소리 파일)
    static readonly (string file, float time, string sound)[] Table =
    {
        ("Assets/Items/Consumables/Bandage.asset",    2.0f, "bandage_use.mp3"),
        ("Assets/Items/Consumables/Painkiller.asset", 2.5f, "pill_use.mp3"),
        ("Assets/Items/Consumables/MedKit.asset",     3.0f, "medkit_use.mp3"),
        ("Assets/Items/Consumables/Coffee.asset",     2.0f, "drink.mp3"),
        ("Assets/Items/Consumables/Water.asset",      1.5f, "drink.mp3"),
        ("Assets/Items/Consumables/Cola.asset",       1.5f, "drink.mp3"),
        ("Assets/Items/Consumables/CannedFood.asset", 2.0f, "eat.mp3"),
        ("Assets/Items/Consumables/Bread.asset",      2.0f, "eat.mp3"),
        ("Assets/Items/Consumables/Apple.asset",      2.0f, "eat.mp3"),
        ("Assets/Items/Consumables/Ramen.asset",      2.0f, "eat.mp3"),
    };

    [MenuItem("Tools/Items/Apply Consumable Use Times")]
    public static void Apply()
    {
        var log = new System.Text.StringBuilder();
        foreach (var (file, time, sound) in Table)
        {
            var c = AssetDatabase.LoadAssetAtPath<ConsumableData>(file);
            if (c == null) { log.AppendLine("  ✗ 없음: " + file); continue; }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundDir + sound);
            if (clip == null) log.AppendLine("  ✗ 소리 없음: " + SoundDir + sound);
            c.useTime = time;
            c.useSound = clip;
            if (file.EndsWith("MedKit.asset")) c.rarity = Rarity.Rare; // 희귀 (파랑 테두리)
            EditorUtility.SetDirty(c);
            log.AppendLine($"  ✓ {c.itemName}: {time}초, {sound}, 등급 {c.rarity}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ConsumableUseSetup] 완료\n" + log);
    }
}

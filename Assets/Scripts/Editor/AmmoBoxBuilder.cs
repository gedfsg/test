using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Items/Build Ammo Box Prefabs (2026-11-18) - 바닥 탄약 드롭 모델을 큐브 → 탄약 상자로.
/// 원본: Assets/Weapon Assets/Ammo/AmmoBoxes.fbx ("Ammo Boxes Pack by TampaJoey (Sketchfab, CC-BY)")
///  - FBX 안 6개 상자를 탄종별 프리팹으로 (Assets/Prefabs/Ammo/ammo_*.prefab)
///      Browning(노란 9mm FMJ) → ammo_pistol / FederalRed(빨간 Federal 대형) → ammo_rifle(AR)
///      FederalWhite(흰 Federal) → ammo_smg / Blazer(검은 Blazer) → ammo_sniper
///      BlazerBrass(갈색 Blazer Brass) → ammo_shotgun / FederalUltra(나머지) → ammo_spare (RPG·공용 예비)
///  - FBX에 텍스처가 안 들어 있어서(임베드·정점 색 없음, 재질이 회색 하나) 상자마다 그 색의 단색 재질을 만듦
///    (Assets/Prefabs/Ammo/Materials). 텍스처를 받으면 재질의 Albedo에 넣으면 됨
///  - 크기: 원본 0.41×0.09×0.2m → 0.6배 (프리팹 안 Model 자식 스케일)
///  - AmmoData(Assets/Items/Ammo)의 worldModel에 탄종(weaponType)별 프리팹 연결 → AmmoDropSpawner가 큐브 대신 씀
/// 다시 실행해도 같은 결과 (덮어씀).
/// </summary>
public static class AmmoBoxBuilder
{
    const string Fbx = "Assets/Weapon Assets/Ammo/AmmoBoxes.fbx";
    const string OutDir = "Assets/Prefabs/Ammo";
    const string MatDir = "Assets/Prefabs/Ammo/Materials";
    const float ModelScale = 0.6f;

    struct Box { public string mesh, prefab; public Color color; public float gloss; public WeaponType? type; }

    static readonly Box[] Boxes =
    {
        new Box { mesh = "Browning",     prefab = "ammo_pistol",  color = new Color32(0xE6, 0xB4, 0x22, 0xFF), gloss = 0.35f, type = WeaponType.Pistol },
        new Box { mesh = "FederalRed",   prefab = "ammo_rifle",   color = new Color32(0xB3, 0x24, 0x22, 0xFF), gloss = 0.35f, type = WeaponType.AR },
        new Box { mesh = "FederalWhite", prefab = "ammo_smg",     color = new Color32(0xE9, 0xE6, 0xDE, 0xFF), gloss = 0.30f, type = WeaponType.SMG },
        new Box { mesh = "Blazer",       prefab = "ammo_sniper",  color = new Color32(0x1E, 0x1F, 0x23, 0xFF), gloss = 0.45f, type = WeaponType.Sniper },
        new Box { mesh = "BlazerBrass",  prefab = "ammo_shotgun", color = new Color32(0x7A, 0x50, 0x2C, 0xFF), gloss = 0.35f, type = WeaponType.Shotgun },
        new Box { mesh = "FederalUltra", prefab = "ammo_spare",   color = new Color32(0x3B, 0x4C, 0x6B, 0xFF), gloss = 0.35f, type = null },
    };

    [MenuItem("Tools/Items/Build Ammo Box Prefabs")]
    public static void Build()
    {
        var meshes = new Dictionary<string, Mesh>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is Mesh m) meshes[m.name] = m;
        if (meshes.Count == 0) { Debug.LogError("[AmmoBox] " + Fbx + " 에 메시가 없음"); return; }
        EnsureFolder("Assets/Prefabs", "Ammo");
        EnsureFolder(OutDir, "Materials");

        var byType = new Dictionary<WeaponType, GameObject>();
        var log = new List<string>();
        foreach (var b in Boxes)
        {
            if (!meshes.TryGetValue(b.mesh, out var mesh)) { log.Add(b.mesh + " 없음"); continue; }
            string matPath = $"{MatDir}/{b.prefab}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.color = b.color;
            mat.SetFloat("_Glossiness", b.gloss);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);

            var root = new GameObject(b.prefab);
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * ModelScale;
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = mat;
            string path = $"{OutDir}/{b.prefab}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            if (b.type.HasValue) byType[b.type.Value] = prefab;
            log.Add($"{b.prefab} ← {b.mesh}");
        }

        // AmmoData.worldModel 연결 (탄종별)
        int linked = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:AmmoData"))
        {
            var ad = AssetDatabase.LoadAssetAtPath<AmmoData>(AssetDatabase.GUIDToAssetPath(guid));
            if (ad == null || !byType.TryGetValue(ad.weaponType, out var p)) continue;
            ad.worldModel = p;
            EditorUtility.SetDirty(ad);
            linked++;
            log.Add($"{ad.itemName}({ad.weaponType}) → {p.name}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[AmmoBox] 프리팹 {Boxes.Length}개, AmmoData 연결 {linked}개\n" + string.Join("\n", log));
    }

    /// <summary>
    /// (2026-11-24) 탄약 아이템 아이콘 연결. 4종 모두 icon이 비어 있어서 가방·핫바·상점 칸에 그림이 안 보였음
    /// → 탄약 HUD와 같은 탄 그림(Resources/HUD/Sprites)을 씀: 권총 BulletP / AR BulletAR / 샷건 ShellSG / 저격 BulletSR
    /// </summary>
    [MenuItem("Tools/Items/Assign Ammo Icons")]
    public static void AssignAmmoIcons()
    {
        var icons = new Dictionary<WeaponType, string>
        {
            { WeaponType.Pistol, "BulletP" }, { WeaponType.AR, "BulletAR" },
            { WeaponType.Shotgun, "ShellSG" }, { WeaponType.Sniper, "BulletSR" },
            { WeaponType.SMG, "BulletP" },   // SMG탄 에셋은 아직 없음 (생기면 권총탄 그림)
        };
        var log = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:AmmoData"))
        {
            var ad = AssetDatabase.LoadAssetAtPath<AmmoData>(AssetDatabase.GUIDToAssetPath(guid));
            if (ad == null || !icons.TryGetValue(ad.weaponType, out var name)) continue;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Resources/HUD/Sprites/{name}.png");
            if (sprite == null) { log.Add($"{ad.itemName}: {name} 스프라이트 없음"); continue; }
            ad.icon = sprite;
            EditorUtility.SetDirty(ad);
            log.Add($"{ad.itemName}({ad.weaponType}) ← {name}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[AmmoIcons] " + string.Join(" / ", log));
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 가방 GLB → 등에 붙일 프리팹 + 아이콘 + 가방(BackpackData, 1종 24칸) 자동 생성.
/// 메뉴: Tools/Items/Build Backpacks (다시 실행하면 덮어씀)
/// 프리팹 규칙: 높이 1m, 끈(등에 닿는 면) = +Z, 원점 = 끈 면 가운데 → BackpackManager가 visualHeight로 스케일
/// GLB 원본은 끈 면이 +X라서 Y축 -90° 회전.
/// </summary>
public static class BackpackBuilder
{
    const string GlbPath = "Assets/Backpack/low_poly_backpack.glb";
    const string PrefabPath = "Assets/Backpack/Prefabs/Backpack.prefab";
    const string IconPath = "Assets/Backpack/Icons/icon_Backpack.png";
    const string DataDir = "Assets/Resources/Backpacks";

    [MenuItem("Tools/Items/Build Backpacks")]
    public static void BuildAll()
    {
        var glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (glb == null) { Debug.LogError("[BackpackBuilder] GLB 없음: " + GlbPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
        Directory.CreateDirectory(DataDir);

        var prefab = BuildPrefab(glb);
        var icon = BuildIcon(prefab);
        // 가방은 1종 (24칸). 예전 3종 중 작은/큰 가방 데이터는 삭제
        Make("Backpack_Medium", "가방", 24, 0.75f, Rarity.Rare, prefab, icon);
        foreach (var old in new[] { "Backpack_Small", "Backpack_Large" }) AssetDatabase.DeleteAsset($"{DataDir}/{old}.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("[BackpackBuilder] 완료 - 가방 1종 (24칸)");
    }

    static GameObject BuildPrefab(GameObject glb)
    {
        var root = new GameObject("Backpack");
        var model = (GameObject)Object.Instantiate(glb);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f) * model.transform.localRotation; // 끈 면 +X → +Z (GLB 루트 원래 회전 유지)

        Bounds b = WorldBounds(model);
        float s = 1f / b.size.y;
        model.transform.localScale = Vector3.one * s;
        b = WorldBounds(model);
        // 원점 = 끈 면(z 최대) 가운데
        model.transform.localPosition -= new Vector3(b.center.x, b.center.y, b.max.z);

        var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    static Bounds WorldBounds(GameObject go)
    {
        Bounds b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        return b;
    }

    // 바깥면(스마일 패치 쪽)을 정면에서 찍은 아이콘
    static Sprite BuildIcon(GameObject prefab)
    {
        const int S = 128;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = new Vector3(0f, -5000f, 0f);
        Bounds b = WorldBounds(go);
        var camGo = new GameObject("_IconCam"); var cam = camGo.AddComponent<Camera>();
        cam.enabled = false; cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
        camGo.transform.rotation = Quaternion.LookRotation(Vector3.forward); // 뒤(-Z, 바깥면)에서 앞으로 봄
        camGo.transform.position = b.center - Vector3.forward * 10f;
        cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y) * 1.1f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 50f;
        var lightGo = new GameObject("_IconLight"); var li = lightGo.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(0.3f, -0.5f, 1f));

        var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
        RenderTexture.active = prev; cam.targetTexture = null;
        var px = tex.GetPixels32(); for (int i = 0; i < px.Length; i++) if (px[i].a > 0) px[i].a = 255;
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(IconPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(go);

        AssetDatabase.ImportAsset(IconPath);
        var imp = (TextureImporter)AssetImporter.GetAtPath(IconPath);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
    }

    static void Make(string file, string name, int capacity, float height, Rarity rarity, GameObject prefab, Sprite icon)
    {
        string path = $"{DataDir}/{file}.asset";
        var d = AssetDatabase.LoadAssetAtPath<BackpackData>(path);
        bool isNew = d == null;
        if (isNew) d = ScriptableObject.CreateInstance<BackpackData>();
        d.itemName = name; d.icon = icon; d.itemType = ItemType.Armor; d.rarity = rarity;
        d.isStackable = false; d.maxStackSize = 1;
        d.capacity = capacity; d.model = prefab; d.visualHeight = height;
        if (isNew) AssetDatabase.CreateAsset(d, path); else EditorUtility.SetDirty(d);
    }
}

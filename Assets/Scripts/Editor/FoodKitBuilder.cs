using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kenney Food Kit(CC0) 모델을 음식/음료 아이템에 적용.
/// 메뉴: Tools/Items/Apply Food Kit (다시 실행해도 같은 결과)
///  - ConsumableData.worldModel = Food Kit FBX → 바닥에 3D 모델로 놓임 (WorldItemDrop)
///  - 아이콘 = 그 모델을 렌더링한 PNG (기존 이모지 PNG 파일을 덮어씀 → 인벤토리/핫바가 같은 스프라이트를 그대로 사용)
/// 의료 아이템(붕대/의료키트/진통제)은 Food Kit에 없어서 기존 아이콘 유지.
/// </summary>
public static class FoodKitBuilder
{
    const string ModelDir = "Assets/Items/Consumables/FoodKit/Models/FBX format/";
    const string ItemDir = "Assets/Items/Consumables/";
    const int IconSize = 128;

    const string PrefabDir = "Assets/Items/Consumables/FoodKit/Prefabs/";

    // (아이템 파일, Food Kit 모델, 색 보정) - 색 보정이 있으면 머티리얼을 틴트한 프리팹을 만들어 씀
    static readonly (string item, string model, Color? tint)[] Map =
    {
        ("Apple", "apple", null),
        ("Bread", "bread", null),
        ("CannedFood", "can", null),
        ("Ramen", "bowl-broth", null),                          // 라면 → 국물 그릇
        ("Water", "glass", new Color(0.5f, 0.78f, 1f)),         // 생수 → 흰 컵을 하늘색으로 (물병 모델이 없어서)
        ("Cola", "soda-can", null),
        ("Coffee", "cup-coffee", null),
    };

    [MenuItem("Tools/Items/Apply Food Kit")]
    public static void Apply()
    {
        var log = new System.Text.StringBuilder();
        foreach (var (item, model, tint) in Map)
        {
            var data = AssetDatabase.LoadAssetAtPath<ConsumableData>($"{ItemDir}{item}.asset");
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}{model}.fbx");
            if (data == null || fbx == null) { log.AppendLine($"  ✗ {item} / {model} 없음"); continue; }
            if (tint != null) fbx = TintedPrefab(fbx, item, tint.Value);

            string iconPath = $"{ItemDir}Icons/icon_{item}.png";
            RenderIcon(fbx, iconPath);
            data.worldModel = fbx;
            data.icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            EditorUtility.SetDirty(data);
            log.AppendLine($"  ✓ {data.itemName} ← {model}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[FoodKitBuilder] 완료\n" + log);
    }

    // 모델 머티리얼을 틴트한 복사본으로 바꾼 프리팹 (원본 FBX는 안 건드림)
    static GameObject TintedPrefab(GameObject fbx, string item, Color tint)
    {
        Directory.CreateDirectory(PrefabDir);
        string matPath = $"{PrefabDir}{item}_Mat.mat";
        var src = fbx.GetComponentInChildren<Renderer>().sharedMaterial;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(src); AssetDatabase.CreateAsset(mat, matPath); }
        else mat.CopyPropertiesFromMaterial(src);
        mat.color = tint;
        EditorUtility.SetDirty(mat);

        var inst = (GameObject)Object.Instantiate(fbx);
        foreach (var r in inst.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = mat; r.sharedMaterials = mats;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, $"{PrefabDir}{item}.prefab");
        Object.DestroyImmediate(inst);
        return prefab;
    }

    // 위에서 비스듬히 내려다본 3/4 뷰, 투명 배경
    static void RenderIcon(GameObject prefab, string path)
    {
        var go = (GameObject)Object.Instantiate(prefab);
        go.transform.position = new Vector3(0f, -6000f, 0f);
        Bounds b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }

        var camGo = new GameObject("_IconCam"); var cam = camGo.AddComponent<Camera>();
        cam.enabled = false; cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f;
        camGo.transform.rotation = Quaternion.Euler(30f, -35f, 0f);
        camGo.transform.position = b.center - camGo.transform.forward * 20f;
        cam.orthographicSize = b.extents.magnitude * 0.8f;

        var key = new GameObject("_Key").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.1f;
        key.transform.rotation = Quaternion.Euler(50f, -60f, 0f);
        var fill = new GameObject("_Fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.5f;
        fill.transform.rotation = Quaternion.Euler(20f, 120f, 0f);

        var rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
        RenderTexture.active = prev; cam.targetTexture = null;
        var px = tex.GetPixels32(); for (int i = 0; i < px.Length; i++) if (px[i].a > 0) px[i].a = 255; // 불투명 셰이더 알파 보정
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(key.gameObject); Object.DestroyImmediate(fill.gameObject); Object.DestroyImmediate(go);

        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
        imp.SaveAndReimport();
    }
}

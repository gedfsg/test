using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 수류탄·화염병 → 모델 프리팹 + 투척 프리팹(Rigidbody) + 아이콘 + ThrowableData 자동 생성 (2026-11-16).
/// 메뉴: Tools/Items/Build Throwables (다시 실행하면 같은 경로에 덮어씀). 루팅은 Tools/Items/Build Loot Tables.
///  - 모델 프리팹(SW_*_Model): 바닥에 떨어진 아이템 / 손에 쥔 모습용 (물리 없음)
///  - 투척 프리팹(SW_*_Thrown): 모델 + Rigidbody + 콜라이더. ThrowableController가 Grenade/Molotov를 붙여 던짐
///  - 아이콘: Resources/Throwables/icon_*.png (핫바 3번 빈 칸 자리 표시에도 씀)
/// </summary>
public static class ThrowableBuilder
{
    const string PrefabDir = "Assets/Weapon Assets/Prefabs";
    const string IconDir = "Assets/Resources/Throwables";
    const string DataDir = "Assets/Items/Throwables";
    const string SoundDir = "Assets/Audio/throwable/";

    [MenuItem("Tools/Items/Build Throwables")]
    public static void BuildAll()
    {
        foreach (var d in new[] { PrefabDir, IconDir, DataDir }) Directory.CreateDirectory(d);

        // 수류탄: 지름 약 6cm·높이 10cm (실물 MK2 크기) / 화염병: 병 높이 약 25cm
        var grenade = Build("Grenade", "수류탄", "Assets/Weapon Assets/Throwables/low-poly_mk2_grenade.glb", 0.11f, ThrowableType.Grenade, d =>
        {
            d.radius = 8f; d.innerRadius = 3f; d.damage = 500f; d.fuseTime = 3f; d.noiseRadius = 100f;
            d.rangeColor = new Color(1f, 0.2f, 0.15f);
            d.pinSound = Clip("grenade_pin.mp3"); d.throwSound = Clip("throw.mp3"); d.explodeSound = Clip("explosin.mp3"); // 파일 이름이 explosin (오타 그대로)
        });
        var molotov = Build("Molotov", "화염병", "Assets/Weapon Assets/Throwables/MolotovGrenadeBonus.fbx", 0.26f, ThrowableType.Molotov, d =>
        {
            d.radius = 5f; d.damage = 30f; d.fireDuration = 8f; d.igniteDps = 10f; d.igniteDuration = 3f; d.noiseRadius = 40f;
            d.rangeColor = new Color(1f, 0.55f, 0.1f);
            d.throwSound = Clip("throw.mp3"); d.breakSound = Clip("glass_break.mp3"); d.igniteSound = Clip("fire_burst.mp3"); d.loopSound = Clip("fire_loop.mp3");
        });
        AssetDatabase.SaveAssets();
        Debug.Log($"[ThrowableBuilder] 완료 - {grenade?.itemName}, {molotov?.itemName}");
    }

    static AudioClip Clip(string file)
    {
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundDir + file);
        if (c == null) Debug.LogWarning("[ThrowableBuilder] 소리 없음: " + SoundDir + file);
        return c;
    }

    static ThrowableData Build(string file, string display, string modelPath, float size, ThrowableType type, System.Action<ThrowableData> stats)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (src == null) { Debug.LogError("[ThrowableBuilder] 모델 없음: " + modelPath); return null; }

        // ── 모델 프리팹: 가장 긴 변 = size(m), 중심이 원점 ──
        var inst = (GameObject)Object.Instantiate(src);
        inst.name = src.name;
        inst.transform.position = Vector3.zero;
        foreach (var cam in inst.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam.gameObject);
        foreach (var li in inst.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(li.gameObject);
        foreach (var an in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
        foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        // 화염병 원본은 텍스처 아틀라스(TS_militia_weapons)가 빠져 흰색 → 갈색 유리병 색 재질로 교체
        if (type == ThrowableType.Molotov)
        {
            const string matPath = "Assets/Weapon Assets/Materials/SW_Molotov_Glass.mat";
            Directory.CreateDirectory("Assets/Weapon Assets/Materials");
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, matPath); }
            m.color = new Color(0.42f, 0.30f, 0.14f); m.SetFloat("_Metallic", 0.1f); m.SetFloat("_Glossiness", 0.8f);
            EditorUtility.SetDirty(m);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
        }
        Bounds b = new Bounds(); bool first = true;
        foreach (var r in inst.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        float scale = size / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.y, b.size.z));

        // 루트 스케일은 1로 두고 자식 "Model"에 스케일 (원점 = 모델 중심)
        var root = new GameObject("SW_" + file + "_Model");
        var holder = new GameObject("Model");
        holder.transform.SetParent(root.transform, false);
        holder.transform.localScale = Vector3.one * scale;
        inst.transform.SetParent(holder.transform, false); // 회전은 원본 그대로
        inst.transform.localPosition = -b.center;
        var modelPrefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{root.name}.prefab");
        Object.DestroyImmediate(root);

        // ── 투척 프리팹: 모델 + Rigidbody + 콜라이더 ──
        var thrown = new GameObject("SW_" + file + "_Thrown");
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        vis.transform.SetParent(thrown.transform, false);
        var rb = thrown.AddComponent<Rigidbody>();
        rb.mass = 0.6f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (type == ThrowableType.Molotov)
        {
            var cap = thrown.AddComponent<CapsuleCollider>(); cap.radius = size * 0.22f; cap.height = size; cap.direction = 1;
        }
        else thrown.AddComponent<SphereCollider>().radius = size * 0.45f;
        var thrownPrefab = PrefabUtility.SaveAsPrefabAsset(thrown, $"{PrefabDir}/{thrown.name}.prefab");
        Object.DestroyImmediate(thrown);

        // ── 아이콘 (Liberator 아이콘 렌더러 재사용) ──
        Sprite icon = LiberatorWeaponBuilder.RenderIcon(modelPrefab, $"{IconDir}/icon_{file}.png");

        // ── 데이터 ──
        string path = $"{DataDir}/{file}.asset";
        var d = AssetDatabase.LoadAssetAtPath<ThrowableData>(path);
        bool isNew = d == null;
        if (isNew) d = ScriptableObject.CreateInstance<ThrowableData>();
        d.itemName = display; d.icon = icon; d.itemType = ItemType.Weapon; // 인벤토리 메뉴: 핫바에 등록 / 버리기
        d.rarity = Rarity.Rare; // 희귀 (파랑 테두리)
        d.isStackable = true; d.maxStackSize = 10;
        d.throwableType = type;
        d.throwablePrefab = thrownPrefab;
        d.worldModel = modelPrefab;
        stats(d);
        if (isNew) AssetDatabase.CreateAsset(d, path); else EditorUtility.SetDirty(d);
        return d;
    }
}

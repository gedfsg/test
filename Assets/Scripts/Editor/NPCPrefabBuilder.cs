using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/NPC/Build NPC Prefabs (2026-11-23) - low__polygon__characters__pack.glb 안의 캐릭터 4명을 각각 프리팹으로 꺼냄.
///   Assets/Character/NPC/Prefabs/npc_character_01~04.prefab  (Barbar / Grenadier / Cube(luck) / Gigante 순)
///   + NPCSpawnPoint.prefab (NPCSpawner에 4종이 등록된 스폰 지점 - 씬에 끌어다 놓으면 됨)
///
///  - GLB는 뼈대 없는 정적 메시(MeshRenderer) 4개 + glTFast 재질. 애니메이션 없음 (A자세)
///    → 상점 상인은 기존 애니메이션 시민 모델 9종 유지 (사용자 결정 2026-11-23). 이 프리팹은 나중에 다른 용도로
///  - 크기: 보통 체격(Barbar)의 키를 플레이어 키(약 2.4m, 머리 뼈 2.24m + 머리)에 맞추고 4명 모두 같은 배율 → 거인(Gigante)은 그만큼 더 큼
///  - 기준점: 발 가운데 = (0,0,0), 정면 = +Z (Unity 앞쪽)
///  - 재질: glTF 셰이더는 빌드에서 빠질 수 있어서 Standard 재질로 복사 (Assets/Character/NPC/Materials)
///  - 충돌: 몸통 캡슐 (플레이어·좀비가 통과하지 않게)
/// 다시 실행하면 덮어씀.
/// </summary>
public static class NPCPrefabBuilder
{
    const string GlbPath = "Assets/Character/NPC/low__polygon__characters__pack.glb";
    const string PrefabDir = "Assets/Character/NPC/Prefabs";
    const string MatDir = "Assets/Character/NPC/Materials";
    const float TargetHeight = 2.4f;   // 플레이어 키 (m)
    const string ReferenceName = "Barbar_0";

    // GLB 안 노드 이름 → 프리팹 번호 (보통 체격 3명 먼저, 거인 마지막)
    static readonly string[] Order = { "Barbar_0", "Grenadier_2", "Cube_3", "Gigante_1" };

    [MenuItem("Tools/NPC/Build NPC Prefabs")]
    public static void Build()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (src == null) { Debug.LogError("[NPC] GLB를 찾을 수 없습니다: " + GlbPath); return; }
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(MatDir);

        // 원본을 잠깐 꺼내서 월드 기준 크기·위치를 읽음 (GLB 루트에 0.46 배율 + 축 회전이 걸려 있음)
        var tmp = (GameObject)Object.Instantiate(src);
        tmp.transform.SetPositionAndRotation(Vector3.zero, src.transform.rotation);
        try
        {
            var refR = Find(tmp.transform, ReferenceName)?.GetComponentInChildren<MeshRenderer>();
            if (refR == null) { Debug.LogError("[NPC] 기준 캐릭터(" + ReferenceName + ")가 없습니다"); return; }
            float scale = TargetHeight / refR.bounds.size.y;

            var prefabs = new List<GameObject>();
            for (int i = 0; i < Order.Length; i++)
            {
                var node = Find(tmp.transform, Order[i]);
                var mr = node != null ? node.GetComponentInChildren<MeshRenderer>() : null;
                if (mr == null) { Debug.LogWarning("[NPC] " + Order[i] + " 없음 - 건너뜀"); continue; }
                prefabs.Add(MakePrefab(i + 1, mr, scale));
            }
            MakeSpawnPoint(prefabs);
            Debug.Log($"[NPC] 프리팹 {prefabs.Count}개 생성: {PrefabDir} (배율 ×{scale:F3}, 기준 키 {TargetHeight}m)");
        }
        finally { Object.DestroyImmediate(tmp); }
        AssetDatabase.SaveAssets();
    }

    static GameObject MakePrefab(int index, MeshRenderer mr, float scale)
    {
        string name = $"npc_character_{index:00}";
        var root = new GameObject(name);
        var model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);

        // 메시 월드 행렬(원본 기준)을 그대로 옮긴 뒤, 발 가운데를 원점으로 맞추고 배율 적용
        var b = mr.bounds;
        Vector3 feet = new Vector3(b.center.x, b.min.y, b.center.z);
        model.transform.localRotation = mr.transform.rotation;
        model.transform.localScale = mr.transform.lossyScale * scale;
        model.transform.localPosition = (mr.transform.position - feet) * scale;

        model.AddComponent<MeshFilter>().sharedMesh = mr.GetComponent<MeshFilter>().sharedMesh;
        var r = model.AddComponent<MeshRenderer>();
        r.sharedMaterial = StandardCopy(mr.sharedMaterial);

        // 몸통 캡슐
        float h = b.size.y * scale;
        var cap = root.AddComponent<CapsuleCollider>();
        cap.height = h;
        cap.radius = 0.35f;
        cap.center = new Vector3(0f, h * 0.5f, 0f);

        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>glTF 재질 → Standard (색 텍스처·색만. 이 팩은 단순 저폴리라 이걸로 충분)</summary>
    static Material StandardCopy(Material gltf)
    {
        string path = $"{MatDir}/npc_{gltf.name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Standard");
        Texture tex = gltf.HasProperty("baseColorTexture") ? gltf.GetTexture("baseColorTexture") : gltf.mainTexture;
        Color col = gltf.HasProperty("baseColorFactor") ? gltf.GetColor("baseColorFactor") : Color.white;
        m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", col);
        m.SetFloat("_Glossiness", 0.15f);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void MakeSpawnPoint(List<GameObject> prefabs)
    {
        var go = new GameObject("NPCSpawnPoint");
        var sp = go.AddComponent<NPCSpawner>();
        sp.npcPrefabs = new List<GameObject>(prefabs);
        PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/NPCSpawnPoint.prefab");
        Object.DestroyImmediate(go);
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
        return null;
    }
}

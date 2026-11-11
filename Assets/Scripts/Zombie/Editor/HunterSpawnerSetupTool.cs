using System.IO;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 사냥꾼 스포너를 한 번에 설치한다: Assets/Resources/HunterPackSpawner.prefab 를 만들거나 고친다.
/// (게임 씬이 열리면 HunterPackSpawner가 이 프리팹으로 자동 생성된다 —— main 씬은 건드리지 않는다)
///
///   1. 사격 대응(_RL_Shoot) 좀비 프리팹을 모두 찾는다 (Zombie_Normal을 기본, 나머지는 생김새 섞기 후보.
///      Fat(탱커 체형)은 체력이 너무 높아져서 뺀다 —— 넣고 싶으면 스포너의 Hunter Variants에 직접 추가)
///   2. 그 프리팹들의 모델을 가장 최근의 사격 대응 모델(이름에 'shoot'이 든 .onnx)로 맞춘다
///   3. 조율기+RL(SlotRL) 방식으로 설정한 스포너 프리팹을 Resources에 저장한다
///      (이미 있으면 사냥꾼 프리팹·방식만 고치고 나머지 조절값은 그대로 둔다)
/// </summary>
public static class HunterSpawnerSetupTool
{
    private const string PrefabPath = "Assets/Resources/" + HunterPackSpawner.ResourcePath + ".prefab";

    [MenuItem("Tools/Zombie/사냥꾼 스포너 설치 (상자 열면 등장)")]
    private static void Setup()
    {
        // 1. 사냥꾼 프리팹 (기본 + 생김새 후보)
        string hunterPath = null;
        var variantPaths = new System.Collections.Generic.List<string>();
        foreach (var g in AssetDatabase.FindAssets("_RL_Shoot t:Prefab"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string n = Path.GetFileNameWithoutExtension(p);
            if (!n.EndsWith("_RL_Shoot")) continue;
            if (n.Contains("Fat")) continue;
            if (!variantPaths.Contains(p)) variantPaths.Add(p);
            if (hunterPath == null || p.Contains("Normal")) hunterPath = p;
        }
        if (hunterPath == null)
        {
            EditorUtility.DisplayDialog("사냥꾼 스포너 설치",
                "_RL_Shoot 프리팹이 없습니다.\n좀비 프리팹을 선택하고 'Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기 (사격 대응 모델)'을 먼저 실행하세요.", "확인");
            return;
        }

        // 2. 모델을 최신 사격 대응 모델로 (후보 전부)
        string modelInfo = AssignNewestShootModel(hunterPath);
        foreach (var vp in variantPaths) if (vp != hunterPath) AssignNewestShootModel(vp);
        var variants = new System.Collections.Generic.List<GameObject>();
        foreach (var vp in variantPaths)
        {
            var v = AssetDatabase.LoadAssetAtPath<GameObject>(vp);
            if (v != null) variants.Add(v);
        }

        // 3. 스포너 프리팹
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var hunter = AssetDatabase.LoadAssetAtPath<GameObject>(hunterPath);
        bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        if (existed)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var s = root.GetComponent<HunterPackSpawner>();
                if (s == null) s = root.AddComponent<HunterPackSpawner>();
                s.tactic = HunterPackSpawner.HunterTactic.SlotRL;
                s.hunterPrefab = hunter;
                s.hunterVariants = variants.ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        else
        {
            var go = new GameObject(HunterPackSpawner.ResourcePath);
            try
            {
                var s = go.AddComponent<HunterPackSpawner>();
                s.tactic = HunterPackSpawner.HunterTactic.SlotRL;
                s.hunterPrefab = hunter;
                s.hunterVariants = variants.ToArray();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally { Object.DestroyImmediate(go); }
        }
        AssetDatabase.SaveAssets();

        var created = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = created;
        EditorGUIUtility.PingObject(created);
        var names = new System.Text.StringBuilder();
        foreach (var v in variants) names.Append(names.Length > 0 ? ", " : "").Append(v.name.Replace("_RL_Shoot", ""));
        string msg = $"{(existed ? "고침" : "만듦")}: {PrefabPath}\n방식: 조율기+RL (SlotRL)\n사냥꾼: {names} (섞여서 나옴)\n모델: {modelInfo}\n\n" +
                     "게임 씬을 플레이하면 자동으로 생깁니다. F6 = 바로 소환, 상자를 열면 확률로 등장.";
        Debug.Log("[사냥꾼 스포너 설치] " + msg.Replace("\n", " / "));
        EditorUtility.DisplayDialog("사냥꾼 스포너 설치", msg, "확인");
    }

    /// <summary>프리팹의 Behavior Parameters 모델을 이름에 'shoot'이 든 가장 최근 모델로 바꾼다</summary>
    private static string AssignNewestShootModel(string prefabPath)
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        if (prop == null) return "그대로 (Model 속성을 찾지 못함)";

        string chosen = null;
        System.DateTime chosenTime = System.DateTime.MinValue;
        foreach (var g in AssetDatabase.FindAssets("t:" + prop.PropertyType.Name))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!Path.GetFileName(p).ToLowerInvariant().Contains("shoot")) continue;
            var t = File.GetLastWriteTime(p);
            if (chosen == null || t > chosenTime) { chosen = p; chosenTime = t; }
        }
        if (chosen == null) return "그대로 (이름에 'shoot'이 든 모델이 없음)";

        var model = AssetDatabase.LoadAssetAtPath(chosen, prop.PropertyType);
        if (model == null) return "그대로 (모델 불러오기 실패)";

        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var bp = root.GetComponent<BehaviorParameters>();
            if (bp == null) return "그대로 (프리팹에 Behavior Parameters가 없음)";
            var before = prop.GetValue(bp) as Object;
            if (before == model) return chosen + " (이미 최신)";
            prop.SetValue(bp, model);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return chosen + (before != null ? $" (← {before.name}에서 교체)" : "");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}

using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기존 좀비 프리팹을 복사해서 RL 뇌를 장착한 '_RL' 프리팹을 만든다. 원본은 건드리지 않는다.
///
/// 사용법: Project 창에서 좀비 프리팹을 선택 → Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기
///         → 스포너의 프리팹 칸을 새로 생긴 _RL 프리팹으로 바꾼다.
/// </summary>
public static class ZombieRLPrefabTool
{
    [MenuItem("Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기")]
    private static void MakeRLPrefab()
    {
        var src     = Selection.activeObject as GameObject;
        string path = src != null ? AssetDatabase.GetAssetPath(src) : null;

        if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
        {
            EditorUtility.DisplayDialog("RL 프리팹 만들기", "Project 창에서 좀비 프리팹(.prefab)을 먼저 선택하세요.", "확인");
            return;
        }
        if (src.GetComponent<ZombieController>() == null)
        {
            EditorUtility.DisplayDialog("RL 프리팹 만들기", "선택한 프리팹에 ZombieController가 없습니다.", "확인");
            return;
        }

        string dir     = Path.GetDirectoryName(path).Replace('\\', '/');
        string dstPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{src.name}_RL.prefab");
        if (!AssetDatabase.CopyAsset(path, dstPath))
        {
            Debug.LogError($"[RL 프리팹] 복사 실패: {path} → {dstPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(dstPath);
        string modelName = "없음";
        try
        {
            var controller = root.GetComponent<ZombieController>();

            if (root.GetComponent<ZombieFSMBrain>() == null) root.AddComponent<ZombieFSMBrain>();   // 폴백용

            var ml = root.GetComponent<ZombieMLBrain>();
            if (ml == null) ml = root.AddComponent<ZombieMLBrain>();
            controller.brain = ml;

            var bp = root.GetComponent<BehaviorParameters>();
            if (bp == null) bp = root.AddComponent<BehaviorParameters>();
            bp.BehaviorName = "Zombie";
            bp.BrainParameters.VectorObservationSize        = ZombieAgent.ObservationSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec                   = new ActionSpec(2, new[] { 2 });
            bp.BehaviorType = BehaviorType.InferenceOnly;
            modelName = AssignModel(bp);

            // ⚠ 순서: ZombieAgent를 DecisionRequester보다 먼저 붙인다.
            //    반대로 하면 DecisionRequester의 RequireComponent가 빈 Agent를 끼워 넣어 좀비가 굳는다.
            if (root.GetComponent<ZombieAgent>() == null)
            {
                var agent = root.AddComponent<ZombieAgent>();
                agent.MaxStep = 0;
            }
            if (root.GetComponent<DecisionRequester>() == null)
            {
                var dr = root.AddComponent<DecisionRequester>();
                dr.DecisionPeriod = 5;
                dr.TakeActionsBetweenDecisions = true;
            }
            foreach (var a in root.GetComponents<Agent>())
                if (!(a is ZombieAgent)) Object.DestroyImmediate(a);

            PrefabUtility.SaveAsPrefabAsset(root, dstPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var created = AssetDatabase.LoadAssetAtPath<GameObject>(dstPath);
        Selection.activeObject = created;
        EditorGUIUtility.PingObject(created);
        Debug.Log($"[RL 프리팹] 생성: {dstPath} / 모델: {modelName}\n" +
                  "스포너의 좀비 프리팹 칸을 이 프리팹으로 바꾸세요.");
    }

    /// <summary>
    /// 프로젝트에서 학습된 모델을 찾아 넣는다. 여러 개면 이름에 'squad'가 들어간 것을 우선한다.
    /// 모델 형식(ModelAsset)의 이름이 ML-Agents 버전마다 달라서 BehaviorParameters.Model의 타입으로 찾는다.
    /// </summary>
    private static string AssignModel(BehaviorParameters bp)
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        if (prop == null) return "없음 (Model 속성을 찾지 못함)";

        string[] guids = AssetDatabase.FindAssets("t:" + prop.PropertyType.Name);
        if (guids.Length == 0) return "없음 — 프리팹의 Behavior Parameters → Model 칸에 직접 넣으세요";

        string chosen = null;
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (chosen == null || p.ToLowerInvariant().Contains("squad")) chosen = p;
        }

        var model = AssetDatabase.LoadAssetAtPath(chosen, prop.PropertyType);
        if (model == null) return "없음 (불러오기 실패)";
        prop.SetValue(bp, model);
        return chosen + (guids.Length > 1 ? $" (후보 {guids.Length}개 중 선택 — 맞는지 확인)" : "");
    }
}

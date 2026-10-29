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
///
/// 모델 종류마다 입력(센서)이 달라서 프리팹도 따로 만든다:
///   기본          : 센서 없음            → 이름에 terrain/slot이 없는 모델 ('squad' 우선)
///   지형·역할     : 지형 + 역할 센서     → 이름에 'terrain'이 든 가장 최근 모델
///   조율기+RL     : 지형 + 역할 + 자리 센서 → 이름에 'slot'이 든 가장 최근 모델
///   사격 대응     : 지형 + 역할 + 자리 + 위협 센서 → 이름에 'shoot'이 든 가장 최근 모델
/// </summary>
public static class ZombieRLPrefabTool
{
    private enum Kind { Plain, Terrain, Slot, Shoot }

    [MenuItem("Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기")]
    private static void MakeRLPrefabMenu() => MakeRLPrefab(Kind.Plain);

    /// <summary>
    /// 지형 센서 모델용. 지형 센서(ZombieTerrainSensor)를 같이 붙이고, 이름에 'terrain'이 든 모델을 고른다.
    /// 지형 센서로 학습한 모델은 입력이 하나 더 있어서, 센서 없는 프리팹에 넣으면 오류가 난다 (반대도 마찬가지).
    /// </summary>
    [MenuItem("Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기 (지형·역할 모델)")]
    private static void MakeRLTerrainPrefabMenu() => MakeRLPrefab(Kind.Terrain);

    /// <summary>
    /// 조율기 + 강화학습 모델용. 지형·역할 센서에 자리 센서(ZombieSlotSensor)까지 붙이고, 이름에 'slot'이 든 모델을 고른다.
    /// 사냥꾼 소환기 / 테스트 소환기에서 '조율기+RL' 방식으로 쓴다 (조율기가 자리를 계산해 줘야 자리 센서가 채워진다).
    /// </summary>
    [MenuItem("Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기 (조율기+RL 모델)")]
    private static void MakeRLSlotPrefabMenu() => MakeRLPrefab(Kind.Slot);

    /// <summary>
    /// 사격 대응 모델용 (반격하는 더미 상대로 학습). 조율기+RL 구성에 위협 센서(ZombieThreatSensor)를 더한다.
    /// 소환기에서는 조율기+RL(SlotRL) 방식으로 쓴다.
    /// </summary>
    [MenuItem("Tools/Zombie/선택한 좀비 프리팹으로 RL 프리팹 만들기 (사격 대응 모델)")]
    private static void MakeRLShootPrefabMenu() => MakeRLPrefab(Kind.Shoot);

    private static void MakeRLPrefab(Kind kind)
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

        bool withTerrain = kind != Kind.Plain;   // 지형·역할 센서: 지형 모델과 조율기+RL 모델 둘 다
        bool withSlot    = kind == Kind.Slot || kind == Kind.Shoot;
        bool withThreat  = kind == Kind.Shoot;

        string dir     = Path.GetDirectoryName(path).Replace('\\', '/');
        string suffix  = kind == Kind.Shoot ? "_RL_Shoot" : kind == Kind.Slot ? "_RL_Slot" : kind == Kind.Terrain ? "_RL_Terrain" : "_RL";
        string dstPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{src.name}{suffix}.prefab");
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
            // 조율기+RL 모델은 '조율기 방향 + 정책 보정'(잔차)으로 학습했다 → 같은 크기를 넣어야 학습한 대로 움직인다
            ml.residualScale = withThreat ? ZombieMLBrain.DefaultShootResidual
                             : withSlot   ? ZombieMLBrain.DefaultSlotResidual
                             : 0f;
            // 사격 대응 모델(shoot2부터)은 보정을 옆으로만 허용해 학습했다 (속도를 줄여 머뭇거리지 못하게)
            ml.residualLateralOnly = withThreat;

            var bp = root.GetComponent<BehaviorParameters>();
            if (bp == null) bp = root.AddComponent<BehaviorParameters>();
            bp.BehaviorName = "Zombie";
            bp.BrainParameters.VectorObservationSize        = ZombieAgent.ObservationSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec                   = new ActionSpec(2, new[] { 2 });
            bp.BehaviorType = BehaviorType.InferenceOnly;
            modelName = AssignModel(bp, kind);

            // 센서 구성은 학습한 모델과 정확히 같아야 한다 (하나라도 다르면 입력 크기가 안 맞아 오류)
            SetComponent<ZombieTerrainSensor>(root, withTerrain);
            SetComponent<ZombieRoleSensor>(root, withTerrain);
            SetComponent<ZombieSlotSensor>(root, withSlot);
            SetComponent<ZombieThreatSensor>(root, withThreat);

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

    private static void SetComponent<T>(GameObject root, bool want) where T : Component
    {
        var c = root.GetComponent<T>();
        if (want && c == null) root.AddComponent<T>();
        if (!want && c != null) Object.DestroyImmediate(c, true);
    }

    /// <summary>
    /// 프로젝트에서 학습된 모델을 찾아 넣는다.
    ///   조율기+RL: 이름에 'slot'이 든 가장 최근 파일
    ///   지형·역할: 이름에 'terrain'이 들고 'slot'이 없는 가장 최근 파일
    ///   기본     : 둘 다 없는 것 중 'squad' 우선
    /// 모델 형식(ModelAsset)의 이름이 ML-Agents 버전마다 달라서 BehaviorParameters.Model의 타입으로 찾는다.
    /// </summary>
    private static string AssignModel(BehaviorParameters bp, Kind kind)
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        if (prop == null) return "없음 (Model 속성을 찾지 못함)";

        string[] guids = AssetDatabase.FindAssets("t:" + prop.PropertyType.Name);
        if (guids.Length == 0) return "없음 — 프리팹의 Behavior Parameters → Model 칸에 직접 넣으세요";

        string chosen = null;
        System.DateTime chosenTime = System.DateTime.MinValue;
        foreach (var g in guids)
        {
            string p   = AssetDatabase.GUIDToAssetPath(g);
            string low = Path.GetFileName(p).ToLowerInvariant();
            bool isShoot   = low.Contains("shoot");
            bool isSlot    = low.Contains("slot") && !isShoot;
            bool isTerrain = low.Contains("terrain") && !isSlot && !isShoot;
            Kind k = isShoot ? Kind.Shoot : isSlot ? Kind.Slot : isTerrain ? Kind.Terrain : Kind.Plain;
            if (k != kind) continue;

            if (kind != Kind.Plain)
            {
                var t = File.GetLastWriteTime(p);
                if (chosen == null || t > chosenTime) { chosen = p; chosenTime = t; }
            }
            else if (chosen == null || low.Contains("squad")) chosen = p;
        }
        if (chosen == null)
            return kind == Kind.Shoot
                ? "없음 — 이름에 'shoot'이 든 모델이 없습니다. 학습한 .onnx를 Assets/Models/Zombie_shoot.onnx 같은 이름으로 넣으세요"
                : kind == Kind.Slot
                ? "없음 — 이름에 'slot'이 든 모델이 없습니다. 학습한 .onnx를 Assets/Models/Zombie_slot.onnx 같은 이름으로 넣으세요"
                : kind == Kind.Terrain
                ? "없음 — 이름에 'terrain'이 든 모델이 없습니다. 학습한 .onnx를 Assets/Models/Zombie_terrain.onnx 같은 이름으로 넣으세요"
                : "없음 — 프리팹의 Behavior Parameters → Model 칸에 직접 넣으세요";

        var model = AssetDatabase.LoadAssetAtPath(chosen, prop.PropertyType);
        if (model == null) return "없음 (불러오기 실패)";
        prop.SetValue(bp, model);
        return chosen + (guids.Length > 1 ? $" (후보 {guids.Length}개 중 선택 — 맞는지 확인)" : "");
    }
}

using System.IO;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 노리는 좀비(돌진 타이밍 모델)를 게임에 넣는다: Assets/Resources/ZombieLungeBrain.asset 을 만들거나 고친다.
/// 이름에 'lunge'가 든 가장 최근 .onnx 모델을 넣는다. 이미 있으면 모델만 바꾸고 나머지 값은 그대로 둔다.
///
/// 학습장 촬영용 메뉴도 여기 있다: 열려 있는 돌진 학습 씬의 좀비 전부에 모델을 넣고 Inference Only로 바꾼다.
/// (Hierarchy에서 여러 개를 골라 고치면 컴포넌트 구성이 달라 다중 편집이 막히는 경우가 있어서)
/// </summary>
public static class ZombieLungeSetupTool
{
    private const string AssetPath = "Assets/Resources/" + ZombieLungeBrainSettings.ResourcePath + ".asset";

    [MenuItem("Tools/Zombie/노리는 좀비 모델 설치 (돌진 타이밍)")]
    private static void Setup()
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        if (prop == null) { EditorUtility.DisplayDialog("노리는 좀비", "BehaviorParameters.Model 속성을 찾지 못했습니다.", "확인"); return; }

        string chosen = FindLatestLungeModel(prop.PropertyType);
        if (chosen == null)
        {
            EditorUtility.DisplayDialog("노리는 좀비",
                "이름에 'lunge'가 든 모델이 없습니다.\nresults/zombie_lunge2/ZombieLunge.onnx 를 Assets/Models/Zombie_lunge2_10M.onnx 로 복사하세요.", "확인");
            return;
        }
        var model = AssetDatabase.LoadAssetAtPath(chosen, prop.PropertyType);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var s = AssetDatabase.LoadAssetAtPath<ZombieLungeBrainSettings>(AssetPath);
        bool existed = s != null;
        if (!existed)
        {
            s = ScriptableObject.CreateInstance<ZombieLungeBrainSettings>();
            AssetDatabase.CreateAsset(s, AssetPath);
        }
        s.model = model;
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();

        Selection.activeObject = s;
        EditorGUIUtility.PingObject(s);
        string msg = $"{(existed ? "고침" : "만듦")}: {AssetPath}\n모델: {chosen}\n\n" +
                     "게임의 돌진형 좀비가 이 모델로 '언제 덮칠지' 정합니다 (재장전·다른 좀비를 겨눌 때를 노림).\n" +
                     "끄려면 이 파일을 지우거나 Use Chance를 0으로.";
        Debug.Log("[노리는 좀비 설치] " + msg.Replace("\n", " / "));
        EditorUtility.DisplayDialog("노리는 좀비", msg, "확인");
    }

    /// <summary>이름에 'lunge'가 든 가장 최근 모델 경로 (없으면 null)</summary>
    private static string FindLatestLungeModel(System.Type modelType)
    {
        string chosen = null;
        System.DateTime chosenTime = System.DateTime.MinValue;
        foreach (var g in AssetDatabase.FindAssets("t:" + modelType.Name))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!Path.GetFileName(p).ToLowerInvariant().Contains("lunge")) continue;
            var t = File.GetLastWriteTime(p);
            if (chosen == null || t > chosenTime) { chosen = p; chosenTime = t; }
        }
        return chosen;
    }

    // ── 학습장 촬영 ─────────────────────────────────
    [MenuItem("Tools/Zombie/학습장 촬영: 돌진 좀비에 학습 모델 넣기")]
    private static void PrepareArenaForFilming()
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        if (prop == null) { EditorUtility.DisplayDialog("학습장 촬영", "BehaviorParameters.Model 속성을 찾지 못했습니다.", "확인"); return; }
        string chosen = FindLatestLungeModel(prop.PropertyType);
        if (chosen == null)
        {
            EditorUtility.DisplayDialog("학습장 촬영", "이름에 'lunge'가 든 모델이 없습니다 (Assets/Models/Zombie_lunge2_10M.onnx).", "확인");
            return;
        }
        var model = AssetDatabase.LoadAssetAtPath(chosen, prop.PropertyType);

        int n = 0;
        foreach (var agent in Object.FindObjectsByType<ZombieLungeAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var bp = agent.GetComponent<BehaviorParameters>();
            if (bp == null) continue;
            Undo.RecordObject(bp, "학습장 촬영 준비");
            prop.SetValue(bp, model);
            bp.BehaviorType = BehaviorType.InferenceOnly;
            EditorUtility.SetDirty(bp);
            n++;
        }
        if (n == 0)
        {
            EditorUtility.DisplayDialog("학습장 촬영", "열려 있는 씬에 돌진 좀비(ZombieLungeAgent)가 없습니다.\nAssets/Scenes/ZombieTrainingLunge.unity 를 먼저 여세요.", "확인");
            return;
        }
        string msg = $"돌진 좀비 {n}마리에 모델을 넣었습니다 (Inference Only).\n모델: {chosen}\n\n" +
                     "이제 Play → Recorder로 녹화하면 됩니다.\n" +
                     "⚠ 씬을 저장하지 마세요 (저장하면 이 씬으로 다시 학습할 때 학습이 안 됩니다).\n" +
                     "실수로 저장했다면 'Tools/Zombie/학습장 촬영 설정 되돌리기'.";
        Debug.Log("[학습장 촬영] " + msg.Replace("\n", " / "));
        EditorUtility.DisplayDialog("학습장 촬영", msg, "확인");
    }

    [MenuItem("Tools/Zombie/학습장 촬영 설정 되돌리기")]
    private static void RevertArenaFilming()
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        int n = 0;
        foreach (var agent in Object.FindObjectsByType<ZombieLungeAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var bp = agent.GetComponent<BehaviorParameters>();
            if (bp == null) continue;
            Undo.RecordObject(bp, "학습장 촬영 되돌리기");
            if (prop != null) prop.SetValue(bp, null);
            bp.BehaviorType = BehaviorType.Default;   // 학습 씬 원래 값 (학습기가 붙으면 학습)
            EditorUtility.SetDirty(bp);
            n++;
        }
        Debug.Log($"[학습장 촬영] 돌진 좀비 {n}마리를 학습용 설정(모델 없음, Default)으로 되돌렸습니다");
        EditorUtility.DisplayDialog("학습장 촬영", $"돌진 좀비 {n}마리를 학습용 설정으로 되돌렸습니다.\n이 상태로는 저장해도 됩니다.", "확인");
    }
}

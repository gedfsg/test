using System.IO;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 노리는 좀비(돌진 타이밍 모델)를 게임에 넣는다: Assets/Resources/ZombieLungeBrain.asset 을 만들거나 고친다.
/// 이름에 'lunge'가 든 가장 최근 .onnx 모델을 넣는다. 이미 있으면 모델만 바꾸고 나머지 값은 그대로 둔다.
/// </summary>
public static class ZombieLungeSetupTool
{
    private const string AssetPath = "Assets/Resources/" + ZombieLungeBrainSettings.ResourcePath + ".asset";

    [MenuItem("Tools/Zombie/노리는 좀비 모델 설치 (돌진 타이밍)")]
    private static void Setup()
    {
        var prop = typeof(BehaviorParameters).GetProperty("Model");
        if (prop == null) { EditorUtility.DisplayDialog("노리는 좀비", "BehaviorParameters.Model 속성을 찾지 못했습니다.", "확인"); return; }

        string chosen = null;
        System.DateTime chosenTime = System.DateTime.MinValue;
        foreach (var g in AssetDatabase.FindAssets("t:" + prop.PropertyType.Name))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!Path.GetFileName(p).ToLowerInvariant().Contains("lunge")) continue;
            var t = File.GetLastWriteTime(p);
            if (chosen == null || t > chosenTime) { chosen = p; chosenTime = t; }
        }
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
}

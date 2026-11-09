using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 메인/서브 미션 풀 에셋 생성 (Tools/Mission/Build Mission Data). 값은 MissionDefaults.
/// 이미 있는 에셋은 덮어쓰지 않음 (Inspector에서 조정한 값 보존) - 새로 만들려면 해당 에셋을 지우고 다시 실행.
/// </summary>
public static class MissionDataBuilder
{
    const string MainDir = "Assets/Resources/Missions/Main";
    const string SubDir = "Assets/Resources/Missions/Sub";

    [MenuItem("Tools/Mission/Build Mission Data")]
    public static void Build()
    {
        Directory.CreateDirectory(MainDir);
        Directory.CreateDirectory(SubDir);
        int created = 0;
        foreach (var d in MissionDefaults.Main()) created += Save(d, $"{MainDir}/{d.name}.asset");
        foreach (var d in MissionDefaults.Sub()) created += Save(d, $"{SubDir}/{d.name}.asset");
        AssetDatabase.SaveAssets();
        Debug.Log($"[MissionDataBuilder] 미션 에셋 {created}개 생성 (메인 {MissionDefaults.Main().Count} / 서브 {MissionDefaults.Sub().Count})");
    }

    static int Save(ScriptableObject so, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) != null) { Object.DestroyImmediate(so); return 0; }
        AssetDatabase.CreateAsset(so, path);
        return 1;
    }
}

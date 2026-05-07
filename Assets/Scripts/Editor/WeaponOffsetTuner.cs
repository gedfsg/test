using UnityEngine;
using UnityEditor;

/// <summary>
/// 플레이 중 총 위치/회전을 실시간으로 맞추는 에디터 도구
/// Tools → Weapon Offset Tuner
/// </summary>
public class WeaponOffsetTuner : EditorWindow
{
    [MenuItem("Tools/Weapon Offset Tuner")]
    public static void Open() => GetWindow<WeaponOffsetTuner>("Weapon Tuner");

    private WeaponHandAttacher attacher;
    private Vector3 pos;
    private Vector3 rot;

    void OnGUI()
    {
        EditorGUILayout.LabelField("🔫 총 위치/회전 실시간 조정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("플레이 중에 사용하세요. Apply 누르면 즉시 반영됩니다.", MessageType.Info);

        // Scene에서 WeaponHandAttacher 자동 찾기
        if (attacher == null)
            attacher = FindObjectOfType<WeaponHandAttacher>();

        if (attacher == null)
        {
            EditorGUILayout.HelpBox("WeaponHandAttacher를 씬에서 찾을 수 없어요.\nPlayer에 컴포넌트를 추가해주세요.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("현재 타겟: " + attacher.gameObject.name);
        EditorGUILayout.Space();

        pos = EditorGUILayout.Vector3Field("Position Offset", attacher.positionOffset);
        rot = EditorGUILayout.Vector3Field("Rotation Offset", attacher.rotationOffset);

        EditorGUILayout.Space();

        if (GUILayout.Button("▶ Apply (즉시 반영)", GUILayout.Height(30)))
        {
            attacher.positionOffset = pos;
            attacher.rotationOffset = rot;
            attacher.ApplyOffset();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("── 자주 쓰는 회전 프리셋 ──", EditorStyles.boldLabel);

        // 일반적인 총 방향 프리셋들
        if (GUILayout.Button("프리셋 A: (0, 0, 0)"))       SetRot(0, 0, 0);
        if (GUILayout.Button("프리셋 B: (-90, 0, 0)"))     SetRot(-90, 0, 0);
        if (GUILayout.Button("프리셋 C: (0, 90, 0)"))      SetRot(0, 90, 0);
        if (GUILayout.Button("프리셋 D: (90, 0, 0)"))      SetRot(90, 0, 0);
        if (GUILayout.Button("프리셋 E: (0, -90, 0)"))     SetRot(0, -90, 0);
        if (GUILayout.Button("프리셋 F: (-90, 0, 90)"))    SetRot(-90, 0, 90);
        if (GUILayout.Button("프리셋 G: (0, 180, 0)"))     SetRot(0, 180, 0);
        if (GUILayout.Button("프리셋 H: (-90, 180, 0)"))   SetRot(-90, 180, 0);
    }

    void SetRot(float x, float y, float z)
    {
        if (attacher == null) return;
        rot = new Vector3(x, y, z);
        attacher.rotationOffset = rot;
        attacher.ApplyOffset();
    }

    void OnInspectorUpdate() => Repaint();
}

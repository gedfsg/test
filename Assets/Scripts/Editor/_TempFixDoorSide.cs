using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 자동 문 배치(가장 가까운 도로 방향 추정)가 실제 모델의 문 위치랑 다른 건물이 있으면,
// 그 건물만 Hierarchy에서 선택한 뒤 아래 4개 메뉴 중 실제 문이 있는 쪽을 골라 실행.
// 선택한 건물의 막힌 벽만 다시 계산해서 문을 옮김 (다른 건물/트리거/지붕 컷어웨이는 그대로 유지).
public static class TempFixDoorSide
{
    [MenuItem("Tools/Map/Fix Door Side/Front +Z (Selected)")]
    static void FixForward() => Fix(Vector3.forward);

    [MenuItem("Tools/Map/Fix Door Side/Back -Z (Selected)")]
    static void FixBack() => Fix(Vector3.back);

    [MenuItem("Tools/Map/Fix Door Side/Right +X (Selected)")]
    static void FixRight() => Fix(Vector3.right);

    [MenuItem("Tools/Map/Fix Door Side/Left -X (Selected)")]
    static void FixLeft() => Fix(Vector3.left);

    static void Fix(Vector3 doorSideLocal)
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("Hierarchy에서 문을 고칠 건물을 먼저 선택하세요.");
            return;
        }

        bool ok = TempApplyBuildingInterior.RebuildDoorForSelected(go, doorSideLocal);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        if (ok) Debug.Log($"✅ '{go.name}' — 문을 {doorSideLocal}(로컬) 방향으로 재배치했습니다.");
        else Debug.LogWarning($"⚠️ '{go.name}' — 그 방향은 벽 폭이 문보다 좁아서 문을 못 냈습니다.");
    }
}

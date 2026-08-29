using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 이름에 "building-" 또는 "house-"가 들어간 오브젝트에 BoxCollider(Trigger) + BuildingInterior를 일괄 적용.
public static class TempApplyBuildingInterior
{
    [MenuItem("Tools/Map/Apply Building Interior (Batch)")]
    static void Apply()
    {
        List<GameObject> targets = new List<GameObject>();
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            string n = root.name.ToLowerInvariant();
            if (n.Contains("building-") || n.Contains("house-")) targets.Add(root);
        }

        int applied = 0;
        foreach (GameObject go in targets)
        {
            if (go.GetComponent<BuildingInterior>() != null) continue; // 이미 적용됨

            Bounds? localBounds = ComputeLocalBounds(go);
            if (localBounds == null) continue;

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = localBounds.Value.center;
            box.size = localBounds.Value.size;

            BuildingInterior interior = go.AddComponent<BuildingInterior>();
            // Kenney 건물은 지붕이 분리된 서브 메시가 아니라 단일 메시라서
            // 건물 자체를 숨김 대상으로 지정 (진입 시 건물이 페이드 아웃되어 캐릭터가 보임)
            interior.objectsToHide = new GameObject[] { go };

            applied++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ BuildingInterior 적용 완료: {applied}/{targets.Count}개 (나머지는 이미 적용되어 있었거나 렌더러 없음)");
    }

    // 오브젝트의 실제 회전을 고려해 로컬 좌표계 기준 바운드를 계산
    static Bounds? ComputeLocalBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        bool started = false;
        Bounds localBounds = default;
        foreach (var r in renderers)
        {
            Bounds wb = r.bounds;
            Vector3 c = wb.center;
            Vector3 e = wb.extents;
            Vector3[] corners =
            {
                c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, e.y, -e.z),  c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),  c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(-e.x, e.y, e.z),   c + new Vector3(e.x, e.y, e.z),
            };
            foreach (var corner in corners)
            {
                Vector3 local = go.transform.InverseTransformPoint(corner);
                if (!started) { localBounds = new Bounds(local, Vector3.zero); started = true; }
                else localBounds.Encapsulate(local);
            }
        }
        return started ? localBounds : (Bounds?)null;
    }
}

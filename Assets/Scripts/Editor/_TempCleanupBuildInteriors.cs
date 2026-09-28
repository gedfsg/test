using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// _TempBuildInteriors.cs가 만든 것들(Interior/Walls/Props/FacilityMarker, 노란 큐브 벽/바닥/가구)을
// 전부 삭제하고 건물을 원상복구. Interior를 만들 때 지웠던 원래 외벽 MeshCollider도 재생성함.
public static class TempCleanupBuildInteriors
{
    [MenuItem("Tools/Map/Cleanup Build Interiors (Undo)")]
    static void Cleanup()
    {
        GameObject buildingsRoot = GameObject.Find("Buildings");
        if (buildingsRoot == null)
        {
            Debug.LogError("'Buildings' 부모를 찾을 수 없습니다.");
            return;
        }

        int removed = 0;
        int collidersRestored = 0;

        List<Transform> buildings = buildingsRoot.transform.Cast<Transform>().ToList();
        foreach (Transform building in buildings)
        {
            Transform interior = building.Find("Interior");
            if (interior == null) continue;

            Object.DestroyImmediate(interior.gameObject);
            removed++;

            // BuildOne()이 원래 건물의 non-trigger MeshCollider를 지웠으므로 복구
            collidersRestored += RestoreMeshColliders(building.gameObject);
        }

        // 혹시 다른 곳에 남은 잔재(FacilityMarker_* 등)도 정리
        int stray = 0;
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t == null) continue;
            if (t.name.StartsWith("FacilityMarker_") || t.name == "Interior")
            {
                Object.DestroyImmediate(t.gameObject);
                stray++;
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ Interior {removed}개 삭제, 콜라이더 {collidersRestored}개 파트 복구, 잔재 {stray}개 추가 정리.");
    }

    static int RestoreMeshColliders(GameObject root)
    {
        int count = 0;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            GameObject part = mf.gameObject;
            MeshCollider mc = part.GetComponent<MeshCollider>();
            if (mc == null) mc = part.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.isTrigger = false;
            count++;
        }
        return count;
    }
}

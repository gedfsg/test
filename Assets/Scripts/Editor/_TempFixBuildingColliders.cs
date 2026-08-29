using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 건물(building-, house-)의 뭉뚱그린 BoxCollider를 실제 모양을 따라가는
// MeshCollider(들)로 교체. 도로/잔디·모래 구역에 잘못 붙은 콜라이더는 제거.
public static class TempFixBuildingColliders
{
    [MenuItem("Tools/Map/Fix Building Colliders (Mesh)")]
    static void Fix()
    {
        int buildingCount = 0, partCount = 0, clearedCount = 0;

        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            string n = go.name.ToLowerInvariant();

            bool isBuilding = n.Contains("building") || n.Contains("house-");
            bool isPassThrough = n.StartsWith("road-") || n.StartsWith("zone_") || n == "groundzones";

            if (isBuilding)
            {
                partCount += ReplaceWithMeshColliders(go);
                buildingCount++;
            }
            else if (isPassThrough)
            {
                clearedCount += RemoveAllColliders(go);
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ 건물 {buildingCount}개 → MeshCollider {partCount}개 파트 적용, " +
                  $"도로/잔디·모래에서 콜라이더 {clearedCount}개 제거.");
    }

    // 건물(및 자식 파츠)의 막힘용(non-trigger) BoxCollider를 제거하고,
    // 메시가 있는 모든 파트에 MeshCollider(Convex 꺼짐, non-trigger)를 붙임.
    // BuildingInterior가 쓰는 isTrigger=true 콜라이더는 건드리지 않음.
    static int ReplaceWithMeshColliders(GameObject root)
    {
        foreach (var bc in root.GetComponentsInChildren<BoxCollider>(true))
            if (!bc.isTrigger) Object.DestroyImmediate(bc);

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

    static int RemoveAllColliders(GameObject go)
    {
        int count = 0;
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(col);
            count++;
        }
        return count;
    }
}

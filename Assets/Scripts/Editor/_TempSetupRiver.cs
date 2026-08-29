using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
public static class TempSetupRiver
{
    const string WaterPrefabPath = "Assets/IgniteCoders/Simple Water Shader/Prefabs/WaterBlock_50m.prefab";

    [MenuItem("Tools/Map/Setup River Object")]
    static void Setup()
    {
        GameObject old = GameObject.Find("River");
        if (old != null) Object.DestroyImmediate(old);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"프리팹을 찾을 수 없음: {WaterPrefabPath}");
            return;
        }

        GameObject river = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        river.name = "River";

        // Ground(Terrain) 높이에 맞춰 대략 중앙에 배치. 실제 파신 물길 모양에 맞게 직접 위치/크기 조정 필요.
        GameObject groundGo = GameObject.Find("Ground");
        Terrain terrain = groundGo != null ? groundGo.GetComponent<Terrain>() : null;
        float y = 8f;
        if (terrain != null)
            y = terrain.SampleHeight(Vector3.zero) + terrain.transform.position.y;

        river.transform.position = new Vector3(0, y, 0);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ River 생성 완료 (WaterBlock_50m 기반), 위치 (0,{y},0). 파신 물길 모양/범위에 맞게 Position과 Scale을 직접 조정해주세요.");
    }
}

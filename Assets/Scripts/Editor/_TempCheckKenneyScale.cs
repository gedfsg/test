using UnityEngine;
using UnityEditor;

// 일회성 스크립트 - 확인 후 삭제할 것. 스케일만 확인하고 생성한 테스트 오브젝트는 자동 삭제함.
public static class TempCheckKenneyScale
{
    [MenuItem("Tools/Map/Check Kenney Scale Vs Player")]
    static void Check()
    {
        GameObject player = GameObject.Find("Player");
        Bounds? playerBounds = player != null ? GetBounds(player) : null;

        if (playerBounds.HasValue)
            Debug.Log($"[기준] Player 크기: W={playerBounds.Value.size.x:F2} H={playerBounds.Value.size.y:F2} D={playerBounds.Value.size.z:F2}");
        else
            Debug.LogWarning("Player를 찾지 못해 기준 크기 없이 절대 크기만 비교합니다.");

        CheckOne("Assets/KenneyAssets/Roads/FBX format/road-straight.fbx", playerBounds);
        CheckOne("Assets/KenneyAssets/Suburban/FBX format/building-type-a.fbx", playerBounds);
        CheckOne("Assets/KenneyAssets/Commercial/FBX format/building-a.fbx", playerBounds);
        CheckOne("Assets/KenneyAssets/Commercial/FBX format/building-skyscraper-a.fbx", playerBounds);
        CheckOne("Assets/IgniteCoders/Simple Water Shader/Prefabs/WaterBlock_50m.prefab", playerBounds);
    }

    static void CheckOne(string path, Bounds? playerBounds)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"못 찾음: {path}");
            return;
        }

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Bounds b = GetBounds(inst) ?? new Bounds(inst.transform.position, Vector3.zero);
        string name = System.IO.Path.GetFileNameWithoutExtension(path);

        string ratioInfo = "";
        if (playerBounds.HasValue && playerBounds.Value.size.y > 0)
        {
            float ratio = b.size.y / playerBounds.Value.size.y;
            ratioInfo = $" | 플레이어 대비 높이 비율={ratio:F2}배";
        }

        Debug.Log($"{name}: W={b.size.x:F2} H={b.size.y:F2} D={b.size.z:F2}{ratioInfo}");
        Object.DestroyImmediate(inst);
    }

    static Bounds? GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}

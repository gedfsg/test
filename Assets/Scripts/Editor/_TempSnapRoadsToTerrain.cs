using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// [수정판] 이전 버전은 바운딩 박스 "윗면"을 지형에 맞췄는데, 방호벽/기둥이
// 붙은 도로는 그 튀어나온 부분 때문에 도로 전체가 지형 아래로 꺼져서 안 보이게
// 되는 버그가 있었음. 이번 버전은 바운딩 박스를 아예 안 쓰고, 오브젝트의
// 피벗(Position) 자체를 지형 높이에 직접 맞춘다 (Player/MapGuide 리스냅에
// 쓰던 것과 동일한, 이미 검증된 단순한 방식).
public static class TempSnapRoadsToTerrainFixed
{
    const float YOffset = 0f; // 필요하면 살짝 띄우기용 (0 = 지형 표면과 동일 높이)

    [MenuItem("Tools/Map/Snap Roads To Terrain Surface (Fixed)")]
    static void Snap()
    {
        GameObject groundGo = GameObject.Find("Ground");
        Terrain terrain = groundGo != null ? groundGo.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("Ground(Terrain)를 찾을 수 없습니다.");
            return;
        }

        List<Transform> roads = new List<Transform>();
        GameObject roadsRoot = GameObject.Find("Roads");
        if (roadsRoot != null)
        {
            foreach (Transform child in roadsRoot.transform) roads.Add(child);
        }
        else
        {
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.ToLowerInvariant().StartsWith("road-")) roads.Add(root.transform);
        }

        if (roads.Count == 0)
        {
            Debug.LogWarning("도로 오브젝트를 찾지 못했습니다.");
            return;
        }

        int fixedCount = 0;
        float maxAbsDelta = 0f;

        foreach (Transform road in roads)
        {
            float terrainY = terrain.SampleHeight(road.position) + terrain.transform.position.y + YOffset;
            float delta = terrainY - road.position.y;

            if (Mathf.Abs(delta) < 0.001f) continue;

            Vector3 p = road.position;
            p.y = terrainY;
            road.position = p;

            fixedCount++;
            if (Mathf.Abs(delta) > Mathf.Abs(maxAbsDelta)) maxAbsDelta = delta;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ 도로 {fixedCount}/{roads.Count}개 높이 재보정 완료 (최대 보정량 {maxAbsDelta:F3}m). " +
                  "바운딩 박스 대신 피벗 위치를 직접 지형 높이에 맞췄습니다.");
    }
}

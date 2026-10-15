using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;

/// <summary>
/// 건물 벽을 반영해서 NavMesh 다시 굽기 (Tools → Map → Rebake NavMesh With Building Walls). 다시 실행해도 안전.
///
/// 문제 (2026-10-15 Play 실측): 좀비가 건물 벽을 뚫고 들어옴.
///  - NavMeshSurface는 "Physics Colliders"로 굽는데, 건물 벽 MeshCollider는 BuildingWallCollider.Awake에서
///    런타임에만 생성됨 → 굽는 시점엔 실내 진입용 트리거 박스만 있어서 NavMesh가 벽 자리에 끊김 없이 이어짐
///  - 좀비(NavMeshAgent, kinematic)는 콜라이더가 아니라 NavMesh만 따라가므로 벽을 그대로 통과
///  - 건물 바깥 → 중앙 NavMesh 경로 393개 중 233개가 벽 콜라이더를 관통했음
/// 하는 일:
///  1) 모든 BuildingWallCollider에 런타임과 같은 벽 콜라이더(문 구멍 뚫린 두께 있는 벽)를 임시로 생성
///  2) NavMeshSurface 굽기 → 벽 자리는 끊기고 문 구멍으로만 실내와 연결됨
///  3) 결과를 기존 NavMesh 에셋 경로에 저장, 임시 콜라이더 삭제 (씬엔 NavMesh 데이터 참조만 바뀜)
/// </summary>
public static class RebakeNavMeshWithWalls
{
    [MenuItem("Tools/Map/Rebake NavMesh With Building Walls")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NavBake] Play 모드에서는 실행 불가"); return; }

        var surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null) { Debug.LogError("[NavBake] NavMeshSurface가 씬에 없음"); return; }

        // 1) 임시 벽 콜라이더 생성 (이미 MeshCollider가 붙어 있는 건 건너뜀 - 씬에 저장된 콜라이더 보호)
        var temp = new List<MeshCollider>();
        foreach (var w in Object.FindObjectsByType<BuildingWallCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (w.GetComponent<MeshCollider>() != null) continue;
            var mc = w.Build();
            if (mc != null) temp.Add(mc);
        }

        try
        {
            // 2) 굽기
            string assetPath = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            surface.BuildNavMesh();
            sw.Stop();

            // 3) 기존 에셋 경로에 저장 (경로가 없으면 씬 폴더에 새로 만듦)
            if (string.IsNullOrEmpty(assetPath))
            {
                string scenePath = surface.gameObject.scene.path;
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scenePath), System.IO.Path.GetFileNameWithoutExtension(scenePath)).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(scenePath), System.IO.Path.GetFileNameWithoutExtension(scenePath));
                assetPath = dir + "/NavMesh-" + surface.name + ".asset";
            }
            var data = surface.navMeshData;
            if (!AssetDatabase.Contains(data))
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(data, assetPath);
            }
            surface.navMeshData = data; // 에셋 참조로 다시 세팅 (OnEnable 재등록)
            EditorUtility.SetDirty(surface);
            AssetDatabase.SaveAssets();

            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            Debug.Log($"[NavBake] 벽 {temp.Count}개 반영해서 굽기 완료 ({sw.ElapsedMilliseconds}ms, 삼각형 {tri.indices.Length / 3}) → {assetPath}");
        }
        finally
        {
            // 임시 콜라이더 삭제 (런타임엔 Awake에서 다시 만듦)
            foreach (var mc in temp)
            {
                if (mc == null) continue;
                var mesh = mc.sharedMesh;
                Object.DestroyImmediate(mc);
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        var scene = surface.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}

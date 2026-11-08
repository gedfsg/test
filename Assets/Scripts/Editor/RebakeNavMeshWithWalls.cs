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
///  4) 그래도 바깥과 끊긴 실내(문 폭이 좁아 에이전트 반경에 막히거나, 경찰서처럼 실내 바닥이 climb 0.75m보다 높은 곳)는
///     문 안팎을 잇는 NavMeshLink를 자동 생성 (씬의 "ZombieDoorLinks" 아래, 다시 실행하면 지우고 새로 만듦)
///     - 굽기만 했을 땐 실내 100곳 중 9곳(경찰서 포함)이 끊겨서 좀비가 실내 플레이어에게 못 왔음 (Play 실측)
/// </summary>
public static class RebakeNavMeshWithWalls
{
    const string LinkRootName = "ZombieDoorLinks";

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

            // 4) 끊긴 실내를 문 링크로 연결
            ConnectInteriors(surface);
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

    /// <summary>
    /// 건물 주변 8곳(기준점)에서 실내 바닥까지 경로가 하나도 안 이어지는 건물을 찾아, 문 바깥 ↔ 문 안쪽에 NavMeshLink를 놓는다.
    /// 문 방향 = 건물 중심 → 문 중심. 바깥 쪽은 기준점과 이어진 지점, 안쪽은 실내 바닥과 이어진 지점을 문에서 0.5m씩 넓혀가며 찾음.
    /// (기준점을 맵 반대편 한 곳으로 잡으면 수백 m 경로 계산이 탐색 한도에 걸려 PathPartial이 나와서, 이어진 건물도 끊긴 걸로 오판했음)
    /// </summary>
    static void ConnectInteriors(NavMeshSurface surface)
    {
        var old = GameObject.Find(LinkRootName);
        if (old != null) Object.DestroyImmediate(old);

        var path = new UnityEngine.AI.NavMeshPath();
        bool Reach(Vector3 a, Vector3 b) => UnityEngine.AI.NavMesh.CalculatePath(a, b, UnityEngine.AI.NavMesh.AllAreas, path)
                                            && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;

        GameObject root = null;
        int linked = 0, failed = 0;
        foreach (var bi in Object.FindObjectsByType<BuildingInterior>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var wall = bi.GetComponentInChildren<BuildingWallCollider>();
            if (wall == null && bi.transform.parent != null) wall = bi.transform.parent.GetComponentInChildren<BuildingWallCollider>();
            if (wall == null || !wall.hasDoor) continue;

            Vector3 door = wall.transform.TransformPoint(wall.doorSlotCenter);
            Bounds trig = bi.GetComponent<Collider>().bounds;
            // 문 바닥 높이: 문 중심은 벽 높이 중간이라 실내 트리거 바닥 기준 (아래로 레이를 쏘면 임시 벽 콜라이더 지붕에 맞음)
            // 단, FirstAge 주택처럼 트리거가 땅속까지 내려간 건물은 지형 높이 기준
            float groundY = trig.min.y;
            var terrain = Terrain.activeTerrain;
            if (terrain != null) groundY = Mathf.Max(groundY, terrain.SampleHeight(door) + terrain.transform.position.y);
            groundY += 0.3f;
            door.y = groundY;

            // 실내 바닥 지점 (트리거 중심, 문 바닥 높이 근처)
            if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(trig.center.x, groundY + 0.5f, trig.center.z), out var inHit, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;

            // 기준점: 건물 둘레 (가장 긴 반경 + 12m) 8방향의 바깥 NavMesh 지점
            var refs = new List<Vector3>();
            float r = Mathf.Max(trig.extents.x, trig.extents.z) + 12f;
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad;
                var q = new Vector3(trig.center.x + Mathf.Cos(a) * r, groundY, trig.center.z + Mathf.Sin(a) * r);
                if (UnityEngine.AI.NavMesh.SamplePosition(q, out var qh, 3f, UnityEngine.AI.NavMesh.AllAreas)) refs.Add(qh.position);
            }
            bool FromOutside(Vector3 p) { foreach (var rp in refs) if (Reach(rp, p)) return true; return false; }
            if (refs.Count == 0 || FromOutside(inHit.position)) continue; // 이미 연결됨

            Vector3 outDir = door - trig.center; outDir.y = 0f;
            if (outDir.sqrMagnitude < 0.01f) continue;
            outDir.Normalize();

            // 바깥 지점: 문에서 바깥으로 0~16m, 기준점과 이어진 첫 지점
            Vector3? outside = null, inside = null;
            for (float d = 0f; d <= 16f && outside == null; d += 0.5f)
                if (UnityEngine.AI.NavMesh.SamplePosition(door + outDir * d, out var h, 1f, UnityEngine.AI.NavMesh.AllAreas) && FromOutside(h.position)) outside = h.position;
            // 안쪽 지점: 문에서 안으로 0~20m, 실내 바닥과 이어진 첫 지점
            for (float d = 0f; d <= 20f && inside == null; d += 0.5f)
                if (UnityEngine.AI.NavMesh.SamplePosition(door - outDir * d, out var h, 1f, UnityEngine.AI.NavMesh.AllAreas) && Reach(h.position, inHit.position)) inside = h.position;

            if (outside == null || inside == null)
            {
                failed++;
                Debug.LogWarning($"[NavBake] '{bi.name}' 실내가 바깥과 끊겼는데 문 링크 지점을 못 찾음 (바깥 {outside != null}, 안쪽 {inside != null})");
                continue;
            }

            if (root == null) root = new GameObject(LinkRootName);
            var go = new GameObject("DoorLink_" + bi.name);
            go.transform.SetParent(root.transform, false);
            var link = go.AddComponent<NavMeshLink>();
            link.agentTypeID = surface.agentTypeID;
            link.startPoint = outside.Value; // 부모가 원점이라 월드 좌표 그대로
            link.endPoint = inside.Value;
            link.width = 1.5f;
            link.bidirectional = true;
            linked++;
        }
        Debug.Log($"[NavBake] 끊긴 실내 문 링크 {linked}개 생성" + (failed > 0 ? $", 실패 {failed}개" : ""));
    }
}

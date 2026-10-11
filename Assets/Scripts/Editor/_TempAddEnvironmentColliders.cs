using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 대상 4개 루트만 스캔함 (나머지는 이미 콜라이더 있거나 범위 밖):
//   - Landskape plane_Landscape color 1_0_Cut 계열(12개) = 맵 테두리 산 → MeshCollider
//   - Proop3(자동차 70개) → BoxCollider
//   - Props(217개, 가로등/쓰레기통/전봇대/바리케이드 등) → BoxCollider, 작은 건 스킵
//   - "Urban Bits..." (128개, 벤치/간판/나무 등) → BoxCollider(나무는 CapsuleCollider), 작은 건 스킵
// 제외: Ground/Roads/Buildings(이미 콜라이더 있음), low_poly_scene_forest_waterfall(맵 밖 배경숲,
// 2225개라 성능상 제외), Player/Canvas/Zones/MapGuide/무기·아이템/좀비/UI.
public static class TempAddEnvironmentColliders
{
    const float SkipFootprintThreshold = 0.5f; // 이보다 가로/세로 작으면 스킵(풀, 꽃 등)
    static readonly string[] NameSkipContains = { "shadow", "walkingroad", "band" };

    [MenuItem("Tools/Map/Add Environment Colliders")]
    static void Run()
    {
        int boxCount = 0, meshCount = 0, capsuleCount = 0, skippedSmall = 0, skippedName = 0, alreadyHas = 0;
        var log = new System.Text.StringBuilder();

        // (루트 이름, 모드) 모드: "mesh"=MeshCollider 강제(산), "auto"=크기/이름 기반 자동 판단
        var targets = new (string root, string mode)[]
        {
            ("Landskape plane_Landscape color 1_0_Cut", "mesh"),
            ("Proop3", "box"),
            ("Props", "auto"),
            ("Urban Bits – Free Lowpoly City Props Pack - By MaHa", "auto"),
        };

        foreach (var (rootPattern, mode) in targets)
        {
            var roots = FindRootsByNamePrefix(rootPattern);
            foreach (var root in roots)
            {
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var go = mr.gameObject;
                    if (go.GetComponent<Collider>() != null) { alreadyHas++; continue; }

                    string lname = go.name.ToLowerInvariant();
                    bool nameSkip = false;
                    foreach (var s in NameSkipContains) if (lname.Contains(s)) { nameSkip = true; break; }
                    if (nameSkip) { skippedName++; continue; }

                    var mf = go.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Bounds lb = mf.sharedMesh.bounds;

                    if (mode != "mesh")
                    {
                        float footprint = Mathf.Max(lb.size.x, lb.size.z) * MaxAbsScale(go.transform.lossyScale);
                        if (footprint < SkipFootprintThreshold) { skippedSmall++; continue; }
                    }

                    if (mode == "mesh")
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;
                        meshCount++;
                    }
                    else if (mode == "box" || !lname.Contains("tree"))
                    {
                        var bc = go.AddComponent<BoxCollider>();
                        bc.center = lb.center;
                        bc.size = lb.size;
                        boxCount++;
                    }
                    else // auto 모드 + 이름에 tree 포함 → 몸통만 캡슐로 (잎 캐노피까지 안 막게)
                    {
                        var cc = go.AddComponent<CapsuleCollider>();
                        cc.center = lb.center;
                        cc.height = lb.size.y;
                        cc.radius = Mathf.Min(lb.size.x, lb.size.z) * 0.15f;
                        capsuleCount++;
                    }
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string msg = $"✅ 환경 콜라이더 추가 완료\nBoxCollider: {boxCount}개\nMeshCollider(산): {meshCount}개\nCapsuleCollider(나무): {capsuleCount}개\n" +
                     $"스킵(작음): {skippedSmall}개\n스킵(이름제외): {skippedName}개\n이미있음: {alreadyHas}개";
        Debug.Log(msg);
    }

    static float MaxAbsScale(Vector3 s) => Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

    [MenuItem("Tools/Map/Rebake NavMesh Surface")]
    static void RebakeNavMesh()
    {
        var navSurfaceGo = GameObject.Find("NavMeshSurface");
        var surface = navSurfaceGo.GetComponent<Unity.AI.Navigation.NavMeshSurface>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        surface.BuildNavMesh();
        sw.Stop();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[NavBake] 완료, 소요시간=" + sw.ElapsedMilliseconds + "ms");
    }

    // "Landskape plane_Landscape color 1_0_Cut", "...(1)", "...(2)" 등 숫자 접미사 변형까지 전부 찾음
    static List<GameObject> FindRootsByNamePrefix(string prefix)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var result = new List<GameObject>();
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == prefix || go.name.StartsWith(prefix + " ("))
                result.Add(go);
        return result;
    }
}

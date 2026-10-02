using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 조형물/구조물(교회, 농장, 집, 경찰서 등 임포트 오브젝트)에 콜라이더가 아예 없어서
// 플레이어가 그냥 통과하던 문제 해결. 루트 하나에 전체 바운즈를 감싸는 콜라이더
// 1개만 붙임(파츠별로 안 붙임 - 파츠가 수백 개인 것도 있어서 성능 부담 없게).
// 기본은 BoxCollider(제일 가벼움), 풋프린트가 박스보다 많이 비어있는(비정형) 경우만
// 렌더러가 적은 오브젝트에 한해 Convex MeshCollider로.
public static class TempAddPropColliders
{
    const float MinFillRatioForBox = 0.45f;
    const int MaxRenderersForMesh = 30; // 이보다 파츠 많으면 무조건 Box(Convex Mesh는 파츠 적을 때만)

    // 도로/바닥/Zone/무기·아이템/UI/Player/Zombie/맵가이드/오프맵 배경은 대상에서 제외.
    // 여기 나열된 건 "콜라이더 없이 방치돼 있던, 실제 맵 안에 있는 구조물"만 추림.
    static readonly string[] TargetNames =
    {
        "scene", "scene (2)", "scene (3)", "scene (4)",
        "Church by Poly by Google - 2-J0Y-8AQOG",
        "Farm by Poly by Google - 5GDbUJV2vQb",
        "Structure by Quaternius - ilWoURnbZW",
        "Structure by Quaternius - PB5Cd3dL24",
        "Farm by Quaternius - 91wMLb9kKo",
        "Houses_FirstAge_1_Level1",
        "Houses_FirstAge_1_Level1 (1)",
    };

    [MenuItem("Tools/Map/Add Colliders To Uncollided Props")]
    static void Run()
    {
        int boxCount = 0, meshCount = 0, skipped = 0;
        var added = new List<string>();

        foreach (var n in TargetNames)
        {
            GameObject go = GameObject.Find(n);
            if (go == null) { Debug.LogWarning($"'{n}' 을(를) 씬에서 못 찾음, 건너뜀"); continue; }
            if (go.GetComponentsInChildren<Collider>(true).Length > 0) { skipped++; continue; } // 이미 있으면 안전하게 건너뜀

            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) continue;

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

            bool tryMesh = renderers.Length <= MaxRenderersForMesh
                           && EstimateFillRatio(go, worldBounds) < MinFillRatioForBox;

            if (tryMesh && TryAddConvexMesh(go))
            {
                meshCount++;
                added.Add($"{n} (Mesh Convex, 파츠 {renderers.Length}개)");
                continue;
            }

            AddBoxCollider(go, worldBounds);
            boxCount++;
            added.Add($"{n} (Box, 파츠 {renderers.Length}개)");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string msg = $"✅ 콜라이더 추가: Box {boxCount}개, Mesh(Convex) {meshCount}개, 총 {boxCount + meshCount}개"
                     + (skipped > 0 ? $" (이미 콜라이더 있어서 건너뜀 {skipped}개)" : "") + "\n"
                     + string.Join("\n", added);
        Debug.Log(msg);
    }

    // 루트 로컬 공간 기준 바운즈(회전 반영)를 감싸는 BoxCollider 1개를 루트에 부착
    static void AddBoxCollider(GameObject go, Bounds worldBounds)
    {
        Bounds lb = ComputeLocalBounds(go, worldBounds);
        var box = go.AddComponent<BoxCollider>();
        box.center = lb.center;
        box.size = lb.size;
    }

    // 파츠가 적은 오브젝트만: 가장 큰 메시 하나에 Convex MeshCollider
    static bool TryAddConvexMesh(GameObject go)
    {
        MeshFilter biggest = null;
        float biggestVol = 0f;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            Vector3 s = mf.sharedMesh.bounds.size;
            float vol = s.x * s.y * s.z;
            if (vol > biggestVol) { biggestVol = vol; biggest = mf; }
        }
        if (biggest == null) return false;

        var mc = biggest.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = biggest.sharedMesh;
        mc.convex = true;
        return true;
    }

    static Bounds ComputeLocalBounds(GameObject go, Bounds worldBounds)
    {
        Vector3 c = worldBounds.center, e = worldBounds.extents;
        Vector3[] corners =
        {
            c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
            c + new Vector3(-e.x, e.y, -e.z),  c + new Vector3(e.x, e.y, -e.z),
            c + new Vector3(-e.x, -e.y, e.z),  c + new Vector3(e.x, -e.y, e.z),
            c + new Vector3(-e.x, e.y, e.z),   c + new Vector3(e.x, e.y, e.z),
        };
        Bounds lb = new Bounds(go.transform.InverseTransformPoint(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++) lb.Encapsulate(go.transform.InverseTransformPoint(corners[i]));
        return lb;
    }

    // 건물 풋프린트(XZ) 중 실제 메시가 채우는 비율 추정. Read/Write Enabled 꺼진
    // 메시는 못 읽으니 그런 경우 1(=박스로 취급)을 반환해 안전하게 Box로 감.
    static float EstimateFillRatio(GameObject go, Bounds worldBounds)
    {
        var tri2D = new List<Vector2>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var verts = mf.sharedMesh.vertices;
            var tris = mf.sharedMesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = mf.transform.TransformPoint(verts[tris[i]]);
                Vector3 b = mf.transform.TransformPoint(verts[tris[i + 1]]);
                Vector3 c = mf.transform.TransformPoint(verts[tris[i + 2]]);
                tri2D.Add(new Vector2(a.x, a.z));
                tri2D.Add(new Vector2(b.x, b.z));
                tri2D.Add(new Vector2(c.x, c.z));
            }
        }
        if (tri2D.Count == 0) return 1f;

        const int grid = 6;
        int hit = 0, total = 0;
        for (int ix = 0; ix < grid; ix++)
        for (int iz = 0; iz < grid; iz++)
        {
            float px = Mathf.Lerp(worldBounds.min.x, worldBounds.max.x, (ix + 0.5f) / grid);
            float pz = Mathf.Lerp(worldBounds.min.z, worldBounds.max.z, (iz + 0.5f) / grid);
            total++;
            var p = new Vector2(px, pz);
            for (int i = 0; i < tri2D.Count; i += 3)
                if (PointInTriangle(p, tri2D[i], tri2D[i + 1], tri2D[i + 2])) { hit++; break; }
        }
        return total > 0 ? (float)hit / total : 1f;
    }

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0;
        bool pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static float Cross(Vector2 p1, Vector2 p2, Vector2 p3)
        => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
}

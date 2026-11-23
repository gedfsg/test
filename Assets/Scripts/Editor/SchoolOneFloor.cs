using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools/Interior/School 1F Only (2026-11-24) - 학교(low_poly_school)를 1층 건물로 만듦 (사용자 요청: 2층·계단 필요 없음).
///
/// 실측 (Edit 모드, 2026-11-24):
///   1층 바닥 16.5 / 2층 바닥 27.6 / 지붕 38.7~52.6 (Group43 Mesh37)
///   1층 = Group1(Mesh1), 바닥 = Group44(Mesh38), 2층 = Group46(Mesh44 27.0~41.5), 계단 = Stairs1(2층→옥상)·Stairs2(1층→2층)
///   바깥벽 Group45(Mesh39 15.1~38.7)와 현관 탑(Mesh39_Color_A07 ~37.4)은 두 층이 메시 하나 → 27.6에서 잘라 아래만 남김
///
/// 하는 일 (다시 실행해도 안전 - 이미 했으면 건너뜀, 표시 = Enterable_School/_School1F):
///  1) 건물 영역(z ≥ 112) 안에서 2층 높이(27.6) 위에만 있는 조각(창문·문·2층 벽 등) 끔 + 계단 2개 끔 (렌더러·콜라이더 같이)
///  2) 두 층에 걸친 메시 3개를 27.6 아래만 남기게 잘라서 새 메시로 (Assets/_CutMeshes/School) - 보이는 메시·콜라이더 메시 둘 다
///  3) 지붕(Group43)을 11.1m 내려서 1층 위에 얹음
/// 끝나면 Tools/Map/Rebake NavMesh With Building Walls 필요 (계단·2층 길이 사라짐)
/// 되돌리기: Undo(Ctrl+Z) 또는 씬을 저장 전 상태로
/// </summary>
public static class SchoolOneFloor
{
    const string SchoolPath = "Buildings/Enterable_School";
    const string MeshDir = "Assets/_CutMeshes/School";
    const float CutY = 27.6f;            // 2층 바닥 (= 1층 천장 위)
    const float RoofDrop = 38.7f - 27.6f; // 지붕 밑면 38.7 → 27.6
    const float BuildingMinZ = 112f;      // 이보다 남쪽은 마당 (울타리·차양 등은 건드리지 않음)

    [MenuItem("Tools/Interior/School 1F Only (학교 1층만)")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[School1F] Play 모드에서는 실행 불가"); return; }
        var school = GameObject.Find(SchoolPath);
        if (school == null) { Debug.LogError("[School1F] " + SchoolPath + " 없음"); return; }
        if (school.transform.Find("_School1F") != null) { Debug.Log("[School1F] 이미 1층 건물로 바뀌어 있음 - 건너뜀"); return; }
        var model = school.transform.Find("scene (2)");
        if (model == null) { Debug.LogError("[School1F] scene (2) 없음"); return; }
        Directory.CreateDirectory(MeshDir);

        int off = 0, cut = 0;
        var log = new List<string>();
        var roof = FindDeep(model, "Group43");

        foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (roof != null && r.transform.IsChildOf(roof)) continue;           // 지붕은 내림 (아래)
            var b = r.bounds;
            if (b.max.z < BuildingMinZ) continue;                                 // 마당
            bool stairs = InParent(r.transform, "Stairs");
            if (stairs || b.min.y >= CutY - 0.05f)
            {
                if (r.gameObject.activeSelf) { Undo.RecordObject(r.gameObject, "School 1F"); r.gameObject.SetActive(false); off++; }
                continue;
            }
            if (b.max.y > CutY + 0.05f)
            {
                var mf = r.GetComponent<MeshFilter>();
                var mc = r.GetComponent<MeshCollider>();
                var src = mf.sharedMesh;
                var renderCut = CutBelow(src, r.transform, CutY, src.name + "_1F");
                Undo.RecordObject(mf, "School 1F");
                mf.sharedMesh = renderCut;
                if (mc != null && mc.sharedMesh != null)
                {
                    Undo.RecordObject(mc, "School 1F");
                    mc.sharedMesh = mc.sharedMesh == src ? renderCut : CutBelow(mc.sharedMesh, r.transform, CutY, mc.sharedMesh.name + "_1F");
                }
                cut++;
                log.Add($"{r.name}: {src.vertexCount} → {renderCut.vertexCount}정점");
            }
        }

        if (roof != null)
        {
            Undo.RecordObject(roof, "School 1F");
            roof.position += Vector3.down * RoofDrop;
            log.Add($"지붕 {roof.name} {RoofDrop:F1}m 내림");
            RemoveRoofStairHouse(roof, log);
        }

        var mark = new GameObject("_School1F");
        Undo.RegisterCreatedObjectUndo(mark, "School 1F");
        mark.transform.SetParent(school.transform, false);
        mark.SetActive(false);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(school.scene);
        Debug.Log($"[School1F] 2층·계단 조각 {off}개 끔, 걸친 메시 {cut}개 자름\n" + string.Join("\n", log) +
                  "\n→ Tools/Map/Rebake NavMesh With Building Walls 를 실행하세요");
    }

    // 옥상 계단실 (지붕 메시 Mesh37 안, 실측 x -282.5~-275.2, 뒤쪽 z 128~165, 꼭대기 41.5) - 계단이 없어졌으니 옥상 높이(낮은 턱 28.3)까지 잘라냄
    static readonly Rect StairHouse = Rect.MinMaxRect(-282.8f, 128f, -274.9f, 166f);
    const float StairHouseKeepY = 28.3f;

    /// <summary>(2026-11-24) 지붕을 내린 뒤 옥상 계단실 상자 제거 (지붕 메시의 그 영역만 잘라냄)</summary>
    public static void RemoveRoofStairHouse(Transform roof, List<string> log)
    {
        foreach (var r in roof.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var b = r.bounds;
            if (b.max.y <= StairHouseKeepY + 0.05f || !new Rect(b.min.x, b.min.z, b.size.x, b.size.z).Overlaps(StairHouse)) continue;
            var src = mf.sharedMesh;
            var cutMesh = CutBelow(src, r.transform, StairHouseKeepY, src.name + "_NoStairHouse", StairHouse);
            Undo.RecordObject(mf, "School 1F");
            mf.sharedMesh = cutMesh;
            var mc = r.GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh != null)
            {
                Undo.RecordObject(mc, "School 1F");
                mc.sharedMesh = mc.sharedMesh == src ? cutMesh : CutBelow(mc.sharedMesh, r.transform, StairHouseKeepY, mc.sharedMesh.name + "_NoStairHouse", StairHouse);
            }
            log?.Add($"옥상 계단실 제거: {r.name} {src.vertexCount} → {cutMesh.vertexCount}정점");
        }
        AddRoofPatch(roof, log);
    }

    /// <summary>계단실 아래 지붕 구멍(계단 통로)을 같은 재질 판으로 덮음. 지붕(Group43) 아래에 둬서 실내 컷어웨이 때 지붕과 같이 사라짐</summary>
    static void AddRoofPatch(Transform roof, List<string> log)
    {
        var old = roof.Find("RoofPatch_StairWell");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Material mat = null;
        foreach (var r in roof.GetComponentsInChildren<MeshRenderer>(true)) if (r.name.Contains("FrontColor")) { mat = r.sharedMaterial; break; }
        var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        patch.name = "RoofPatch_StairWell";
        Object.DestroyImmediate(patch.GetComponent<Collider>());   // 옥상은 못 올라가므로 충돌 필요 없음 (NavMesh에도 안 잡히게)
        Undo.RegisterCreatedObjectUndo(patch, "School 1F");
        patch.transform.SetParent(roof, true);
        const float top = 28.2f, thick = 0.6f;   // 지붕 윗면 높이 (지붕 메시 실측 27.6~28.2)
        patch.transform.position = new Vector3(StairHouse.center.x, top - thick * 0.5f, StairHouse.center.y);
        patch.transform.rotation = Quaternion.identity;
        var ls = new Vector3(StairHouse.width, thick, StairHouse.height);
        var ps = roof.lossyScale;
        patch.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
        if (mat != null) patch.GetComponent<MeshRenderer>().sharedMaterial = mat;
        log?.Add("계단 구멍 덮개 RoofPatch_StairWell 추가");
    }

    static bool InParent(Transform t, string namePart)
    {
        for (; t != null; t = t.parent) if (t.name.StartsWith(namePart)) return true;
        return false;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    // ── 메시 자르기: 월드 y ≤ cutY 쪽만 남김 (삼각형을 평면으로 잘라 새 정점은 보간) ──
    struct V { public Vector3 p, n; public Vector2 uv; public Vector4 t; public Color c; }

    static Mesh CutBelow(Mesh src, Transform tf, float cutY, string name) => CutBelow(src, tf, cutY, name, null);

    /// <summary>region(월드 xz)이 있으면 그 안에 무게중심이 있는 삼각형만 자르고 나머지는 그대로 둠</summary>
    static Mesh CutBelow(Mesh src, Transform tf, float cutY, string name, Rect? region)
    {
        string path = $"{MeshDir}/{name}.asset";
        var verts = src.vertices; var norms = src.normals; var uvs = src.uv; var tans = src.tangents; var cols = src.colors;
        bool hasN = norms.Length == verts.Length, hasUV = uvs.Length == verts.Length, hasT = tans.Length == verts.Length, hasC = cols.Length == verts.Length;
        var l2w = tf.localToWorldMatrix;
        float[] wy = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++) wy[i] = l2w.MultiplyPoint3x4(verts[i]).y;

        var outV = new List<V>();
        var subTris = new List<List<int>>();
        V Get(int i) => new V { p = verts[i], n = hasN ? norms[i] : Vector3.up, uv = hasUV ? uvs[i] : Vector2.zero, t = hasT ? tans[i] : Vector4.zero, c = hasC ? cols[i] : Color.white };
        V Lerp(V a, V b, float k) => new V { p = Vector3.Lerp(a.p, b.p, k), n = Vector3.Lerp(a.n, b.n, k).normalized, uv = Vector2.Lerp(a.uv, b.uv, k), t = Vector4.Lerp(a.t, b.t, k), c = Color.Lerp(a.c, b.c, k) };

        for (int s = 0; s < src.subMeshCount; s++)
        {
            var tris = src.GetTriangles(s);
            var list = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (region.HasValue)
                {
                    var cen = l2w.MultiplyPoint3x4((verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f);
                    if (!region.Value.Contains(new Vector2(cen.x, cen.z)))
                    {
                        int b0 = outV.Count;
                        for (int k = 0; k < 3; k++) outV.Add(Get(tris[i + k]));
                        list.Add(b0); list.Add(b0 + 1); list.Add(b0 + 2);
                        continue;
                    }
                }
                // Sutherland–Hodgman: 삼각형 → y ≤ cutY 다각형
                var poly = new List<(V v, float y)>();
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[i + k], b = tris[i + (k + 1) % 3];
                    bool ina = wy[a] <= cutY, inb = wy[b] <= cutY;
                    if (ina) poly.Add((Get(a), wy[a]));
                    if (ina != inb)
                    {
                        float t = (cutY - wy[a]) / (wy[b] - wy[a]);
                        poly.Add((Lerp(Get(a), Get(b), t), cutY));
                    }
                }
                if (poly.Count < 3) continue;
                int baseIdx = outV.Count;
                foreach (var pv in poly) outV.Add(pv.v);
                for (int k = 1; k + 1 < poly.Count; k++) { list.Add(baseIdx); list.Add(baseIdx + k); list.Add(baseIdx + k + 1); }
            }
            subTris.Add(list);
        }

        var m = new Mesh { name = name };
        if (outV.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        var pArr = new Vector3[outV.Count]; var nArr = new Vector3[outV.Count]; var uArr = new Vector2[outV.Count]; var tArr = new Vector4[outV.Count]; var cArr = new Color[outV.Count];
        for (int i = 0; i < outV.Count; i++) { pArr[i] = outV[i].p; nArr[i] = outV[i].n; uArr[i] = outV[i].uv; tArr[i] = outV[i].t; cArr[i] = outV[i].c; }
        m.vertices = pArr;
        if (hasN) m.normals = nArr;
        if (hasUV) m.uv = uArr;
        if (hasT) m.tangents = tArr;
        if (hasC) m.colors = cArr;
        m.subMeshCount = subTris.Count;
        for (int s = 0; s < subTris.Count; s++) m.SetTriangles(subTris[s], s);
        m.RecalculateBounds();

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(m, existing); return existing; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}

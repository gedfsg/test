using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 일회성 스크립트 - 확인 후 삭제할 것
// 씬에 있는 큐브(잘라낼 영역 표시용, "CutRegion" 이름)의 월드 바운드를 기준으로,
// 선택된 오브젝트의 메시에서 그 박스 안에 중심이 있는 삼각형만 남기고 나머지는 잘라냄.
// 산/지형처럼 가장자리가 들쭉날쭉해도 자연스러운 소재라 삼각형 단위로 잘라도 티가 안 남.
public static class TempCutMeshToRegion
{
    [MenuItem("Tools/Map/Create Cut Region Box")]
    static void CreateCutRegionBox()
    {
        var existing = GameObject.Find("CutRegion");
        if (existing != null) { Selection.activeGameObject = existing; return; }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "CutRegion";
        go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        go.transform.localScale = new Vector3(50f, 50f, 50f);

        var rend = go.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.2f, 0.2f, 0.3f);
        mat.SetFloat("_Mode", 3); // transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        rend.sharedMaterial = mat;

        Selection.activeGameObject = go;
        Debug.Log("✅ 'CutRegion' 빨간 반투명 박스 생성됨. Scene 뷰에서 Move/Scale 툴로 원하는 영역에 맞게 옮기고 늘린 다음, " +
                  "자르고 싶은 지형 오브젝트를 선택하고 Tools → Map → Cut Selected Mesh To CutRegion 실행하세요.");
    }

    // 오브젝트 자기 자신 바운드가 CutRegion 박스보다 어느 한 축이라도 이만큼 크면
    // "지형처럼 연속된 큰 메시"로 보고 삼각형 단위로 자름. 그보다 작으면 나무/바위처럼
    // "따로따로인 소품"으로 보고 박스 안에 있는지 여부로 통째로 남기거나 버림.
    const float LargeMeshRatio = 1.5f;

    [MenuItem("Tools/Map/Cut Selected Mesh To CutRegion")]
    static void CutSelectedMesh()
    {
        var target = Selection.activeGameObject;
        if (target == null) { Debug.LogError("자를 오브젝트를 먼저 선택하세요."); return; }

        var regionGo = GameObject.Find("CutRegion");
        if (regionGo == null) { Debug.LogError("'CutRegion' 박스가 없습니다. 먼저 Tools → Map → Create Cut Region Box 실행하세요."); return; }

        // 빨간 박스는 위치만 표시하는 도구라, 이걸 옮긴 다음 다시 선택 안 하고 바로 자르기를
        // 누르면 박스 자기 자신이 잘리는 사고가 남 — 그걸 막음
        if (target == regionGo || target.transform.IsChildOf(regionGo.transform))
        {
            Debug.LogError("지금 선택된 게 빨간 CutRegion 박스 자체예요. 박스를 원하는 위치로 옮긴 다음, " +
                            "Hierarchy에서 자르고 싶은 진짜 지형/나무 오브젝트를 다시 클릭해서 선택하고 실행하세요.");
            return;
        }

        Bounds regionBounds = regionGo.GetComponent<Renderer>().bounds;

        // target 자신 + 모든 자식 중에서 실제로 메시를 가진 것들을 전부 모음
        var meshFilters = new List<MeshFilter>(target.GetComponentsInChildren<MeshFilter>(true));
        meshFilters.RemoveAll(mf => mf.sharedMesh == null);
        if (meshFilters.Count == 0) { Debug.LogError($"'{target.name}' 안에 메시를 가진 오브젝트가 하나도 없습니다."); return; }

        GameObject resultParent = new GameObject(target.name + "_Cut");
        int largeCount = 0, propKeptCount = 0, propSkippedCount = 0, totalTris = 0;
        string dir = "Assets/_CutMeshes";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "_CutMeshes");

        foreach (var mf in meshFilters)
        {
            var rend = mf.GetComponent<Renderer>();
            if (rend == null) continue;
            Bounds objBounds = rend.bounds;

            bool isLarge = objBounds.size.x > regionBounds.size.x * LargeMeshRatio
                         || objBounds.size.z > regionBounds.size.z * LargeMeshRatio;

            if (!isLarge)
            {
                // 소품: 박스 안에 중심이 있으면 통째로 복제해서 남기고, 없으면 건너뜀
                if (!regionBounds.Contains(objBounds.center)) { propSkippedCount++; continue; }

                GameObject clone = Object.Instantiate(mf.gameObject, resultParent.transform, true);
                clone.name = mf.gameObject.name;
                propKeptCount++;
                continue;
            }

            // 큰(지형류) 메시: 삼각형 단위로 잘라냄
            int cutTris = CutOneMeshIntoParent(mf, regionBounds, resultParent, dir);
            if (cutTris > 0) { largeCount++; totalTris += cutTris; }
        }

        if (resultParent.transform.childCount == 0)
        {
            Debug.LogError("CutRegion 박스 안에 아무것도 없습니다 (지형도, 소품도 없음). 박스 위치/크기를 확인하세요.");
            Object.DestroyImmediate(resultParent);
            return;
        }

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = resultParent;
        Debug.Log($"✅ '{resultParent.name}' 생성 완료 — 큰 지형 {largeCount}개(삼각형 총 {totalTris}개 유지), " +
                  $"소품 {propKeptCount}개 통째로 유지({propSkippedCount}개는 박스 밖이라 제외). " +
                  $"원본은 그대로 남아있으니 필요없으면 직접 삭제하세요.");
    }

    // 하나의 큰 메시를 CutRegion 박스 기준으로 삼각형 단위로 잘라서 resultParent의 자식으로 추가.
    // 반환값은 남긴 삼각형 개수(0이면 이 메시는 박스와 안 겹침).
    static int CutOneMeshIntoParent(MeshFilter mf, Bounds worldBounds, GameObject resultParent, string dir)
    {
        Transform t = mf.transform;
        Mesh src = mf.sharedMesh;
        Vector3[] verts = src.vertices;
        int[] tris = src.triangles;
        Vector3[] normals = src.normals;
        Vector2[] uvs = src.uv;
        bool hasNormals = normals != null && normals.Length == verts.Length;
        bool hasUVs = uvs != null && uvs.Length == verts.Length;

        var newVerts = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var newUVs = new List<Vector2>();
        var newTris = new List<int>();
        var remap = new Dictionary<int, int>();

        int AddVert(int oldIndex)
        {
            if (remap.TryGetValue(oldIndex, out int ni)) return ni;
            int idx = newVerts.Count;
            newVerts.Add(verts[oldIndex]);
            if (hasNormals) newNormals.Add(normals[oldIndex]);
            if (hasUVs) newUVs.Add(uvs[oldIndex]);
            remap[oldIndex] = idx;
            return idx;
        }

        int keptTriCount = 0;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            Vector3 centroidLocal = (verts[a] + verts[b] + verts[c]) / 3f;
            Vector3 centroidWorld = t.TransformPoint(centroidLocal);
            if (!worldBounds.Contains(centroidWorld)) continue;

            newTris.Add(AddVert(a));
            newTris.Add(AddVert(b));
            newTris.Add(AddVert(c));
            keptTriCount++;
        }

        if (keptTriCount == 0) return 0;

        Mesh newMesh = new Mesh();
        newMesh.name = src.name + "_cut";
        newMesh.indexFormat = newVerts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        newMesh.SetVertices(newVerts);
        if (hasNormals) newMesh.SetNormals(newNormals); else newMesh.RecalculateNormals();
        if (hasUVs) newMesh.SetUVs(0, newUVs);
        newMesh.SetTriangles(newTris, 0);
        newMesh.RecalculateBounds();

        string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + newMesh.name + ".asset");
        AssetDatabase.CreateAsset(newMesh, path);

        GameObject cutGo = new GameObject(mf.gameObject.name + "_Cut");
        cutGo.transform.SetParent(resultParent.transform, false);
        cutGo.transform.position = t.position;
        cutGo.transform.rotation = t.rotation;
        cutGo.transform.localScale = t.lossyScale; // 부모 스케일까지 다 곱해진 실제 월드 스케일 써야 함
        var newMf = cutGo.AddComponent<MeshFilter>();
        newMf.sharedMesh = newMesh;
        var newMr = cutGo.AddComponent<MeshRenderer>();
        var srcRenderer = mf.GetComponent<MeshRenderer>();
        if (srcRenderer != null) newMr.sharedMaterials = srcRenderer.sharedMaterials;

        return keptTriCount;
    }

    [MenuItem("Tools/Map/Delete CutRegion Box")]
    static void DeleteCutRegionBox()
    {
        var go = GameObject.Find("CutRegion");
        if (go != null) Object.DestroyImmediate(go);
    }
}

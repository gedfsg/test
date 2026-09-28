using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// "Buildings"/"SketchfabAssets" 아래 모든 건물에 대해:
//  0) 건물 풋프린트(바운딩 박스)를 실제 메시가 얼마나 채우고 있는지 격자 샘플링으로 추정.
//     ㄷ자/L자처럼 네모가 아닌 건물은 사각형 벽 4개로 못 감싸므로 자동 처리에서 제외하고
//     원래 모양 그대로의 MeshCollider(문 없음, 막힘)로 되돌림 — 문 앞까지는 가지지만 못 들어감.
//  1) 네모인 건물만: 기존 콜라이더를 지우고, 가장 가까운 도로 쪽 벽 한 면에만
//     문 크기 구멍을 낸 4면 BoxCollider(콜라이더만, 렌더러 없음 = 눈에 보이는 큐브 없음)를
//     건물 루트에 직접 부착. 나머지 3면은 완전히 막힘 (실제 문처럼 한쪽으로만 드나듦).
//  2) 건물 풋프린트의 95%로 채운 트리거 BoxCollider 부착 (실내 어디에 있어도 계속 켜져있게)
//  3) BuildingInterior 컴포넌트 부착 (지붕 컷어웨이는 그 컴포넌트의 Awake에서 처리)
// 재실행해도 안전(항상 다시 계산/덮어씀).
public static class TempApplyBuildingInterior
{
    const float WallThickness = 0.3f;
    const float DoorWidth = 2.2f;
    // 트리거는 풋프린트의 95% (거의 꽉 채움). 50%로 줄였더니 건물 안쪽 깊숙이 걸어가면
    // 트리거 밖으로 나가버려서 지붕/벽이 다시 나타나고(아직 건물 안인데!) 어디로
    // 나가야 하는지 알 수 없게 되는 문제가 있었음 — 실내에 있는 동안은 항상 켜져
    // 있어야 하므로 풋프린트 전체에 가깝게 둠.
    const float TriggerShrink = 0.95f;
    const float MinFootprint = 8f;    // 이보다 작으면(가로등 등) 건물로 취급 안 함
    const float MinFillRatio = 0.6f;  // 바운딩 박스 풋프린트 중 실제 메시가 덮는 비율. 이보다 낮으면
                                       // ㄷ자/L자처럼 네모가 아닌 건물로 보고 자동 문 처리 대상에서 제외
    const int FillGridRes = 8;        // 풋프린트 채움 비율 샘플링 격자 해상도 (8x8)

    [MenuItem("Tools/Map/Setup Building Interiors (Roof Cutaway)")]
    static void Apply()
    {
        List<Transform> targets = new List<Transform>();
        AddChildren("Buildings", targets);
        AddChildren("SketchfabAssets", targets);

        int applied = 0, skippedSmall = 0, skippedNoBounds = 0, skippedIrregular = 0;
        List<string> warnings = new List<string>();

        foreach (Transform building in targets)
        {
            GameObject go = building.gameObject;
            Bounds? worldBounds = GetWorldBounds(go);
            if (worldBounds == null) { skippedNoBounds++; continue; }
            if (worldBounds.Value.size.x * worldBounds.Value.size.z < MinFootprint) { skippedSmall++; continue; }

            Bounds? localBounds = ComputeLocalBounds(go);
            if (localBounds == null) { skippedNoBounds++; continue; }
            Bounds lb = localBounds.Value;

            // ── 네모가 아닌 건물(ㄷ자/L자 등)은 사각형 벽 4개로 못 감싸서 자동 처리 제외 ──
            // 원래 정확한 모양의 MeshCollider(문 없음)로 되돌려서 "문 앞까지는 가지지만
            // 못 들어가는" 정상적인 막힌 건물 상태로 둠.
            float fillRatio = EstimateFootprintFillRatio(go, lb);
            if (fillRatio < MinFillRatio)
            {
                RestoreSolidCollider(go);
                skippedIrregular++;
                continue;
            }

            // ── BuildingInterior가 Collider를 필요로 해서(RequireComponent) 그게 붙어있으면
            //    콜라이더를 못 지움 → 항상 이걸 먼저 떼어내고 나서 콜라이더를 정리해야 함 ──
            BuildingInterior existingInterior = go.GetComponent<BuildingInterior>();
            if (existingInterior != null) Object.DestroyImmediate(existingInterior);

            // ── 기존 콜라이더 전부 제거 ──
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);

            Vector3 worldDirToRoad = FindNearestRoadDirection(go.transform.position);
            Vector3 localDir = go.transform.InverseTransformDirection(worldDirToRoad);
            localDir.y = 0f;
            if (localDir.sqrMagnitude < 0.0001f) localDir = Vector3.forward;
            localDir.Normalize();
            Vector3 doorSide = PickAxisSide(localDir);

            bool ok = BuildPerimeterWithDoor(go, lb, go.transform.lossyScale, doorSide);
            if (!ok) warnings.Add($"'{go.name}' — 문 낼 공간이 부족해 벽을 막힌 채로 뒀습니다 (건물이 너무 작음).");

            // ── 트리거 (풋프린트 50%) ──
            BoxCollider trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = lb.center;
            trigger.size = new Vector3(lb.size.x * TriggerShrink, lb.size.y, lb.size.z * TriggerShrink);

            // ── BuildingInterior 컴포넌트 (매번 새로 부착 — 위에서 이미 제거함) ──
            go.AddComponent<BuildingInterior>();

            applied++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        string msg = $"✅ 건물 {applied}개 적용 (문 구멍 + 축소 트리거 + BuildingInterior), " +
                     $"제외: 풋프린트 미달 {skippedSmall}개, 렌더러 없음 {skippedNoBounds}개, " +
                     $"네모 아님(자동 문 처리 안 함, 원래 막힌 상태로 복구) {skippedIrregular}개.";
        if (warnings.Count > 0) msg += "\n⚠️ " + string.Join("\n⚠️ ", warnings);
        Debug.Log(msg);
    }

    // ── 건물 풋프린트(바운딩 박스) 중 실제 메시가 덮고 있는 비율을 격자 샘플링으로 추정.
    // 네모 건물은 거의 1.0에 가깝고, ㄷ자/L자처럼 빈 공간이 많은 건물은 훨씬 낮게 나옴.
    static float EstimateFootprintFillRatio(GameObject go, Bounds lb)
    {
        List<Vector2> tri2D = new List<Vector2>();
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            EnsureReadable(mf.sharedMesh); // Kenney FBX 등은 기본적으로 Read/Write Enabled가 꺼져있어서 강제로 켬
            if (!mf.sharedMesh.isReadable) continue; // 그래도 안 읽히면(임포터를 못 찾음 등) 건너뜀
            Vector3[] verts = mf.sharedMesh.vertices;
            int[] tris = mf.sharedMesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 lA = go.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[tris[i]]));
                Vector3 lB = go.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[tris[i + 1]]));
                Vector3 lC = go.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[tris[i + 2]]));
                tri2D.Add(new Vector2(lA.x, lA.z));
                tri2D.Add(new Vector2(lB.x, lB.z));
                tri2D.Add(new Vector2(lC.x, lC.z));
            }
        }
        // 메시 정보를 못 얻으면(Read/Write Enabled 꺼진 Sketchfab 임포트 등) "네모"로 단정할
        // 근거가 없으므로 안전하게 "네모 아님(자동 문 처리 제외)"으로 처리. 반대로 하면 실제로는
        // ㄷ자/L자인 건물이 검사를 못 받고 그냥 통과해버리는 사고가 남.
        if (tri2D.Count == 0) return 0f;

        int hit = 0, total = 0;
        for (int ix = 0; ix < FillGridRes; ix++)
        {
            for (int iz = 0; iz < FillGridRes; iz++)
            {
                float px = Mathf.Lerp(lb.min.x, lb.max.x, (ix + 0.5f) / FillGridRes);
                float pz = Mathf.Lerp(lb.min.z, lb.max.z, (iz + 0.5f) / FillGridRes);
                total++;
                Vector2 p = new Vector2(px, pz);
                for (int i = 0; i < tri2D.Count; i += 3)
                {
                    if (PointInTriangle2D(p, tri2D[i], tri2D[i + 1], tri2D[i + 2])) { hit++; break; }
                }
            }
        }
        return total > 0 ? (float)hit / total : 1f;
    }

    static bool PointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross2D(p, a, b);
        float d2 = Cross2D(p, b, c);
        float d3 = Cross2D(p, c, a);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    static float Cross2D(Vector2 p1, Vector2 p2, Vector2 p3)
        => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

    // 임포트 세팅에서 Read/Write Enabled를 강제로 켬 (Kenney FBX 등 기본적으로 꺼져있어서
    // mesh.vertices/triangles를 못 읽던 문제의 진짜 원인). 이미 켜져 있으면 아무것도 안 함.
    static void EnsureReadable(Mesh mesh)
    {
        if (mesh == null || mesh.isReadable) return;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path)) return;
        if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
    }

    // 자동 문 처리 대상에서 제외된(네모가 아닌) 건물을 원래대로 되돌림: 문 없이 실제
    // 메시 모양 그대로 따라가는 MeshCollider(막힘, non-trigger)만 붙여서 "문 앞까지는
    // 가지지만 못 들어가는" 정상적인 막힌 건물로 만듦. BuildingInterior/트리거는 안 붙임.
    static void RestoreSolidCollider(GameObject go)
    {
        BuildingInterior existingInterior = go.GetComponent<BuildingInterior>();
        if (existingInterior != null) Object.DestroyImmediate(existingInterior);

        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(col);

        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.isTrigger = false;
        }
    }

    static void AddChildren(string rootName, List<Transform> into)
    {
        GameObject root = GameObject.Find(rootName);
        if (root == null) return;
        foreach (Transform child in root.transform) into.Add(child);
    }

    // 자동으로(도로 방향 추정) 뚫은 문이 실제 모델 문 위치랑 다른 건물을 수동으로 고칠 때 씀.
    // _TempFixDoorSide.cs에서 호출. 트리거/BuildingInterior는 그대로 두고 막힌 벽(문 없는
    // 4면 콜라이더)만 지우고 지정된 면에 문을 다시 뚫음.
    internal static bool RebuildDoorForSelected(GameObject go, Vector3 doorSideLocal)
    {
        Bounds? localBounds = ComputeLocalBounds(go);
        if (localBounds == null) return false;
        Bounds lb = localBounds.Value;

        foreach (var col in go.GetComponents<BoxCollider>())
            if (!col.isTrigger) Object.DestroyImmediate(col);

        return BuildPerimeterWithDoor(go, lb, go.transform.lossyScale, doorSideLocal);
    }

    // ── 도로 쪽 벽 한 면에만 문 크기 구멍, 나머지 3면은 완전히 막힘 ──
    // BoxCollider의 size/center는 로컬 좌표라 실행 시 오브젝트의 scale이 곱해져서 월드 크기가 됨.
    // WallThickness/DoorWidth는 "실제 세계 미터" 기준 상수이므로, scale로 나눠서 로컬 값으로
    // 변환해야 스케일이 걸린 건물에서도 실제로 0.3m/2.2m가 됨 (안 나누면 스케일만큼 부풀어서
    // 건물 근처 전체가 막히거나, 반대로 문이 안 뚫린 것처럼 작아짐).
    static bool BuildPerimeterWithDoor(GameObject building, Bounds lb, Vector3 scale, Vector3 doorSide)
    {
        float minX = lb.min.x, maxX = lb.max.x, minZ = lb.min.z, maxZ = lb.max.z;
        float wallY = lb.center.y;
        bool doorPlaced = true;

        float sx = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        float sz = Mathf.Max(Mathf.Abs(scale.z), 0.0001f);
        float thicknessOnZ = WallThickness / sz; // 앞/뒤 벽 두께 (Z축 방향)
        float thicknessOnX = WallThickness / sx; // 좌/우 벽 두께 (X축 방향)
        float doorOnX = DoorWidth / sx;          // 앞/뒤 벽에 뚫는 문 폭 (X축 방향으로 뚫림)
        float doorOnZ = DoorWidth / sz;          // 좌/우 벽에 뚫는 문 폭 (Z축 방향으로 뚫림)

        doorPlaced &= AddWallOrDoor(building, doorSide == Vector3.forward,
            new Vector3((minX + maxX) / 2f, wallY, maxZ), new Vector3(maxX - minX, lb.size.y, thicknessOnZ), true, doorOnX);
        doorPlaced &= AddWallOrDoor(building, doorSide == Vector3.back,
            new Vector3((minX + maxX) / 2f, wallY, minZ), new Vector3(maxX - minX, lb.size.y, thicknessOnZ), true, doorOnX);
        doorPlaced &= AddWallOrDoor(building, doorSide == Vector3.right,
            new Vector3(maxX, wallY, (minZ + maxZ) / 2f), new Vector3(thicknessOnX, lb.size.y, maxZ - minZ), false, doorOnZ);
        doorPlaced &= AddWallOrDoor(building, doorSide == Vector3.left,
            new Vector3(minX, wallY, (minZ + maxZ) / 2f), new Vector3(thicknessOnX, lb.size.y, maxZ - minZ), false, doorOnZ);

        return doorPlaced;
    }

    // isDoorSide가 아니면 완전히 막힌 벽. isDoorSide면 가운데 doorWidthLocal만큼 비우고 두 조각으로 배치.
    // 반환값 false면 공간 부족으로 막힌 벽 그대로 씀(경고 대상).
    static bool AddWallOrDoor(GameObject building, bool isDoorSide, Vector3 localCenter, Vector3 size, bool alongX, float doorWidthLocal)
    {
        if (!isDoorSide)
        {
            AddBoxCollider(building, localCenter, size);
            return true;
        }

        float fullLength = alongX ? size.x : size.z;
        float segLength = (fullLength - doorWidthLocal) / 2f;
        if (segLength <= 0.05f)
        {
            AddBoxCollider(building, localCenter, size);
            return false;
        }

        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
        Vector3 seg1Center = localCenter - axis * (fullLength / 2f - segLength / 2f);
        Vector3 seg2Center = localCenter + axis * (fullLength / 2f - segLength / 2f);
        Vector3 segSize = alongX ? new Vector3(segLength, size.y, size.z) : new Vector3(size.x, size.y, segLength);

        AddBoxCollider(building, seg1Center, segSize);
        AddBoxCollider(building, seg2Center, segSize);
        return true;
    }

    // ── 로컬 방향을 로컬 축(±X/±Z) 중 가장 가까운 쪽으로 스냅 ──
    static Vector3 PickAxisSide(Vector3 localDir)
    {
        Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        Vector3 best = sides[0];
        float bestDot = -2f;
        foreach (var s in sides)
        {
            float d = Vector3.Dot(s, localDir);
            if (d > bestDot) { bestDot = d; best = s; }
        }
        return best;
    }

    // ── 가장 가까운 도로 방향 (월드 공간) ──
    static Vector3 FindNearestRoadDirection(Vector3 pos)
    {
        GameObject roadsRoot = GameObject.Find("Roads");
        Transform nearest = null;
        float bestSqr = float.MaxValue;

        IEnumerable<Transform> roadTransforms = roadsRoot != null
            ? roadsRoot.transform.Cast<Transform>()
            : EditorSceneManager.GetActiveScene().GetRootGameObjects()
                .Where(g => g.name.ToLowerInvariant().StartsWith("road-")).Select(g => g.transform);

        foreach (Transform t in roadTransforms)
        {
            float d = (t.position - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; nearest = t; }
        }

        if (nearest == null) return Vector3.forward;
        Vector3 dir = nearest.position - pos;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
    }

    static void AddBoxCollider(GameObject go, Vector3 center, Vector3 size)
    {
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.isTrigger = false;
    }

    // 풋프린트(면적) 판단용 월드 공간 바운드 — 스케일이 있는 오브젝트를 로컬 바운드로
    // 판단하면 면적이 축소되어 잘못 걸러지므로, 크기 필터링은 항상 이걸로 함
    static Bounds? GetWorldBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // 오브젝트의 실제 회전을 고려해 로컬 좌표계 기준 바운드를 계산
    static Bounds? ComputeLocalBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        bool started = false;
        Bounds localBounds = default;
        foreach (var r in renderers)
        {
            Bounds wb = r.bounds;
            Vector3 c = wb.center;
            Vector3 e = wb.extents;
            Vector3[] corners =
            {
                c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, e.y, -e.z),  c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),  c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(-e.x, e.y, e.z),   c + new Vector3(e.x, e.y, e.z),
            };
            foreach (var corner in corners)
            {
                Vector3 local = go.transform.InverseTransformPoint(corner);
                if (!started) { localBounds = new Bounds(local, Vector3.zero); started = true; }
                else localBounds.Encapsulate(local);
            }
        }
        return started ? localBounds : (Bounds?)null;
    }
}

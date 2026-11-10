using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 건물 벽 통과 점검 / 수정 (Tools → Map → Building Colliders).
///
///  1) Check (에디터): 씬 전체를 돌며
///     - 들어갈 수 있는 건물(BuildingInterior): 벽 메시에 BuildingWallCollider(실행 시 벽 MeshCollider 생성)나 Collider가 있는지
///     - 그 밖의 건물·구조물(학교, 물탱크 등 크기 4m 이상 모델): 단단한 Collider가 하나라도 있는지
///     → 없는 것들을 콘솔에 목록 + 고치는 방법 출력
///  2) Passthrough Test (Play 중): 실제 물리로 확인. 건물 대각선 바깥에서 플레이어 몸 크기 구(반지름 0.45, 발밑 0.4m)를
///     36방향 × 평행선 3줄로 건물 안쪽을 향해 쏴서, 벽에 안 막히고 "벽으로 둘러싸인 실내"까지 들어간 경로 중
///     문 구멍(BuildingWallCollider.doorSlot)을 지나지 않은 것을 "통과 지점"으로 보고
///     (BuildingWallCollider 벽은 Play 때만 생기므로 실제 판정은 이걸로)
///  3) Fix (에디터): 1)에서 콜라이더가 하나도 없던 구조물의 메시마다 MeshCollider 추가 + 씬 저장
///     → 이후 Tools → Map → Rebake NavMesh With Building Walls 로 좀비 길(NavMesh)도 다시 구움 (Physics Colliders 기준으로 구움)
/// </summary>
[InitializeOnLoad]
public static class BuildingColliderChecker
{
    // Play 중에는 glTF/FBX 인스턴스의 프리팹(에셋) 연결 정보가 사라져서 "들어갈 수 없는 건물"(학교·물탱크)을 못 알아봄
    // → Play 들어가기 직전(에디터)에 그 목록(계층 경로)을 SessionState에 적어 두고 Passthrough Test가 씀
    const string StructureListKey = "BuildingColliderChecker.NonEnterable";

    static BuildingColliderChecker()
    {
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) SaveNonEnterableList(); };
    }

    static void SaveNonEnterableList()
    {
        var paths = new List<string>();
        foreach (var root in StructureRoots())
            if (IsBuildingAsset(root) && root.GetComponentInParent<BuildingInterior>() == null && root.GetComponentInChildren<BuildingInterior>(true) == null)
                paths.Add(Path(root));
        SessionState.SetString(StructureListKey, string.Join("\n", paths));
    }

    const float MinStructureSize = 4f;   // 이보다 작은 모델은 건물/구조물로 안 봄 (소품은 Add Environment Colliders 담당)
    const float MinMeshFootprint = 0.5f; // 이보다 작은 메시는 콜라이더 생략 (장식)
    // 플레이어 몸 = 발밑 구(반지름 0.5, 중심 0.4m) + Locomotion 스텝업(발이 막히고 1.2·1.8m가 비어 있으면 최대 1.2m까지 올라감)
    const float ProbeRadius = 0.45f;     // 플레이어 구(0.5)보다 살짝 작게 = 실제로 몸이 빠져나갈 수 있는 틈만
    const float WalkHeight = 0.4f;       // 걸을 때 몸 중심
    // 건물이 아닌 큰 것 (지형·바닥·배경숲·물·도로·구역 표시 등) - 이름에 들어 있으면 제외
    static readonly string[] SkipRootNames = { "ground", "terrain", "landskape", "road", "zone", "forest", "waterfall", "sky", "player", "canvas", "navmesh", "camera", "light", "decal", "debris", "fog", "rain", "mapguide" };
    // 건물이 아닌 큰 모델 (에셋 경로로 판단 - 씬 이름이 "scene"처럼 의미 없는 경우): 공원(나무·벤치·잔디 바닥)은 메시 콜라이더를 씌우면 잔디에 걸려서 제외
    static readonly string[] SkipAssetPaths = { "city_park", "forest", "landscape" };

    // ── 1) 점검 ───────────────────────────────────
    [MenuItem("Tools/Map/Building Colliders/1. Check (Editor)")]
    public static void Check()
    {
        var sb = new StringBuilder();
        var noCol = FindStructuresWithoutColliders(sb);
        int wallIssues = CheckEnterableBuildings(sb);
        sb.Insert(0, $"[BuildingColliderChecker] 들어갈 수 있는 건물 문제 {wallIssues}곳 / 콜라이더가 하나도 없는 구조물 {noCol.Count}개\n");
        if (noCol.Count > 0 || wallIssues > 0)
            sb.AppendLine("\n[수정 방법]\n - 콜라이더 없는 구조물: Tools → Map → Building Colliders → 3. Fix 실행 (메시마다 MeshCollider, IsTrigger 끔) → 이어서 Tools → Map → Rebake NavMesh With Building Walls" +
                          "\n - 수동으로 할 때: 벽 메시 오브젝트에 Mesh Collider 추가(Convex 끔, Is Trigger 끔). 들어갈 수 있는 건물은 Mesh Collider 대신 BuildingWallCollider를 붙이고 Tools → Map → Fix Building Wall Colliders로 문 위치 계산" +
                          "\n - 실제로 막히는지는 Play 중 Tools → Map → Building Colliders → 2. Passthrough Test 로 확인");
        else sb.AppendLine("문제 없음. 실제 통과 여부는 Play 중 2. Passthrough Test로 확인");
        Debug.Log(sb.ToString());
    }

    /// <summary>들어갈 수 있는 건물: 큰 메시인데 BuildingWallCollider도 Collider도 없는 것</summary>
    static int CheckEnterableBuildings(StringBuilder sb)
    {
        int issues = 0;
        foreach (var b in Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            Transform root = BuildingRoot(b);
            int walls = root.GetComponentsInChildren<BuildingWallCollider>(true).Length;
            if (walls == 0) { sb.AppendLine($" - [실내 건물] {Path(root)}: BuildingWallCollider 없음 → 벽이 전부 통과됨"); issues++; continue; }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !IsSolidCandidate(mf)) continue;
                if (mf.GetComponent<Collider>() != null || mf.GetComponent<BuildingWallCollider>() != null) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || r.bounds.size.y < 1.2f || Mathf.Max(r.bounds.size.x, r.bounds.size.z) < 1.5f) continue; // 사람 키보다 낮거나 작은 장식
                sb.AppendLine($" - [실내 건물] {Path(mf.transform)}: 벽 크기 메시({r.bounds.size.x:F1}×{r.bounds.size.y:F1}×{r.bounds.size.z:F1}m)에 콜라이더 없음 (참고: 벽 MeshCollider와 겹치면 문제 아님 → Passthrough Test로 확인)");
                issues++;
            }
        }
        return issues;
    }

    static Transform BuildingRoot(BuildingInterior b) => b.transform;

    /// <summary>크기 4m 이상 모델(프리팹/씬 루트) 중 단단한 Collider·BuildingWallCollider가 하나도 없는 것</summary>
    static List<Transform> FindStructuresWithoutColliders(StringBuilder sb)
    {
        var result = new List<Transform>();
        foreach (var root in StructureRoots())
        {
            if (root.GetComponentInChildren<BuildingWallCollider>(true) != null) continue;
            bool solid = false;
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger && c.enabled) { solid = true; break; }
            if (solid) continue;
            var bb = RendererBounds(root);
            sb.AppendLine($" - [구조물] {Path(root)}: 콜라이더 0개 (크기 {bb.size.x:F0}×{bb.size.y:F0}×{bb.size.z:F0}m, 위치 {bb.center:F0}, 메시 {root.GetComponentsInChildren<MeshFilter>(true).Length}개) → 플레이어·좀비·총알이 전부 통과");
            result.Add(root);
        }
        return result;
    }

    /// <summary>건물/구조물 후보: 씬 루트 + 프리팹 인스턴스 루트 중 렌더러 범위가 4m 이상인 것 (지형·바닥·배경 등 제외)</summary>
    static List<Transform> StructureRoots()
    {
        var list = new List<Transform>();
        var seen = new HashSet<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            bool isRoot = t.parent == null || PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject;
            if (!isRoot || seen.Contains(t)) continue;
            if (HasSkippedAncestor(t)) continue;
            // 더 큰 프리팹 안에 들어 있는 프리팹은 바깥 것으로 판단
            var outer = t.parent != null ? PrefabUtility.GetNearestPrefabInstanceRoot(t.parent.gameObject) : null;
            if (outer != null) continue;
            if (t.GetComponentInChildren<Renderer>(true) == null) continue;
            if (t.childCount > 0 && t.parent == null && t.GetComponent<Renderer>() == null && PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) != t.gameObject && IsContainer(t)) continue;
            var bb = RendererBounds(t);
            if (Mathf.Max(bb.size.x, bb.size.z) < MinStructureSize || bb.size.y < 2.5f) continue;
            seen.Add(t);
            list.Add(t);
        }
        return list;
    }

    // "Buildings"·"Props"처럼 여러 모델을 담기만 하는 빈 부모는 그 자체를 하나의 구조물로 보지 않음 (자식 프리팹이 각각 후보)
    static bool IsContainer(Transform t)
    {
        foreach (Transform c in t) if (PrefabUtility.GetNearestPrefabInstanceRoot(c.gameObject) == c.gameObject) return true;
        return false;
    }

    static bool HasSkippedAncestor(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            string n = p.name.ToLowerInvariant();
            foreach (var s in SkipRootNames) if (n.Contains(s)) return true;
            if (p.GetComponent<ZombieController>() != null || p.GetComponent<Canvas>() != null) return true;
            string asset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject);
            if (!string.IsNullOrEmpty(asset)) foreach (var s in SkipAssetPaths) if (asset.ToLowerInvariant().Contains(s)) return true;
        }
        return false;
    }

    static bool IsSolidCandidate(MeshFilter mf)
    {
        string n = mf.name.ToLowerInvariant();
        return !n.Contains("shadow") && !n.Contains("decal") && !n.Contains("glass_fx");
    }

    static Bounds RendererBounds(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(root.position, Vector3.zero);
        var bb = rs[0].bounds;
        foreach (var r in rs) if (!(r is ParticleSystemRenderer)) bb.Encapsulate(r.bounds);
        return bb;
    }

    static string Path(Transform t)
    {
        string p = t.name;
        for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
        return p;
    }

    // ── 2) 실제 통과 테스트 (Play) ─────────────────────
    [MenuItem("Tools/Map/Building Colliders/2. Passthrough Test (Play)")]
    public static void PassthroughTestMenu()
    {
        if (!EditorApplication.isPlaying) { Debug.Log("[BuildingColliderChecker] Passthrough Test는 Play 중에만 (벽 콜라이더가 실행 시 생성됨)"); return; }
        Debug.Log(PassthroughTest(out _));
    }

    /// <summary>모든 건물·구조물에 구를 쏴서 문이 아닌 통과 지점을 찾음. leaks = 통과 지점이 있는 건물 수</summary>
    public static string PassthroughTest(out int leaks)
    {
        var sb = new StringBuilder();
        leaks = 0;
        int buildings = 0, probes = 0, occluded = 0;
        var targets = new List<(Transform root, List<BuildingWallCollider> doors)>();
        var done = new HashSet<Transform>();
        foreach (var b in Object.FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            var root = BuildingRoot(b);
            if (!done.Add(root)) continue;
            // 문 = 벽 콜라이더의 문 구멍 영역 (BuildingWallCollider.doorSlotCenter/Size, 로컬 상자)
            var walls = root.GetComponentsInChildren<BuildingWallCollider>(true);
            if (walls.Length == 0 && root.parent != null) walls = root.parent.GetComponentsInChildren<BuildingWallCollider>(true);
            var doors = new List<BuildingWallCollider>();
            foreach (var w in walls) if (w.hasDoor) doors.Add(w);
            targets.Add((root, doors));
        }
        // 들어갈 수 없는 건물 (학교·물탱크 등): Play 직전에 적어 둔 목록 (Play 중엔 에셋 경로를 몰라서 건물인지 판단 불가)
        foreach (var path in SessionState.GetString(StructureListKey, "").Split('\n'))
        {
            if (string.IsNullOrEmpty(path)) continue;
            var go = GameObject.Find("/" + path);
            if (go != null && done.Add(go.transform)) targets.Add((go.transform, new List<BuildingWallCollider>()));
        }

        foreach (var (root, doors) in targets)
        {
            buildings++;
            // 단단한 콜라이더가 하나도 없으면 "실내"를 잴 벽 자체가 없음 → 전부 통과로 바로 보고
            bool solid = false;
            foreach (var col in root.GetComponentsInChildren<Collider>()) if (!col.isTrigger && col.enabled) { solid = true; break; }
            if (!solid)
            {
                leaks++;
                sb.AppendLine($" - {Path(root)} (위치 {RendererBounds(root).center:F0}): 콜라이더 0개 → 어디로든 통과 (3. Fix로 MeshCollider 추가)");
                continue;
            }
            var bb = RendererBounds(root);
            Vector3 c = bb.center;
            // 시작점은 건물 대각선 바깥 (반폭만 쓰면 튀어나온 차양·계단과 시작 구가 겹쳐서, SphereCast가 그 벽을 무시함 → 가짜 통과)
            float reach = new Vector2(bb.extents.x, bb.extents.z).magnitude + 1.5f;
            float side = Mathf.Min(bb.extents.x, bb.extents.z) * 0.45f;
            var walk = new List<string>();
            for (int k = 0; k < 36; k++)
            {
                float ang = k * 10f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                for (int o = -1; o <= 1; o++) // 가운데 + 좌우 평행선 (가운데만 노리면 벽 사이 틈을 놓침)
                {
                    Vector3 startXZ = c + dir * reach + perp * (side * o);
                    Vector3 endXZ = c + perp * (side * o);
                    // 시작점 바닥 높이 (위에서 아래로)
                    if (!Physics.Raycast(new Vector3(startXZ.x, bb.max.y + 30f, startXZ.z), Vector3.down, out RaycastHit g, bb.size.y + 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (g.collider.transform.IsChildOf(root)) continue; // 시작점이 건물 위(지붕·계단 등)
                    // 시작점이 옆 건물 지붕이면 판단 불가 (실제 바닥은 건물 밑면 근처)
                    if (g.point.y > bb.min.y + 3f || g.collider.GetComponentInParent<BuildingInterior>() != null || g.collider.GetComponentInParent<BuildingWallCollider>() != null) continue;
                    probes++;
                    Vector3 s0 = new Vector3(startXZ.x, g.point.y, startXZ.z), e0 = new Vector3(endXZ.x, g.point.y, endXZ.z);
                    int r = Probe(root, s0, e0, Mathf.Max(bb.extents.x, bb.extents.z) * 2.2f);
                    if (r == 0) continue;
                    if (r < 0) { occluded++; continue; }
                    // 벽에 안 막히고 실내까지 들어감: 문 구멍 상자를 지나간 경로면 정상
                    bool viaDoor = false;
                    foreach (var d in doors) if (CrossesDoorSlot(d, s0 + Vector3.up * WalkHeight, e0 + Vector3.up * WalkHeight)) { viaDoor = true; break; }
                    if (viaDoor) continue;
                    walk.Add($"{k * 10}°{(o < 0 ? "L" : o > 0 ? "R" : "")}");
                }
            }
            if (walk.Count == 0) continue;
            leaks++;
            sb.AppendLine($" - {Path(root)} (위치 {c:F0}, 문 {doors.Count}개): 벽을 지나 실내까지 들어간 경로 {walk.Count}줄 {Sample(walk)}");
        }
        sb.Insert(0, $"[BuildingColliderChecker] 통과 테스트: 건물 {buildings}개, 경로 {probes}줄 (다른 물체에 가려 판단 못 한 줄 {occluded}개) → 문이 아닌 곳이 뚫린 건물 {leaks}개\n");
        return sb.ToString();
    }

    /// <summary>선분이 벽 콜라이더의 문 구멍 상자(로컬 AABB, 플레이어 몸 두께만큼 넓힘)를 지나는지</summary>
    static bool CrossesDoorSlot(BuildingWallCollider w, Vector3 a, Vector3 b)
    {
        var t = w.transform;
        Vector3 la = t.InverseTransformPoint(a), lb = t.InverseTransformPoint(b);
        Vector3 s = t.lossyScale;
        Vector3 margin = new Vector3(ProbeRadius / Mathf.Max(0.001f, Mathf.Abs(s.x)), ProbeRadius / Mathf.Max(0.001f, Mathf.Abs(s.y)), ProbeRadius / Mathf.Max(0.001f, Mathf.Abs(s.z)));
        var box = new Bounds(w.doorSlotCenter, w.doorSlotSize + margin * 2f);
        Vector3 d = lb - la;
        var ray = new Ray(la, d.normalized);
        return box.Contains(la) || box.IntersectRay(ray, out float dist) && dist <= d.magnitude;
    }

    static string Sample(List<string> l) => l.Count == 0 ? "" : "[" + string.Join(", ", l.GetRange(0, Mathf.Min(6, l.Count))) + (l.Count > 6 ? " …" : "") + "]";

    /// <summary>
    /// 바닥 높이의 s→e 경로: 0 = 이 건물 벽에 막힘 / 1 = 걸어서 실내까지 통과 / -1 = 다른 물체에 먼저 막혀 판단 불가
    /// 막히지 않고 도착한 곳이 "벽으로 둘러싸인 실내"일 때만 통과로 봄 (ㄱ자 건물의 지붕 있는 현관·필로티처럼 바깥 공간이면 정상)
    /// (스텝업으로 창턱을 넘는 경우도 따로 쟀었지만, 실제 플레이어로 걸어 보니 5/5 다 막혀서 뺐음 - 스텝업은 올라설 윗면이 있어야 함)
    /// </summary>
    static int Probe(Transform root, Vector3 s, Vector3 e, float encloseReach)
    {
        Vector3 v = e - s;
        Vector3 dir = v.normalized;
        // 시작 구가 이미 무언가(바닥 제외)와 겹치면 SphereCast가 그걸 무시하므로 판단 불가로 처리
        foreach (var ov in Physics.OverlapSphere(s + Vector3.up * WalkHeight, ProbeRadius, ~0, QueryTriggerInteraction.Ignore))
            if (ov.transform.IsChildOf(root) || ov.name != "Ground" && ov.bounds.max.y > s.y + 0.15f) return -1;
        if (!Physics.SphereCast(s + Vector3.up * WalkHeight, ProbeRadius, dir, out RaycastHit hit, v.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            // 발밑 구는 빠져도 1.2m 높이가 이 건물에 막히면 실제 플레이어는 못 지나감 (학교 아래가 빈 구조물에서 실측)
            if (Physics.Raycast(s + Vector3.up * 1.2f, dir, out RaycastHit head, v.magnitude, ~0, QueryTriggerInteraction.Ignore) && head.collider.transform.IsChildOf(root)) return 0;
            return Enclosed(root, e + Vector3.up * 1f, encloseReach) ? 1 : 0;
        }
        return hit.collider.transform.IsChildOf(root) ? 0 : -1;
    }

    /// <summary>p에서 수평 8방향 중 7방향 이상이 이 건물 벽에 막히면 실내</summary>
    static bool Enclosed(Transform root, Vector3 p, float reach)
    {
        // 한쪽 면만 있는 모델(학교 등)은 안에서 보면 벽 뒷면이라 레이가 안 맞음 → 이 판정만 뒷면도 맞게 (위치가 실내인지 보는 용도)
        bool back = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int n = 0;
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            if (Physics.Raycast(p, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), out RaycastHit h, reach, ~0, QueryTriggerInteraction.Ignore)
                && h.collider.transform.IsChildOf(root)) n++;
        }
        Physics.queriesHitBackfaces = back;
        return n >= 7;
    }

    /// <summary>건물 에셋인지 (Kenney building-*, SketchfabAssets/Buildings, 이름에 building/house/school/church/hospital/police/water tank)</summary>
    static bool IsBuildingAsset(Transform root)
    {
        string a = (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) + " " + root.name).ToLowerInvariant();
        if (a.Contains("electricity") || a.Contains("pole")) return false;
        return a.Contains("/buildings/") || a.Contains("building") || a.Contains("house") || a.Contains("school") || a.Contains("church")
            || a.Contains("hospital") || a.Contains("police") || a.Contains("water tank");
    }

    // ── 3) 수정 ───────────────────────────────────
    [MenuItem("Tools/Map/Building Colliders/3. Fix Structures Without Colliders")]
    public static void Fix()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[BuildingColliderChecker] Play 중에는 수정할 수 없어요"); return; }
        var sb = new StringBuilder();
        var roots = FindStructuresWithoutColliders(sb);
        int added = 0, skipped = 0, split = 0;
        var fixedRoots = new List<Transform>(roots);
        foreach (var root in roots)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null || !IsSolidCandidate(mf)) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || Mathf.Max(r.bounds.size.x, r.bounds.size.z) < MinMeshFootprint && r.bounds.size.y < MinMeshFootprint) { skipped++; continue; }
                // AddComponent는 MeshFilter 메시를 자동으로 넣고 바로 구움 → 큰 삼각형이면 경고가 나므로, 붙이는 순간만 비움
                Mesh render = mf.sharedMesh;
                mf.sharedMesh = null;
                var mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                mf.sharedMesh = render;
                mc.sharedMesh = render;
                mc.convex = false;   // 오목한 건물 모양 그대로 (Convex는 감싸는 덩어리가 됨)
                mc.isTrigger = false;
                added++;
            }
            EditorUtility.SetDirty(root.gameObject);
        }
        // 크게 축소된 모델(학교 0.04배)은 메시 로컬 좌표가 커서 PhysX가 "triangles > 500 units" 경고를 냄
        // → 그 메시만 큰 삼각형을 쪼갠 충돌용 사본(Assets/Prefabs/Collision)으로 교체 (모양은 같음)
        // 이미 콜라이더를 붙여 둔 구조물도 다시 실행하면 처리되도록, 콜라이더 없던 것 + 들어갈 수 없는 건물 전부 검사
        foreach (var root in StructureRoots())
            if (IsBuildingAsset(root) && root.GetComponentInChildren<BuildingInterior>(true) == null && !fixedRoots.Contains(root)) fixedRoots.Add(root);
        foreach (var root in fixedRoots)
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                if (mc.sharedMesh != null && mc.sharedMesh.isReadable && MaxEdge(mc.sharedMesh) > 490f) { mc.sharedMesh = SplitCopy(mc.sharedMesh, root.name); split++; }
        var scene = EditorSceneManager.GetActiveScene();
        if (added > 0 || split > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets(); }
        Debug.Log($"[BuildingColliderChecker] 콜라이더 없던 구조물 {roots.Count}개에 MeshCollider {added}개 추가 (작은 장식 {skipped}개 생략), 큰 삼각형 쪼갠 충돌 메시 {split}개, 씬 저장\n{sb}" +
                  "\n→ 좀비 길도 막히게: Tools → Map → Rebake NavMesh With Building Walls");
    }

    static float MaxEdge(Mesh m)
    {
        var v = m.vertices; var t = m.triangles; float mx = 0f;
        for (int i = 0; i < t.Length; i += 3)
            mx = Mathf.Max(mx, Mathf.Max((v[t[i]] - v[t[i + 1]]).sqrMagnitude, Mathf.Max((v[t[i + 1]] - v[t[i + 2]]).sqrMagnitude, (v[t[i + 2]] - v[t[i]]).sqrMagnitude)));
        return Mathf.Sqrt(mx);
    }

    /// <summary>큰 삼각형을 400 단위 이하로 쪼갠 충돌용 메시 사본을 에셋으로 저장 (BuildingWallCollider와 같은 방식). 이미 있으면 덮어씀</summary>
    static Mesh SplitCopy(Mesh src, string owner)
    {
        const string dir = "Assets/Prefabs/Collision";
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Collision");
        var verts = new List<Vector3>(src.vertices);
        var tris = new List<int>(src.triangles);
        BuildingWallCollider.SplitLargeTriangles(verts, tris);
        var m = new Mesh { name = src.name + "_Collision" };
        if (verts.Count > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        string safe = (owner + "_" + src.name).Replace(' ', '_').Replace('(', '_').Replace(')', '_').Replace('/', '_');
        string path = $"{dir}/{safe}.asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old != null) { EditorUtility.CopySerialized(m, old); Object.DestroyImmediate(m); return old; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}

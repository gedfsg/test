using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// "안 지나가지는 곳" 점검 (2026-11-16).
/// 메뉴:
///  - Tools/Map/Check Invisible Colliders : 열린 씬 전체를 돌며 눈에 안 보이는 콜라이더(같은 오브젝트·자식에 렌더러 없음)를
///    콘솔에 위치·경로·크기로 출력하고, Scene 뷰에 빨간 와이어 상자로 표시 (의도된 투명 벽은 초록)
///  - Tools/Map/Toggle Invisible Collider Gizmos : Scene 뷰 표시 켜고 끄기
///  - BuildingInterior가 건물이 아닌 곳(지형·바닥·구역 판)에 붙어 있는지도 같이 출력
///
/// 의도된 것(초록, 고치지 않음): InvisibleWall(다리 안전 벽), FenceBlockers(농장 울타리), 트리거, 지형(Terrain), 바닥(Ground),
///  물(WaterZone), 런타임 생성 벽(BuildingWallCollider - 에디터에선 없음)
/// </summary>
public static class MapColliderChecker
{
    public struct Entry
    {
        public Collider col;
        public bool intended;
        public string reason;
        public Bounds bounds;
    }

    static readonly List<Entry> found = new List<Entry>();
    static bool gizmos;

    [MenuItem("Tools/Map/Check Invisible Colliders")]
    public static void Check()
    {
        found.Clear();
        var sb = new StringBuilder();
        int suspicious = 0, intended = 0;
        foreach (var col in AllInScenes<Collider>())
        {
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
            if (HasVisibleMesh(col)) continue;
            string why = IntendedReason(col);
            var e = new Entry { col = col, intended = why != null, reason = why ?? "보이는 메시 없음", bounds = col.bounds };
            found.Add(e);
            if (e.intended) intended++;
            else
            {
                suspicious++;
                sb.AppendLine($"  ✗ {Path(col.transform)}  [{col.GetType().Name}] 중심 {e.bounds.center:F1} 크기 {e.bounds.size:F1}");
            }
        }

        // BuildingInterior가 야외 지형/바닥에 붙었는지
        var bi = new StringBuilder();
        foreach (var b in AllInScenes<BuildingInterior>())
        {
            string n = Path(b.transform).ToLowerInvariant();
            bool hasBuildingMesh = b.GetComponentsInChildren<MeshRenderer>(true).Length > 0;
            if (b.GetComponent<Terrain>() != null || n.Contains("terrain") || n.Contains("ground") || n.Contains("zone_") || !hasBuildingMesh)
                bi.AppendLine($"  ✗ BuildingInterior 의심: {Path(b.transform)} (메시 {(hasBuildingMesh ? "있음" : "없음")})");
        }

        Debug.Log($"[MapColliderChecker] 안 보이는 콜라이더: 의심 {suspicious}개 / 의도된 것 {intended}개\n{sb}" +
                  (bi.Length > 0 ? "\n[BuildingInterior 점검]\n" + bi : "\n[BuildingInterior 점검] 이상 없음"));
        SetGizmos(true);
    }

    /// <summary>점검 결과 (다른 도구·테스트용)</summary>
    public static IReadOnlyList<Entry> Results => found;

    [MenuItem("Tools/Map/Toggle Invisible Collider Gizmos")]
    public static void Toggle() => SetGizmos(!gizmos);

    static void SetGizmos(bool on)
    {
        SceneView.duringSceneGui -= Draw;
        gizmos = on;
        if (on) SceneView.duringSceneGui += Draw;
        SceneView.RepaintAll();
    }

    static void Draw(SceneView sv)
    {
        Handles.color = new Color(1f, 0f, 0.6f, 0.9f);
        foreach (var bc in blockCells) Handles.DrawWireCube(bc.pos + Vector3.up * 1f, new Vector3(0.8f, 2f, 0.8f));
        foreach (var e in found)
        {
            if (e.col == null) continue;
            Handles.color = e.intended ? new Color(0.2f, 1f, 0.3f, 0.5f) : new Color(1f, 0.15f, 0.1f, 0.95f);
            Handles.DrawWireCube(e.bounds.center, e.bounds.size);
            if (!e.intended) Handles.Label(e.bounds.center + Vector3.up * e.bounds.extents.y, e.col.name);
        }
    }

    // 같은 오브젝트나 자식에 켜진 렌더러가 있으면 "보이는" 콜라이더
    static bool HasVisibleMesh(Collider c)
    {
        if (c is TerrainCollider) return true;
        foreach (var r in c.GetComponentsInChildren<Renderer>(false))
            if (r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer)) return true;
        // 메시 콜라이더인데 같은 메시를 그리는 렌더러가 부모에 있는 경우 (모델 루트에 콜라이더만 따로 붙인 구조)
        if (c is MeshCollider mc && mc.sharedMesh != null && c.transform.parent != null)
            foreach (var mf in c.transform.parent.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh == mc.sharedMesh && mf.GetComponent<Renderer>() != null && mf.GetComponent<Renderer>().enabled) return true;
        return false;
    }

    static string IntendedReason(Collider c)
    {
        if (c.isTrigger) return "트리거";
        if (c.GetComponent<InvisibleWall>() != null) return "다리 안전 벽(InvisibleWall)";
        string p = Path(c.transform);
        if (p.Contains("FenceBlocker")) return "농장 울타리 막이";
        if (p.Contains("BridgeSafetyWalls")) return "다리 안전 벽";
        if (c.name == "Ground" || p.StartsWith("Ground")) return "바닥";
        if (c.name.StartsWith("Water")) return "물"; // WaterZone은 정적 클래스라 이름으로
        if (c.GetComponentInParent<Rigidbody>() != null && c.GetComponentInParent<Animator>() != null) return "캐릭터";
        return null;
    }

    static IEnumerable<T> AllInScenes<T>() where T : Component
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;
            foreach (var root in s.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<T>(true)) yield return c;
        }
    }

    // ── Play 모드 전수 검사: 플레이어 크기 캡슐로 1m 격자를 훑어 "막혔는데 보이는 게 없는" 자리 찾기 ──
    //  BuildingWallCollider 벽처럼 Play 때만 생기는 콜라이더까지 포함. 막은 콜라이더가 그 자리 근처(캡슐 반경 + 여유)에
    //  자기 렌더 메시 삼각형이 하나도 없으면 "안 보이는 벽" 칸. 칸들을 콜라이더별로 묶어 위치·칸 수 출력 + Scene 뷰에 빨간 점
    public struct BlockCell { public Vector3 pos; public Collider col; }
    static readonly List<BlockCell> blockCells = new List<BlockCell>();
    public static IReadOnlyList<BlockCell> BlockCells => blockCells;
    const float CapR = 0.4f, CapLow = 0.45f, CapHigh = 1.6f, Slack = 0.35f;

    [MenuItem("Tools/Map/Find Invisible Blocks (Play)")]
    public static void FindInvisibleBlocks() => FindInvisibleBlocks(1f, null);

    /// <summary>area가 null이면 맵 전체 (Ground/지형 범위). step = 격자 간격(m)</summary>
    public static string FindInvisibleBlocks(float step, Bounds? area)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[MapColliderChecker] Play 모드에서 실행 (건물 벽 콜라이더가 Play 때만 생김)"); return "not playing"; }
        Physics.SyncTransforms();
        Bounds map = area ?? MapBounds();
        blockCells.Clear();
        var triCache = new Dictionary<Collider, List<Vector3>>();
        var hits = new Collider[16];
        int mask = ~0;
        for (float x = map.min.x; x <= map.max.x; x += step)
            for (float z = map.min.z; z <= map.max.z; z += step)
            {
                // 그 자리 바닥 (가장 위 표면). 지붕 위 등 높은 곳도 걸을 수 있는 면이면 검사 대상
                if (!Physics.Raycast(new Vector3(x, map.max.y + 50f, z), Vector3.down, out RaycastHit g, map.size.y + 200f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (g.normal.y < 0.7f) continue; // 경사·벽 면은 서 있을 곳이 아님
                // 플레이어가 실제로 다니는 높이만: 바닥(Ground/지형)에서 3m 안 (전선·지붕·간판 위는 제외)
                if (!GroundHeight(x, z, map, out float baseY) || g.point.y > baseY + 3f) continue;
                Vector3 a = g.point + Vector3.up * CapLow, b = g.point + Vector3.up * CapHigh;
                int n = Physics.OverlapCapsuleNonAlloc(a, b, CapR, hits, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i];
                    if (c == g.collider || c is TerrainCollider) continue;
                    if (c.GetComponentInParent<PlayerController>() != null || c.GetComponentInParent<ZombieController>() != null) continue;
                    if (c.GetComponent<PickupItem>() != null || c.GetComponentInParent<LootCrate>() != null) continue;
                    if (IntendedReason(c) != null) continue;
                    if (IsExactMeshCollider(c)) continue; // 보이는 메시와 똑같은 모양 = 안 보이는 벽 아님
                    if (!triCache.TryGetValue(c, out var tris)) triCache[c] = tris = VisibleTriangles(c);
                    if (NearAny(tris, a, b, CapR + Slack)) continue; // 보이는 메시가 바로 옆 → 정상 충돌
                    blockCells.Add(new BlockCell { pos = g.point, col = c });
                }
            }

        // 콜라이더별로 묶어 출력
        var groups = new Dictionary<Collider, List<Vector3>>();
        foreach (var bc in blockCells) { if (!groups.TryGetValue(bc.col, out var l)) groups[bc.col] = l = new List<Vector3>(); l.Add(bc.pos); }
        var sb = new StringBuilder();
        var sorted = new List<KeyValuePair<Collider, List<Vector3>>>(groups);
        sorted.Sort((p, q) => q.Value.Count.CompareTo(p.Value.Count));
        foreach (var kv in sorted)
        {
            Bounds cb = new Bounds(kv.Value[0], Vector3.zero); foreach (var v in kv.Value) cb.Encapsulate(v);
            sb.AppendLine($"  ✗ {Path(kv.Key.transform)} [{kv.Key.GetType().Name}] 막힌 칸 {kv.Value.Count}개, 범위 중심 {cb.center:F1} 크기 {cb.size:F1}");
        }
        string summary = $"[MapColliderChecker] 안 보이는 벽 칸 {blockCells.Count}개 (콜라이더 {groups.Count}개, 격자 {step}m)";
        Debug.Log(summary + "\n" + sb);
        SetGizmos(true);
        return summary + "\n" + sb;
    }

    // Ground / 지형 콜라이더만 맞히는 높이 (그 위의 소품·전선 무시)
    static bool GroundHeight(float x, float z, Bounds map, out float y)
    {
        y = 0f;
        var all = Physics.RaycastAll(new Vector3(x, map.max.y + 50f, z), Vector3.down, map.size.y + 200f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in all)
            if ((h.collider is TerrainCollider || h.collider.name == "Ground") && h.point.y > best) best = h.point.y;
        if (best == float.MinValue) return false;
        y = best; return true;
    }

    // MeshCollider가 같은 오브젝트에서 그리는 메시를 그대로 쓰면 보이는 모양 그대로
    static bool IsExactMeshCollider(Collider c)
    {
        if (!(c is MeshCollider mc) || mc.sharedMesh == null) return false;
        var mf = c.GetComponent<MeshFilter>();
        var r = c.GetComponent<Renderer>();
        return mf != null && mf.sharedMesh == mc.sharedMesh && r != null && r.enabled;
    }

    // ── 고치기 (Edit 모드): 전수 검사에서 나온 종류별로 콜라이더를 보이는 모양에 맞춤 ──
    //  - 나무(Urban Bits Tree-*): 캡슐 반경 → 플레이어 높이(지면 0.2~2.2m, 정점 없으면 밑동 고리)의 줄기 반경 + 5cm, 중심도 줄기로 (가지·잎은 머리 위라 충돌 불필요)
    //  - 자동차(Proop3/car*), 공원 가로등(ParkLight*): BoxCollider → 같은 메시의 MeshCollider (둥근 모서리·위쪽 팔 아래 빈 곳 통과)
    [MenuItem("Tools/Map/Fit Prop Colliders To Mesh")]
    public static string FitPropColliders()
    {
        if (Application.isPlaying) { Debug.LogWarning("[MapColliderChecker] Edit 모드에서 실행 (씬에 저장해야 함)"); return "playing"; }
        int trees = 0, boxes = 0;
        var sb = new StringBuilder();
        foreach (var cap in AllInScenes<CapsuleCollider>())
        {
            string path = Path(cap.transform);
            if (!path.Contains("Urban Bits") || !cap.name.StartsWith("Tree-")) continue;
            var mf = cap.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var t = cap.transform; var v = mf.sharedMesh.vertices;
            float baseY = float.MaxValue; foreach (var p in v) baseY = Mathf.Min(baseY, t.TransformPoint(p).y);
            // 줄기 중심 = 플레이어 높이 띠 정점들의 평균 (원래 캡슐 중심은 줄기에서 0.3~0.5m 어긋나 있었음)
            // 띠: 지면 0.2~2.2m (밑동 뿌리 퍼짐 제외). 그 높이에 정점이 없으면(Tree-2: 줄기 위·아래 끝에만 정점) 밑동 고리까지 포함
            float bandLo = baseY + 0.2f;
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var lp in v) { var p = t.TransformPoint(lp); if (p.y < bandLo || p.y > baseY + 2.2f) continue; sum += p; count++; }
            if (count == 0)
            {
                bandLo = baseY - 0.01f;
                foreach (var lp in v) { var p = t.TransformPoint(lp); if (p.y < bandLo || p.y > baseY + 2.2f) continue; sum += p; count++; }
            }
            if (count == 0) continue;
            Vector3 axis = sum / count;
            float maxR = 0f;
            foreach (var lp in v)
            {
                var p = t.TransformPoint(lp);
                if (p.y < bandLo || p.y > baseY + 2.2f) continue;
                maxR = Mathf.Max(maxR, new Vector2(p.x - axis.x, p.z - axis.z).magnitude);
            }
            float scale = Mathf.Max(t.lossyScale.x, t.lossyScale.z);
            float newR = (maxR + 0.05f) / scale;
            Vector3 localAxis = t.InverseTransformPoint(new Vector3(axis.x, t.TransformPoint(cap.center).y, axis.z));
            if (newR >= cap.radius && (localAxis - cap.center).sqrMagnitude < 1e-6f) continue;
            Undo.RecordObject(cap, "Fit tree collider");
            sb.AppendLine($"  나무 {path}: 반경 {cap.radius * scale:F2}m → {newR * scale:F2}m, 중심 줄기로 {(localAxis - cap.center).magnitude * scale:F2}m 이동");
            cap.radius = newR;
            cap.center = localAxis;
            EditorUtility.SetDirty(cap);
            trees++;
        }
        foreach (var box in AllInScenes<BoxCollider>())
        {
            string path = Path(box.transform);
            bool car = path.StartsWith("Proop3/car"), lamp = box.name.StartsWith("ParkLight");
            if (!car && !lamp) continue;
            var mf = box.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var go = box.gameObject;
            bool trig = box.isTrigger;
            Undo.DestroyObjectImmediate(box);
            var mc = Undo.AddComponent<MeshCollider>(go);
            mc.sharedMesh = mf.sharedMesh; mc.isTrigger = trig;
            EditorUtility.SetDirty(go);
            sb.AppendLine($"  {(car ? "자동차" : "가로등")} {path}: BoxCollider → MeshCollider");
            boxes++;
        }
        for (int i = 0; i < SceneManager.sceneCount; i++) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
        string msg = $"[MapColliderChecker] 나무 캡슐 {trees}개 줄임, 상자→메시 콜라이더 {boxes}개";
        Debug.Log(msg + "\n" + sb);
        return msg + "\n" + sb;
    }

    static Bounds MapBounds()
    {
        Bounds b = new Bounds(); bool first = true;
        foreach (var c in AllInScenes<Collider>())
        {
            if (!(c is TerrainCollider) && c.name != "Ground") continue;
            if (first) { b = c.bounds; first = false; } else b.Encapsulate(c.bounds);
        }
        return b;
    }

    // 콜라이더 오브젝트(와 자식, 부모 모델)의 보이는 렌더 메시 삼각형 (월드 좌표, 3개씩)
    static List<Vector3> VisibleTriangles(Collider c)
    {
        var list = new List<Vector3>();
        Transform root = c.transform.parent != null && c.GetComponent<Renderer>() == null ? c.transform.parent : c.transform;
        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled) continue;
            Mesh m = null;
            if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
            else if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
            if (m == null || !m.isReadable) { if (m != null) { var bb = r.bounds; AddBox(list, bb); } continue; }
            var mat = r.transform.localToWorldMatrix; var v = m.vertices; var idx = m.triangles;
            for (int i = 0; i < idx.Length; i += 3)
            {
                list.Add(mat.MultiplyPoint3x4(v[idx[i]])); list.Add(mat.MultiplyPoint3x4(v[idx[i + 1]])); list.Add(mat.MultiplyPoint3x4(v[idx[i + 2]]));
            }
        }
        return list;
    }

    // 메시를 읽을 수 없으면 렌더러 바운딩 박스로 대신 (보수적: 상자 안이면 보이는 것으로)
    static void AddBox(List<Vector3> list, Bounds b)
    {
        // 상자 6면 = 삼각형 12개
        Vector3 mn = b.min, mx = b.max;
        Vector3[] c = { new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mx.z),
                        new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z) };
        int[] f = { 0,1,2, 0,2,3, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7 };
        foreach (int i in f) list.Add(c[i]);
    }

    static bool NearAny(List<Vector3> tris, Vector3 a, Vector3 b, float r)
    {
        float r2 = r * r;
        for (int i = 0; i < tris.Count; i += 3)
            for (int k = 0; k <= 4; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / 4f);
                if ((ClosestOnTri(p, tris[i], tris[i + 1], tris[i + 2]) - p).sqrMagnitude <= r2) return true;
            }
        return false;
    }

    // 점 p에서 삼각형(a,b,c) 위 가장 가까운 점 (Ericson, Real-Time Collision Detection)
    static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    public static string Path(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}

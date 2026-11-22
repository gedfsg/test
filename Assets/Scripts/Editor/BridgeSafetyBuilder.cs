using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 다리·경사로 가장자리 낙하 방지 투명 벽 (Tools → Map → Build Bridge Safety Walls). 다시 실행하면 지우고 새로 만듦.
///
/// 문제 (2026-11-12): 다리 3개(Roads/road-bridge*)와 양 끝 경사로(기울어진 road-straight)에 난간 충돌이 전혀 없어서
///  - 다리 옆으로 걸어가면 강바닥(y≈0)으로 ~17m 추락
///  - 경사로 옆으로 걸어가면 1~3m 아래 지면/도로로 떨어짐
/// 하는 일:
///  1) 다리 중심선을 따라 다리 + 양쪽 45m(경사로)를 2m 간격으로 측정
///  2) 각 지점에서 좌우로 0.25m씩 나가며 "도로 면이 끝나고 0.6m 이상 뚝 떨어지는 곳"(= 낭떠러지 가장자리)을 찾음
///     (지형 경사로 서서히 내려가는 강둑은 낭떠러지가 아니라 제외 → 강가 도로 옆엔 벽이 안 생김)
///  3) 연속된 가장자리 점을 이어 높이 2.5m 투명 BoxCollider 벽(경사 따라 기울임) + InvisibleWall(총알 통과)
///  4) 씬 루트 "BridgeSafetyWalls"에 모아 저장. 이후 NavMesh 재굽기(Tools → Map → Rebake NavMesh With Building Walls) 필요
/// (2026-11-21) 피드백 "다리에 통과하는 부분이 있음": 측정 결과 다리 3곳에 0.5~2m 틈 8개(주로 다리↔경사로 이음새, 경사로 끝)
///  → 측정 간격 2m → 0.5m, 경사로 범위 45 → 60m
/// </summary>
public static class BridgeSafetyBuilder
{
    const string RootName = "BridgeSafetyWalls";
    // const float Step = 2f;         // (이전) 중심선 측정 간격 - 한 지점에서 가장자리 검출이 빠지면 앞뒤 4m가 벽 없이 뚫렸음
    // const float Extend = 45f;      // (이전) 경사로 끝부분이 범위 밖이라 벽이 끝에서 끊김
    const float Step = 0.5f;          // (2026-11-21) 중심선 측정 간격 - 다리·경사로 이음새 틈(0.5~2m) 메움
    const float Extend = 60f;         // (2026-11-21) 다리 끝에서 경사로 쪽으로 더 볼 거리
    const float Probe = 0.25f;        // 옆으로 나가는 간격
    const float MaxHalfWidth = 25f;   // 중심에서 가장자리까지 최대
    const float LedgeDrop = 0.6f;     // 이만큼 뚝 떨어지면 낭떠러지
    const float WallHeight = 2.5f, WallThickness = 0.3f, WallSink = 0.3f; // 벽은 면 아래 0.3m부터 2.2m 위까지
    const float MaxEdgeJump = 4f;     // 이웃 지점 가장자리가 이보다 멀면 벽을 잇지 않음
    const int MaxMissedStations = 2;  // (2026-11-21) 이 수까지 연속으로 검출이 빠진 지점은 건너뛰고 이음

    [MenuItem("Tools/Map/Build Bridge Safety Walls")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[BridgeSafety] Play 모드에서는 실행 불가"); return; }
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        Physics.SyncTransforms();

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Bridge safety walls");
        root.isStatic = true;

        var log = new System.Text.StringBuilder();
        int total = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("road-bridge")) continue;
            int n = BuildForBridge(r, root.transform, out string info);
            total += n;
            log.AppendLine($"  {r.name}: 벽 {n}개 {info}");
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[BridgeSafety] 다리 가장자리 투명 벽 {total}개 생성 → NavMesh 재굽기 필요\n{log}");
    }

    static int BuildForBridge(Renderer bridge, Transform root, out string info)
    {
        Bounds b = bridge.bounds;
        bool alongZ = b.size.z > b.size.x;
        Vector3 dir = alongZ ? Vector3.forward : Vector3.right;
        Vector3 side = alongZ ? Vector3.right : Vector3.forward;
        float half = (alongZ ? b.size.z : b.size.x) / 2f + Extend;
        Vector3 c0 = new Vector3(b.center.x, 0f, b.center.z);

        var group = new GameObject(bridge.name);
        group.transform.SetParent(root, false);
        group.isStatic = true;

        int count = 0, ledgeStations = 0;
        float minS = float.MaxValue, maxS = float.MinValue;
        foreach (float sgn in new[] { 1f, -1f })
        {
            Vector3? prev = null;
            int misses = 0; // (2026-11-21) 연속으로 가장자리를 못 찾은 지점 수
            for (float s = -half; s <= half; s += Step)
            {
                Vector3 c = c0 + dir * s;
                Vector3? edge = FindLedge(c, side * sgn);
                // (2026-11-21) 직전 가장자리에서 MaxEdgeJump 넘게 튄 점(다리↔경사로 이음새의 도로 안쪽 단차를 가장자리로 오인, 9m 안쪽)은
                //  연속 MaxMissedStations까지 "빠진 점"으로 보고 건너뜀 → 앞뒤 정상 가장자리끼리 이어짐
                if (edge != null && prev != null && misses < MaxMissedStations && Vector3.Distance(prev.Value, edge.Value) > MaxEdgeJump)
                {
                    misses++;
                    continue;
                }
                if (edge != null)
                {
                    ledgeStations++;
                    minS = Mathf.Min(minS, s); maxS = Mathf.Max(maxS, s);
                    if (prev != null && Vector3.Distance(prev.Value, edge.Value) <= MaxEdgeJump)
                    {
                        MakeWall(group.transform, prev.Value, edge.Value, side * sgn, count);
                        count++;
                    }
                }
                // prev = edge; // (이전) 한 지점만 검출이 빠져도 앞뒤가 안 이어져 틈이 생김 (다리↔경사로 이음새 0.58m - 2026-11-21 측정)
                // (2026-11-21) 연속 2지점(1m)까지는 빠져도 마지막 가장자리에서 다음 가장자리로 바로 이음 (MaxEdgeJump 안일 때만)
                if (edge != null) { prev = edge; misses = 0; }
                else if (++misses > MaxMissedStations) prev = null;
            }
        }
        info = ledgeStations > 0 ? $"(중심선 {minS:0}~{maxS:0}m 구간, 다리 길이 {(alongZ ? b.size.z : b.size.x):0}m)" : "(낭떠러지 없음)";
        return count;
    }

    /// <summary>중심 c에서 out 방향으로 나가며 도로 면이 뚝 끊기는 가장자리 점 (없으면 null)</summary>
    static Vector3? FindLedge(Vector3 c, Vector3 outDir)
    {
        if (!TopHit(c, out RaycastHit hc) || !IsRoad(hc.collider)) return null; // 중심이 도로/다리가 아니면 측정 안 함
        float surf = hc.point.y;
        Vector3 last = hc.point;
        for (float o = Probe; o <= MaxHalfWidth; o += Probe)
        {
            Vector3 p = c + outDir * o;
            bool hit = TopHit(p, out RaycastHit h);
            // (이전) 높은 것에 막히면 낭떠러지 아님으로 처리 → 다리 가장자리의 construction-barrier(띄엄띄엄, 밑이 뚫림) 때문에 다리 본체에 벽이 안 생겼음
            // if (hit && h.point.y > surf + 1.0f) return null;
            if (!hit || h.point.y < last.y - LedgeDrop)
                return IsRoadAt(last) ? last : (Vector3?)null;      // 도로 면 끝에서 뚝 떨어짐 → 가장자리
            if (!IsRoad(h.collider) && Mathf.Abs(h.point.y - last.y) < 0.3f && h.point.y > surf - 0.6f)
                return null;                                         // 도로 옆이 평평한 지면으로 이어짐 → 낭떠러지 아님
            last = h.point;
        }
        return null;
    }

    /// <summary>그 자리의 가장 높은 "바닥" (도로·다리·지면만. 바리케이드·차·가로등 같은 소품은 무시)</summary>
    static bool TopHit(Vector3 p, out RaycastHit hit)
    {
        hit = default;
        var hits = Physics.RaycastAll(new Vector3(p.x, 80f, p.z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (!IsRoad(h.collider) && h.collider.name != "Ground") continue;
            if (h.point.y > best) { best = h.point.y; hit = h; }
        }
        return best > float.MinValue;
    }

    // ── (2026-11-22) 경사로·다리 밑 낮은 틈 메우기 ──
    // 문제: 피드백 "농장 쪽 다리 밑에서 위로 갈 때 뚫리는 데가 있음".
    //  - 다리 양 끝 경사로(기울어진 road-straight/road-bend)와 다리 상판 끝 아래로 둑이 비스듬히 올라와 밑면과 만나는 "쐐기" 틈이 있음
    //  - 플레이어 충돌체는 지름 1m 구(높이 0 캡슐, 발밑 -0.1 ~ 0.9m)인데 모델 머리는 2.2m → 1m 틈만 있어도 몸판정은 들어가고
    //    보이는 몸은 위 도로를 뚫고 나옴. 더 밀면 도로 위로 튀어 오름 (실측: 둑 14.38m, 머리 위 경사로 15.23m에 끼임)
    //  - 도로 충돌 메시는 윗면만 막는 한쪽 면이라 밑에서는 안 막힘
    // 해결: 다리 + 다리 주변 기울어진 도로 조각 아래를 1m 격자로 재서 "지면 ~ 도로 밑면" 틈이 FillMin~FillMax인 칸을
    //  지면 아래 0.3m부터 밑면까지 BoxCollider로 꽉 채움 (같은 줄 이웃 칸은 최대 FillMergeCells개까지 한 박스로 합침).
    //  틈이 FillMax 이상인 곳(머리가 안 닿는 다리 한가운데 밑)은 그대로 둠. 윗면(도로)은 안 건드려서 위로 걷는 데 영향 없음
    const string RampRootName = "RampUndersideBlockers";
    const float RampMinTilt = 0.3f;        // 이 각도(도) 이상 기울어진 도로 조각 = 경사로
    const float RampBridgeRange = 130f;    // 다리 중심에서 이 거리(XZ) 안의 경사로만
    const float FillCell = 1f;             // 격자 간격 (m)
    // const float FillMin = 0.8f; // (1차) 0.74m 틈에도 파고듦 (밑면이 안 막아서 구가 그대로 들어감)
    const float FillMin = 0.4f, FillMax = 3.0f; // 메울 틈 범위: 평평한 도로가 땅 위에 놓인 틈(0.35m)은 제외, 위는 모델 키 2.2m + 가파른 둑에 걸친 높이 여유 (2.6 → 3.0, 본 다리 끝 실측)
    const float FillPad = 0.25f;           // 칸 가장자리 빈틈 메우기: 박스를 사방으로 이만큼 넓힘
    const int FillMergeCells = 6;          // 한 박스로 합칠 최대 칸 수 (경사 때문에 높이 차가 커지지 않게)
    const float FillMergeMaxStep = 0.15f;  // 합칠 칸들의 밑면 높이 차 최대 (m)

    [MenuItem("Tools/Map/Build Ramp Underside Blockers")]
    public static void BuildRampBlockers()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[BridgeSafety] Play 모드에서는 실행 불가"); return; }
        var old = GameObject.Find(RampRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var roads = GameObject.Find("Roads");
        if (roads == null) { Debug.LogWarning("[BridgeSafety] Roads 없음"); return; }

        // 대상: 다리 본체 + 다리 근처 기울어진 도로 조각
        var bridges = new List<Vector3>();
        foreach (Transform t in roads.transform) if (t.name.StartsWith("road-bridge")) bridges.Add(t.GetComponentInChildren<Collider>().bounds.center);
        var targets = new List<MeshCollider>();
        foreach (Transform t in roads.transform)
        {
            var mc = t.GetComponentInChildren<MeshCollider>();
            if (mc == null) continue;
            if (t.name.StartsWith("road-bridge")) { targets.Add(mc); continue; }
            if (Vector3.Angle(mc.transform.up, Vector3.up) < RampMinTilt) continue;
            foreach (var c in bridges) { var d = c - t.position; d.y = 0f; if (d.magnitude < RampBridgeRange) { targets.Add(mc); break; } }
        }

        var root = new GameObject(RampRootName);
        Undo.RegisterCreatedObjectUndo(root, "Ramp Underside Blockers");
        bool hb = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true; // 도로 밑면(뒷면)을 위로 쏴서 찾음
        int boxes = 0, cells = 0;
        try
        {
            foreach (var mc in targets)
            {
                var b = mc.bounds;
                for (float x = b.min.x + FillCell / 2f; x < b.max.x; x += FillCell)
                {
                    // z 방향 한 줄씩: 메울 칸을 모아 이웃끼리 합침
                    float runStart = 0f, runGround = 0f, runUnder = 0f, runUnderMax = 0f; int runLen = 0;
                    for (float z = b.min.z + FillCell / 2f; z < b.max.z + FillCell; z += FillCell)
                    {
                        float ground = 0f, under = 0f;
                        bool fill = z < b.max.z && GapAt(mc, x, z, out ground, out under);
                        // 밑면 높이가 비슷한 이웃 칸만 합침 (다리 들보처럼 밑면이 들쭉날쭉하면 박스 위에 빈 틈이 생겨 올라가 낄 수 있음 - 다리(2) 실측)
                        if (fill && runLen > 0 && runLen < FillMergeCells
                            && Mathf.Max(runUnderMax, under) - Mathf.Min(runUnder, under) < FillMergeMaxStep)
                        {
                            runLen++; runGround = Mathf.Min(runGround, ground); runUnder = Mathf.Min(runUnder, under); runUnderMax = Mathf.Max(runUnderMax, under); cells++;
                            continue;
                        }
                        if (runLen > 0) { MakeFill(root.transform, x, runStart, runLen, runGround, runUnder, boxes++); runLen = 0; }
                        if (fill) { runStart = z; runLen = 1; runGround = ground; runUnder = runUnderMax = under; cells++; }
                    }
                }
            }
        }
        finally { Physics.queriesHitBackfaces = hb; }

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log($"[BridgeSafety] 다리·경사로 밑 틈 메움: 칸 {cells}개 → 박스 {boxes}개 (대상 {targets.Count}개, {RampRootName})");
    }

    /// <summary>(x, z)에서 도로(mc) 바로 아래 지면과 도로 밑면 사이 틈이 메울 범위인지</summary>
    static bool GapAt(MeshCollider mc, float x, float z, out float ground, out float under)
    {
        ground = under = 0f;
        // 도로 윗면
        if (!mc.Raycast(new Ray(new Vector3(x, mc.bounds.max.y + 5f, z), Vector3.down), out RaycastHit top, mc.bounds.size.y + 10f)) return false;
        // 그 아래 가장 높은 지면 (도로·투명 벽·물체 제외)
        float best = float.MinValue; Vector3 n = Vector3.up;
        foreach (var h in Physics.RaycastAll(new Vector3(x, top.point.y - 0.05f, z), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsRoad(h.collider) || h.collider.GetComponent<InvisibleWall>() != null || h.collider.attachedRigidbody != null) continue;
            if (h.point.y > best) { best = h.point.y; n = h.normal; }
        }
        // if (best == float.MinValue || n.y < 0.5f) return false; // (1차) 가파른 둑(다리 끝 밑)이 빠짐
        if (best == float.MinValue || n.y < 0.2f) return false; // 지면 없음 / 거의 수직인 벽
        // 지면에서 위로 → 처음 닿는 도로 밑면
        if (!mc.Raycast(new Ray(new Vector3(x, best + 0.02f, z), Vector3.up), out RaycastHit up, top.point.y - best + 1f)) return false;
        float gap = up.point.y - best;
        if (gap < FillMin || gap >= FillMax) return false;
        ground = best; under = up.point.y;
        return true;
    }

    static void MakeFill(Transform parent, float x, float z0, int len, float ground, float under, int i)
    {
        float bottom = ground - 0.3f, top = under - 0.02f; // 위는 밑면 바로 아래 (도로 면은 안 건드림)
        if (top - bottom < 0.1f) return;
        var go = new GameObject("Fill_" + i);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x, (bottom + top) / 2f, z0 + (len - 1) * FillCell / 2f);
        go.isStatic = true;
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(FillCell + FillPad * 2f, top - bottom, len * FillCell + FillPad * 2f);
        go.AddComponent<InvisibleWall>(); // 총알 통과
    }

    static bool IsRoad(Collider c) => c != null && c.name.StartsWith("road");

    static bool IsRoadAt(Vector3 p) => TopHit(p, out RaycastHit h) && IsRoad(h.collider);

    static void MakeWall(Transform parent, Vector3 a, Vector3 b, Vector3 outDir, int i)
    {
        // 가장자리 바깥쪽으로 반 두께만큼 밀어서 도로 면을 안 먹게
        Vector3 shift = outDir * (WallThickness / 2f);
        a += shift; b += shift;
        Vector3 along = b - a;
        var go = new GameObject("Wall_" + i);
        go.transform.SetParent(parent, false);
        go.transform.position = (a + b) / 2f + Vector3.up * (WallHeight / 2f - WallSink);
        go.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
        go.isStatic = true;
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(WallThickness, WallHeight, along.magnitude + 0.4f); // 이웃 벽과 0.2m씩 겹쳐 틈 없음
        go.AddComponent<InvisibleWall>();
    }
}

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
/// </summary>
public static class BridgeSafetyBuilder
{
    const string RootName = "BridgeSafetyWalls";
    const float Step = 2f;            // 중심선 측정 간격
    const float Extend = 45f;         // 다리 끝에서 경사로 쪽으로 더 볼 거리
    const float Probe = 0.25f;        // 옆으로 나가는 간격
    const float MaxHalfWidth = 25f;   // 중심에서 가장자리까지 최대
    const float LedgeDrop = 0.6f;     // 이만큼 뚝 떨어지면 낭떠러지
    const float WallHeight = 2.5f, WallThickness = 0.3f, WallSink = 0.3f; // 벽은 면 아래 0.3m부터 2.2m 위까지
    const float MaxEdgeJump = 4f;     // 이웃 지점 가장자리가 이보다 멀면 벽을 잇지 않음

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
            for (float s = -half; s <= half; s += Step)
            {
                Vector3 c = c0 + dir * s;
                Vector3? edge = FindLedge(c, side * sgn);
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
                prev = edge;
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

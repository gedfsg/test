using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools/Utility/Audit Occluder Coverage (2026-11-21) - 외부 가림 구멍(OccluderFader 구멍 모드)이 빠지는 곳 스캔.
///  1) 빠짐: 카메라와 플레이어 사이에 들어올 수 있는 큰 메시(높이 MinHeight 이상, 지면 아님)인데 Occludable 레이어가 아닌 것
///     (건물 외벽·지붕·광고판·간판·담장·다리·터널 등). 레이어별로 묶어 보여줌
///  2) 잘못 포함: Occludable인데 플레이어·좀비·떨어진 아이템·투사체(물리로 움직이는 것) → 가림 처리 대상이면 안 됨
///  3) 감지 불가: Occludable이지만 콜라이더가 없고 건물(BuildingInterior) 안이라 범위(Bounds) 검사도 안 하는 것
/// "Audit Occluder Coverage"는 보고 + 빠진 것 선택만, "(Fix)"는 1)의 Default/Obstacle 레이어를 Occludable로,
/// 2)를 Default로 바꾸고 씬 저장. 판정 기준은 Mark Occludable Objects(OccludableMarker)와 같음.
/// </summary>
/// (2026-11-22 C1) 차량·높이 3.5m 미만 소품·실내 장식(Building_Interior_*)은 가림 대상 아님 (OccluderFader.IsIgnoredOccluder와 같은 기준)
///  → "Prune Occluders" 메뉴로 Occludable에서 빼고 Default로. 빠짐 검사도 같은 기준이라 Fix가 도로 넣지 않음
public static class OccluderCoverageAudit
{
    // const float MinHeight = 2.0f;     // (이전)
    const float MinHeight = OccluderFader.MinOccluderHeight; // (2026-11-22 C1) 3.5m
    const float MaxFootprint = 40f;      // 땅째 붙은 큰 메시(농장 등)는 제외 (통째로 뚫리면 땅이 사라짐)

    [MenuItem("Tools/Utility/Audit Occluder Coverage")]
    public static void Audit() => Run(false);

    [MenuItem("Tools/Utility/Audit Occluder Coverage (Fix)")]
    public static void AuditFix() => Run(true);

    static void Run(bool fix)
    {
        if (Application.isPlaying) { Debug.LogWarning("[OccluderAudit] Play 중에는 실행 안 함"); return; }
        int occ = LayerMask.NameToLayer(OccluderFader.LayerName);
        if (occ < 0) { Debug.LogError("[OccluderAudit] 'Occludable' 레이어 없음 → Tools/Utility/Mark Occludable Objects 먼저"); return; }
        int obstacle = LayerMask.NameToLayer("Obstacle");
        var scene = EditorSceneManager.GetActiveScene();

        var missing = new Dictionary<string, List<Renderer>>();
        var wrong = new List<Renderer>();
        var undetectable = new List<Renderer>();
        int checkedCount = 0, okCount = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<Canvas>() != null) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled) continue;
                checkedCount++;
                var go = r.gameObject;
                bool dynamic = IsDynamic(r);

                if (go.layer == occ)
                {
                    if (dynamic) { wrong.Add(r); continue; }
                    okCount++;
                    bool inBuilding = r.GetComponentInParent<BuildingInterior>(true) != null;
                    if (inBuilding && r.GetComponentInParent<Collider>(true) == null && r.GetComponentInChildren<Collider>(true) == null
                        && !HasColliderInBuilding(r))
                        undetectable.Add(r);
                    continue;
                }

                if (dynamic || OccludableMarker.IsExcluded(go.transform) || OccludableMarker.IsGroundLike(r)) continue;
                if (OccluderFader.IsVehicle(r.transform) || IsInteriorDecor(r.transform)) continue; // (2026-11-22 C1)
                var s = r.bounds.size;
                if (s.y < MinHeight || Mathf.Max(s.x, s.z) >= MaxFootprint) continue;
                string ln = LayerMask.LayerToName(go.layer);
                if (string.IsNullOrEmpty(ln)) ln = "Layer" + go.layer;
                if (!missing.TryGetValue(ln, out var list)) missing[ln] = list = new List<Renderer>();
                list.Add(r);
            }
        }

        int fixedIn = 0, fixedOut = 0;
        if (fix)
        {
            foreach (var kv in missing)
                foreach (var r in kv.Value)
                {
                    int l = r.gameObject.layer;
                    if (l != 0 && l != obstacle) continue; // 다른 용도 레이어(물·UI·지면 등)는 안 건드림 - 보고만
                    Undo.RecordObject(r.gameObject, "Occludable");
                    r.gameObject.layer = occ;
                    EditorUtility.SetDirty(r.gameObject);
                    fixedIn++;
                }
            foreach (var r in wrong)
            {
                Undo.RecordObject(r.gameObject, "Not Occludable");
                r.gameObject.layer = 0;
                EditorUtility.SetDirty(r.gameObject);
                fixedOut++;
            }
            if (fixedIn + fixedOut > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        }

        var sb = new StringBuilder();
        int missCount = 0; foreach (var kv in missing) missCount += kv.Value.Count;
        sb.AppendLine($"[OccluderAudit] 렌더러 {checkedCount}개 검사 - Occludable 정상 {okCount} / 빠짐 {missCount} / 잘못 포함 {wrong.Count} / 감지 불가 {undetectable.Count}");
        foreach (var kv in missing)
            sb.AppendLine($"  빠짐 [{kv.Key}] {kv.Value.Count}개: " + Names(kv.Value));
        if (wrong.Count > 0) sb.AppendLine("  잘못 포함(플레이어·좀비·아이템·투사체): " + Names(wrong));
        if (undetectable.Count > 0) sb.AppendLine("  감지 불가(콜라이더 없는 건물 부품): " + Names(undetectable));
        if (fix) sb.AppendLine($"  (Fix) Occludable로 {fixedIn}개, Default로 {fixedOut}개 바꾸고 씬 저장");
        Debug.Log(sb.ToString());

        if (!fix)
        {
            var sel = new List<Object>();
            foreach (var kv in missing) foreach (var r in kv.Value) sel.Add(r.gameObject);
            sel.AddRange(wrong.ConvertAll(r => (Object)r.gameObject));
            if (sel.Count > 0) Selection.objects = sel.ToArray();
        }
    }

    static bool IsInteriorDecor(Transform t) => t.root.name.StartsWith("Building_Interior");

    /// <summary>(2026-11-22 C1) 차량·작은 소품·실내 장식을 Occludable에서 빼고 Default로 (건물 본체는 그대로)</summary>
    [MenuItem("Tools/Utility/Prune Occluders (차량·작은 소품·실내 장식 제외)")]
    public static void Prune()
    {
        if (Application.isPlaying) { Debug.LogWarning("[OccluderAudit] Play 중에는 실행 안 함"); return; }
        int occ = LayerMask.NameToLayer(OccluderFader.LayerName);
        var scene = EditorSceneManager.GetActiveScene();
        int cars = 0, small = 0, decor = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer != occ) continue;
                if (t.GetComponentInParent<BuildingInterior>(true) != null) continue; // 건물 본체는 유지
                string why = null;
                if (OccluderFader.IsVehicle(t)) why = "car";
                else if (IsInteriorDecor(t)) why = "decor";
                else
                {
                    // 렌더러가 있으면 렌더러 높이, 없으면(콜라이더만) 부모 쪽 렌더러 기준
                    var r = t.GetComponent<Renderer>() ?? t.GetComponentInParent<Renderer>();
                    if (r != null && r.bounds.size.y < OccluderFader.MinOccluderHeight) why = "small";
                }
                if (why == null) continue;
                Undo.RecordObject(t.gameObject, "Prune Occluder");
                t.gameObject.layer = 0;
                EditorUtility.SetDirty(t.gameObject);
                if (why == "car") cars++; else if (why == "decor") decor++; else small++;
            }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[OccluderAudit] Prune: 차량 {cars} / 작은 소품 {small} / 실내 장식 {decor} 오브젝트를 Occludable → Default");
    }

    static bool HasColliderInBuilding(Renderer r)
    {
        var b = r.GetComponentInParent<BuildingInterior>(true);
        if (b == null) return false;
        // 건물 루트 아래 아무 비트리거 콜라이더라도 있으면 SphereCast가 건물 단위로 잡음 (RenderersFor가 건물 전체 렌더러를 모음)
        foreach (var c in b.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) return true;
        return false;
    }

    // OccluderFader.IsDynamic과 같은 기준
    static bool IsDynamic(Renderer r)
    {
        for (var t = r.transform; t != null; t = t.parent)
        {
            string ln = LayerMask.LayerToName(t.gameObject.layer);
            if (ln == "Player" || ln == "Pickup") return true;
            if (t.CompareTag("Player")) return true;
        }
        if (r.GetComponentInParent<PlayerController>(true) != null || r.GetComponentInParent<ZombieController>(true) != null
            || r.GetComponentInParent<PickupItem>(true) != null) return true;
        var rb = r.GetComponentInParent<Rigidbody>(true);
        return rb != null && !rb.isKinematic;
    }

    static string Names(List<Renderer> list)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count && i < 30; i++) { if (i > 0) sb.Append(", "); sb.Append(list[i].transform.root.name).Append('/').Append(list[i].name); }
        if (list.Count > 30) sb.Append($" … 외 {list.Count - 30}개");
        return sb.ToString();
    }

    // ── (2026-11-22) 큰 외벽·구조물 Occludable 지정 ──
    // 피드백 "외부에서 건물에 캐릭터가 가려졌는데 원형(구멍)이 안 보이는 곳이 있음".
    // 맵 전체 실외 지점(3m 간격) × 카메라 4방향 스캔: 몸·머리가 다 가려지는데 구멍이 안 열리는 경우의 대부분이
    //  위 Audit의 "가로 40m 넘는 메시 제외"(MaxFootprint)에 걸려 Default로 남은 큰 외벽·구조물 + 그 콜라이더였음
    //  (OccluderFader는 Occludable "콜라이더"로 감지하므로 렌더러만 Occludable이어도 안 열림)
    //  - 학교 정면·옆 외벽(Mesh1/37/39 등), 광장 둘레 담벽(MURO), Kay Lousberg 건물 본체 → 렌더러+콜라이더가 같은 오브젝트: 높이 3.5m 이상 조각만
    //  - 농장 2곳·급수탑 2개·물탱크·다리 3개 → 콜라이더가 렌더러와 따로 있어서 루트째 (RenderersFor가 가장 위 Occludable 조상 아래 렌더러를 모음)
    // 구멍은 플레이어 발 + HoleMinHeight 위만 자르므로 땅이 붙은 농장 메시도 발밑은 안 잘림
    static readonly string[] PieceRoots =
    {
        "Buildings/Enterable_School",
        "scene/2e6e3a56b6f4412db7eb377c179d50a9.fbx",          // 광장 담벽(MURO)·분수
        "Buildings/Building by Kay Lousberg - qOhhGLftam",
    };
    static readonly string[] WholeRoots =
    {
        "Buildings/Enterable_Farm by Poly by Google - 5GDbUJV2vQb",
        "Buildings/Enterable_Farm by Quaternius - 91wMLb9kKo",
        "Water/Water Tank by Quaternius - XVB8vUbnZb",
        "Water/Water Tank by Quaternius - XVB8vUbnZb (1)",
        "Urban Bits – Free Lowpoly City Props Pack - By MaHa/WaterSource",
        "Roads/road-bridge",
        "Roads/road-bridge (1)",
        "Roads/road-bridge (2)",
    };

    [MenuItem("Tools/Utility/Mark Large Occluders (2026-11-22)")]
    public static void MarkLargeOccluders()
    {
        if (Application.isPlaying) { Debug.LogWarning("[OccluderAudit] Play 중에는 실행 안 함"); return; }
        int occ = LayerMask.NameToLayer(OccluderFader.LayerName);
        if (occ < 0) { Debug.LogError("[OccluderAudit] 'Occludable' 레이어 없음"); return; }
        int changed = 0;
        foreach (var path in PieceRoots)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogWarning("[OccluderAudit] 없음: " + path); continue; }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.layer == occ || IsDynamic(r) || r.bounds.size.y < OccluderFader.MinOccluderHeight) continue;
                Undo.RecordObject(r.gameObject, "Mark Large Occluders");
                r.gameObject.layer = occ; changed++;
            }
        }
        foreach (var path in WholeRoots)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogWarning("[OccluderAudit] 없음: " + path); continue; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer == occ) continue;
                var c = t.GetComponent<Collider>();
                if (c != null && c.isTrigger && t != go.transform) continue; // 실내 트리거 등은 그대로
                Undo.RecordObject(t.gameObject, "Mark Large Occluders");
                t.gameObject.layer = occ; changed++;
            }
        }
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[OccluderAudit] 큰 외벽·구조물 Occludable 지정: 오브젝트 {changed}개");
    }
}

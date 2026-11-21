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
public static class OccluderCoverageAudit
{
    const float MinHeight = 2.0f;        // 이보다 낮으면 위에서 내려다보는 카메라 시선을 거의 안 가림
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
}

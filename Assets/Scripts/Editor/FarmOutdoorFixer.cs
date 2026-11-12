using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 농장·밭을 건물이 아닌 야외 지역으로 (Tools → Map → Farms As Outdoor). 다시 실행해도 안전.
///
/// 문제 (2026-11-12 피드백): 농장·밭 오브젝트에 BuildingInterior가 붙어 있어서 밭에 들어가면
/// 실내 판정 → 지붕 컷어웨이·실내 안개·X-Ray가 켜짐. 루팅도 창고(Warehouse)로 분류돼 창고 좀비 4마리가 생겼음.
/// 대상 (이름에 Farm / Field / 농장 / 밭): "Enterable_Farm by Quaternius"(집 + 울타리 밭), "Enterable_Farm by Poly by Google"(헛간 + 사일로 + 마당)
/// 하는 일:
///  1) BuildingInterior 컴포넌트 제거 → 실내 판정·컷어웨이·실내 루팅/좀비 없음
///  2) 태그를 "Outdoor"(야외 지역)로 (태그가 없으면 TagManager에 추가)
///  3) 트리거 Collider는 그대로 둠 (지역 경계 표시용, isTrigger라 막지 않음)
///  4) 모델 충돌(BuildingWallCollider = 보이는 메시 그대로의 벽/울타리 충돌)은 그대로
///  5) 단, 밭 두둑(Dirt 재질 서브메시, 높이 ~1.6m)은 충돌에서 뺌 (walkThroughMaterials) → 밭 안 자유 이동.
///     울타리 출입구는 모델에 원래 있는 틈(남서쪽)을 그대로 씀. 실행 후 NavMesh 재굽기 필요
///  6) 울타리 선에 투명 BoxCollider 벽(FenceBlockers, 높이 2.5m) → 가로대 밑으로 빠져나가지 못하게
/// 되돌리려면 git에서 main.unity를 되돌리면 됨.
/// </summary>
public static class FarmOutdoorFixer
{
    public const string OutdoorTag = "Outdoor";
    public const string DirtMaterial = "Dirt"; // Quaternius 농장 밭 두둑 재질
    static readonly string[] Keywords = { "farm", "field", "농장", "밭" };

    [MenuItem("Tools/Map/Farms As Outdoor")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[FarmOutdoor] Play 모드에서는 실행 불가"); return; }
        EnsureTag(OutdoorTag);

        int removed = 0, tagged = 0, dirt = 0, fences = 0;
        var log = new System.Text.StringBuilder();
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!IsFarmName(go.name)) continue;
            // 농장 루트만 (자식 메시 이름 Farm_1 등은 루트가 처리)
            if (go.transform.parent != null && IsFarmName(go.transform.parent.name)) continue;

            var bi = go.GetComponent<BuildingInterior>();
            if (bi != null)
            {
                Undo.DestroyObjectImmediate(bi);
                removed++;
            }
            if (!go.CompareTag(OutdoorTag))
            {
                Undo.RecordObject(go, "Farm outdoor tag");
                go.tag = OutdoorTag;
                tagged++;
            }
            // 밭 두둑(Dirt 재질)은 충돌에서 뺌 → 밭 안 자유 이동, 울타리·집은 그대로 막음 (2026-11-12 추가)
            foreach (var wall in go.GetComponentsInChildren<BuildingWallCollider>(true))
            {
                if (System.Array.IndexOf(wall.walkThroughMaterials ?? new string[0], DirtMaterial) >= 0) continue;
                Undo.RecordObject(wall, "Farm walk-through dirt");
                var list = new System.Collections.Generic.List<string>(wall.walkThroughMaterials ?? new string[0]) { DirtMaterial };
                wall.walkThroughMaterials = list.ToArray();
                EditorUtility.SetDirty(wall);
                dirt++;
            }
            fences += BuildFenceBlockers(go);
            var trig = go.GetComponent<Collider>();
            log.AppendLine($"  {go.name} (트리거 {(trig != null && trig.isTrigger ? "유지" : "없음")})");
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[FarmOutdoor] BuildingInterior 제거 {removed}개, Outdoor 태그 {tagged}개, 두둑 통과 설정 {dirt}개, 울타리 막이 {fences}개\n{log}");
    }

    // ── 울타리 막이 (2026-11-12) ──
    // Quaternius 농장 울타리는 가로대가 지면 위 ~1.3m·~2.2m에만 있어서, 키 1m짜리 플레이어 충돌(구 반지름 0.5)은
    // 가로대 아래로 그냥 지나갔음 (좀비 NavMesh는 막힘). → 울타리 선을 따라 지면~2.5m 투명 BoxCollider를 세움.
    // 좌표는 Play 중 레이캐스트로 잰 울타리 선 (월드 좌표, x1,z1 → x2,z2). 남쪽 x 110.6~127.5 틈 = 출입구(Gate)는 비워 둠.
    const string QuaterniusFarm = "Enterable_Farm by Quaternius";
    const string FenceRootName = "FenceBlockers";
    const float FenceGroundY = 15.0f, FenceHeight = 2.5f, FenceThickness = 0.4f;
    static readonly Vector4[] QuaterniusFence =
    {
        new Vector4(108.6f, -332.7f, 110.6f, -332.7f),   // 남쪽 (출입구 서쪽 기둥)
        new Vector4(127.5f, -332.7f, 164.9f, -332.7f),   // 남쪽 (출입구 동쪽 ~ 남동 모서리)
        new Vector4(164.4f, -332.9f, 164.4f, -283.9f),   // 동쪽
        new Vector4(108.6f, -284.2f, 164.9f, -284.2f),   // 북쪽
        new Vector4(108.8f, -332.9f, 108.8f, -283.9f),   // 서쪽
    };

    /// <summary>농장 울타리 선에 투명 벽 생성 (다시 실행하면 지우고 새로 만듦). 만든 개수 반환</summary>
    static int BuildFenceBlockers(GameObject farmRoot)
    {
        if (!farmRoot.name.StartsWith(QuaterniusFarm)) return 0;
        var old = farmRoot.transform.Find(FenceRootName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(FenceRootName);
        Undo.RegisterCreatedObjectUndo(root, "Farm fence blockers");
        root.transform.SetParent(farmRoot.transform, false);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Vector3 ps = farmRoot.transform.lossyScale; // 부모 스케일 상쇄 → 자식은 월드 단위
        root.transform.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
        root.isStatic = true;

        int n = 0;
        foreach (var s in QuaterniusFence)
        {
            var a = new Vector3(s.x, FenceGroundY, s.y);
            var b = new Vector3(s.z, FenceGroundY, s.w);
            var seg = new GameObject("Fence_" + n);
            seg.transform.SetParent(root.transform, false);
            seg.transform.position = (a + b) * 0.5f + Vector3.up * FenceHeight * 0.5f;
            seg.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            seg.isStatic = true;
            var box = seg.AddComponent<BoxCollider>();
            box.size = new Vector3(FenceThickness, FenceHeight, Vector3.Distance(a, b));
            n++;
        }
        return n;
    }

    public static bool IsFarmName(string n)
    {
        n = n.ToLowerInvariant();
        foreach (var k in Keywords) if (n.Contains(k)) return true;
        return false;
    }

    static void EnsureTag(string tag)
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = tm.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        tm.ApplyModifiedProperties();
    }
}

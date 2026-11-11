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
/// 되돌리려면 git에서 main.unity를 되돌리면 됨.
/// </summary>
public static class FarmOutdoorFixer
{
    public const string OutdoorTag = "Outdoor";
    static readonly string[] Keywords = { "farm", "field", "농장", "밭" };

    [MenuItem("Tools/Map/Farms As Outdoor")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[FarmOutdoor] Play 모드에서는 실행 불가"); return; }
        EnsureTag(OutdoorTag);

        int removed = 0, tagged = 0;
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
            var trig = go.GetComponent<Collider>();
            log.AppendLine($"  {go.name} (트리거 {(trig != null && trig.isTrigger ? "유지" : "없음")})");
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[FarmOutdoor] BuildingInterior 제거 {removed}개, Outdoor 태그 {tagged}개\n{log}");
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

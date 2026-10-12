using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 선택한 건물 모델을 "들어갈 수 있는 건물"로 만드는 툴 (Tools → Map → Make Selected Building Enterable).
/// Sketchfab 등 외부 모델은 루트가 회전돼 있거나(glTF 축 변환) 통째로 막는 콜라이더를 갖고 있어서
/// 기존 건물 시스템(BuildingInterior + BuildingWallCollider)을 바로 못 씀. 이 툴이:
///   1) 회전 없는 새 부모(Enterable_이름)를 만들어 모델을 그 밑으로 옮기고 (월드 위치 유지)
///   2) 모델의 막힌 콜라이더(트리거 아닌 것)를 지우고
///   3) 이름에 "Collider"가 들어간 보이지 않는 충돌용 메시는 렌더러를 끄고
///   4) 새 부모에 진입 트리거 + BuildingInterior를 붙임
/// 이후 문 위치를 _TempFixBuildingWallColliders의 DoorTable에 추가하고
/// Tools → Map → Fix Building Wall Colliders 를 실행하면 벽 콜라이더/문 구멍/트리거 크기가 잡힘.
/// </summary>
public static class MakeBuildingEnterable
{
    [MenuItem("Tools/Map/Make Selected Building Enterable")]
    static void Run()
    {
        GameObject model = Selection.activeGameObject;
        if (model == null) { Debug.LogWarning("[MakeEnterable] 건물 모델을 선택하세요."); return; }
        if (model.GetComponentInParent<BuildingInterior>() != null || model.GetComponentInChildren<BuildingInterior>() != null)
        { Debug.LogWarning($"[MakeEnterable] '{model.name}'은 이미 들어갈 수 있는 건물입니다."); return; }

        // 보이는 메시 기준 범위 (충돌용 숨김 메시 제외)
        Bounds? bounds = null;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (IsColliderProxy(r.gameObject)) continue;
            if (bounds == null) bounds = r.bounds; else { var b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; }
        }
        if (bounds == null) { Debug.LogWarning($"[MakeEnterable] '{model.name}'에 보이는 메시가 없습니다."); return; }
        Bounds wb = bounds.Value;

        // 1) 회전 없는 새 부모
        var wrapper = new GameObject("Enterable_" + model.name);
        Undo.RegisterCreatedObjectUndo(wrapper, "Make Enterable");
        var buildings = GameObject.Find("Buildings");
        if (buildings != null) wrapper.transform.SetParent(buildings.transform, false);
        wrapper.transform.position = new Vector3(wb.center.x, wb.min.y, wb.center.z);
        wrapper.transform.rotation = Quaternion.identity;
        wrapper.transform.localScale = Vector3.one;
        Undo.SetTransformParent(model.transform, wrapper.transform, "Make Enterable");

        // 2) 막힌 콜라이더 제거
        int removed = 0;
        foreach (var c in model.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            Undo.DestroyObjectImmediate(c);
            removed++;
        }

        // 3) 충돌용 숨김 메시 렌더러 끄기 (BuildingInterior가 재질을 바꾸면 보이게 되므로)
        int hidden = 0;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsColliderProxy(r.gameObject)) continue;
            Undo.RecordObject(r, "Make Enterable");
            r.enabled = false;
            hidden++;
        }

        // 4) 진입 트리거 + BuildingInterior (트리거 크기는 Fix Building Wall Colliders가 실내 크기로 줄임)
        var trigger = Undo.AddComponent<BoxCollider>(wrapper);
        trigger.isTrigger = true;
        trigger.center = wrapper.transform.InverseTransformPoint(wb.center);
        trigger.size = wb.size;
        Undo.AddComponent<BuildingInterior>(wrapper);

        Selection.activeGameObject = wrapper;
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[MakeEnterable] ✅ '{model.name}' → '{wrapper.name}' (막힌 콜라이더 {removed}개 제거, 충돌용 메시 {hidden}개 숨김). " +
                  "다음: DoorTable에 문 위치 추가 후 Tools → Map → Fix Building Wall Colliders 실행");
    }

    static bool IsColliderProxy(GameObject go) => go.name.Contains("Collider");
}

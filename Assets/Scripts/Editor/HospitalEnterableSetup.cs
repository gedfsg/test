using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 병원(Sketchfab "scene (3)" 모델)을 들어갈 수 있는 건물로 만드는 툴 (Tools → Map → Make Hospital Enterable). 다시 실행해도 안전.
///
/// 문제: 병원 모델에 콜라이더가 하나도 없어서 플레이어가 벽을 그냥 통과해 건물 속으로 들어감
///       → 카메라엔 지붕 판만 보여서 "지붕 위에 서 있는 것처럼" 보이고, 실내 시스템(BuildingInterior)도 없었음.
///
/// 구조 (2026-10-14 레이캐스트 실측, 월드 좌표):
///  - 본관 벽 Cube.055: x -337 ~ -253, z -28 ~ 34.6, 높이 25m. 서쪽 면은 z 4~34 구간이 x -307로 30m 들어간 현관 마당
///  - 정문(유리문 vetri + 문틀 frames): 현관 마당 안쪽 벽 x ≈ -306, z 14 ~ 23 (위에 포치 지붕 Plane.016)
///  - 별관 Cube.059 (z -42 ~ -27), 바닥판 Cube.058 (지면보다 0.4m 높음), 간판 Cube.061, 화분 Cube.060
/// 하는 일:
///  1) Make Selected Building Enterable 과 같은 처리 (회전 없는 부모 Enterable_ + 진입 트리거 + BuildingInterior)
///  2) 지면에 닿는 벽 조각마다 BuildingWallCollider (보이는 메시 그대로 두께 있는 벽). 정문 조각들만 문 구멍
///     (유리창/창틀에도 붙임 - 안 붙이면 1층 큰 창으로 빠져나감)
///  3) 바닥판에 MeshCollider (없으면 실내에서 발이 바닥판 속 0.4m에 파묻힘)
///  4) 진입 트리거를 본관 실내 크기로 (현관 마당 포함: 트리거는 상자 하나라 ㄱ자 실내를 다 덮으려면 포함해야 함)
/// </summary>
public static class HospitalEnterableSetup
{
    const string ModelRootName = "scene (3)";

    // 정문 구멍 (월드 좌표): 문 바깥 3.8m ~ 안쪽 5m, 바닥 ~ 5m 높이, 유리문 두 짝 + 여유
    static readonly Bounds DoorWorld = new Bounds(new Vector3(-306f, 17.5f, 18.5f), new Vector3(9f, 5f, 10.6f));

    // 진입 트리거 (월드 좌표): 본관 실내 (바깥 벽면에서 1m 안쪽), 바닥 ~ 1층 높이
    //  1층 창유리가 벽면보다 0.8m 안쪽에 박혀 있어서 0.3m만 안쪽이면 바깥 창틀 홈에 서도 실내로 판정됐음 (실측)
    static readonly Bounds TriggerWorld = new Bounds(new Vector3(-295.1f, 17.0f, 3.2f), new Vector3(82.0f, 3.8f, 60.8f));

    // 벽 콜라이더를 붙일 조각 (메시 오브젝트 이름) / 그중 정문이 걸린 조각
    static readonly string[] WallParts = { "Cube.055_Colore_0", "vetri_Vetro_0", "frames_Colore_0", "Cube.059_Colore_0", "Plane.016_Colore_0", "Cube.061_Colore_0", "Cube.060_Colore_0" };
    static readonly string[] DoorParts = { "Cube.055_Colore_0", "vetri_Vetro_0", "frames_Colore_0" };
    const string FloorPart = "Cube.058_Colore_0";

    [MenuItem("Tools/Map/Make Hospital Enterable")]
    public static void Run()
    {
        GameObject model = null;
        foreach (var g in EditorSceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == ModelRootName) model = g;
        if (model == null)
        {
            // 이미 감싸진 경우 (Buildings/Enterable_scene (3)/scene (3))
            var w = GameObject.Find("Enterable_" + ModelRootName);
            if (w != null) model = w.transform.Find(ModelRootName)?.gameObject;
        }
        if (model == null) { Debug.LogWarning("[Hospital] 병원 모델(scene (3))을 찾지 못했습니다."); return; }

        // 1) 감싸기 (아직 안 됐으면 기존 툴 그대로 실행)
        if (model.GetComponentInParent<BuildingInterior>() == null)
        {
            Selection.activeGameObject = model;
            EditorApplication.ExecuteMenuItem("Tools/Map/Make Selected Building Enterable");
        }
        var interior = model.GetComponentInParent<BuildingInterior>();
        if (interior == null) { Debug.LogWarning("[Hospital] 감싸기 실패"); return; }
        Transform wrapper = interior.transform;

        // 2) 벽 콜라이더
        int walls = 0;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (System.Array.IndexOf(WallParts, mf.name) < 0) continue;
            var wall = mf.GetComponent<BuildingWallCollider>();
            if (wall == null) wall = Undo.AddComponent<BuildingWallCollider>(mf.gameObject);
            Undo.RecordObject(wall, "Hospital walls");
            wall.hasDoor = System.Array.IndexOf(DoorParts, mf.name) >= 0;
            if (wall.hasDoor)
            {
                Bounds local = ToLocalBounds(mf.transform, DoorWorld);
                wall.doorSlotCenter = local.center;
                wall.doorSlotSize = local.size;
            }
            EditorUtility.SetDirty(wall);
            walls++;
        }

        // 3) 바닥
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.name != FloorPart) continue;
            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null) mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
        }

        // 4) 진입 트리거
        BoxCollider trigger = null;
        foreach (var c in wrapper.GetComponents<BoxCollider>()) if (c.isTrigger) trigger = c;
        if (trigger != null)
        {
            Undo.RecordObject(trigger, "Hospital trigger");
            Bounds local = ToLocalBounds(wrapper, TriggerWorld);
            trigger.center = local.center;
            trigger.size = local.size;
        }

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Hospital] ✅ 벽 콜라이더 {walls}개 (정문 구멍 {DoorParts.Length}개 조각), 바닥 콜라이더, 진입 트리거 설정 완료");
    }

    /// <summary>월드 축 정렬 상자를 t의 로컬 축 정렬 상자로 (모델 회전이 90° 단위라 정확히 맞음)</summary>
    static Bounds ToLocalBounds(Transform t, Bounds world)
    {
        Bounds b = new Bounds(t.InverseTransformPoint(world.min), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? world.min.x : world.max.x, (i & 2) == 0 ? world.min.y : world.max.y, (i & 4) == 0 ? world.min.z : world.max.z);
            b.Encapsulate(t.InverseTransformPoint(corner));
        }
        return b;
    }
}

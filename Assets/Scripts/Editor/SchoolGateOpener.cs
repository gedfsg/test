using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 학교 정문 열기 (Tools → Map → Open School Gate). 다시 실행해도 안전 (이미 열려 있으면 건너뜀).
///
/// 문제: 학교 정문(철제 양문, glTF 모델 "scene (2)"의 Component_15_1 / 15_2)이 닫힌 채 세로 철봉마다 MeshCollider가 있어
///       플레이어·좀비가 교내로 못 들어감.
/// 하는 일:
///  1) 원래 문짝 두 개는 끔 (모델 프리팹 안이라 옮기거나 돌릴 수 없음 + 부모가 비균일 축소(0.036×0.056)라
///     그 자리에서 90도 돌리면 문짝 모양이 찌그러짐)
///  2) 같은 모양의 사본을 씬 최상위 "SchoolGate_Open" 아래에 만들고, 경첩(문 양 끝)을 축으로 안쪽(교내 쪽)으로 90도 엶
///     - 안쪽 바닥(현관 단 15.97m)이 문짝 바닥(16.19m)과 맞음. 바깥(인도 15.0m)으로 열면 문짝이 1.2m 떠 보임
///  3) 씬 저장 → 이어서 Tools → Map → Rebake NavMesh With Building Walls (좀비도 정문으로 들어오게)
/// </summary>
public static class SchoolGateOpener
{
    const string SchoolRoot = "scene (2)";
    const string RootName = "SchoolGate_Open";
    const float InsetFromPost = 0.12f; // 경첩을 기둥 안쪽으로 살짝 (문짝이 기둥에 파묻히지 않게)

    [MenuItem("Tools/Map/Open School Gate")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[SchoolGate] Play 중에는 실행할 수 없어요"); return; }
        var school = GameObject.Find(SchoolRoot);
        if (school == null) { Debug.LogError("[SchoolGate] 학교 모델(scene (2))을 못 찾음"); return; }
        if (GameObject.Find(RootName) != null) { Debug.Log("[SchoolGate] 이미 열려 있음 (SchoolGate_Open 있음) - 마당 링크만 다시 만듦"); AddYardLinks(); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); return; }

        Transform left = Find(school.transform, "Component_15_1"), right = Find(school.transform, "Component_15_2");
        if (left == null || right == null) { Debug.LogError("[SchoolGate] 문짝(Component_15_1/15_2)을 못 찾음"); return; }

        Bounds lb = BoundsOf(left), rb = BoundsOf(right);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Open School Gate");

        // 왼쪽 문짝: 경첩 = 서쪽 끝, 문짝이 +X로 뻗음 → -90도면 +Z(교내)로 / 오른쪽: 경첩 = 동쪽 끝, -X로 뻗음 → +90도
        OpenCopy(left, root.transform, new Vector3(lb.min.x + InsetFromPost, lb.center.y, lb.center.z), -90f);
        OpenCopy(right, root.transform, new Vector3(rb.max.x - InsetFromPost, rb.center.y, rb.center.z), 90f);

        Undo.RecordObject(left.gameObject, "Open School Gate"); left.gameObject.SetActive(false);
        Undo.RecordObject(right.gameObject, "Open School Gate"); right.gameObject.SetActive(false);
        AddYardLinks();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SchoolGate] 학교 정문을 안쪽으로 열었어요 (원래 문짝 2개 끔 + 열린 사본). 씬 저장\n→ 좀비도 들어오게: Tools → Map → Rebake NavMesh With Building Walls");
    }

    // 정문 안 현관 길(15.97m)이 양옆 마당 잔디(15.10m)보다 0.87m 높아서 좀비(오르기 0.75m)가 마당으로 못 내려감
    // → 현관 길 양옆 가장자리와 잔디를 잇는 양방향 NavMeshLink (RebakeNavMeshWithWalls의 문 링크와 같은 방식). 다시 실행하면 새로 만듦
    const string LinkRootName = "SchoolYardLinks";
    static readonly float[] LinkZ = { 93f, 101f, 109f };
    const float WalkwayWestX = -283.4f, WalkwayEastX = -274.2f; // 현관 길(Mesh505_Color_E20) 가장자리

    static void AddYardLinks()
    {
        var old = GameObject.Find(LinkRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(LinkRootName);
        Undo.RegisterCreatedObjectUndo(root, "School Yard Links");
        int made = 0;
        foreach (float z in LinkZ)
        {
            made += Link(root.transform, new Vector3(WalkwayWestX + 0.8f, 16f, z), new Vector3(WalkwayWestX - 1.2f, 15.1f, z)) ? 1 : 0;
            made += Link(root.transform, new Vector3(WalkwayEastX - 0.8f, 16f, z), new Vector3(WalkwayEastX + 1.2f, 15.1f, z)) ? 1 : 0;
        }
        Debug.Log($"[SchoolGate] 현관 길 ↔ 마당 잔디 링크 {made}개");
    }

    static bool Link(Transform parent, Vector3 top, Vector3 bottom)
    {
        if (!UnityEngine.AI.NavMesh.SamplePosition(top, out var a, 1f, -1) || !UnityEngine.AI.NavMesh.SamplePosition(bottom, out var b, 1f, -1)) return false;
        var go = new GameObject("YardLink");
        go.transform.SetParent(parent, false);
        var link = go.AddComponent<Unity.AI.Navigation.NavMeshLink>();
        link.startPoint = a.position;   // 부모가 원점이라 로컬 = 월드
        link.endPoint = b.position;
        link.width = 6f;
        link.bidirectional = true;
        return true;
    }

    static void OpenCopy(Transform original, Transform parent, Vector3 hinge, float angle)
    {
        // 부모 축소를 사본 자체 스케일로 옮김 (회전이 축 정렬(0,180,0)이라 lossyScale이 정확)
        var copy = Object.Instantiate(original.gameObject, original.position, original.rotation);
        copy.name = original.name + "_Open";
        copy.transform.localScale = original.lossyScale;
        copy.transform.SetParent(parent, true);
        copy.transform.RotateAround(hinge, Vector3.up, angle);
        copy.SetActive(true);
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static Bounds BoundsOf(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}

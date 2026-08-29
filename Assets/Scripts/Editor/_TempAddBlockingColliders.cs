using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 건물/담장/방호벽/나무 = 부딪힘(충돌체 추가), 신호등/가로등 = 얇은 충돌체,
// 도로/잔디/모래(Zone_*)는 그대로 통과 가능하게 둠.
public static class TempAddBlockingColliders
{
    const float ThinPostSize = 0.6f; // 신호등/가로등 기둥 두께

    [MenuItem("Tools/Map/Add Blocking Colliders")]
    static void Add()
    {
        int blockCount = 0, thinCount = 0, skipped = 0;

        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            string n = go.name.ToLowerInvariant();

            if (n.StartsWith("road-")) { skipped++; continue; }               // 도로는 통과
            if (n.StartsWith("zone_") || n == "groundzones") { skipped++; continue; } // 잔디/모래 구역

            bool isBuilding = n.Contains("building") || n.Contains("house-");
            bool isFence = n.Contains("fence");
            bool isBarrier = n.Contains("barrier"); // road- 접두는 위에서 이미 걸러짐
            bool isTree = n.Contains("tree-");
            bool isThinPost = n.Contains("traffic-light") || n.Contains("light-curved")
                               || n.Contains("light-square") || n.Contains("electricity-pole")
                               || n.Contains("electricity-side");

            if (isBuilding || isFence || isBarrier || isTree)
            {
                if (AddSolidBoxCollider(go, thin: false)) blockCount++;
            }
            else if (isThinPost)
            {
                if (AddSolidBoxCollider(go, thin: true)) thinCount++;
            }
            else
            {
                skipped++;
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ 충돌체 추가 완료: 건물/담장/방호벽/나무 {blockCount}개, 신호등/가로등 {thinCount}개, 대상 아님(통과) {skipped}개");
    }

    static bool AddSolidBoxCollider(GameObject go, bool thin)
    {
        // 이미 막힘용(non-trigger) BoxCollider가 있으면 건너뜀 (재실행해도 안전)
        foreach (var existing in go.GetComponents<BoxCollider>())
            if (!existing.isTrigger) return false;

        Bounds? localBounds = ComputeLocalBounds(go);
        if (localBounds == null) return false;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = false;

        Vector3 size = localBounds.Value.size;
        if (thin)
            size = new Vector3(Mathf.Min(size.x, ThinPostSize), size.y, Mathf.Min(size.z, ThinPostSize));

        box.center = localBounds.Value.center;
        box.size = size;
        return true;
    }

    // 오브젝트의 회전을 고려해 로컬 좌표계 기준 바운드를 계산
    static Bounds? ComputeLocalBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        bool started = false;
        Bounds localBounds = default;
        foreach (var r in renderers)
        {
            Bounds wb = r.bounds;
            Vector3 c = wb.center;
            Vector3 e = wb.extents;
            Vector3[] corners =
            {
                c + new Vector3(-e.x, -e.y, -e.z), c + new Vector3(e.x, -e.y, -e.z),
                c + new Vector3(-e.x, e.y, -e.z),  c + new Vector3(e.x, e.y, -e.z),
                c + new Vector3(-e.x, -e.y, e.z),  c + new Vector3(e.x, -e.y, e.z),
                c + new Vector3(-e.x, e.y, e.z),   c + new Vector3(e.x, e.y, e.z),
            };
            foreach (var corner in corners)
            {
                Vector3 local = go.transform.InverseTransformPoint(corner);
                if (!started) { localBounds = new Bounds(local, Vector3.zero); started = true; }
                else localBounds.Encapsulate(local);
            }
        }
        return started ? localBounds : (Bounds?)null;
    }
}

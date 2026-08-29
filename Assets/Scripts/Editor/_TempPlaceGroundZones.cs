using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 도로가 몰려있는 구역과 강가에 모래색 Plane을 깔아준다.
public static class TempPlaceGroundZones
{
    const float SandY = 0.05f;
    const float ZoneMargin = 5f;      // 도로 구역 바깥 여유
    const float ClusterDistance = 15f; // 이 거리 안이면 같은 구역으로 묶음
    static readonly Color CityZoneColor = new Color(0.788f, 0.659f, 0.463f); // #C9A876
    static readonly Color RiverZoneColor = new Color(0.910f, 0.831f, 0.627f); // #E8D4A0
    const float RiverSandWidth = 10f;

    static Terrain groundTerrain;

    [MenuItem("Tools/Map/Place Ground Zones (Sand)")]
    static void Place()
    {
        GameObject parent = GameObject.Find("GroundZones");
        if (parent == null) parent = new GameObject("GroundZones");

        GameObject groundGo = GameObject.Find("Ground");
        groundTerrain = groundGo != null ? groundGo.GetComponent<Terrain>() : null;

        PlaceCityZones(parent.transform);
        PlaceRiverSand(parent.transform);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("✅ GroundZones 배치 완료.");
    }

    // 해당 XZ 위치의 실제 Terrain 표면 높이 + 여유값 (지형 굴곡에 파묻히지 않게)
    static float SandYAt(float worldX, float worldZ)
    {
        if (groundTerrain == null) return SandY;
        float terrainSurfaceY = groundTerrain.SampleHeight(new Vector3(worldX, 0f, worldZ))
            + groundTerrain.transform.position.y;
        return terrainSurfaceY + SandY;
    }

    // ── 도로 구역 클러스터링 + 배치 ──────────────────────
    static void PlaceCityZones(Transform parent)
    {
        List<GameObject> roads = FindByNamePrefix("road-");
        if (roads.Count == 0)
        {
            Debug.LogWarning("도로 오브젝트를 찾지 못했습니다 (이름에 'road-' 포함된 오브젝트 없음).");
            return;
        }

        List<List<GameObject>> clusters = Cluster(roads, ClusterDistance);
        clusters = clusters.OrderBy(c => AverageX(c)).ToList();

        string[] leftRightNames = { "Zone_City_Left", "Zone_City_Right" };
        for (int i = 0; i < clusters.Count; i++)
        {
            Bounds b = EncapsulateRenderers(clusters[i]);
            b.Expand(ZoneMargin * 2f); // Expand는 양쪽에 절반씩 적용되므로 *2

            string zoneName = clusters.Count == 2 ? leftRightNames[i] : $"Zone_City_{i + 1}";
            float y = SandYAt(b.center.x, b.center.z);
            CreateSandPlane(zoneName, parent, new Vector3(b.center.x, y, b.center.z),
                new Vector2(b.size.x, b.size.z), CityZoneColor);
        }
    }

    // ── 강가 모래 띠 배치 ────────────────────────────────
    static void PlaceRiverSand(Transform parent)
    {
        GameObject river = GameObject.Find("River");
        if (river == null) river = FindByNamePrefix("WaterBlock").FirstOrDefault();
        if (river == null)
        {
            Debug.LogWarning("강(River/WaterBlock) 오브젝트를 찾지 못했습니다.");
            return;
        }

        Bounds b = EncapsulateRenderers(new List<GameObject> { river });
        bool runsAlongX = b.size.x >= b.size.z;

        if (runsAlongX)
        {
            // 강이 X축 방향으로 뻗어있음 → 북/남(Z+/Z-) 양옆에 모래 띠
            float length = b.size.x + ZoneMargin * 2f;
            float northZ = b.max.z + RiverSandWidth / 2f;
            float southZ = b.min.z - RiverSandWidth / 2f;
            CreateSandPlane("Zone_River_Sand_North", parent,
                new Vector3(b.center.x, SandYAt(b.center.x, northZ), northZ),
                new Vector2(length, RiverSandWidth), RiverZoneColor);
            CreateSandPlane("Zone_River_Sand_South", parent,
                new Vector3(b.center.x, SandYAt(b.center.x, southZ), southZ),
                new Vector2(length, RiverSandWidth), RiverZoneColor);
        }
        else
        {
            // 강이 Z축 방향으로 뻗어있음 → 동/서(X+/X-) 양옆에 모래 띠
            float length = b.size.z + ZoneMargin * 2f;
            float eastX = b.max.x + RiverSandWidth / 2f;
            float westX = b.min.x - RiverSandWidth / 2f;
            CreateSandPlane("Zone_River_Sand_East", parent,
                new Vector3(eastX, SandYAt(eastX, b.center.z), b.center.z),
                new Vector2(RiverSandWidth, length), RiverZoneColor);
            CreateSandPlane("Zone_River_Sand_West", parent,
                new Vector3(westX, SandYAt(westX, b.center.z), b.center.z),
                new Vector2(RiverSandWidth, length), RiverZoneColor);
        }
    }

    // ── 공용 유틸 ───────────────────────────────────────
    static List<GameObject> FindByNamePrefix(string prefix)
    {
        List<GameObject> result = new List<GameObject>();
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name.Contains(prefix)) result.Add(root);
        }
        return result;
    }

    static float AverageX(List<GameObject> group) => group.Average(g => g.transform.position.x);

    static Bounds EncapsulateRenderers(List<GameObject> objs)
    {
        Bounds b = default;
        bool started = false;
        foreach (var go in objs)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!started) { b = r.bounds; started = true; }
                else b.Encapsulate(r.bounds);
            }
        }
        if (!started)
        {
            // 렌더러가 없으면 위치만이라도 사용
            foreach (var go in objs)
            {
                if (!started) { b = new Bounds(go.transform.position, Vector3.zero); started = true; }
                else b.Encapsulate(go.transform.position);
            }
        }
        return b;
    }

    static List<List<GameObject>> Cluster(List<GameObject> objs, float distance)
    {
        int n = objs.Count;
        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int x) => parent[x] == x ? x : (parent[x] = Find(parent[x]));
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            float d = Vector3.Distance(
                new Vector3(objs[i].transform.position.x, 0, objs[i].transform.position.z),
                new Vector3(objs[j].transform.position.x, 0, objs[j].transform.position.z));
            if (d <= distance) Union(i, j);
        }

        Dictionary<int, List<GameObject>> groups = new Dictionary<int, List<GameObject>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out var list)) groups[root] = list = new List<GameObject>();
            list.Add(objs[i]);
        }
        return groups.Values.ToList();
    }

    static void CreateSandPlane(string name, Transform parent, Vector3 worldPos, Vector2 sizeXZ, Color color)
    {
        GameObject existing = parent.Find(name)?.gameObject;
        if (existing != null) Object.DestroyImmediate(existing);

        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = name;
        plane.transform.SetParent(parent, false);
        plane.transform.position = worldPos;
        // 기본 Plane은 10x10 유닛
        plane.transform.localScale = new Vector3(sizeXZ.x / 10f, 1f, sizeXZ.y / 10f);

        Object.DestroyImmediate(plane.GetComponent<Collider>());

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        plane.GetComponent<MeshRenderer>().sharedMaterial = mat;

        string matPath = $"Assets/Materials/{name}Material.mat";
        AssetDatabase.DeleteAsset(matPath); // 재실행 시 이전 머티리얼 정리
        AssetDatabase.CreateAsset(mat, matPath);
    }
}

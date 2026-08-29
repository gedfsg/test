using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 일회성 스크립트 - 확인 후 삭제할 것
// 씬 루트에 흩어진 오브젝트들을 카테고리별 부모(Roads/Buildings/Water/Props/
// Nature/Zones/SketchfabAssets)로 정리. 월드 위치는 그대로 유지됨.
public static class TempOrganizeHierarchy
{
    [MenuItem("Tools/Map/Organize Hierarchy")]
    static void Organize()
    {
        Transform roads = GetOrCreateParent("Roads");
        Transform buildings = GetOrCreateParent("Buildings");
        Transform water = GetOrCreateParent("Water");
        Transform props = GetOrCreateParent("Props");
        Transform nature = GetOrCreateParent("Nature");
        Transform sketchfab = GetOrCreateParent("SketchfabAssets");

        // Zones: 기존 GroundZones가 있으면 이름만 Zones로 바꿔서 재사용, 없으면 새로 생성
        GameObject groundZones = GameObject.Find("GroundZones");
        Transform zones;
        if (groundZones != null)
        {
            groundZones.name = "Zones";
            zones = groundZones.transform;
        }
        else
        {
            zones = GetOrCreateParent("Zones");
        }

        HashSet<GameObject> parents = new HashSet<GameObject>
        {
            roads.gameObject, buildings.gameObject, water.gameObject,
            props.gameObject, nature.gameObject, zones.gameObject, sketchfab.gameObject,
        };

        // 재부모화하면서 루트 목록이 바뀌므로 미리 스냅샷을 떠둠
        List<GameObject> roots = new List<GameObject>(EditorSceneManager.GetActiveScene().GetRootGameObjects());

        int moved = 0;
        foreach (GameObject go in roots)
        {
            if (parents.Contains(go)) continue;

            Transform target = Categorize(go.name.ToLowerInvariant(), roads, buildings, water, props, nature, zones);
            if (target == null) continue;

            go.transform.SetParent(target, true); // worldPositionStays: 위치 그대로 유지
            moved++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"✅ Hierarchy 정리 완료: {moved}개 오브젝트를 카테고리 부모로 이동.");
    }

    static Transform Categorize(string n, Transform roads, Transform buildings, Transform water,
        Transform props, Transform nature, Transform zones)
    {
        if (n.StartsWith("road-")) return roads;
        if (n.Contains("building") || n.StartsWith("house-")) return buildings;
        if (n.Contains("waterblock") || n.Contains("water")) return water;
        if (n.Contains("tree-") || n.Contains("bush") || n.Contains("shrub")) return nature;
        if (n == "groundzones" || n.StartsWith("zone_")) return zones;
        if (IsPropKeyword(n)) return props;
        return null;
    }

    static bool IsPropKeyword(string n)
    {
        string[] keywords =
        {
            "traffic-light", "light-curved", "light-square", "electricity-pole",
            "electricity-side", "electricity-wires", "sign-highway", "construction-",
            "dumpster", "bridge-pillar", "driveway-", "path-", "planter",
            "detail-awning", "detail-overhang", "detail-parasol", "tile-", "fence",
        };
        foreach (var k in keywords)
            if (n.Contains(k)) return true;
        return false;
    }

    static Transform GetOrCreateParent(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);
        return go.transform;
    }
}

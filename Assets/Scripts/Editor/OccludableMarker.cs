using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools/Utility/Mark Occludable Objects (2026-11-18) - Fade 방식 가림 처리(OccluderFader)용 레이어 일괄 지정.
///  1) "Occludable" 레이어가 없으면 빈 칸에 만듦
///  2) 지정 대상
///     - Buildings 아래 건물 전부 (지붕·벽·외벽·내벽 = 건물 오브젝트와 자식 전체, 실내 트리거 포함)
///     - 큰 Prop: 높이 1.2m 이상인 메시 (나무·울타리·간판·자판기·바리케이드·풍차·트럭·차·바위·가로등 등)
///  3) 제외: 지면(이름에 ground/terrain/road/grass/plane/landscape/floor/asphalt… 또는 넓고 납작한 메시), 물,
///     플레이어·좀비·떨어진 아이템·상자·UI·투사체·이펙트(기존 레이어가 Default가 아닌 것은 안 건드림)
///  4) 씬에 OccluderFader가 없으면 만들고, PlayerXRay는 enabled=false
/// 다시 실행해도 결과 같음. 끝나면 씬 저장.
/// </summary>
public static class OccludableMarker
{
    const float MinPropHeight = 1.2f;
    // 실내(BuildingInterior)가 없고 바닥 넓이가 이보다 큰 건물 = 땅까지 한 메시로 붙은 농장 등 → 통째로 반투명해지면 땅이 사라져 배경색이 보임 (2026-11-18 Play)
    const float MaxPlainBuildingFootprint = 40f;

    static readonly string[] GroundWords =
    {
        "ground", "terrain", "road", "grass", "plane", "landscape", "landskape", "floor", "asphalt", "walking",
        "sidewalk", "water", "river", "lake", "mapguide", "zone", "cutregion", "seperator", "line", "crossing",
        "bridge", "shadow", "fog", "decal",
    };

    // 이런 컴포넌트가 붙은 오브젝트(또는 부모)는 가림 대상이 아님
    static readonly string[] ExcludeComponents =
    {
        "PlayerController", "ZombieController", "PickupItem", "LootCrate", "Health", "Weapon", "Projectile",
        "Grenade", "Molotov", "ParticleSystem", "Canvas", "ZombieSpawner", "Trader",
    };

    [MenuItem("Tools/Utility/Mark Occludable Objects")]
    public static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Occludable] Play 중에는 실행 안 함"); return; }
        int layer = EnsureLayer(OccluderFader.LayerName);
        if (layer < 0) { Debug.LogError("[Occludable] 레이어를 만들 빈 칸이 없음"); return; }

        var scene = EditorSceneManager.GetActiveScene();
        int buildings = 0, props = 0, objs = 0;
        var marked = new HashSet<GameObject>();
        var noCollider = new List<string>();
        var skippedBig = new List<string>();

        // ── 건물: Buildings 아래 직계 자식 = 건물 하나 → 자식 전체 ──
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "Buildings") continue;
            foreach (Transform b in root.transform)
            {
                if (IsExcluded(b)) continue;
                if (b.GetComponentInChildren<BuildingInterior>(true) == null && Footprint(b) >= MaxPlainBuildingFootprint)
                {
                    foreach (var t in b.GetComponentsInChildren<Transform>(true)) if (t.gameObject.layer == layer) Set(t.gameObject, 0);
                    skippedBig.Add(b.name);
                    continue;
                }
                buildings++;
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                {
                    if (t.gameObject.layer != 0 && t.gameObject.layer != layer) continue; // 다른 레이어(이펙트·UI 등)는 그대로
                    if (IsExcluded(t)) continue;
                    if (Set(t.gameObject, layer)) objs++;
                    marked.Add(t.gameObject);
                }
                if (b.GetComponentInChildren<Collider>(true) == null) noCollider.Add(b.name);
            }
        }

        // ── 큰 Prop: 씬 전체 MeshRenderer 중 높이 1.2m 이상 + 지면 아님 ──
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "Buildings" || root.GetComponent<Canvas>() != null) continue;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var go = r.gameObject;
                if (marked.Contains(go)) continue;
                if (Mathf.Max(r.bounds.size.x, r.bounds.size.z) >= MaxPlainBuildingFootprint) { if (go.layer == layer) Set(go, 0); continue; } // 땅째 붙은 큰 메시
                if (go.layer != 0 && go.layer != layer) continue;
                if (IsExcluded(go.transform) || IsGroundLike(r)) { if (go.layer == layer) Set(go, 0); continue; }
                if (r.bounds.size.y < MinPropHeight) { if (go.layer == layer) Set(go, 0); continue; }
                props++;
                if (Set(go, layer)) objs++;
                marked.Add(go);
                // 콜라이더가 자식(예: 나무 줄기 캡슐)에 따로 있으면 같이
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                    if (c.gameObject.layer == 0 && Set(c.gameObject, layer)) objs++;
                if (go.GetComponentInChildren<Collider>(true) == null && go.GetComponentInParent<Collider>(true) == null) noCollider.Add(go.name);
            }
        }

        // ── 매니저 + 예전 X-Ray 끄기 ──
        var fader = Object.FindFirstObjectByType<OccluderFader>(FindObjectsInactive.Include);
        if (fader == null)
        {
            var go = new GameObject("OccluderFader");
            fader = go.AddComponent<OccluderFader>();
            Undo.RegisterCreatedObjectUndo(go, "OccluderFader");
        }
        foreach (var x in Object.FindObjectsByType<PlayerXRay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (x.enabled) { Undo.RecordObject(x, "Disable PlayerXRay"); x.enabled = false; EditorUtility.SetDirty(x); }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var sb = new StringBuilder();
        sb.AppendLine($"[Occludable] 레이어 {layer} - 건물 {buildings}채 / 큰 Prop {props}개 / 레이어 바뀐 오브젝트 {objs}개");
        if (skippedBig.Count > 0) sb.AppendLine("  땅째 붙은 큰 건물(제외): " + string.Join(", ", skippedBig));
        if (noCollider.Count > 0)
            sb.AppendLine($"  콜라이더 없음(가려도 페이드 안 됨) {noCollider.Count}개: " + string.Join(", ", noCollider.GetRange(0, Mathf.Min(40, noCollider.Count))));
        Debug.Log(sb.ToString());
    }

    static float Footprint(Transform t)
    {
        bool any = false; Bounds b = default;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        return any ? Mathf.Max(b.size.x, b.size.z) : 0f;
    }

    static bool Set(GameObject go, int layer)
    {
        if (go.layer == layer) return false;
        Undo.RecordObject(go, "Mark Occludable");
        go.layer = layer;
        EditorUtility.SetDirty(go);
        return true;
    }

    static bool IsExcluded(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            int l = p.gameObject.layer;
            string ln = LayerMask.LayerToName(l);
            if (ln == "Player" || ln == "Pickup" || ln == "UI" || ln == "Ground" || ln == "Water") return true;
            if (p.CompareTag("Player")) return true;
            foreach (var c in p.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                foreach (var ex in ExcludeComponents) if (n == ex) return true;
            }
            if (p.GetComponent<Terrain>() != null) return true;
        }
        return false;
    }

    static bool IsGroundLike(Renderer r)
    {
        string n = r.name.ToLowerInvariant();
        foreach (var w in GroundWords) if (n.Contains(w)) return true;
        if (r.sharedMaterial != null)
        {
            string mn = r.sharedMaterial.name.ToLowerInvariant();
            if (mn.Contains("ground") || mn.Contains("grass") || mn.Contains("terrain") || mn.Contains("water")) return true;
        }
        // 넓고 납작한 메시 = 언덕·지면 조각 (예: 숲 FBX 바닥)
        var s = r.bounds.size;
        float foot = Mathf.Max(s.x, s.z);
        if (foot > 12f && s.y < foot * 0.35f) return true;
        return false;
    }

    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            var sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tm.ApplyModifiedProperties();
                return i;
            }
        }
        return -1;
    }
}

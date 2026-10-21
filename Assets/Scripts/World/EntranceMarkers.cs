using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 주요 건물 입구 색상 구분: 경찰서(파랑) / 병원(빨강) / 마트(노랑) / 교회(흰색).
/// 문(BuildingWallCollider.doorSlot) 바깥 바닥에 반투명 색 매트 + 문 위 색 조명을 깔아서
/// 탑뷰에서도 "어느 건물의 입구인지" 멀리서 알아볼 수 있게 함. 일반 집/창고는 표시 안 함.
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성 (LootWorldSpawner와 같은 방식).
/// </summary>
public class EntranceMarkers : MonoBehaviour
{
    const float MatWidth = 3.2f, MatDepth = 2.2f; // 매트 크기 (문 폭 방향 × 바깥 방향, m)
    const float MatOffset = 1.4f;                 // 문에서 바깥으로 (m)

    static readonly Dictionary<LootDatabase.BuildingKind, Color> Colors = new Dictionary<LootDatabase.BuildingKind, Color>
    {
        { LootDatabase.BuildingKind.Police,   new Color(0.2f, 0.45f, 1f) },
        { LootDatabase.BuildingKind.Hospital, new Color(1f, 0.18f, 0.18f) },
        { LootDatabase.BuildingKind.Mart,     new Color(1f, 0.85f, 0.15f) },
        { LootDatabase.BuildingKind.Church,   new Color(1f, 1f, 1f) },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (GameObject.FindWithTag("Player") == null || FindFirstObjectByType<EntranceMarkers>() != null) return;
        new GameObject("EntranceMarkers").AddComponent<EntranceMarkers>();
    }

    readonly List<Material> mats = new List<Material>();

    void Start()
    {
        int placed = 0;
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            if (!Colors.TryGetValue(LootDatabase.Classify(b), out Color color)) continue;
            var kind = LootDatabase.Classify(b);
            var trig = LootWorldSpawner.FindTrigger(b);
            // 문 찾기: 건물 자식의 벽 콜라이더 중 문 있는 것 전부 (병원은 정문이 벽 조각 3개에 걸쳐 있음) → 문 중심 평균
            var doors = DoorCenters(b.GetComponentsInChildren<BuildingWallCollider>(true));
            if (doors.Count == 0 && b.transform.parent != null) doors = DoorCenters(b.transform.parent.GetComponentsInChildren<BuildingWallCollider>(true));
            if (trig == null || doors.Count == 0) { Debug.LogWarning($"[EntranceMarkers] '{b.name}' 문을 못 찾음 - 입구 표시 생략"); continue; }
            Vector3 door = Vector3.zero;
            foreach (var d in doors) door += d;
            door /= doors.Count;

            // 바깥 방향: 실내 트리거 중심 → 문 (건물 축에 맞춰 정렬). 병원은 트리거가 현관 마당까지 덮어서 이 방법이 대각선이 됨
            // → HospitalEnterableSetup의 정문 구멍(바깥 3.8m / 안쪽 5m, x -310.5~-301.5) 기준 바깥 = +X 로 고정
            Vector3 outDir = kind == LootDatabase.BuildingKind.Hospital ? Vector3.right : SnapToAxis(door - trig.bounds.center, b.transform);
            if (outDir.sqrMagnitude < 0.01f) continue;
            PlaceMarker(b, door, outDir, color, trig.bounds.min.y);
            placed++;
        }
        Debug.Log($"[EntranceMarkers] 입구 표시 {placed}곳");
    }

    void PlaceMarker(BuildingInterior building, Vector3 door, Vector3 outDir, Color color, float floorY)
    {
        Vector3 pos = door + outDir * MatOffset;
        // +0.3m: 바닥 위에 콜라이더 없는 장식 지면판(Zone_City, 바닥 +0.2m)이 깔려 있어서 그보다 위로 (낮으면 가려서 안 보임)
        pos.y = GroundY(pos, floorY, door.y) + 0.3f;

        var root = new GameObject("Entrance_" + building.name);
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(outDir));

        // 바닥 매트 (조명 영향 없는 반투명 색, 콜라이더 없음). 지붕/처마에 가려도 연하게 보이는 2패스 재질 (Custom/EntranceMarker)
        var mat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mat.name = "Mat";
        Destroy(mat.GetComponent<Collider>());
        mat.transform.SetParent(root.transform, false);
        mat.transform.localScale = new Vector3(MatWidth, 0.02f, MatDepth);
        var r = mat.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sharedMaterial = new Material(MarkerMaterial) { color = new Color(color.r, color.g, color.b, 0.45f) };
        mats.Add(r.sharedMaterial);

        // 문 위 조명 (밤에도 입구가 그 색으로 비침)
        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 6f;
        l.intensity = 1.6f;
        l.shadows = LightShadows.None;
    }

    // Resources 재질로 셰이더를 빌드에 포함 (BuildingInterior의 마스크 재질과 같은 방식). 없으면 셰이더 직접 → 최후엔 Sprites/Default
    static Material markerMat;
    static Material MarkerMaterial
    {
        get
        {
            if (markerMat != null) return markerMat;
            markerMat = Resources.Load<Material>("EntranceMarker");
            if (markerMat == null)
            {
                var sh = Shader.Find("Custom/EntranceMarker");
                markerMat = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
            }
            return markerMat;
        }
    }

    static List<Vector3> DoorCenters(BuildingWallCollider[] walls)
    {
        var list = new List<Vector3>();
        foreach (var w in walls) if (w.hasDoor) list.Add(w.transform.TransformPoint(w.doorSlotCenter));
        return list;
    }

    /// <summary>v(수평)를 건물의 ±right / ±forward 중 가장 가까운 축으로 (매트가 벽과 나란히 놓이게)</summary>
    static Vector3 SnapToAxis(Vector3 v, Transform building)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.01f) return Vector3.zero;
        Vector3 best = Vector3.zero; float bestDot = -2f;
        foreach (var a in new[] { building.right, -building.right, building.forward, -building.forward })
        {
            Vector3 f = a; f.y = 0f; f.Normalize();
            float d = Vector3.Dot(f, v.normalized);
            if (d > bestDot) { bestDot = d; best = f; }
        }
        return best;
    }

    // 문 바깥 바닥 높이: 지형 높이 / 건물 트리거 바닥 / 문 높이(차양 아래)에서 아래로 쏜 레이에 맞은 바닥(병원 현관 바닥 등) 중 가장 높은 곳
    static float GroundY(Vector3 p, float floorY, float doorY)
    {
        var t = Terrain.activeTerrain;
        float g = t != null ? t.SampleHeight(p) + t.transform.position.y : floorY;
        g = Mathf.Max(g, floorY);
        if (Physics.Raycast(new Vector3(p.x, doorY, p.z), Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            g = Mathf.Max(g, hit.point.y);
        return g;
    }

    void Update()
    {
        // 은은하게 숨쉬듯 밝기 변화 (눈에 띄게)
        float a = 0.38f + Mathf.Sin(Time.time * 2f) * 0.1f;
        foreach (var m in mats) { var c = m.color; c.a = a; m.color = c; }
    }
}

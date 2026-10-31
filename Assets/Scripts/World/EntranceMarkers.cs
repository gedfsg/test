using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 건물 입구 표시: 경찰서(파랑) / 병원(빨강) / 마트(노랑) / 교회(흰색) / 그 외(연한 회색).
/// 문(BuildingWallCollider.doorSlot) 바깥 바닥에 반투명 매트 + 문 위 이름 라벨을 깔아서 탑뷰에서도 입구를 찾을 수 있게 함.
/// 주요 건물은 문 위 색 조명도 켬 (건물 100채에 전부 켜면 조명이 너무 많음).
/// 실내에 있으면 그 건물 문 안쪽에 연한 출구 매트가 보임.
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성 (LootWorldSpawner와 같은 방식).
/// </summary>
public class EntranceMarkers : MonoBehaviour
{
    const float MatWidth = 3.2f, MatDepth = 2.2f; // 매트 크기 (문 폭 방향 × 바깥 방향, m)
    const float MatOffset = 1.4f;                 // 문에서 바깥으로 (m)
    const float ExitMatAlpha = 0.3f;              // 실내 출구 매트 (더 연하게)
    const float LabelHeight = 3.4f;               // 라벨 높이 = 지면 + 문 위쪽 2.9m + 0.5m
    const float LabelFontSize = 3f;               // 예전 MARKET 네온(자동 크기 최대 약 6)의 절반
    static readonly Color NeutralColor = new Color(0.85f, 0.85f, 0.85f);

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
    readonly Dictionary<BuildingInterior, GameObject> exitMats = new Dictionary<BuildingInterior, GameObject>();
    BuildingInterior shownExit;

    void Start()
    {
        int placed = 0;
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            var kind = LootDatabase.Classify(b);
            bool landmark = Colors.TryGetValue(kind, out Color color);
            if (!landmark) color = NeutralColor;
            var trig = LootWorldSpawner.FindTrigger(b);
            if (!TryGetDoor(b, out Vector3 door, out Vector3 outDir)) { Debug.LogWarning($"[EntranceMarkers] '{b.name}' 문을 못 찾음 - 입구 표시 생략"); continue; }
            PlaceMarker(b, door, outDir, color, trig.bounds.min.y, landmark);
            PlaceExitMat(b, door, outDir, color, trig);
            PlaceLabel(b, door, outDir, trig.bounds.min.y, MissionManager.BuildingLabel(kind));
            placed++;
        }
        Debug.Log($"[EntranceMarkers] 입구 표시 {placed}곳");
    }

    void PlaceMarker(BuildingInterior building, Vector3 door, Vector3 outDir, Color color, float floorY, bool withLight)
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

        if (!withLight) return;

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

    // 실내 출구 매트: 문 안쪽 트리거 안 바닥. 그 건물에 들어가 있을 때만 켬 (2패스 재질이라 켜두면 밖에서 지붕 너머로 비침)
    void PlaceExitMat(BuildingInterior building, Vector3 door, Vector3 outDir, Color color, Collider trig)
    {
        Vector3 pos = door;
        for (int i = 0; i < 40 && !InsideXZ(trig.bounds, pos); i++) pos -= outDir * 0.5f; // 문이 트리거 밖이면 들어올 때까지
        pos -= outDir * 1.2f;
        float top = Mathf.Max(trig.bounds.min.y, GroundY(pos, trig.bounds.min.y, door.y)) + 1.5f;
        pos.y = Physics.Raycast(new Vector3(pos.x, top, pos.z), Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : top - 1.5f;
        pos.y += 0.05f;

        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "Exit_" + building.name;
        Destroy(root.GetComponent<Collider>());
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(outDir));
        root.transform.localScale = new Vector3(MatWidth, 0.02f, MatDepth);
        var r = root.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sharedMaterial = new Material(MarkerMaterial) { color = new Color(color.r, color.g, color.b, ExitMatAlpha) };
        root.SetActive(false);
        exitMats[building] = root;
    }

    static bool InsideXZ(Bounds b, Vector3 p) => p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;

    // 건물 이름 라벨: 문 위, 흰 글자 + 반투명 배경, 항상 카메라를 향함 (FaceCamera)
    void PlaceLabel(BuildingInterior building, Vector3 door, Vector3 outDir, float floorY, string text)
    {
        Vector3 ground = door + outDir * 0.35f;
        ground.y = GroundY(ground, floorY, door.y);

        var go = new GameObject("Label_" + building.name);
        go.transform.SetParent(transform, false);
        go.transform.position = ground + Vector3.up * LabelHeight;
        go.AddComponent<FaceCamera>();

        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        tmp.font = HudStyle.SemiBold;
        tmp.text = text;
        tmp.fontSize = LabelFontSize;
        tmp.color = Color.white;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 1f);
        var textRenderer = go.GetComponent<MeshRenderer>();
        textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 반투명 배경 (글자 폭에 맞춤)
        Vector2 size = tmp.GetPreferredValues(text);
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        Destroy(bg.GetComponent<Collider>());
        bg.transform.SetParent(go.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        bg.transform.localScale = new Vector3(size.x + 0.5f, size.y + 0.15f, 1f);
        var bgRenderer = bg.GetComponent<Renderer>();
        bgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bgRenderer.sharedMaterial = LabelBackground;
    }

    static Material labelBackground;
    static Material LabelBackground => labelBackground != null ? labelBackground
        : (labelBackground = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0.5f), renderQueue = 2990 });

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

    /// <summary>건물 문 중심(여러 벽 조각에 걸치면 평균)과 바깥 방향. 문/트리거가 없으면 false</summary>
    public static bool TryGetDoor(BuildingInterior b, out Vector3 door, out Vector3 outDir)
    {
        door = outDir = Vector3.zero;
        var trig = LootWorldSpawner.FindTrigger(b);
        // 문 찾기: 건물 자식의 벽 콜라이더 중 문 있는 것 전부 (병원은 정문이 벽 조각 3개에 걸쳐 있음) → 문 중심 평균
        var doors = DoorCenters(b.GetComponentsInChildren<BuildingWallCollider>(true));
        if (doors.Count == 0 && b.transform.parent != null) doors = DoorCenters(b.transform.parent.GetComponentsInChildren<BuildingWallCollider>(true));
        if (trig == null || doors.Count == 0) return false;
        foreach (var d in doors) door += d;
        door /= doors.Count;

        // 바깥 방향: 실내 트리거 중심 → 문 (건물 축에 맞춰 정렬). 병원은 트리거가 현관 마당까지 덮어서 이 방법이 대각선이 됨
        // → HospitalEnterableSetup의 정문 구멍(바깥 3.8m / 안쪽 5m, x -310.5~-301.5) 기준 바깥 = +X 로 고정
        outDir = LootDatabase.Classify(b) == LootDatabase.BuildingKind.Hospital ? Vector3.right : SnapToAxis(door - trig.bounds.center, b.transform);
        return outDir.sqrMagnitude > 0.01f;
    }

    internal static List<Vector3> DoorCenters(BuildingWallCollider[] walls) // NeonSigns도 사용
    {
        var list = new List<Vector3>();
        foreach (var w in walls) if (w.hasDoor) list.Add(w.transform.TransformPoint(w.doorSlotCenter));
        return list;
    }

    /// <summary>v(수평)를 건물의 ±right / ±forward 중 가장 가까운 축으로 (매트가 벽과 나란히 놓이게)</summary>
    internal static Vector3 SnapToAxis(Vector3 v, Transform building)
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

        // 출구 매트: 지금 들어가 있는 건물 것만
        BuildingInterior now = BuildingInterior.Current;
        if (now == shownExit) return;
        if (shownExit != null && exitMats.TryGetValue(shownExit, out GameObject prev)) prev.SetActive(false);
        if (now != null && exitMats.TryGetValue(now, out GameObject next)) next.SetActive(true);
        shownExit = now;
    }
}

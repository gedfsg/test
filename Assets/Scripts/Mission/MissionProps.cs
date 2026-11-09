using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 미션 오브젝트 모양 (기본 도형으로 만든 간단한 모형). 모두 groundPos(바닥)에 놓이고 root를 반환.
///  - 라디오 타워 / 공원 (MissionPlaces가 게임 시작 시 1번)
///  - 비상 무전기 / 발전기 / 무기고 사물함 / 연료통 / 바리케이드 자재·완성 / 정찰 깃발 / 좀비 떼 지역 표시 (MissionManager가 그날 메인 미션에 맞춰)
/// </summary>
public static class MissionProps
{
    // ── 공용 ──
    static Material Mat(Color c, float emission = 0f)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emission); }
        return m;
    }

    static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider = true, Vector3? euler = null)
        => Prim(PrimitiveType.Cube, parent, name, localPos, size, mat, collider, euler);

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider, Vector3? euler = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Transform Root(string name, Vector3 groundPos, float yaw = 0f)
    {
        var root = new GameObject(name).transform;
        root.SetPositionAndRotation(groundPos, Quaternion.Euler(0f, yaw, 0f));
        return root;
    }

    /// <summary>점 조명 (표시등)</summary>
    public static Light Beacon(Transform parent, Vector3 localPos, Color c, float range = 5f)
    {
        var go = new GameObject("Beacon");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.range = range;
        l.intensity = 1.5f;
        l.shadows = LightShadows.None;
        return l;
    }

    /// <summary>건물 라벨과 같은 모양의 이름표 (흰 글자 + 반투명 배경, 카메라를 향함)</summary>
    public static void Label(Transform parent, string text, float height)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * height;
        go.AddComponent<FaceCamera>();
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = HudStyle.SemiBold;
        tmp.text = text;
        tmp.fontSize = 3f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 1f);
        tmp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Vector2 size = tmp.GetPreferredValues(text);
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        Object.Destroy(bg.GetComponent<Collider>());
        bg.transform.SetParent(go.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        bg.transform.localScale = new Vector3(size.x + 0.5f, size.y + 0.15f, 1f);
        var r = bg.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0.5f), renderQueue = 2990 };
    }

    /// <summary>바닥 원 (조명 영향 없는 반투명). 장식 지면판(+0.2m)에 가리지 않게 +0.3m</summary>
    public static GameObject GroundRing(Transform parent, float radius, Color c)
    {
        var ring = Prim(PrimitiveType.Cylinder, parent, "Ring", Vector3.up * 0.3f, new Vector3(radius * 2f, 0.01f, radius * 2f),
            new Material(Shader.Find("Sprites/Default")) { color = c }, false);
        ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ring;
    }

    // 플레이어/좀비가 통과하지 않게 (런타임 오브젝트라 NavMesh 굽기엔 안 들어감 → 장애물로 구멍)
    static void Obstacle(GameObject go, Vector3 center, Vector3 size)
    {
        var o = go.AddComponent<NavMeshObstacle>();
        o.shape = NavMeshObstacleShape.Box;
        o.center = center;
        o.size = size;
        o.carving = true;
    }

    // ── 장소 ──

    /// <summary>라디오 타워: 높은 철탑 + 가로대 + 꼭대기 빨간 경고등 + 아래 통신실</summary>
    public static Transform RadioTower(Vector3 groundPos)
    {
        var root = Root("RadioTower", groundPos, Random.Range(0f, 360f));
        var steel = Mat(new Color(0.35f, 0.36f, 0.38f));
        // 4개 다리가 위로 모이는 철탑
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            Vector3 foot = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 1.6f);
            var leg = Box(root, "Leg" + i, foot * 0.5f + Vector3.up * 9f, new Vector3(0.18f, 18.2f, 0.18f), steel, false);
            leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up * 18f - foot).normalized);
        }
        for (int i = 1; i <= 5; i++)
        {
            float h = i * 3f, w = Mathf.Lerp(2.2f, 0.4f, h / 18f);
            Box(root, "Bar" + i, Vector3.up * h, new Vector3(w, 0.1f, 0.1f), steel, false);
            Box(root, "BarZ" + i, Vector3.up * h, new Vector3(0.1f, 0.1f, w), steel, false);
        }
        Box(root, "Mast", Vector3.up * 20f, new Vector3(0.12f, 4f, 0.12f), steel, false);
        Prim(PrimitiveType.Sphere, root, "WarningLamp", Vector3.up * 22.1f, Vector3.one * 0.45f, Mat(new Color(1f, 0.1f, 0.05f), 3f), false);
        Beacon(root, Vector3.up * 22f, new Color(1f, 0.15f, 0.1f), 10f);
        // 통신실 (밟고 지나가지 않게 콜라이더 + NavMesh 장애물)
        var hut = Box(root, "Hut", new Vector3(0f, 1.1f, -3.2f), new Vector3(2.6f, 2.2f, 2.2f), Mat(new Color(0.55f, 0.57f, 0.55f)));
        Obstacle(hut, Vector3.zero, Vector3.one);
        Label(root, "라디오 타워", 4.2f);
        return root;
    }

    /// <summary>공원: 잔디 원 + 분수 + 벤치 4개 + 가로등 4개 (씬에 있는 벤치/가로등을 복제해서 씀, 없으면 생략)</summary>
    public static Transform Park(Vector3 groundPos)
    {
        var root = Root("Park", groundPos);
        var grass = Prim(PrimitiveType.Cylinder, root, "Lawn", Vector3.up * 0.28f, new Vector3(16f, 0.01f, 16f), Mat(new Color(0.33f, 0.55f, 0.25f)), false);
        grass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var stone = Mat(new Color(0.7f, 0.68f, 0.64f));
        var basin = Prim(PrimitiveType.Cylinder, root, "FountainBasin", Vector3.up * 0.3f, new Vector3(3f, 0.3f, 3f), stone, true);
        Obstacle(basin, Vector3.zero, new Vector3(1f, 2f, 1f));
        Prim(PrimitiveType.Cylinder, root, "FountainWater", Vector3.up * 0.58f, new Vector3(2.6f, 0.02f, 2.6f), Mat(new Color(0.3f, 0.6f, 0.9f), 0.4f), false);
        Prim(PrimitiveType.Cylinder, root, "FountainPillar", Vector3.up * 1f, new Vector3(0.4f, 0.7f, 0.4f), stone, false);

        GameObject bench = FindSceneProp("Bench"), lamp = FindSceneProp("ParkLight");
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f;
            Quaternion rot = Quaternion.Euler(0f, a, 0f);
            if (bench != null) PlaceClone(bench, root, rot * new Vector3(0f, 0f, 4.8f), Quaternion.Euler(0f, a + 180f, 0f));
            if (lamp != null) PlaceClone(lamp, root, Quaternion.Euler(0f, a + 45f, 0f) * new Vector3(0f, 0f, 6.8f), Quaternion.identity);
        }
        Label(root, "공원", 3.6f);
        return root;
    }

    static GameObject FindSceneProp(string prefix)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith(prefix) && t.GetComponentInChildren<Renderer>() != null && t.parent != null && !t.name.Contains("Shadow")) return t.gameObject;
        return null;
    }

    // 씬 소품 복제: 원본의 지면 기준 높이를 유지하며 공원 바닥에 놓음
    static void PlaceClone(GameObject src, Transform root, Vector3 localPos, Quaternion rot)
    {
        var clone = Object.Instantiate(src, root);
        clone.name = src.name + "_Park";
        clone.transform.localPosition = localPos;
        // 원본의 기울기(모델 축 보정)는 유지하고 수평 방향만 rot으로
        clone.transform.rotation = root.rotation * rot * Quaternion.Inverse(Quaternion.Euler(0f, src.transform.eulerAngles.y, 0f)) * src.transform.rotation;
        // 밑면을 바닥에 맞춤
        var rs = clone.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        clone.transform.position += Vector3.up * (root.position.y + 0.02f - b.min.y);
    }

    // ── 메인 미션 오브젝트 ──

    /// <summary>비상 무전기: 나무 상자 위 군용 무전기 + 안테나</summary>
    public static Transform Radio(Vector3 groundPos, out Light beacon)
    {
        var root = Root("EmergencyRadio", groundPos, Random.Range(0f, 360f));
        Box(root, "Crate", Vector3.up * 0.4f, new Vector3(0.9f, 0.8f, 0.7f), Mat(new Color(0.45f, 0.32f, 0.2f)));
        Box(root, "Radio", Vector3.up * 1.02f, new Vector3(0.6f, 0.42f, 0.32f), Mat(new Color(0.25f, 0.3f, 0.2f)), false);
        Box(root, "Antenna", new Vector3(0.22f, 1.65f, 0f), new Vector3(0.03f, 0.85f, 0.03f), Mat(Color.black), false);
        beacon = Beacon(root, new Vector3(0f, 1.4f, 0f), Color.red, 4f);
        return root;
    }

    /// <summary>발전기: 노란 본체 + 배기관 + 계기판</summary>
    public static Transform Generator(Vector3 groundPos, out Light beacon)
    {
        var root = Root("Generator", groundPos, Random.Range(0f, 360f));
        var body = Box(root, "Body", Vector3.up * 0.65f, new Vector3(1.9f, 1.3f, 1.1f), Mat(new Color(0.85f, 0.65f, 0.12f)));
        Obstacle(body, Vector3.zero, Vector3.one);
        Box(root, "Panel", new Vector3(0f, 0.8f, 0.56f), new Vector3(0.7f, 0.5f, 0.04f), Mat(new Color(0.15f, 0.15f, 0.15f)), false);
        Prim(PrimitiveType.Cylinder, root, "Exhaust", new Vector3(-0.6f, 1.6f, 0f), new Vector3(0.15f, 0.35f, 0.15f), Mat(new Color(0.2f, 0.2f, 0.2f)), false);
        beacon = Beacon(root, new Vector3(0f, 1.8f, 0f), Color.red, 5f);
        return root;
    }

    /// <summary>무기고 사물함: 회색 철제 사물함 2개</summary>
    public static Transform ArmoryLocker(Vector3 groundPos, out Light beacon)
    {
        var root = Root("ArmoryLocker", groundPos, Random.Range(0f, 360f));
        var steel = Mat(new Color(0.4f, 0.43f, 0.45f));
        Box(root, "LockerL", new Vector3(-0.42f, 1f, 0f), new Vector3(0.8f, 2f, 0.6f), steel);
        Box(root, "LockerR", new Vector3(0.42f, 1f, 0f), new Vector3(0.8f, 2f, 0.6f), steel);
        Box(root, "Stripe", new Vector3(0f, 1.6f, 0.31f), new Vector3(1.6f, 0.12f, 0.02f), Mat(new Color(0.9f, 0.75f, 0.1f)), false);
        beacon = Beacon(root, new Vector3(0f, 2.4f, 0.4f), Color.red, 4f);
        return root;
    }

    /// <summary>연료통: 빨간 제리캔</summary>
    public static Transform FuelCan(Vector3 groundPos, out Light beacon)
    {
        var root = Root("FuelCan", groundPos, Random.Range(0f, 360f));
        var red = Mat(new Color(0.8f, 0.1f, 0.08f), 0.3f);
        Box(root, "Can", Vector3.up * 0.25f, new Vector3(0.36f, 0.5f, 0.18f), red, false);
        Box(root, "Handle", new Vector3(0f, 0.56f, 0f), new Vector3(0.24f, 0.06f, 0.06f), red, false);
        Prim(PrimitiveType.Cylinder, root, "Spout", new Vector3(0.12f, 0.58f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Mat(Color.black), false);
        beacon = Beacon(root, new Vector3(0f, 1f, 0f), Color.red, 3.5f);
        return root;
    }

    /// <summary>바리케이드 설치 지점: 쌓인 나무판 + 바닥 표시</summary>
    public static Transform BarricadeSpot(Vector3 groundPos, out Light beacon)
    {
        var root = Root("BarricadeSpot", groundPos, Random.Range(0f, 360f));
        var wood = Mat(new Color(0.5f, 0.35f, 0.2f));
        for (int i = 0; i < 4; i++)
            Box(root, "Plank" + i, new Vector3(0f, 0.08f + i * 0.13f, 0f), new Vector3(2.2f, 0.12f, 0.3f), wood, i == 0, new Vector3(0f, i * 9f, 0f));
        GroundRing(root, 2.4f, new Color(1f, 0.6f, 0.1f, 0.25f));
        beacon = Beacon(root, new Vector3(0f, 1.2f, 0f), Color.red, 4f);
        return root;
    }

    /// <summary>바리케이드 완성 모양 (설치 완료 후 남김): 기둥 2개 + 가로판 3장 + X자</summary>
    public static Transform BarricadeBuilt(Vector3 groundPos, float yaw)
    {
        var root = Root("Barricade", groundPos, yaw);
        var wood = Mat(new Color(0.48f, 0.33f, 0.19f));
        Box(root, "PostL", new Vector3(-1.5f, 0.9f, 0f), new Vector3(0.2f, 1.8f, 0.2f), wood);
        Box(root, "PostR", new Vector3(1.5f, 0.9f, 0f), new Vector3(0.2f, 1.8f, 0.2f), wood);
        for (int i = 0; i < 3; i++) Box(root, "Board" + i, new Vector3(0f, 0.45f + i * 0.5f, 0.12f), new Vector3(3.3f, 0.25f, 0.06f), wood);
        Box(root, "CrossA", new Vector3(0f, 0.95f, 0.18f), new Vector3(3.4f, 0.2f, 0.05f), wood, false, new Vector3(0f, 0f, 28f));
        Box(root, "CrossB", new Vector3(0f, 0.95f, 0.18f), new Vector3(3.4f, 0.2f, 0.05f), wood, false, new Vector3(0f, 0f, -28f));
        var block = new GameObject("Block");
        block.transform.SetParent(root, false);
        Obstacle(block, new Vector3(0f, 0.9f, 0f), new Vector3(3.4f, 1.8f, 0.4f));
        return root;
    }

    /// <summary>정찰 지점 깃발 (방문하면 SetFlagVisited로 초록)</summary>
    public static Transform ReconFlag(Vector3 groundPos, int index)
    {
        var root = Root("ReconFlag" + index, groundPos);
        Prim(PrimitiveType.Cylinder, root, "Pole", Vector3.up * 1.5f, new Vector3(0.08f, 1.5f, 0.08f), Mat(new Color(0.8f, 0.8f, 0.8f)), false);
        Box(root, "Flag", new Vector3(0.45f, 2.7f, 0f), new Vector3(0.9f, 0.55f, 0.03f), Mat(new Color(1f, 0.55f, 0.05f), 0.6f), false);
        GroundRing(root, 2f, new Color(1f, 0.6f, 0.1f, 0.25f));
        Label(root, $"정찰 지점 {index + 1}", 3.6f);
        return root;
    }

    public static void SetFlagVisited(Transform flag)
    {
        var f = flag.Find("Flag");
        if (f != null) f.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.3f, 0.9f, 0.4f), 0.6f);
        var ring = flag.Find("Ring");
        if (ring != null) ring.GetComponent<Renderer>().sharedMaterial.color = new Color(0.3f, 1f, 0.45f, 0.25f);
    }

    /// <summary>좀비 떼 지역: 바닥 빨간 원 + 옅은 빨간 빛기둥</summary>
    public static Transform HordeArea(Vector3 groundPos, float radius)
    {
        var root = Root("ZombieHordeArea", groundPos);
        GroundRing(root, Mathf.Min(radius, 8f), new Color(1f, 0.15f, 0.1f, 0.18f));
        var beam = Prim(PrimitiveType.Cylinder, root, "Beam", Vector3.up * 12f, new Vector3(1f, 12f, 1f),
            new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.15f, 0.1f, 0.12f) }, false);
        beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Label(root, "좀비 떼 지역", 3.6f);
        return root;
    }
}

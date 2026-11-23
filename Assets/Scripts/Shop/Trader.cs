using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 상인 NPC 하나 (상점 3단계): 노점(테이블·상자·파라솔·랜턴) + 시민 모델 + 머리 위 '$' 표시.
///  - 카메라가 위에서 내려다보므로 상인 머리 위는 비워 둔다 (지붕이 상인을 가리지 않게 파라솔은 옆 상자 위에)
///  - 가까이(interactRadius) 가면 'F  상점' 안내 → F키로 상점 창 열기/닫기. 멀어지면 창이 닫힘 (ShopUI)
///  - 좀비는 상인을 공격하지 않음 (Player 태그·체력이 없음). 노점은 NavMeshObstacle로 좀비가 돌아감
///  - 진열대(ShopStock)는 상인마다 따로. TraderManager가 만들고 날마다 옮김
/// </summary>
public class Trader : MonoBehaviour
{
    public ShopStock Stock { get; private set; }
    /// <summary>오늘 플레이어가 이 상인을 직접 봤는지 (봤으면 미니맵에 정확한 핀, 못 봤으면 범위 원만)</summary>
    public bool Discovered { get; private set; }
    /// <summary>미니맵 '이 근처 어딘가' 원의 중심 (실제 위치에서 어긋남)과 반경</summary>
    public Vector3 HintCenter { get; private set; }
    public float HintRadius { get; private set; } = 45f;
    /// <summary>발견한 순간 (미니맵 핀이 톡 튀어나오는 연출용)</summary>
    public float DiscoveredAt { get; private set; } = -100f;

    private float discoverRadius = 20f;

    private float interactRadius = 3f;
    private Transform player;
    private GameObject prompt;
    private Transform marker;
    private Light lantern;
    private float markerBaseY;
    private float lanternBase = 0.8f;

    private static Material matWood, matCanvas, matCanvas2, matMetal, matLanternGlow, matGoods1, matGoods2, matSkin, matCloth;

    public static Trader Create(Vector3 ground, float yaw, ShopStock stock, TraderSettings settings, System.Random rng)
    {
        var go = new GameObject("Trader");
        go.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));
        var t = go.AddComponent<Trader>();
        t.Stock = stock;
        stock.boundToTrader = true;
        if (settings != null)
        {
            t.interactRadius = settings.interactRadius;
            t.discoverRadius = settings.discoverRadius;
            t.HintRadius = Mathf.Max(5f, settings.hintRadius);
        }
        // 원 중심을 실제 위치에서 반경의 0~60%만큼 아무 방향으로 어긋나게 (상인은 늘 원 안에 있음)
        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
        float off = (float)rng.NextDouble() * 0.6f * t.HintRadius;
        t.HintCenter = ground + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * off;
        t.BuildStall();
        t.BuildNpc(settings, rng);
        t.BuildMarkers();
        return t;
    }

    private void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    private void Update()
    {
        if (player == null) return;
        Vector3 d = player.position - transform.position; d.y = 0f;
        bool near = d.sqrMagnitude <= interactRadius * interactRadius;
        if (!Discovered && d.sqrMagnitude <= discoverRadius * discoverRadius) Discover();
        bool open = ShopUI.IsOpen && ShopUI.CurrentStock == Stock;
        if (prompt != null) prompt.SetActive(near && !open && !MenuUI.PauseOpen);

        var kb = Keyboard.current;
        // (2026-11-24) 튜토리얼 말풍선이 떠 있으면 F로 상점을 열지 않음 (말풍선과 상점 창이 겹쳤음)
        if (near && kb != null && kb.fKey.wasPressedThisFrame && !MenuUI.PauseOpen && !MenuUI.DeathShown && !MenuUI.CutsceneActive && !MenuUI.TutorialOpen)
        {
            Discover();
            ShopUI.Toggle(Stock, transform);
        }

        // 머리 위 '$' 살짝 오르내림 + 랜턴 밝기 (밤에 더 밝게)
        if (marker != null)
        {
            var mp = marker.localPosition;
            mp.y = markerBaseY + Mathf.Sin(Time.time * 2f) * 0.08f;
            marker.localPosition = mp;
        }
        if (lantern != null)
        {
            var dnc = DayNightCycle.Instance;
            bool dark = dnc != null && (dnc.CurrentPhase == DayNightCycle.Phase.Night || dnc.CurrentPhase == DayNightCycle.Phase.Dusk);
            lanternBase = Mathf.MoveTowards(lanternBase, dark ? 3f : 0.8f, Time.deltaTime * 2f);
            lantern.intensity = lanternBase * (1f + Mathf.Sin(Time.time * 9f) * 0.04f);   // 살짝 일렁임
        }
    }

    private void Discover()
    {
        if (Discovered) return;
        Discovered = true;
        DiscoveredAt = Time.unscaledTime;
    }

    // ── 노점 ─────────────────────────────────────
    private void BuildStall()
    {
        EnsureMaterials();
        var stall = new GameObject("Stall").transform;
        stall.SetParent(transform, false);

        // 앞(+Z)이 손님 쪽. 상인은 테이블 뒤(-Z)에 섬
        Box(stall, "Table", new Vector3(0f, 0.4f, 0.55f), new Vector3(1.8f, 0.8f, 0.7f), matWood);
        Box(stall, "Crate1", new Vector3(-1.05f, 0.3f, -0.6f), new Vector3(0.6f, 0.6f, 0.6f), matWood);
        Box(stall, "Crate2", new Vector3(-1.05f, 0.85f, -0.6f), new Vector3(0.5f, 0.5f, 0.5f), matWood, 12f);
        Box(stall, "Crate3", new Vector3(1.1f, 0.3f, -0.7f), new Vector3(0.6f, 0.6f, 0.6f), matMetal, -8f);

        // 테이블 위 물건 (통조림·상자·물병 느낌)
        Box(stall, "Goods1", new Vector3(-0.55f, 0.9f, 0.5f), new Vector3(0.25f, 0.2f, 0.25f), matGoods1);
        Box(stall, "Goods2", new Vector3(-0.2f, 0.92f, 0.6f), new Vector3(0.3f, 0.24f, 0.2f), matGoods2, 20f);
        Cyl(stall, "Can1", new Vector3(0.25f, 0.9f, 0.5f), new Vector3(0.12f, 0.1f, 0.12f), matMetal);
        Cyl(stall, "Can2", new Vector3(0.42f, 0.9f, 0.62f), new Vector3(0.12f, 0.1f, 0.12f), matGoods1);
        Cyl(stall, "Bottle", new Vector3(0.6f, 0.95f, 0.45f), new Vector3(0.08f, 0.15f, 0.08f), matGoods2);

        // 파라솔: 왼쪽 상자 위에만 (상인 머리 위는 비움 —— 위에서 내려다봐도 상인이 보이게)
        Vector3 ps = new Vector3(-1.2f, 0f, -0.6f);
        Cyl(stall, "ParasolPole", ps + new Vector3(0f, 1.2f, 0f), new Vector3(0.05f, 1.2f, 0.05f), matWood);
        Cyl(stall, "Parasol", ps + new Vector3(0f, 2.4f, 0f), new Vector3(1.6f, 0.03f, 1.6f), matCanvas2);
        Cyl(stall, "ParasolTop", ps + new Vector3(0f, 2.43f, 0f), new Vector3(0.8f, 0.03f, 0.8f), matCanvas);

        // 랜턴 (테이블 오른쪽 앞 모서리에 올려 둠)
        var lamp = Box(stall, "Lantern", new Vector3(0.78f, 0.92f, 0.75f), new Vector3(0.14f, 0.22f, 0.14f), matLanternGlow);
        var lightGo = new GameObject("LanternLight");
        lightGo.transform.SetParent(lamp, false);
        lantern = lightGo.AddComponent<Light>();
        lantern.type = LightType.Point;
        lantern.color = new Color(1f, 0.72f, 0.38f);
        lantern.range = 7f;   // 랜턴이 낮아져서 바닥을 넓게 비춤
        lantern.intensity = 0.8f;
        lantern.shadows = LightShadows.None;

        // 막힘 판정: 노점 전체를 상자 하나로 (총알·플레이어가 통과하지 않게) + 좀비가 돌아가게
        var col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(-0.1f, 1.0f, -0.1f);
        col.size = new Vector3(3.0f, 2.0f, 2.0f);
        var obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = col.center;
        obstacle.size = col.size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    // ── 상인 모델 ─────────────────────────────────
    private void BuildNpc(TraderSettings settings, System.Random rng)
    {
        Vector3 spot = new Vector3(0.25f, 0f, -0.3f);   // 테이블 뒤, 파라솔 옆 (위에서 보임)
        if (settings != null && settings.models != null && settings.models.Count > 0)
        {
            var m = settings.models[rng.Next(settings.models.Count)];
            if (m != null && m.prefab != null)
            {
                var npc = Instantiate(m.prefab, transform);
                npc.name = "TraderModel";
                npc.transform.localPosition = spot;
                npc.transform.localRotation = Quaternion.identity;   // 손님 쪽(+Z)을 봄
                npc.transform.localScale = Vector3.one * Mathf.Max(0.1f, settings.modelScale);
                foreach (var c in npc.GetComponentsInChildren<Collider>(true)) Destroy(c);   // 노점 상자 하나로 충분

                var anim = npc.GetComponent<Animator>();
                if (anim == null) anim = npc.AddComponent<Animator>();
                if (m.avatar != null) anim.avatar = m.avatar;
                if (settings.idleController != null) anim.runtimeAnimatorController = settings.idleController;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                if (settings.idleController != null) anim.Play(0, 0, (float)rng.NextDouble());   // 상인마다 다른 박자
                return;
            }
        }

        // 설정이 없으면 캡슐 사람
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "TraderBody";
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(transform, false);
        body.transform.localPosition = spot + Vector3.up * 0.9f;
        body.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
        body.GetComponent<Renderer>().sharedMaterial = matCloth;
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "TraderHead";
        Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(transform, false);
        head.transform.localPosition = spot + Vector3.up * 1.95f;
        head.transform.localScale = Vector3.one * 0.32f;
        head.GetComponent<Renderer>().sharedMaterial = matSkin;
    }

    // ── 머리 위 '$' / 'F 상점' 안내 ──────────────────
    private void BuildMarkers()
    {
        var m = new GameObject("TraderMarker");
        m.transform.SetParent(transform, false);
        markerBaseY = 2.95f;
        m.transform.localPosition = new Vector3(0f, markerBaseY, 0f);
        m.AddComponent<FaceCamera>();
        var t = m.AddComponent<TextMeshPro>();
        if (HudStyle.Bold != null) t.font = HudStyle.Bold;
        t.text = "$";
        t.fontSize = 6f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color32(0xFF, 0xD7, 0x00, 0xFF);
        t.rectTransform.sizeDelta = new Vector2(2f, 2f);
        marker = m.transform;

        prompt = new GameObject("TraderPrompt");
        prompt.transform.SetParent(transform, false);
        prompt.transform.localPosition = new Vector3(0f, 1.5f, 1.3f);
        prompt.AddComponent<FaceCamera>();
        var p = prompt.AddComponent<TextMeshPro>();
        if (HudStyle.Bold != null) p.font = HudStyle.Bold;
        p.text = "F  상점";
        p.fontSize = 2f;
        p.alignment = TextAlignmentOptions.Center;
        p.color = Color.white;
        p.textWrappingMode = TextWrappingModes.NoWrap;
        p.rectTransform.sizeDelta = new Vector2(8f, 1f);
        prompt.SetActive(false);
    }

    // ── 도형 헬퍼 ─────────────────────────────────
    private static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float yaw = 0f)
        => Prim(PrimitiveType.Cube, parent, name, pos, size, mat, yaw);

    private static Transform Cyl(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        => Prim(PrimitiveType.Cylinder, parent, name, pos, size, mat, 0f);

    private static Transform Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float yaw)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    private static void EnsureMaterials()
    {
        if (matWood != null) return;
        matWood = Mat(new Color(0.42f, 0.29f, 0.18f));
        matCanvas = Mat(new Color(0.36f, 0.42f, 0.30f));     // 바랜 국방색 천
        matCanvas2 = Mat(new Color(0.62f, 0.38f, 0.20f));    // 주황 띠
        matMetal = Mat(new Color(0.40f, 0.42f, 0.44f), 0.5f);
        matGoods1 = Mat(new Color(0.70f, 0.20f, 0.18f));
        matGoods2 = Mat(new Color(0.25f, 0.45f, 0.70f));
        matSkin = Mat(new Color(0.85f, 0.68f, 0.55f));
        matCloth = Mat(new Color(0.30f, 0.27f, 0.22f));
        matLanternGlow = Mat(new Color(1f, 0.8f, 0.45f));
        matLanternGlow.EnableKeyword("_EMISSION");
        matLanternGlow.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.35f) * 2.5f);
    }

    private static Material Mat(Color c, float metallic = 0f)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }
}

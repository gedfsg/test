using UnityEngine;

/// <summary>
/// 탈출 지점 = 헬리패드: 흰색 "H" 헬리패드 + 상태 테두리 원 + 점 조명.
///  - 잠김(빨강): 7일차 메인 미션 "구조 헬기 탑승"이 공개되기 전 → 들어가면 헬기가 오는 날 안내
///  - 열림(초록): 초록 빛기둥으로 탑승 가능 표시. 들어가면 구조 헬기 컷신(HelicopterCutscene) 한 번 실행
///    → 컷신이 끝나면 MissionManager.Escape() → 게임 클리어
///  (이전: 열림 상태에서 들어가면 바로 Escape(), 빛기둥은 기본 꺼둠)
/// MissionManager가 Create()로 생성.
/// </summary>
public class EscapePoint : MonoBehaviour
{
    // public float radius = 3f; // (이전)
    public float radius = 5f;   // 헬리패드 반경 (메인 로터 지름 8m가 들어가게)
    public static bool showBeam = false; // 잠긴 동안에도 빛기둥을 켤지 (열리면 항상 켬 - 탑승 가능 표시)

    static readonly Color LockedColor = new Color(1f, 0.25f, 0.2f);
    static readonly Color OpenColor = new Color(0.25f, 1f, 0.4f);

    Transform player;
    Material ringMat, beamMat;
    Light glow;
    bool wasInside;
    bool playedOnce;      // 헬기 컷신은 한 번만
    GameObject beamObj;

    public static EscapePoint Create(Vector3 groundPos)
    {
        var root = new GameObject("EscapePoint");
        root.transform.position = groundPos;
        var ep = root.AddComponent<EscapePoint>();

        // 반투명 무광 재질 (Built-in RP: Sprites/Default = 조명 영향 없음 + 알파)
        var shader = Shader.Find("Sprites/Default");
        ep.ringMat = new Material(shader);
        ep.beamMat = new Material(shader);

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Ring";
        Object.Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = Vector3.up * 0.05f;
        // ring.transform.localScale = new Vector3(ep.radius * 2f, 0.02f, ep.radius * 2f); // (이전) 반투명 원판
        ring.transform.localScale = new Vector3((ep.radius + 0.6f) * 2f, 0.02f, (ep.radius + 0.6f) * 2f); // 헬리패드 둘레 상태 테두리 (빨강/초록)
        ring.GetComponent<Renderer>().sharedMaterial = ep.ringMat;
        BuildHelipad(root.transform, ep.radius);

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Beam";
        Object.Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(root.transform, false);
        beam.transform.localPosition = Vector3.up * 15f;
        beam.transform.localScale = new Vector3(1.2f, 15f, 1.2f);
        var br = beam.GetComponent<Renderer>();
        br.sharedMaterial = ep.beamMat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.SetActive(showBeam);
        ep.beamObj = beam;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * 2f;
        ep.glow = lightGo.AddComponent<Light>();
        ep.glow.type = LightType.Point;
        ep.glow.range = 8f;
        ep.glow.intensity = 1.5f;
        ep.glow.shadows = LightShadows.None;
        return ep;
    }

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        if (MissionManager.Instance != null) MissionManager.Instance.EscapeUnlocked += OnUnlocked;
    }

    void OnDestroy()
    {
        if (MissionManager.Instance != null) MissionManager.Instance.EscapeUnlocked -= OnUnlocked;
    }

    // void OnUnlocked() => PickupFeedbackUI.Instance?.Show("탈출 지점이 열렸다! 미니맵 ◆ 근처를 찾아가자"); // (이전) "초록 빛기둥으로 이동"
    void OnUnlocked() => PickupFeedbackUI.Instance?.Show("구조 요청 수신. 헬리패드로 이동하세요");

    // 흰색 "H" 헬리패드: 흰 원 테두리 + 어두운 바닥 + H (조명 영향 없는 불투명 재질, 바닥과 겹쳐 깜빡이지 않게 높이를 조금씩 다르게)
    static void BuildHelipad(Transform root, float r)
    {
        var shader = Shader.Find("Sprites/Default");
        var white = new Material(shader) { color = new Color(0.92f, 0.92f, 0.9f) };
        var dark = new Material(shader) { color = new Color(0.16f, 0.17f, 0.19f) };
        Disc(root, "PadEdge", r, 0.12f, white); // 길 가운데가 볼록하거나 연석이 있어도 묻히지 않게 조금 띄움
        Disc(root, "PadFloor", r - 0.45f, 0.135f, dark);
        Slab(root, "H_Left", new Vector3(-1.2f, 0.15f, 0f), new Vector3(0.55f, 0.01f, 3.6f), white);
        Slab(root, "H_Right", new Vector3(1.2f, 0.15f, 0f), new Vector3(0.55f, 0.01f, 3.6f), white);
        Slab(root, "H_Bar", new Vector3(0f, 0.15f, 0f), new Vector3(1.9f, 0.01f, 0.55f), white);
    }

    static void Disc(Transform root, string name, float r, float y, Material m)
    {
        var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        d.name = name;
        Object.Destroy(d.GetComponent<Collider>());
        d.transform.SetParent(root, false);
        d.transform.localPosition = Vector3.up * y;
        d.transform.localScale = new Vector3(r * 2f, 0.005f, r * 2f);
        var rend = d.GetComponent<Renderer>();
        rend.sharedMaterial = m;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Slab(Transform root, string name, Vector3 pos, Vector3 size, Material m)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s.name = name;
        Object.Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(root, false);
        s.transform.localPosition = pos;
        s.transform.localScale = size;
        var rend = s.GetComponent<Renderer>();
        rend.sharedMaterial = m;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        var mm = MissionManager.Instance;
        if (mm == null) return;

        // 잠김/열림 색 + 은은한 깜빡임
        bool open = mm.CanEscape || mm.Escaped; // 탈출한 뒤에도 초록 유지 (CanEscape는 Finished면 false)
        Color c = open ? OpenColor : LockedColor;
        float pulse = 0.75f + Mathf.Sin(Time.time * 3f) * 0.25f;
        ringMat.color = new Color(c.r, c.g, c.b, 0.12f * pulse); // 낮게: 가까이서 보면 지름 6m 원판이 화면을 크게 덮음 (플레이테스트 확인)
        beamMat.color = new Color(c.r, c.g, c.b, (open ? 0.2f : 0.07f) * pulse);
        glow.color = c;
        if (beamObj != null) beamObj.SetActive((open || showBeam) && !HelicopterCutscene.Playing); // 열리면 초록 빛기둥 (컷신 중엔 숨김)

        if (player == null) return;
        Vector3 d = player.position - transform.position; d.y = 0f;
        bool inside = d.sqrMagnitude <= radius * radius;
        // if (inside && open) mm.Escape(); // (이전) 안에서 기다리다 열려도 바로 탈출
        if (inside && open && !mm.Escaped && !playedOnce) // 안에서 기다리다 열려도 바로 시작
        {
            playedOnce = true;
            HelicopterCutscene.Get().Play(transform.position);
        }
        else if (inside && !wasInside && !open) PickupFeedbackUI.Instance?.ShowWarning(mm.EscapeLockedReason());
        wasInside = inside;
    }
}

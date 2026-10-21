using UnityEngine;

/// <summary>
/// 탈출 지점: 바닥 원 + 하늘로 솟는 빛기둥 + 점 조명.
///  - 잠김(빨강): 열쇠 카드 부족 또는 아직 탈출일 전 → 들어가면 남은 조건 안내
///  - 열림(초록): 들어가면 MissionManager.Escape() → 게임 클리어
/// MissionManager가 Create()로 생성.
/// </summary>
public class EscapePoint : MonoBehaviour
{
    public float radius = 3f;

    static readonly Color LockedColor = new Color(1f, 0.25f, 0.2f);
    static readonly Color OpenColor = new Color(0.25f, 1f, 0.4f);

    Transform player;
    Material ringMat, beamMat;
    Light glow;
    bool wasInside;
    bool? lastOpen;

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
        ring.transform.localScale = new Vector3(ep.radius * 2f, 0.02f, ep.radius * 2f);
        ring.GetComponent<Renderer>().sharedMaterial = ep.ringMat;

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Beam";
        Object.Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(root.transform, false);
        beam.transform.localPosition = Vector3.up * 15f;
        beam.transform.localScale = new Vector3(1.2f, 15f, 1.2f);
        var br = beam.GetComponent<Renderer>();
        br.sharedMaterial = ep.beamMat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

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
    }

    void Update()
    {
        var mm = MissionManager.Instance;
        if (mm == null) return;

        // 잠김/열림 색 + 은은한 깜빡임
        bool open = mm.CanEscape;
        Color c = open ? OpenColor : LockedColor;
        float pulse = 0.75f + Mathf.Sin(Time.time * 3f) * 0.25f;
        ringMat.color = new Color(c.r, c.g, c.b, 0.12f * pulse); // 낮게: 가까이서 보면 지름 6m 원판이 화면을 크게 덮음 (플레이테스트 확인)
        beamMat.color = new Color(c.r, c.g, c.b, (open ? 0.2f : 0.07f) * pulse);
        glow.color = c;
        if (lastOpen.HasValue && open && !lastOpen.Value)
            PickupFeedbackUI.Instance?.Show("탈출 지점이 열렸다! 초록 빛기둥으로 이동");
        lastOpen = open;

        if (player == null) return;
        Vector3 d = player.position - transform.position; d.y = 0f;
        bool inside = d.sqrMagnitude <= radius * radius;
        if (inside && open) mm.Escape(); // 안에서 기다리다 열려도 바로 탈출
        else if (inside && !wasInside) PickupFeedbackUI.Instance?.ShowWarning(mm.EscapeLockedReason());
        wasInside = inside;
    }
}

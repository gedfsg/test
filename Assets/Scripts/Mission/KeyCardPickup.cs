using UnityEngine;

/// <summary>
/// 메인 미션용 열쇠 카드 (인벤토리에 안 들어감 - MissionManager가 수만 셈).
/// 빛나는 카드가 떠서 돌고, 플레이어가 pickupRadius 안에 오면 자동 획득.
/// MissionManager가 건물 안에 Create()로 생성.
/// </summary>
public class KeyCardPickup : MonoBehaviour
{
    public float pickupRadius = 1.6f;
    public string buildingLabel; // 어느 건물에 있는지 (HUD "남은 곳" 표시용)

    Transform player;
    Transform visual;
    Vector3 basePos;

    static readonly Color CardColor = new Color(0.2f, 0.85f, 1f);

    /// <summary>groundPos(바닥) 위에 열쇠 카드 생성</summary>
    public static KeyCardPickup Create(Vector3 groundPos, string buildingLabel)
    {
        var root = new GameObject("KeyCard_" + buildingLabel);
        root.transform.position = groundPos;
        var card = root.AddComponent<KeyCardPickup>();
        card.buildingLabel = buildingLabel;

        // 카드 본체: 얇은 판 (발광 재질). 콜라이더는 필요 없음 (거리로 획득)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Card";
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(0.45f, 0.03f, 0.3f);
        var mat = new Material(Shader.Find("Standard"));
        mat.color = CardColor;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", CardColor * 2.5f);
        body.GetComponent<Renderer>().sharedMaterial = mat;
        card.visual = body.transform;

        // 주변을 비추는 빛 (어두운 실내에서도 찾을 수 있게)
        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * 0.8f;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = CardColor;
        l.range = 4f;
        l.intensity = 2f;
        l.shadows = LightShadows.None;
        return card;
    }

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        basePos = transform.position;
    }

    void Update()
    {
        // 떠서 천천히 돌기
        if (visual != null)
        {
            visual.localPosition = Vector3.up * (0.7f + Mathf.Sin(Time.time * 2f) * 0.12f);
            visual.localRotation = Quaternion.Euler(20f, Time.time * 90f, 0f);
        }

        if (player == null) return;
        Vector3 d = player.position - basePos;
        if (Mathf.Abs(d.y) > 2.5f) return; // 위층/아래층은 제외
        d.y = 0f;
        if (d.sqrMagnitude > pickupRadius * pickupRadius) return;

        MissionManager.Instance?.CollectKeyCard(this);
        Destroy(gameObject);
    }
}

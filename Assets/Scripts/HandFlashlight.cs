using UnityEngine;

/// <summary>
/// 손전등 비주얼 (2026-11-18). DayNightCycle이 찾은 손전등 Light(Player/Spot Light)에 붙음.
/// 예전: 플레이어 루트 앞 0.6m·높이 1.5m에 떠 있는 45° Spot(밝기 5, 흰색)만 있고 손에 든 물건이 없어서,
///       앞 바닥의 넓고 밝은 빛 덩어리가 "손 위에 뜬 큰 주황 사각형(placeholder)"처럼 보였음.
/// 지금:
///  - 왼손에 작은 손전등 모형(검은 원통 몸통 + 넓은 머리 + 렌즈, 길이 약 17cm)을 런타임에 만들어 쥐어 줌 (기본 도형 메시, 콜라이더 없음)
///  - 빛은 렌즈에서 조준점(커서, 총과 같은 방향) 쪽으로 약간 아래(pitchDown)를 비추는 Spot: Range 15m, Angle 35°, Intensity 3, #FFE8B0 따뜻한 백색
///  - 켜고 끄기(T키)·배터리는 DayNightCycle 그대로 - 이 스크립트는 Light.enabled를 보고 렌즈만 밝힘
/// 방향을 손 뼈 회전에 맡기지 않는 이유: 걷기·총 잡기 애니메이션으로 손이 흔들리면 빛이 사방으로 튐 → 위치만 손, 방향은 몸 정면.
/// </summary>
[RequireComponent(typeof(Light))]
public class HandFlashlight : MonoBehaviour
{
    [Header("빛 (Spot)")]
    public float range = 15f;
    public float spotAngle = 35f;
    public float intensity = 3f;
    public Color color = new Color32(0xFF, 0xE8, 0xB0, 0xFF);
    [Tooltip("몸 정면에서 아래로 숙이는 각도(도)")]
    public float pitchDown = 12f;

    [Header("손에 쥔 위치 (왼손 뼈 기준, 몸 방향 축: x 오른쪽, y 위, z 앞) m")]
    public Vector3 holdOffset = new Vector3(0.02f, 0.01f, 0.06f);

    Light lt;
    Transform body;        // 플레이어 루트 (정면 방향)
    PlayerController pc;   // 조준점(aimTarget) - 총과 같은 방향을 비추게
    Transform hand;        // 왼손 뼈
    Transform model;       // 손전등 모형 루트 (렌즈가 +Z)
    Material lensMat;
    bool lastOn;
    static readonly Color LensOff = new Color(0.25f, 0.24f, 0.2f);

    void Start()
    {
        lt = GetComponent<Light>();
        lt.type = LightType.Spot;
        lt.range = range;
        lt.spotAngle = spotAngle;
        lt.innerSpotAngle = spotAngle * 0.6f;
        lt.intensity = intensity;
        lt.color = color;

        pc = GetComponentInParent<PlayerController>();
        body = pc != null ? pc.transform : transform.parent;
        var anim = body != null ? body.GetComponentInChildren<Animator>() : null;
        if (anim != null && anim.isHuman) hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        BuildModel();
        lastOn = !lt.enabled; // 첫 프레임에 렌즈 색 맞춤
    }

    void BuildModel()
    {
        var root = new GameObject("HandFlashlightModel").transform;
        root.SetParent(body != null ? body : transform, false);
        model = root;
        int layer = body != null ? body.gameObject.layer : gameObject.layer;
        root.gameObject.layer = layer;

        var bodyMat = new Material(Shader.Find("Standard")) { color = new Color(0.08f, 0.08f, 0.09f) };
        bodyMat.SetFloat("_Metallic", 0.6f); bodyMat.SetFloat("_Glossiness", 0.55f);
        var headMat = new Material(Shader.Find("Standard")) { color = new Color(0.16f, 0.16f, 0.17f) };
        headMat.SetFloat("_Metallic", 0.8f); headMat.SetFloat("_Glossiness", 0.7f);
        lensMat = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.88f, 0.8f) };
        lensMat.EnableKeyword("_EMISSION");

        // 원통 메시: 높이 2(Y), 지름 1 → X 90° 돌려 길이 방향을 +Z로
        var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        Part(root, "Body", cyl, new Vector3(0f, 0f, -0.06f), new Vector3(0.032f, 0.06f, 0.032f), bodyMat, layer);   // 몸통 12cm (손잡이)
        Part(root, "Neck", cyl, new Vector3(0f, 0f, 0.012f), new Vector3(0.04f, 0.012f, 0.04f), headMat, layer);   // 목 (넓어지는 부분)
        Part(root, "Head", cyl, new Vector3(0f, 0f, 0.035f), new Vector3(0.05f, 0.016f, 0.05f), headMat, layer);   // 머리
        Part(root, "Lens", cyl, new Vector3(0f, 0f, 0.052f), new Vector3(0.042f, 0.002f, 0.042f), lensMat, layer); // 렌즈
        Part(root, "Button", Resources.GetBuiltinResource<Mesh>("Cube.fbx"), new Vector3(0f, 0.017f, -0.03f), new Vector3(0.01f, 0.006f, 0.018f), headMat, layer, false);
    }

    static void Part(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Material mat, int layer, bool cylinder = true)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = cylinder ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void LateUpdate()
    {
        if (model == null || body == null) return;
        // 방향: 조준점(커서) 쪽 = 총과 같은 방향 (너무 가까우면 몸 정면) + 약간 아래
        Vector3 f = body.forward; f.y = 0f;
        if (pc != null && pc.aimTarget != null)
        {
            Vector3 to = pc.aimTarget.position - body.position; to.y = 0f;
            if (to.sqrMagnitude > 1f) f = to;
        }
        f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, f);
        Vector3 dir = Quaternion.AngleAxis(pitchDown, right) * f;
        Vector3 basePos = hand != null ? hand.position : body.position + Vector3.up * 1.1f + f * 0.3f;
        Vector3 pos = basePos + right * holdOffset.x + Vector3.up * holdOffset.y + f * holdOffset.z;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        model.SetPositionAndRotation(pos, rot);
        transform.SetPositionAndRotation(pos + dir * 0.06f, rot); // 빛은 렌즈 바로 앞에서

        if (lt.enabled != lastOn)
        {
            lastOn = lt.enabled;
            lensMat.SetColor("_EmissionColor", lastOn ? color * 2.5f : LensOff * 0f);
            lensMat.color = lastOn ? color : LensOff;
        }
    }
}

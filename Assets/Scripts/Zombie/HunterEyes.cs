using UnityEngine;

/// <summary>
/// 사냥꾼의 붉게 빛나는 눈 (+ 얼굴을 비추는 작은 붉은 빛).
/// 휴머노이드 눈 뼈(LeftEye/RightEye)가 있으면 거기에, 없으면 머리 뼈 기준으로 눈 자리를 어림해서 붙인다.
/// 죽으면 천천히 꺼진다. HunterPackSpawner가 등장할 때 붙인다.
/// </summary>
[DisallowMultipleComponent]
public class HunterEyes : MonoBehaviour
{
    public Color color = new Color(1f, 0.12f, 0.05f);
    [Tooltip("눈빛 크기 (m)")]
    public float size = 0.03f;
    [Tooltip("얼굴 주변을 비추는 붉은 빛")]
    public bool withLight = true;
    public float lightRange = 2.5f;
    public float lightIntensity = 1.5f;
    [Tooltip("죽은 뒤 꺼지는 데 걸리는 시간(초)")]
    public float fadeTime = 0.8f;

    private Transform[] glows;
    private Vector3[]   glowScale;
    private Light       glowLight;
    private ZombieController body;
    private float fade = 1f, seed;
    private static Material glowMat;

    public static HunterEyes Attach(GameObject go, Color color, bool withLight)
    {
        if (go == null) return null;
        var e = go.GetComponent<HunterEyes>();
        if (e == null) e = go.AddComponent<HunterEyes>();
        e.color = color;
        e.withLight = withLight;
        return e;
    }

    void Start()
    {
        body = GetComponent<ZombieController>();
        seed = Random.value * 100f;
        var anim = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) { enabled = false; return; }

        Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) { enabled = false; return; }
        Transform le = anim.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform re = anim.GetBoneTransform(HumanBodyBones.RightEye);
        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);

        // 몸 크기 기준 길이 (골반 → 머리). 사람 크기면 약 0.6m
        float unit = hips != null ? Mathf.Max(0.2f, Vector3.Distance(hips.position, head.position)) : 0.6f;
        Vector3 fwd = transform.forward, up = transform.up, right = transform.right;

        Vector3 lPos, rPos;
        Transform lParent = head, rParent = head;
        if (le != null && re != null)
        {
            lPos = le.position + fwd * unit * 0.02f;     // 눈알 표면보다 살짝 앞
            rPos = re.position + fwd * unit * 0.02f;
            lParent = le; rParent = re;
        }
        else
        {
            Vector3 c = head.position + up * unit * 0.15f + fwd * unit * 0.15f;
            lPos = c - right * unit * 0.055f;
            rPos = c + right * unit * 0.055f;
        }

        float s = size * (unit / 0.6f);
        glows = new[] { MakeGlow(lParent, lPos, s), MakeGlow(rParent, rPos, s) };
        glowScale = new[] { glows[0].localScale, glows[1].localScale };

        if (withLight)
        {
            var lg = new GameObject("HunterEyeLight");
            lg.transform.SetParent(head, false);
            lg.transform.position = (lPos + rPos) * 0.5f + fwd * unit * 0.1f;
            glowLight = lg.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = color;
            glowLight.range = lightRange * (unit / 0.6f);
            glowLight.intensity = lightIntensity;
            glowLight.shadows = LightShadows.None;
        }
    }

    private Transform MakeGlow(Transform parent, Vector3 worldPos, float worldSize)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "HunterEyeGlow";
        var col = g.GetComponent<Collider>();
        if (col != null) Destroy(col);
        g.transform.SetParent(parent, false);
        g.transform.position = worldPos;
        float ps = Mathf.Max(1e-5f, parent.lossyScale.x);
        g.transform.localScale = Vector3.one * (worldSize / ps);
        var r = g.GetComponent<Renderer>();
        if (glowMat == null) glowMat = new Material(Shader.Find("Sprites/Default")) { name = "HunterEyeGlow(Runtime)" };
        r.sharedMaterial = glowMat;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        r.SetPropertyBlock(block);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return g.transform;
    }

    void Update()
    {
        if (glows == null) return;
        bool dead = body == null || body.IsDead;
        if (dead) fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime / Mathf.Max(0.05f, fadeTime));

        // 살아 있으면 은은하게 일렁인다
        float flicker = dead ? 1f : 0.85f + 0.15f * Mathf.PerlinNoise(seed, Time.time * 3f);
        for (int i = 0; i < glows.Length; i++)
            if (glows[i] != null) glows[i].localScale = glowScale[i] * Mathf.Max(0.001f, fade);
        if (glowLight != null) glowLight.intensity = lightIntensity * flicker * fade;

        if (fade <= 0f)
        {
            foreach (var g in glows) if (g != null) Destroy(g.gameObject);
            if (glowLight != null) Destroy(glowLight.gameObject);
            glows = null;
            enabled = false;
        }
    }
}

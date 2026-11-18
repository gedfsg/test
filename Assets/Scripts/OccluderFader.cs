using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가림 오브젝트 페이드 (2026-11-18, 씬에 하나). BuildingInterior 컷어웨이(지붕 잘라내기 + 검정 마스크)를 대신함.
///  - 매 프레임 Main Camera → 플레이어(머리 높이) SphereCast(반경 RayRadius)로 "Occludable" 레이어 콜라이더(트리거 포함)를 모음
///  - 맞은 콜라이더가 속한 오브젝트(가장 위쪽 Occludable 조상)의 렌더러 전부를 Custom/FadeOccluder 재질로 잠깐 바꾸고
///    MaterialPropertyBlock의 _FadeAmount를 0→1 (FadeInSpeed초), 벗어나면 1→0 (FadeOutSpeed초) 뒤 원래 재질로 되돌림
///  - 원본 재질 에셋은 안 건드림: 원본마다 페이드용 복사본을 한 번 만들어 공유, 투명도는 MPB로만 조작
///  - 지면·플레이어·좀비·투사체·이펙트는 Occludable 레이어가 아니라서 영향 없음 (Tools/Utility/Mark Occludable Objects가 레이어 지정)
/// 예전 방식: BuildingInterior.CutawayEnabled(컷어웨이 셰이더), PlayerXRay(가려진 몸 비침), CameraObstacleFader(렌더러 끄기) - 코드는 남기고 끔.
/// </summary>
public class OccluderFader : MonoBehaviour
{
    public const string LayerName = "Occludable";

    [Header("튜닝")]
    [Tooltip("가렸을 때 알파 (0.3 = 70% 투명)")]
    [Range(0f, 1f)] public float FadeAlpha = 0.3f;
    [Tooltip("불투명 → 반투명까지 걸리는 시간(초)")]
    public float FadeInSpeed = 0.15f;
    [Tooltip("반투명 → 불투명까지 걸리는 시간(초)")]
    public float FadeOutSpeed = 0.25f;
    [Tooltip("카메라→플레이어 SphereCast 반경(m)")]
    public float RayRadius = 0.6f;
    [Tooltip("플레이어 발 위치에서 이 높이(m)를 겨냥 (몸 가운데~머리)")]
    public float playerHeightOffset = 1.2f;

    [Header("대상 (비우면 Player 태그 / Camera.main)")]
    public Transform player;

    class Entry
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] fades;
        public float amount;
        public bool hit;
    }

    readonly Dictionary<Renderer, Entry> entries = new Dictionary<Renderer, Entry>();
    readonly Dictionary<Collider, Renderer[]> colliderCache = new Dictionary<Collider, Renderer[]>();
    readonly List<Renderer> removeList = new List<Renderer>();
    readonly RaycastHit[] hitBuf = new RaycastHit[64];
    static readonly Dictionary<Material, Material> fadeMats = new Dictionary<Material, Material>();
    static Shader fadeShader;
    MaterialPropertyBlock mpb;
    int layer = -1, mask;
    // 건물이 아닌 Occludable 렌더러 (나무·차·간판·바위 등) → 렌더러 범위(Bounds)로도 검사.
    // 콜라이더가 없거나(숲 FBX 잎·바위 1183개) 줄기에만 있어서(공원 나무 캡슐 0.2~2.2m) 수관(잎)은 SphereCast에 안 걸림
    Renderer[] boundsOnly = new Renderer[0];
    const float BoundsCheckRange = 45f; // 플레이어에서 이 거리(XZ) 안의 것만 검사

    static readonly int FadeAmountId = Shader.PropertyToID("_FadeAmount");
    static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");

    void Start()
    {
        layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0)
        {
            Debug.LogWarning("[OccluderFader] 'Occludable' 레이어가 없습니다. 메뉴 Tools/Utility/Mark Occludable Objects를 실행하세요.");
            enabled = false;
            return;
        }
        mask = 1 << layer;
        if (fadeShader == null) fadeShader = Shader.Find("Custom/FadeOccluder");
        if (fadeShader == null) { Debug.LogError("[OccluderFader] Custom/FadeOccluder 셰이더 없음 (Assets/Resources/Shaders)"); enabled = false; return; }
        mpb = new MaterialPropertyBlock();

        var list = new List<Renderer>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.gameObject.layer == layer && r.GetComponentInParent<BuildingInterior>() == null // 건물은 ㄱ자 등 범위가 실제보다 커서 콜라이더로만
                && Mathf.Max(r.bounds.size.x, r.bounds.size.z) < 40f) list.Add(r); // 땅째 붙은 큰 메시(농장 등)는 범위 검사 안 함
        boundsOnly = list.ToArray();

        // 예전 가림 처리는 지우지 않고 끔 (Fade로 대체)
        foreach (var x in FindObjectsByType<PlayerXRay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) x.enabled = false;
        foreach (var x in FindObjectsByType<CameraObstacleFader>(FindObjectsInactive.Include, FindObjectsSortMode.None)) x.enabled = false;
    }

    void LateUpdate()
    {
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p == null) return; player = p.transform; }
        var cam = Camera.main;
        if (cam == null) return;

        foreach (var e in entries.Values) e.hit = false;

        Vector3 from = cam.transform.position;
        Vector3 to = player.position + Vector3.up * playerHeightOffset;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist > 0.01f)
        {
            int n = Physics.SphereCastNonAlloc(from, RayRadius, dir / dist, hitBuf, dist, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = hitBuf[i].collider;
                if (col == null) continue;
                foreach (var r in RenderersFor(col))
                {
                    if (r == null) continue;
                    if (!entries.TryGetValue(r, out var e)) e = Begin(r);
                    if (e != null) e.hit = true;
                }
            }

            // 건물 외: 카메라→플레이어 선분이 (RayRadius만큼 넓힌) 렌더러 범위를 지나는지
            var ray = new Ray(from, dir / dist);
            Vector3 pp = player.position;
            foreach (var r in boundsOnly)
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                float dx = b.center.x - pp.x, dz = b.center.z - pp.z;
                if (dx * dx + dz * dz > BoundsCheckRange * BoundsCheckRange) continue;
                b.Expand(RayRadius * 2f);
                if (!b.IntersectRay(ray, out float d) || d > dist) continue;
                if (!entries.TryGetValue(r, out var e)) e = Begin(r);
                e.hit = true;
            }
        }

        float dt = Time.deltaTime;
        removeList.Clear();
        foreach (var e in entries.Values)
        {
            if (e.renderer == null) { removeList.Add(e.renderer); continue; }
            if (e.hit) e.amount = Mathf.MoveTowards(e.amount, 1f, dt / Mathf.Max(0.01f, FadeInSpeed));
            else e.amount = Mathf.MoveTowards(e.amount, 0f, dt / Mathf.Max(0.01f, FadeOutSpeed));

            if (!e.hit && e.amount <= 0f) { End(e); removeList.Add(e.renderer); continue; }
            e.renderer.GetPropertyBlock(mpb);
            mpb.SetFloat(FadeAmountId, Mathf.SmoothStep(0f, 1f, e.amount));
            mpb.SetFloat(FadeAlphaId, FadeAlpha);
            e.renderer.SetPropertyBlock(mpb);
        }
        foreach (var r in removeList) entries.Remove(r);
    }

    // 콜라이더 → 같은 오브젝트 묶음의 렌더러 (가장 위쪽 Occludable 조상 아래 Occludable 렌더러 전부)
    Renderer[] RenderersFor(Collider col)
    {
        if (colliderCache.TryGetValue(col, out var list)) return list;
        Transform root = col.transform;
        for (var t = col.transform; t != null; t = t.parent)
            if (t.gameObject.layer == layer) root = t;
        var found = new List<Renderer>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.layer == layer && (r is MeshRenderer || r is SkinnedMeshRenderer)) found.Add(r);
        list = found.ToArray();
        colliderCache[col] = list;
        return list;
    }

    Entry Begin(Renderer r)
    {
        var originals = r.sharedMaterials;
        var fades = new Material[originals.Length];
        for (int i = 0; i < originals.Length; i++)
            fades[i] = originals[i] != null ? FadeMaterialFor(originals[i]) : null;
        var e = new Entry { renderer = r, originals = originals, fades = fades, amount = 0f };
        r.sharedMaterials = fades;
        entries[r] = e;
        return e;
    }

    void End(Entry e)
    {
        if (e.renderer == null) return;
        e.renderer.sharedMaterials = e.originals;
        // 페이드 값만 지움 (다른 스크립트가 쓴 MPB 값은 남김)
        e.renderer.GetPropertyBlock(mpb);
        mpb.SetFloat(FadeAmountId, 0f);
        e.renderer.SetPropertyBlock(mpb);
    }

    void OnDisable()
    {
        foreach (var e in entries.Values) End(e);
        entries.Clear();
    }

    /// <summary>원본 재질의 텍스처·색·노멀·금속/거칠기·발광을 복사한 페이드 재질 (원본당 하나, 공유)</summary>
    static Material FadeMaterialFor(Material src)
    {
        if (fadeMats.TryGetValue(src, out var m) && m != null) return m;
        if (fadeShader == null) fadeShader = Shader.Find("Custom/FadeOccluder"); // 아직 쓰는 재질이 없을 때 씬 로드의 UnloadUnusedAssets로 풀릴 수 있음
        if (fadeShader == null) return src;
        m = new Material(fadeShader) { name = src.name + " (Fade)" };
        bool gltf = src.shader != null && src.shader.name.StartsWith("glTF");

        // 텍스처·색: Standard(_MainTex/_Color)와 glTF(baseColorTexture/baseColorFactor) 둘 다 [MainTexture]/[MainColor]
        Texture tex = src.mainTexture;
        if (tex != null) { m.mainTexture = tex; m.mainTextureScale = src.mainTextureScale; m.mainTextureOffset = src.mainTextureOffset; }
        if (src.HasProperty("_Color")) m.color = src.GetColor("_Color");
        else if (src.HasProperty("baseColorFactor")) m.color = src.GetColor("baseColorFactor");
        m.SetFloat("_UseVertexColor", gltf ? 1f : 0f);

        Texture nrm = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : src.HasProperty("normalTexture") ? src.GetTexture("normalTexture") : null;
        if (nrm != null)
        {
            m.SetTexture("_BumpMap", nrm);
            if (src.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", src.GetFloat("_BumpScale"));
            else if (src.HasProperty("normalTexture_scale")) m.SetFloat("_BumpScale", src.GetFloat("normalTexture_scale"));
        }
        if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
        else if (src.HasProperty("metallicFactor")) m.SetFloat("_Metallic", src.GetFloat("metallicFactor"));
        if (src.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", src.GetFloat("_Glossiness"));
        else if (src.HasProperty("roughnessFactor")) m.SetFloat("_Glossiness", 1f - src.GetFloat("roughnessFactor"));

        if (src.IsKeywordEnabled("_EMISSION"))
        {
            if (src.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
            else if (src.HasProperty("emissiveFactor")) m.SetColor("_EmissionColor", src.GetColor("emissiveFactor"));
            Texture em = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : src.HasProperty("emissiveTexture") ? src.GetTexture("emissiveTexture") : null;
            if (em != null) m.SetTexture("_EmissionMap", em);
        }
        fadeMats[src] = m;
        return m;
    }
}

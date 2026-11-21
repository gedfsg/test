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
/// (2026-11-21) 외부 가림 "구멍" 모드(holeMode, 기본 켬) - 피드백 "건물 밖에서 건물·벽 뒤로 가면 플레이어가 안 보이고, 몸이 반투명해짐"
///  - 실내(BuildingInterior.Current)에서는 아무것도 안 함 → 기존 컷어웨이(지붕 잘라내기)·X-Ray 그대로
///  - 실외에서 시선이 가려지면 오브젝트 전체를 흐리게 하지 않고 "플레이어 근처만" 잘라냄 (반경 HoleRadius 3.5m, 페더 HoleFeather 1m)
///    · 건물(재질이 Custom/BuildingCutaway): 재질 교체 없이 렌더러 MPB _HoleAmount만 올림 (셰이더에 구멍 함수 추가)
///    · 그 외 Prop: Custom/OccluderHole(불투명 + 구멍) 재질로 잠깐 바꿈 (예전 FadeOccluder 대신)
///  - 플레이어·좀비·아이템·투사체 등 움직이는 것은 레이어와 상관없이 절대 대상 아님 (IsDynamic)
///  - 실외에서는 PlayerXRay 고스트(반투명 몸)를 끔(indoorOnly) → 몸은 항상 불투명, 대신 구멍으로 보임
///  - 씬에서 이 컴포넌트를 다시 켬 (2026-11-20 Fade 되돌릴 때 enabled=false였음)
/// (2026-11-21 2차) 피드백 "구멍을 크게, 가장자리 디더링 말고 부드럽게, 실내에서도"
///  - 반경 3.5 → 7m, 페더 1 → 2m (최대 MaxHoleRadius 20m로 제한)
///  - 디더링 → 그라데이션: 원본은 페더 구간까지 잘라내고, 같은 메시를 반투명 "링"(_HoleRing 자식 렌더러)으로 한 번 더 그려
///    페더 구간만 알파 0~1 그라데이션 (Custom/OccluderHoleRing, 건물은 Custom/BuildingCutawayHoleRing)
///  - 실내에서도 구멍 적용 (지붕 컷어웨이와 같이) → 큰 건물 안에서도 주변 방이 보임
///  - 감지 범위: SphereCast 반경 = 구멍 반경 (시선이 직접 안 가려져도 구멍 범위에 걸치는 것까지)
/// (2026-11-21 3차) 피드백 "원 경계가 안 보이게, 원 더 크게" + "건물 내부가 보이면 안 됨"
///  - 반경 7 → 14m, 페더 2 → 10m (원 테두리가 거의 안 보이게 넓게 흐림)
///  - 플레이어가 들어가 있지 않은 건물: 구멍 안 바닥 높이(바닥·러그·벽 아래 띠)를 어두운 단색으로 칠함(_HoleInteriorFill)
///    → 벽·지붕·가구는 잘려서 뒤의 플레이어가 보이지만 방 안 모습은 안 보임. 들어가 있는 건물은 그대로(실내가 보여야 하니)
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

    [Header("(2026-11-21) 구멍 모드 - 플레이어 근처만 잘라냄 (끄면 예전 Fade 반투명)")]
    public bool holeMode = true;
    [Tooltip("구멍 반경(m) - 플레이어 위치 기준 이 반경 안을 잘라냄 (최대 MaxHoleRadius)")]
    // public float HoleRadius = 3.5f; // (이전 1차)
    // public float HoleRadius = 7f;   // (이전 2차)
    public float HoleRadius = 14f;     // (2026-11-21 3차)
    [Tooltip("구멍 가장자리 페더링(m) - 반투명 그라데이션")]
    // public float HoleFeather = 1f;  // (이전 1차)
    // public float HoleFeather = 2f;  // (이전 2차)
    public float HoleFeather = 10f;    // (2026-11-21 3차) 넓게 흐려서 원 테두리가 안 보이게
    [Tooltip("(2026-11-21 3차) 건물 내부 가림 색 밝기 (구멍 안 바닥 높이를 이 회색으로)")]
    [Range(0f, 1f)] public float HoleFillBrightness = 0.12f;
    public const float MaxHoleRadius = 20f; // (2026-11-21 2차) 성능: 감지 SphereCast·링 렌더러 수가 반경에 비례해서 늘어남
    [Tooltip("플레이어 발 위 이 높이(m)보다 낮은 부분은 안 뚫음 (바닥·벽 아래 띠 유지)")]
    public float HoleMinHeight = 0.3f;

    [Header("대상 (비우면 Player 태그 / Camera.main)")]
    public Transform player;

    class Entry
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] fades;
        public float amount;
        public bool hit;
        public MeshRenderer ring;      // (2026-11-21 2차) 페더 구간 반투명 링 (없으면 null)
        public Material[] ringSources; // 링 재질마다 컷 값을 복사해 올 원본(건물 인스턴스 재질)
        public BuildingInterior building; // (2026-11-21 3차) 속한 건물 (없으면 null) - 들어가 있지 않으면 내부 가림
        public bool decor;                // (2026-11-21 3차) 건물 자식이 아니라 실내 트리거 안에 놓인 가구·소품 (Building_Interior_* 아래)
    }

    readonly Dictionary<Renderer, Entry> entries = new Dictionary<Renderer, Entry>();
    readonly Dictionary<Collider, Renderer[]> colliderCache = new Dictionary<Collider, Renderer[]>();
    readonly List<Renderer> removeList = new List<Renderer>();
    // readonly RaycastHit[] hitBuf = new RaycastHit[64]; // (이전) 구멍 반경이 커져서 한 번에 걸리는 수가 늘어남
    readonly RaycastHit[] hitBuf = new RaycastHit[256];
    static readonly Dictionary<Material, Material> fadeMats = new Dictionary<Material, Material>();
    static Shader fadeShader;
    static Shader holeShader;            // (2026-11-21)
    static Shader buildingCutawayShader; // (2026-11-21) 이 셰이더 재질은 교체 없이 MPB만
    static readonly Dictionary<Material, Material> holeMats = new Dictionary<Material, Material>();
    static Shader ringShader, buildingRingShader; // (2026-11-21 2차)
    static readonly Dictionary<Material, Material> ringMats = new Dictionary<Material, Material>();
    readonly Dictionary<Renderer, MeshRenderer> ringCache = new Dictionary<Renderer, MeshRenderer>(); // 렌더러별 링 (한 번 만들어 켜고 끔)
    MaterialPropertyBlock mpb;
    int layer = -1, mask;
    // 건물이 아닌 Occludable 렌더러 (나무·차·간판·바위 등) → 렌더러 범위(Bounds)로도 검사.
    // 콜라이더가 없거나(숲 FBX 잎·바위 1183개) 줄기에만 있어서(공원 나무 캡슐 0.2~2.2m) 수관(잎)은 SphereCast에 안 걸림
    Renderer[] boundsOnly = new Renderer[0];
    const float BoundsCheckRange = 45f; // 플레이어에서 이 거리(XZ) 안의 것만 검사

    static readonly int FadeAmountId = Shader.PropertyToID("_FadeAmount");
    static readonly int FadeAlphaId = Shader.PropertyToID("_FadeAlpha");
    static readonly int HoleAmountId = Shader.PropertyToID("_HoleAmount");   // (2026-11-21)
    static readonly int HolePlayerId = Shader.PropertyToID("_OccHolePlayer");
    static readonly int HoleParamsId = Shader.PropertyToID("_OccHoleParams");
    static readonly int CutHeightId = Shader.PropertyToID("_CutHeight");   // (2026-11-21 2차) 건물 링에 컷 값 복사
    static readonly int CutEnabledId = Shader.PropertyToID("_CutEnabled");
    static readonly int HoleFillId = Shader.PropertyToID("_HoleInteriorFill"); // (2026-11-21 3차)
    PlayerXRay xray; // (2026-11-21) 실외에서는 고스트 끔
    // (2026-11-21 3차) 건물별 실내 트리거 범위 - 건물 자식이 아닌 가구(InteriorDecorBuilder가 따로 둔 것)가 어느 건물 안인지
    readonly List<KeyValuePair<BuildingInterior, Bounds>> buildingTriggers = new List<KeyValuePair<BuildingInterior, Bounds>>();
    // (2026-11-21 3차) 건물별 실내 가구·소품 렌더러 (레이어 무관 - 러그·의자처럼 낮아서 Occludable이 아닌 것도) → 건물이 구멍에 걸리면 같이 처리
    readonly Dictionary<BuildingInterior, Renderer[]> decorByBuilding = new Dictionary<BuildingInterior, Renderer[]>();
    readonly List<BuildingInterior> hitBuildings = new List<BuildingInterior>();

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
        if (holeMode)
        {
            if (holeShader == null) holeShader = Shader.Find("Custom/OccluderHole");
            if (buildingCutawayShader == null) buildingCutawayShader = Shader.Find("Custom/BuildingCutaway");
            if (holeShader == null) { Debug.LogError("[OccluderFader] Custom/OccluderHole 셰이더 없음 (Assets/Resources/Shaders)"); enabled = false; return; }
            if (ringShader == null) ringShader = Shader.Find("Custom/OccluderHoleRing");
            if (buildingRingShader == null) buildingRingShader = Shader.Find("Custom/BuildingCutawayHoleRing");
        }

        var list = new List<Renderer>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.gameObject.layer == layer && r.GetComponentInParent<BuildingInterior>() == null // 건물은 ㄱ자 등 범위가 실제보다 커서 콜라이더로만
                && Mathf.Max(r.bounds.size.x, r.bounds.size.z) < 40f // 땅째 붙은 큰 메시(농장 등)는 범위 검사 안 함
                && !IsDynamic(r)) list.Add(r); // (2026-11-21) 플레이어·좀비·아이템·투사체는 레이어가 잘못 돼 있어도 제외
        boundsOnly = list.ToArray();

        // (2026-11-21 3차) 실내 트리거 범위 (가구 → 건물 찾기용)
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            var t = LootWorldSpawner.FindTrigger(b);
            if (t != null) buildingTriggers.Add(new KeyValuePair<BuildingInterior, Bounds>(b, t.bounds));
        }
        var decor = new Dictionary<BuildingInterior, List<Renderer>>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r.GetComponentInParent<BuildingInterior>() != null || IsDynamic(r)) continue;
            if (r.name == "_HoleRing" || r.name.StartsWith("_Rarity")) continue;
            var b = BuildingContaining(r.bounds.center);
            if (b == null) continue;
            if (!decor.TryGetValue(b, out var l)) decor[b] = l = new List<Renderer>();
            l.Add(r);
        }
        foreach (var kv in decor) decorByBuilding[kv.Key] = kv.Value.ToArray();

        // 예전 가림 처리는 지우지 않고 끔 (Fade로 대체)
        // (2026-11-21) 구멍 모드: X-Ray는 끄지 않고 실내 전용으로 (실내 컷어웨이는 그대로, 실외 몸 반투명 고스트만 없앰)
        // foreach (var x in FindObjectsByType<PlayerXRay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) x.enabled = false;
        foreach (var x in FindObjectsByType<PlayerXRay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (holeMode) { x.indoorOnly = true; xray = x; }
            else x.enabled = false;
        }
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

        // (2026-11-21) 구멍 모드: 셰이더 전역 값 (시선 끝 = 플레이어 가슴, w = 발 높이)
        float holeR = Mathf.Clamp(HoleRadius, 0f, MaxHoleRadius);            // (2026-11-21 2차) 20m 제한
        float holeF = Mathf.Clamp(HoleFeather, 0.01f, Mathf.Max(0.01f, holeR));
        if (holeMode)
        {
            Shader.SetGlobalVector(HolePlayerId, new Vector4(to.x, to.y, to.z, player.position.y));
            Shader.SetGlobalVector(HoleParamsId, new Vector4(holeR, holeF, HoleMinHeight, HoleFillBrightness)); // (3차) w = 내부 가림 밝기
            if (xray != null && !xray.indoorOnly) xray.indoorOnly = true; // 실외 고스트 끔

            // (이전 1차) 실내에서는 구멍 안 뚫음 → 2차: 실내에서도 구멍 (지붕 컷어웨이와 같이). 건물 재질은 안 바꾸니 컷어웨이 그대로
            // if (BuildingInterior.Current != null)
            // {
            //     foreach (var e in entries.Values) End(e);
            //     entries.Clear();
            //     return;
            // }
        }
        // (2026-11-21 2차) 감지 반경: 구멍 모드면 구멍 반경만큼 (구멍에 걸치는 것까지 같이 뚫음)
        float castR = holeMode ? Mathf.Max(RayRadius, holeR) : RayRadius;

        if (dist > 0.01f)
        {
            int n = Physics.SphereCastNonAlloc(from, castR, dir / dist, hitBuf, dist, mask, QueryTriggerInteraction.Collide);
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
                b.Expand(castR * 2f); // (2026-11-21 2차) RayRadius → castR
                if (!b.IntersectRay(ray, out float d) || d > dist) continue;
                if (!entries.TryGetValue(r, out var e)) e = Begin(r);
                e.hit = true;
            }
        }

        // (2026-11-21 3차) 구멍에 걸린 건물의 실내 가구·소품도 같이 (내부 가림 / 실내면 같이 뚫림)
        if (holeMode)
        {
            hitBuildings.Clear();
            foreach (var e in entries.Values) if (e.hit && e.building != null && !e.decor && !hitBuildings.Contains(e.building)) hitBuildings.Add(e.building);
            foreach (var b in hitBuildings)
            {
                if (!decorByBuilding.TryGetValue(b, out var list)) continue;
                foreach (var r in list)
                {
                    if (r == null) continue;
                    if (!entries.TryGetValue(r, out var e)) e = Begin(r);
                    if (e != null) e.hit = true;
                }
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
            if (holeMode)
            {
                mpb.SetFloat(HoleAmountId, Mathf.SmoothStep(0f, 1f, e.amount)); // (2026-11-21) 구멍 크기
                mpb.SetFloat(HoleFillId, InteriorFill(e)); // (2026-11-21 3차)
            }
            else
            {
                mpb.SetFloat(FadeAmountId, Mathf.SmoothStep(0f, 1f, e.amount));
                mpb.SetFloat(FadeAlphaId, FadeAlpha);
            }
            e.renderer.SetPropertyBlock(mpb);
            if (holeMode && e.ring != null) UpdateRing(e); // (2026-11-21 2차)
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
            if (r.gameObject.layer == layer && (r is MeshRenderer || r is SkinnedMeshRenderer) && !IsDynamic(r)) found.Add(r); // (2026-11-21) 움직이는 것 제외
        list = found.ToArray();
        colliderCache[col] = list;
        return list;
    }

    /// <summary>(2026-11-21) 가림 처리 대상이 절대 아닌 것: 플레이어·좀비·떨어진 아이템·투사체·물리로 움직이는 것</summary>
    static bool IsDynamic(Renderer r)
    {
        for (var t = r.transform; t != null; t = t.parent)
        {
            string ln = LayerMask.LayerToName(t.gameObject.layer);
            if (ln == "Player" || ln == "Pickup") return true;
            if (t.CompareTag("Player")) return true;
        }
        if (r.GetComponentInParent<PlayerController>() != null || r.GetComponentInParent<ZombieController>() != null
            || r.GetComponentInParent<PickupItem>() != null) return true;
        var rb = r.GetComponentInParent<Rigidbody>();
        return rb != null && !rb.isKinematic; // 투사체·수류탄·튀는 탄약 상자 등
    }

    Entry Begin(Renderer r)
    {
        if (holeMode) return BeginHole(r); // (2026-11-21)
        var originals = r.sharedMaterials;
        var fades = new Material[originals.Length];
        for (int i = 0; i < originals.Length; i++)
            fades[i] = originals[i] != null ? FadeMaterialFor(originals[i]) : null;
        var e = new Entry { renderer = r, originals = originals, fades = fades, amount = 0f };
        r.sharedMaterials = fades;
        entries[r] = e;
        return e;
    }

    /// <summary>(2026-11-21) 구멍 모드 시작: 건물(BuildingCutaway) 재질은 그대로(MPB만), 나머지는 OccluderHole 복사본으로</summary>
    Entry BeginHole(Renderer r)
    {
        var originals = r.sharedMaterials;
        var holes = new Material[originals.Length];
        bool swap = false;
        for (int i = 0; i < originals.Length; i++)
        {
            var m = originals[i];
            if (m == null || (buildingCutawayShader != null && m.shader == buildingCutawayShader)) { holes[i] = m; continue; }
            holes[i] = HoleMaterialFor(m);
            swap = true;
        }
        var e = new Entry { renderer = r, originals = originals, fades = swap ? holes : null, amount = 0f };
        e.building = r.GetComponentInParent<BuildingInterior>(); // (2026-11-21 3차)
        if (e.building == null) { e.building = BuildingContaining(r.bounds.center); e.decor = e.building != null; }
        if (swap) r.sharedMaterials = holes;
        entries[r] = e;
        AttachRing(e); // (2026-11-21 2차)
        return e;
    }

    /// <summary>(2026-11-21 2차) 페더 구간 반투명 링: 원본 자식으로 같은 메시를 링 재질로 (MeshRenderer만, 한 번 만들어 재사용)</summary>
    void AttachRing(Entry e)
    {
        var r = e.renderer;
        if (!(r is MeshRenderer) || ringShader == null) return;
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var src = e.originals;
        var mats = new Material[src.Length];
        for (int i = 0; i < src.Length; i++) mats[i] = src[i] != null ? RingMaterialFor(src[i]) : null;

        if (!ringCache.TryGetValue(r, out var ring) || ring == null)
        {
            var go = new GameObject("_HoleRing");
            go.transform.SetParent(r.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            ring = go.AddComponent<MeshRenderer>();
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ringCache[r] = ring;
        }
        ring.sharedMaterials = mats;
        ring.enabled = true;
        e.ring = ring;
        e.ringSources = src;
    }

    void UpdateRing(Entry e)
    {
        e.ring.enabled = e.renderer.enabled;
        e.ring.GetPropertyBlock(mpb);
        mpb.SetFloat(HoleAmountId, Mathf.SmoothStep(0f, 1f, e.amount));
        mpb.SetFloat(HoleFillId, InteriorFill(e)); // (2026-11-21 3차)
        // 건물: 컷어웨이 값(실내 들어가면 지붕 컷)을 원본 인스턴스 재질에서 그대로
        var src = e.ringSources;
        for (int i = 0; i < src.Length; i++)
        {
            var m = src[i];
            if (m == null || buildingCutawayShader == null || m.shader != buildingCutawayShader) continue;
            mpb.SetFloat(CutHeightId, m.GetFloat(CutHeightId));
            mpb.SetFloat(CutEnabledId, m.GetFloat(CutEnabledId));
            break; // 건물 렌더러 재질은 컷 값이 같음
        }
        e.ring.SetPropertyBlock(mpb);
    }

    /// <summary>(2026-11-21 3차) 건물 내부 가림: 플레이어가 안 들어간 건물이면 본체 1 / 가구·소품 2, 아니면 0</summary>
    static float InteriorFill(Entry e)
    {
        if (e.building == null || e.building.IsPlayerInside) return 0f;
        return e.decor ? 2f : 1f;
    }

    /// <summary>(2026-11-21 3차) 이 위치를 실내 트리거 범위 안에 둔 건물 (없으면 null)</summary>
    BuildingInterior BuildingContaining(Vector3 p)
    {
        foreach (var kv in buildingTriggers)
            if (kv.Key != null && kv.Value.Contains(p)) return kv.Key;
        return null;
    }

    /// <summary>(2026-11-21 2차) 원본 재질 → 링 재질 (원본당 하나). 건물(BuildingCutaway)은 건물 링 셰이더로 속성 전부 복사</summary>
    static Material RingMaterialFor(Material src)
    {
        if (ringMats.TryGetValue(src, out var m) && m != null) return m;
        if (buildingCutawayShader != null && src.shader == buildingCutawayShader && buildingRingShader != null)
        {
            m = new Material(buildingRingShader) { name = src.name + " (HoleRing)" };
            m.CopyPropertiesFromMaterial(src); // 같은 속성 이름 (_MainTex, _Color, _CutHeight, _WallThickness, _EdgeColor, _InnerShade)
        }
        else m = CopyProps(src, new Material(ringShader) { name = src.name + " (HoleRing)" });
        ringMats[src] = m;
        return m;
    }

    void End(Entry e)
    {
        if (e.renderer == null) return;
        if (holeMode)
        {
            // (2026-11-21) 구멍 닫고 원래 재질로 (건물은 재질을 안 바꿨으니 BuildingInterior 컷어웨이 재질 그대로)
            if (e.fades != null) e.renderer.sharedMaterials = e.originals;
            if (e.ring != null) e.ring.enabled = false; // (2026-11-21 2차)
            e.renderer.GetPropertyBlock(mpb);
            mpb.SetFloat(HoleAmountId, 0f);
            mpb.SetFloat(HoleFillId, 0f); // (2026-11-21 3차)
            e.renderer.SetPropertyBlock(mpb);
            return;
        }
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

    /// <summary>(2026-11-21) 원본 재질 속성을 복사한 구멍 재질 (원본당 하나, 공유). 복사 방식은 페이드 재질과 같음</summary>
    static Material HoleMaterialFor(Material src)
    {
        if (holeMats.TryGetValue(src, out var m) && m != null) return m;
        if (holeShader == null) holeShader = Shader.Find("Custom/OccluderHole");
        if (holeShader == null) return src;
        m = CopyProps(src, new Material(holeShader) { name = src.name + " (Hole)" });
        holeMats[src] = m;
        return m;
    }

    /// <summary>원본 재질의 텍스처·색·노멀·금속/거칠기·발광을 복사한 페이드 재질 (원본당 하나, 공유)</summary>
    static Material FadeMaterialFor(Material src)
    {
        if (fadeMats.TryGetValue(src, out var m) && m != null) return m;
        if (fadeShader == null) fadeShader = Shader.Find("Custom/FadeOccluder"); // 아직 쓰는 재질이 없을 때 씬 로드의 UnloadUnusedAssets로 풀릴 수 있음
        if (fadeShader == null) return src;
        m = CopyProps(src, new Material(fadeShader) { name = src.name + " (Fade)" });
        fadeMats[src] = m;
        return m;
    }

    /// <summary>(2026-11-21) 위 FadeMaterialFor의 속성 복사 부분을 구멍 재질과 같이 쓰려고 분리 (내용 그대로)</summary>
    static Material CopyProps(Material src, Material m)
    {
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
        return m;
    }
}

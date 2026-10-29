using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 3 - 파티클 (불/연기/먼지).
///  불 3~5곳: 길가 자동차가 불타는 느낌 (불꽃 + 연기 기둥 + 깜빡이는 붉은 조명)
///  연기 10곳: 건물 지붕에서 희미하게 피어오름
///  먼지: 플레이어 주변을 따라다니며 바람에 날림 (실내에선 꺼짐)
/// 파티클 머티리얼은 Resources/WorldDecor 에셋 (빌드에서 셰이더가 빠지지 않게). 게임 씬 로드 시 자동 생성
/// </summary>
public class WorldFX : MonoBehaviour
{
    public int seed = 4242;
    [Range(3, 5)] public int fires = 4;
    public int roofSmokes = 10;
    public Vector3 windDirection = new Vector3(1f, 0f, 0.4f);

    [Header("불 조명")]
    public Color fireLightColor = new Color(1f, 0.45f, 0.15f);
    public float fireLightIntensity = 2.5f;
    public float fireLightRange = 12f;

    ParticleSystem dust;
    Transform player;
    readonly List<(Light light, float seed)> fireLights = new List<(Light, float)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!WorldDecor.IsGameScene || FindFirstObjectByType<WorldFX>() != null) return;
        new GameObject("WorldFX").AddComponent<WorldFX>();
    }

    Material fireMat, smokeMat, dustMat;

    void Start()
    {
        var rng = new System.Random(seed);
        var group = WorldDecor.Group("FX");
        player = GameObject.FindWithTag("Player")?.transform;

        var flameTex = WorldDecorTextures.Flame();
        var dotTex = WorldDecorTextures.SoftDot("FX_Dot", false);
        var smokeTex = WorldDecorTextures.SoftDot("FX_Smoke", true);
        fireMat = ParticleMat("FireAdditive", flameTex);
        fireMat.renderQueue = 3100; // 연기(3000)보다 나중에 그려서 연기에 묻히지 않게
        smokeMat = ParticleMat("SmokeAlpha", smokeTex);
        dustMat = ParticleMat("SmokeAlpha", dotTex);

        // ── 불: 길가 자동차 위 (서로 30m 이상 떨어진 차) ──
        var spots = new List<Vector3>();
        var cars = WorldDecor.SceneRoot("Proop3");
        if (cars != null)
        {
            var rs = new List<Renderer>(cars.GetComponentsInChildren<Renderer>());
            for (int guard = 0; spots.Count < fires && guard < 200 && rs.Count > 0; guard++)
            {
                // 차 한 대 크기인 렌더러만 (문/바퀴 같은 부품이나 여러 대 묶음 제외)
                var b = rng.Pick(rs).bounds;
                float len = Mathf.Max(b.size.x, b.size.z);
                if (len < 8f || len > 13f || b.size.y < 2.5f || b.size.y > 3.8f) continue; // 이 씬의 차는 2배 크기 (길이 약 11m, 높이 3m)
                var top = new Vector3(b.center.x, b.max.y - 0.05f, b.center.z); // 지붕 위 (아래면 탑뷰에서 지붕에 가려 불꽃이 안 보였음)
                if (spots.TrueForAll(s => Vector3.Distance(s, top) > 30f)) spots.Add(top);
            }
        }
        for (int guard = 0; spots.Count < fires && guard < 50; guard++)
            if (WorldDecor.RoadPoint(rng, out var hit, out _)) spots.Add(hit.point);
        foreach (var p in spots) MakeFire(p, group, rng);

        // ── 연기: 건물 지붕 ──
        var bRoot = WorldDecor.SceneRoot("Buildings");
        int smokes = 0;
        if (bRoot != null)
        {
            var bs = bRoot.GetComponentsInChildren<MeshRenderer>();
            var used = new HashSet<MeshRenderer>();
            for (int guard = 0; smokes < roofSmokes && guard < 100 && bs.Length > 0; guard++)
            {
                var b = rng.Pick(bs);
                if (!used.Add(b) || b.bounds.size.y < 6f) continue;
                var bb = b.bounds;
                var top = new Vector3(bb.center.x + rng.Range(-0.25f, 0.25f) * bb.size.x, bb.max.y, bb.center.z + rng.Range(-0.25f, 0.25f) * bb.size.z);
                MakeSmoke(top, group, faint: true);
                smokes++;
            }
        }

        // ── 먼지: 플레이어를 따라다님 ──
        dust = MakeDust(group);

        Debug.Log($"[WorldFX] 불 {spots.Count}곳 (조명 {fireLights.Count}), 지붕 연기 {smokes}곳, 먼지 1");
    }

    static Material ParticleMat(string asset, Texture tex)
    {
        var src = Resources.Load<Material>("WorldDecor/" + asset);
        var m = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        return m;
    }

    static ParticleSystem NewSystem(string name, Transform parent, Vector3 pos, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // 위로 뿜음
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    void MakeFire(Vector3 pos, Transform group, System.Random rng)
    {
        var root = new GameObject("Fire").transform;
        root.SetParent(group, false); root.position = pos;

        // 불꽃: 노랑 → 주황 → 빨강, 위로 짧게 솟으며 작아짐
        var ps = NewSystem("Flames", root, pos, fireMat);
        var main = ps.main;
        main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        var em = ps.emission; em.rateOverTime = 90f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.8f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.35f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.4f), new GradientColorKey(new Color(0.8f, 0.12f, 0.05f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 1.5f;
        ps.Play();

        // 검은 연기 기둥
        MakeSmoke(pos + Vector3.up * 2.5f, root, faint: false);

        // 깜빡이는 붉은 조명
        var lgo = new GameObject("FireLight");
        lgo.transform.SetParent(root, false); lgo.transform.position = pos + Vector3.up * 1.5f;
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point; l.color = fireLightColor; l.range = fireLightRange; l.intensity = fireLightIntensity;
        l.shadows = LightShadows.None;
        fireLights.Add((l, rng.Range(0f, 100f)));
    }

    void MakeSmoke(Vector3 pos, Transform parent, bool faint)
    {
        // 천천히 올라가며 커지고 바람에 밀림. faint = 지붕 위 희미한 연기, 아니면 불 위 짙은 연기
        var ps = NewSystem(faint ? "RoofSmoke" : "FireSmoke", parent, pos, smokeMat);
        var main = ps.main;
        main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(faint ? 6f : 4f, faint ? 9f : 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(faint ? 2f : 1.5f, faint ? 3.5f : 2.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = faint ? 40 : 60;
        var em = ps.emission; em.rateOverTime = faint ? 4f : 9f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = faint ? 1.2f : 0.6f;
        var col = ps.colorOverLifetime; col.enabled = true;
        float a = faint ? 0.22f : 0.4f;
        Color c0 = faint ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.15f, 0.14f, 0.13f), c1 = faint ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.35f, 0.34f, 0.33f);
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a, 0.15f), new GradientAlphaKey(a * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        Vector3 w = windDirection.normalized * 0.8f;
        vel.x = new ParticleSystem.MinMaxCurve(w.x); vel.y = new ParticleSystem.MinMaxCurve(0.3f); vel.z = new ParticleSystem.MinMaxCurve(w.z);
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        ps.Play();
    }

    ParticleSystem MakeDust(Transform group)
    {
        // 플레이어 주변 40×40m에 작은 먼지가 바람 방향으로 날림
        var ps = NewSystem("Dust", group, player != null ? player.position : Vector3.zero, dustMat);
        ps.transform.rotation = Quaternion.identity;
        var main = ps.main;
        main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startColor = new Color(0.78f, 0.72f, 0.6f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var em = ps.emission; em.rateOverTime = 45f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 3f, 40f); sh.position = new Vector3(0f, 1.5f, 0f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        Vector3 w = windDirection.normalized * 2.2f;
        vel.x = new ParticleSystem.MinMaxCurve(w.x * 0.7f, w.x * 1.3f); vel.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.25f); vel.z = new ParticleSystem.MinMaxCurve(w.z * 0.7f, w.z * 1.3f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.4f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ps.Play();
        return ps;
    }

    void LateUpdate()
    {
        // 불빛 깜빡임 (느린 일렁임 + 빠른 떨림)
        float t = Time.time;
        foreach (var (l, s) in fireLights)
            if (l != null) l.intensity = fireLightIntensity * (0.75f + 0.2f * Mathf.PerlinNoise(t * 1.3f, s) + 0.1f * Mathf.PerlinNoise(t * 9f, s + 7f));

        // 먼지: 플레이어 따라다님, 실내에선 끔
        if (dust != null)
        {
            if (player != null) dust.transform.position = player.position;
            bool indoors = BuildingInterior.IsAnyoneIndoors;
            if (indoors && dust.isPlaying) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            else if (!indoors && !dust.isPlaying) dust.Play();
        }
    }
}

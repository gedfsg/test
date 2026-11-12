using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// 발표 영상 "날씨별 소음 반경 비교" 20초 시나리오 (2026-11-12). Tools → Presentation → Record Weather Comparison이 붙이고 녹화함.
///
///  0~2초   타이틀 카드
///  2~10초  맑음: 2.5초에 소총 1발 → 실제 반경(NoiseSystem) 주황 원 → 반경 안 좀비가 몰려옴
///  10~12초 전환 (어둡게 → "맑음 → 비" → 날씨 비 + 좀비 원위치)
///  12~20초 비: 14초에 같은 소총 1발 → 반경 ×0.5 → 안쪽 좀비만 반응
///
/// 공정한 비교를 위해 영상 속 좀비 20마리는 "소리로만" 반응하게 함:
///  시야 감지(detectionRange)·근접 감각 0, 좀비다움(못 들은 척 25%·멍한 좀비 15% 랜덤) 끔, 청력 배율 1,
///  규칙 기반 두뇌(ZombieFSMBrain) + 배회 끔 (ML 두뇌는 소리 없이도 플레이어 쪽으로 다가감).
///  → 원 안 좀비 = 반응, 원 밖 = 무반응 (NoiseSystem.Emit 규칙 그대로)
/// 좀비 배치는 실제 반경 R 기준: 안쪽 5마리 0.3~0.45R(비에서도 들림), 가운데 10마리 0.6~0.9R(맑음만), 바깥 5마리 1.1~1.3R(못 들음)
/// </summary>
public class WeatherComparisonDirector : MonoBehaviour
{
    [Header("에디터 도구가 넣어줌")]
    public Font sourceFont;            // 한글 글자 (Pretendard .otf → 메모리 TMP 폰트)
    public WeaponData rifle;           // 쏠 총 (소음 반경 = rifle.GetNoiseRadius())

    [Header("타임라인 (초)")]
    public float titleEnd = 2f, clearShot = 2.5f, transitionStart = 10f, transitionEnd = 12f, rainShot = 14f, end = 20f;
    public int zombieCount = 20;
    public float cameraFov = 60f;
    public float ringHold = 4f;        // 원 표시 시간 (기본 NoiseVisualizer 1.5초는 영상에서 너무 짧음)

    public bool Ready { get; private set; }
    public bool Finished { get; private set; }
    public string Report { get; private set; } = "";

    float radiusClear;
    Vector3 center;
    Transform player;
    readonly List<(Vector3 pos, float yaw)> slots = new List<(Vector3, float)>();
    readonly List<ZombieController> zombies = new List<ZombieController>();
    readonly Dictionary<ZombieController, LineRenderer> markers = new Dictionary<ZombieController, LineRenderer>();
    readonly HashSet<ZombieController> heard = new HashSet<ZombieController>();
    readonly Dictionary<ZombieController, Vector3> startPos = new Dictionary<ZombieController, Vector3>();
    GameObject zombiePrefab;
    Material markerMat, rainMat;
    Vector3 fireDir = Vector3.right;
    Transform aimPoint;

    // UI
    TMP_FontAsset font;
    CanvasGroup titleGroup, transitionGroup;
    TextMeshProUGUI weatherLabel, caption, counter, transitionText;
    Image weatherBadge;

    static readonly Color Orange = new Color32(0xFF, 0x8C, 0x00, 0xFF);
    static readonly Color HeardRed = new Color32(0xE5, 0x39, 0x35, 0xFF);
    static readonly Color IdleGray = new Color(0.85f, 0.85f, 0.85f, 0.9f);

    IEnumerator Start()
    {
        radiusClear = rifle != null ? rifle.GetNoiseRadius() : NoiseSystem.RadiusFor(WeaponType.AR);
        PrepareWorld();
        BuildUI();
        yield return FindOpenArea();
        PlacePlayerAndCamera();
        PlanSlots();
        SpawnZombies();
        var vis = gameObject.AddComponent<NoiseVisualizer>();
        vis.duration = ringHold;
        vis.drawOnTop = true; // 지붕·나무에 가려지지 않게
        yield return new WaitForSeconds(1.5f); // 좀비 Awake/NavMesh 안착
        Ready = true;
    }

    /// <summary>녹화 시작 직후 에디터 도구가 호출</summary>
    public void Begin() => StartCoroutine(Timeline());

    IEnumerator Timeline()
    {
        float t0 = Time.time;
        float T() => Time.time - t0;

        // ── 0~2초: 타이틀 ──
        SetWeatherLabel("맑음", new Color(1f, 0.78f, 0.2f));
        caption.text = ""; counter.text = "";
        titleGroup.alpha = 1f;
        while (T() < titleEnd) { titleGroup.alpha = Mathf.Clamp01((titleEnd - T()) / 0.4f); yield return null; }
        titleGroup.alpha = 0f;

        // ── 2~10초: 맑음 ──
        caption.text = $"소총 1발 · 소음 반경 {radiusClear:0.#}m";
        counter.text = $"소리를 들은 좀비  0 / {zombies.Count}";
        while (T() < clearShot) yield return null;
        int nClear = Fire();
        caption.text = $"소총 1발 · 소음 반경 {LastRadius:0.#}m";
        while (T() < transitionStart) { UpdateCounter(); yield return null; }
        int movedClear = CountApproached();

        // ── 10~12초: 전환 ──
        transitionText.text = "맑음  →  비";
        bool switched = false;
        while (T() < transitionEnd)
        {
            float k = (T() - transitionStart) / (transitionEnd - transitionStart); // 0~1
            transitionGroup.alpha = k < 0.35f ? k / 0.35f : k > 0.65f ? (1f - k) / 0.35f : 1f;
            if (!switched && k >= 0.4f)
            {
                switched = true;
                SetRain(true);
                StartDemoRain();
                ResetZombies();
                SetWeatherLabel("비", new Color(0.45f, 0.7f, 1f));
                caption.text = $"소총 1발 · 소음 반경 {radiusClear * WeatherManager.NoiseMultiplier:0.#}m (×{WeatherManager.NoiseMultiplier:0.#})";
                counter.text = $"소리를 들은 좀비  0 / {zombies.Count}";
            }
            yield return null;
        }
        transitionGroup.alpha = 0f;

        // ── 12~20초: 비 ──
        while (T() < rainShot) yield return null;
        int nRain = Fire();
        caption.text = $"소총 1발 · 소음 반경 {LastRadius:0.#}m (×{WeatherManager.NoiseMultiplier:0.#})";
        while (T() < end) { UpdateCounter(); yield return null; }
        int movedRain = CountApproached();

        Report = $"맑음: 반경 {radiusClear:0.#}m, 들음 {nClear}마리, 플레이어 쪽으로 3m+ 이동 {movedClear}마리 / " +
                 $"비: 반경 {radiusClear * WeatherManager.NoiseMultiplier:0.#}m, 들음 {nRain}마리, 이동 {movedRain}마리 (총 {zombies.Count}마리)";
        Debug.Log("[WeatherCompare] " + Report);
        Finished = true;
    }

    float LastRadius;

    // ───────────── 준비 ─────────────
    void PrepareWorld()
    {
        var pgo = GameObject.FindWithTag("Player");
        player = pgo.transform;
        var hp = pgo.GetComponent<Health>(); if (hp != null) hp.isInvincible = true;
        foreach (var s in FindObjectsByType<ZombieSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var s in FindObjectsByType<HunterPackSpawner>(FindObjectsSortMode.None)) s.enabled = false;
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None)) Destroy(z.gameObject);
        SpawnProtection.Cancel();
        Time.timeScale = 1f;

        // 낮 한가운데, 맑음 (전환 없이 바로)
        var dnc = DayNightCycle.Instance;
        if (dnc != null)
        {
            typeof(DayNightCycle).GetMethod("SetPhase", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(dnc, new object[] { DayNightCycle.Phase.Day });
            typeof(DayNightCycle).GetField("phaseTimer", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(dnc, dnc.dayDuration * 0.4f);
        }
        SetRain(false);

        // 게임 HUD 숨김 (영상용 자막만)
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (c.isRootCanvas) c.enabled = false;

        // 좀비 프리팹: 일반 좀비 (사냥꾼 RL_Shoot 제외)
        foreach (var sp in Resources.FindObjectsOfTypeAll<ZombieSpawner>())
        {
            if (sp.zombiePrefabs == null) continue;
            foreach (var p in sp.zombiePrefabs)
                if (p != null && p.name.Contains("Normal") && !p.name.Contains("Shoot")) { zombiePrefab = p; break; }
            if (zombiePrefab == null) foreach (var p in sp.zombiePrefabs) if (p != null) { zombiePrefab = p; break; }
            if (zombiePrefab != null) break;
        }
        markerMat = OnTopMaterial();
    }

    /// <summary>지붕·나무 위에도 보이게 깊이 검사 없이 그리는 재질 (UI/Default + ZTest Always). 원·고리 표시용</summary>
    public static Material OnTopMaterial()
    {
        var m = new Material(Shader.Find("UI/Default"));
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4000; // 일반 반투명보다 나중에
        return m;
    }

    void SetRain(bool rain)
    {
        var wm = WeatherManager.Instance;
        if (wm == null) return;
        wm.SetWeather(rain ? WeatherManager.Weather.Rain : WeatherManager.Weather.Clear);
        // 1.5초 서서히 바뀌는 대신 즉시 (전환 화면 가린 동안)
        typeof(WeatherManager).GetField("rainAmount", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(wm, rain ? 1f : 0f);
        typeof(WeatherManager).GetField("fogAmount", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(wm, 0f);
    }

    /// <summary>나무·건물·가로등에 안 가리고 NavMesh가 고르게 깔린 넓은 평지 (반경 1.3R) 찾기. 여러 프레임에 나눠 계산</summary>
    IEnumerator FindOpenArea()
    {
        float need = radiusClear * 1.3f + 4f;
        var ground = GameObject.Find("Ground");
        Bounds gb = ground != null && ground.GetComponent<Collider>() != null ? ground.GetComponent<Collider>().bounds : new Bounds(Vector3.zero, Vector3.one * 700f);
        var triggers = new List<Collider>();
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
            foreach (var c in b.GetComponents<Collider>()) if (c.isTrigger) triggers.Add(c);
        // 위에서 가리는 것: 렌더러 기준 (1차 녹화: 낮은 폴리 나무는 콜라이더가 없어서 콜라이더 검사로는 못 걸렀음 → 나무 수관이 화면을 덮음)
        var tall = new List<Bounds>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            var b = r.bounds;
            if (b.size.x > 120f || b.size.z > 120f) continue;               // 지형·수면판
            if (b.max.y < 17.5f) continue;                                   // 지면(≈15) + 2.5m 아래
            string nm = r.name;
            if (nm == "Ground" || nm.StartsWith("road") || nm.StartsWith("Zone_")) continue;
            tall.Add(b);
        }
        var cands = new List<(float score, Vector3 c)>();
        float bestScore = float.MaxValue; center = player.position;
        int work = 0;
        for (float x = gb.min.x + need; x <= gb.max.x - need; x += 12f)
            for (float z = gb.min.z + need; z <= gb.max.z - need; z += 12f)
            {
                if (++work % 25 == 0) yield return null;
                if (!NavMesh.SamplePosition(new Vector3(x, 15f, z), out NavMeshHit ch, 1.5f, NavMesh.AllAreas)) continue;
                Vector3 c0 = ch.position;
                if (c0.y > 16.5f || WaterZone.IsUnderwater(c0) || Inside(triggers, c0)) continue;
                // 링 3개 × 16방향이 모두 같은 높이의 바깥 NavMesh
                int ok = 0, total = 0; float dy = 0f;
                foreach (float rr in new[] { 0.35f, 0.75f, 1.2f })
                    for (int a = 0; a < 16; a++)
                    {
                        total++;
                        Vector3 q = c0 + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * (radiusClear * rr);
                        if (!NavMesh.SamplePosition(q, out NavMeshHit qh, 1.2f, NavMesh.AllAreas)) continue;
                        if (Mathf.Abs(qh.position.y - c0.y) > 1.2f || WaterZone.IsUnderwater(qh.position) || Inside(triggers, qh.position)) continue;
                        ok++; dy = Mathf.Max(dy, Mathf.Abs(qh.position.y - c0.y));
                    }
                if (ok < total - 4) continue;
                // 가림 면적: 화면 영역(반경 need) 안에 들어오는 높은 렌더러들의 바닥 면적 합 (m²)
                float covered = 0f;
                foreach (var b in tall)
                {
                    if (b.max.y < c0.y + 2.5f) continue;
                    float cx = Mathf.Clamp(c0.x, b.min.x, b.max.x), cz = Mathf.Clamp(c0.z, b.min.z, b.max.z);
                    if ((cx - c0.x) * (cx - c0.x) + (cz - c0.z) * (cz - c0.z) > need * need) continue;
                    covered += Mathf.Min(b.size.x * b.size.z, 400f);
                }
                float score = covered + (total - ok) * 20f + dy * 10f;
                cands.Add((score, c0));
            }

        // 점수 좋은 후보부터: 소리가 들리는 범위(0.35R·0.75R 링) 좀비 자리에서 플레이어까지 곧게 걸어올 수 있어야 함
        // (2차 녹화: 농장 울타리 우리 안 좀비가 소리를 듣고도 못 나옴)
        cands.Sort((a, b) => a.score.CompareTo(b.score));
        var path = new NavMeshPath();
        int checkedN = 0;
        foreach (var (score, c0) in cands)
        {
            if (++checkedN > 60) break;
            yield return null;
            bool good = true;
            foreach (float rr in new[] { 0.35f, 0.75f })
            {
                for (int a = 0; a < 12 && good; a++)
                {
                    Vector3 q = c0 + Quaternion.Euler(0f, a * 30f + 15f, 0f) * Vector3.forward * (radiusClear * rr);
                    if (!NavMesh.SamplePosition(q, out NavMeshHit qh, 1.2f, NavMesh.AllAreas)) continue;
                    if (!NavMesh.CalculatePath(qh.position, c0, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { good = false; break; }
                    float len = 0f; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    if (len > Vector3.Distance(qh.position, c0) * 1.25f + 2f) good = false;
                }
                if (!good) break;
            }
            if (good) { bestScore = score; center = c0; break; }
        }
        Debug.Log($"[WeatherCompare] 촬영 위치 {center} (가림 점수 {bestScore:0.#}, 높은 렌더러 {tall.Count}개 검사)");
    }

    static bool Inside(List<Collider> triggers, Vector3 p)
    {
        foreach (var t in triggers) if (t != null && t.bounds.Contains(p + Vector3.up)) return true;
        return false;
    }

    void PlacePlayerAndCamera()
    {
        var rb = player.GetComponent<Rigidbody>();
        player.position = center + Vector3.up * 0.1f;
        if (rb != null) { rb.position = player.position; rb.linearVelocity = Vector3.zero; }
        player.rotation = Quaternion.LookRotation(fireDir);

        // 플레이어 표시 (흰 고리)
        MakeRing(player, 2.2f, 0.45f, Color.white);

        var cam = Camera.main;
        foreach (var mb in cam.GetComponents<MonoBehaviour>())
            if (mb is CameraFollow || mb is CameraZoom || mb.GetType().Name == "CameraRotate") mb.enabled = false;
        // 바로 위에서 내려다봄. 화면 세로에 바깥 좀비(1.3R)까지 다 들어오는 높이
        float halfV = radiusClear * 1.3f + 5f;
        float h = halfV / Mathf.Tan(cameraFov * 0.5f * Mathf.Deg2Rad);
        cam.fieldOfView = cameraFov;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, h + 50f);
        cam.transform.SetPositionAndRotation(center + Vector3.up * h, Quaternion.Euler(90f, 0f, 0f));
        Debug.Log($"[WeatherCompare] 카메라 높이 {h:0}m (FOV {cameraFov}, 화면 세로 반폭 {halfV:0}m)");
    }

    void LateUpdate()
    {
        // 다른 스크립트가 카메라를 움직여도 고정
        if (Ready && Camera.main != null)
        {
            float halfV = radiusClear * 1.3f + 5f;
            float h = halfV / Mathf.Tan(cameraFov * 0.5f * Mathf.Deg2Rad);
            Camera.main.transform.SetPositionAndRotation(center + Vector3.up * h, Quaternion.Euler(90f, 0f, 0f));
            Camera.main.fieldOfView = cameraFov;
        }
        foreach (var kv in markers)
            if (kv.Key != null && kv.Value != null) kv.Value.transform.position = kv.Key.transform.position + Vector3.up * 0.3f;
    }

    /// <summary>좀비 자리: 발사 방향(동쪽) ±25°는 비움 (총알이 좀비를 맞혀 깨우지 않게)</summary>
    void PlanSlots()
    {
        slots.Clear();
        var rnd = new System.Random(20261112);
        var groups = new (int n, float r0, float r1)[] { (5, 0.30f, 0.45f), (10, 0.60f, 0.90f), (5, 1.10f, 1.28f) };
        foreach (var g in groups)
        {
            for (int i = 0; i < g.n; i++)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    // 30°~330° 구간에 고르게 + 약간 흔들기 (0° = 발사 방향)
                    float ang = 30f + (300f / g.n) * (i + 0.5f) + (float)(rnd.NextDouble() - 0.5) * (attempt == 0 ? 10f : 40f);
                    float dist = radiusClear * Mathf.Lerp(g.r0, g.r1, (float)rnd.NextDouble());
                    Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * fireDir;
                    if (!NavMesh.SamplePosition(center + dir * dist, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                    float real = Vector3.Distance(new Vector3(hit.position.x, 0, hit.position.z), new Vector3(center.x, 0, center.z));
                    if (real < radiusClear * g.r0 - 0.5f || real > radiusClear * g.r1 + 0.5f) continue;
                    bool close = false;
                    foreach (var s in slots) if ((s.pos - hit.position).sqrMagnitude < 3.5f * 3.5f) { close = true; break; }
                    if (close) continue;
                    slots.Add((hit.position, (float)rnd.NextDouble() * 360f));
                    break;
                }
            }
        }
        int inner = 0, mid = 0, outer = 0;
        foreach (var s in slots)
        {
            float d = Vector3.Distance(new Vector3(s.pos.x, 0, s.pos.z), new Vector3(center.x, 0, center.z));
            if (d < radiusClear * 0.5f) inner++; else if (d < radiusClear) mid++; else outer++;
        }
        Debug.Log($"[WeatherCompare] 좀비 자리 {slots.Count}곳 (비에서도 들림 {inner} / 맑음만 {mid} / 못 들음 {outer})");
    }

    void SpawnZombies()
    {
        foreach (var z in zombies) if (z != null) Destroy(z.gameObject);
        foreach (var m in markers.Values) if (m != null) Destroy(m.gameObject);
        zombies.Clear(); markers.Clear(); heard.Clear(); startPos.Clear();
        if (zombiePrefab == null) { Debug.LogError("[WeatherCompare] 좀비 프리팹을 못 찾음"); return; }
        foreach (var (pos, yaw) in slots)
        {
            var go = Instantiate(zombiePrefab, pos, Quaternion.Euler(0f, yaw, 0f));
            go.name = zombiePrefab.name + "_Demo";
            var z = go.GetComponent<ZombieController>();
            if (z == null) { Destroy(go); continue; }
            // 소리로만 반응 (시야·근접 감각·랜덤 성격 끔)
            z.detectionRange = 0f;
            z.vision.nearSenseRadius = 0f;
            z.vision.hearingSensitivity = 1f;
            z.zombieQuirks = false;
            z.huntPlayer = false;
            // 두뇌: 강화학습(ML) 두뇌는 소리와 상관없이 플레이어 거리를 늘 알고 다가가서(1차 녹화: 비에서 못 들은 좀비 7마리도 접근)
            // → 게임의 규칙 기반 두뇌(ZombieFSMBrain, ML 실패 시 쓰는 기본 두뇌)로. 배회도 끔 → 소리를 들은 좀비만 움직임
            if (z.brain != null && !(z.brain is ZombieFSMBrain)) z.brain.enabled = false;
            var fsm = go.GetComponent<ZombieFSMBrain>();
            if (fsm != null) fsm.enableWander = false;
            zombies.Add(z);
            startPos[z] = pos;
            markers[z] = MakeRing(null, 1.6f, 0.5f, IdleGray);
        }
    }

    void ResetZombies() => SpawnZombies();

    /// <summary>
    /// 영상용 빗줄기: 게임 비(WeatherManager)는 플레이어 머리 위 45m 상자에 굵기 0.025m라 87m 위 카메라에선 안 보임
    /// → 화면 전체(카메라 아래 30m 높이 상자)에 굵은 빗줄기를 추가 (게임 비도 그대로 같이 내림)
    /// </summary>
    void StartDemoRain()
    {
        var go = new GameObject("DemoRain");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(center + Vector3.up * 30f, Quaternion.Euler(90f, 0f, 0f)); // 로컬 +Z(방출) = 아래
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.0f;
        main.startSpeed = 32f;
        main.startSize = 0.16f;
        main.startColor = new Color(0.82f, 0.88f, 1f, 0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 12000;
        main.prewarm = true;
        var em = ps.emission; em.rateOverTime = 9000f;
        float halfV = radiusClear * 1.3f + 5f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(halfV * 2f * 1.9f, halfV * 2f * 1.1f, 1f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 2f; r.velocityScale = 0.06f;
        r.sharedMaterial = rainMat != null ? rainMat : (rainMat = new Material(Shader.Find("Sprites/Default"))); // 반투명 막대 (깊이 검사 있음)
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    LineRenderer MakeRing(Transform follow, float radius, float width, Color color)
    {
        var go = new GameObject("DemoRing");
        if (follow != null) { go.transform.SetParent(follow, false); go.transform.localPosition = Vector3.up * 0.3f; }
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 40; lr.widthMultiplier = width;
        lr.alignment = LineAlignment.TransformZ;
        lr.sharedMaterial = markerMat; lr.startColor = lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int i = 0; i < 40; i++) { float a = i * Mathf.PI * 2f / 40; lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f)); }
        return lr;
    }

    /// <summary>플레이어 총(소총)으로 실제 1발 → Weapon.Shoot → NoiseSystem.Emit. 들은 좀비 수 반환</summary>
    int Fire()
    {
        heard.Clear();
        var pc = player.GetComponent<PlayerController>();
        var weapon = pc != null ? typeof(PlayerController).GetField("rangedWeapon", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pc) as Weapon : null;
        float radius = 0f;
        System.Action<Vector3, float> grab = (p, r) => radius = r;
        NoiseSystem.Emitted += grab;
        if (weapon != null && rifle != null && weapon.isActiveAndEnabled)
        {
            if (aimPoint == null) aimPoint = new GameObject("DemoAim").transform;
            aimPoint.position = player.position + fireDir * 30f + Vector3.up * 1.2f;
            var oldAim = weapon.aimTarget;
            weapon.aimTarget = aimPoint;
            weapon.ChangeWeaponData(rifle);
            typeof(Weapon).GetField("lastFireTime", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(weapon, -999f);
            weapon.TryFireSingle();
            weapon.aimTarget = oldAim;
        }
        if (radius <= 0f) radius = NoiseSystem.Emit(player.position + Vector3.up * 1.2f, rifle != null ? rifle.GetNoiseRadius() : NoiseSystem.RadiusFor(WeaponType.AR)); // 총이 없으면 소음만
        NoiseSystem.Emitted -= grab;
        LastRadius = radius;
        foreach (var z in NoiseSystem.LastHeard) if (zombies.Contains(z)) heard.Add(z);
        foreach (var z in heard) if (markers.TryGetValue(z, out var m) && m != null) m.startColor = m.endColor = HeardRed;
        UpdateCounter();
        return heard.Count;
    }

    void UpdateCounter() => counter.text = $"소리를 들은 좀비  {heard.Count} / {zombies.Count}";

    int CountApproached()
    {
        int n = 0;
        foreach (var z in zombies)
        {
            if (z == null || !startPos.TryGetValue(z, out Vector3 p0)) continue;
            float d0 = Vector3.Distance(p0, center), d1 = Vector3.Distance(z.transform.position, center);
            if (d0 - d1 > 3f) n++;
        }
        return n;
    }

    // ───────────── UI ─────────────
    void BuildUI()
    {
        if (sourceFont != null) { font = TMP_FontAsset.CreateFontAsset(sourceFont); }
        var cgo = new GameObject("WeatherCompareUI");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 좌상단 날씨 배지
        weatherBadge = Panel(cgo.transform, "Badge", new Vector2(0, 1), new Vector2(40, -40), new Vector2(260, 100), new Color(0, 0, 0, 0.55f));
        weatherLabel = Label(weatherBadge.transform, "맑음", 60, Color.white, Vector2.zero, new Vector2(260, 100));

        // 우상단 반응 카운터
        var cnt = Panel(cgo.transform, "Counter", new Vector2(1, 1), new Vector2(-40, -40), new Vector2(560, 90), new Color(0, 0, 0, 0.55f));
        counter = Label(cnt.transform, "", 40, Color.white, Vector2.zero, new Vector2(560, 90));

        // 하단 자막
        var cap = Panel(cgo.transform, "Caption", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(960, 110), new Color(0, 0, 0, 0.6f)); // 좌하단 범례(~420px)와 안 겹치게
        caption = Label(cap.transform, "", 52, Orange, Vector2.zero, new Vector2(960, 110));

        // 범례 (좌하단)
        var leg = Panel(cgo.transform, "Legend", new Vector2(0, 0), new Vector2(40, 50), new Vector2(380, 130), new Color(0, 0, 0, 0.45f));
        // (특수 기호 ●○는 Pretendard에 없을 수 있어 글자로만 - 없으면 TMP 공용 대체 폰트 에셋에 글리프가 추가돼 파일이 바뀜)
        var l1 = Label(leg.transform, "<color=#E53935>빨간 고리</color> 소리를 들은 좀비", 28, Color.white, new Vector2(0, 22), new Vector2(380, 50));
        var l2 = Label(leg.transform, "<color=#DDDDDD>회색 고리</color> 못 들음 · 흰 고리 플레이어", 26, Color.white, new Vector2(0, -24), new Vector2(380, 50));
        l1.alignment = l2.alignment = TextAlignmentOptions.Center;

        // 타이틀 카드
        var title = Panel(cgo.transform, "Title", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080), new Color(0.06f, 0.07f, 0.09f, 0.92f));
        Label(title.transform, "날씨별 소음 반경 비교", 96, Color.white, new Vector2(0, 50), new Vector2(1600, 140));
        Label(title.transform, "같은 소총 1발 · 같은 좀비 20마리 · 맑음 vs 비", 40, new Color(1f, 0.75f, 0.4f), new Vector2(0, -60), new Vector2(1600, 70));
        titleGroup = title.gameObject.AddComponent<CanvasGroup>();

        // 전환 화면
        var tr = Panel(cgo.transform, "Transition", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080), new Color(0.03f, 0.04f, 0.06f, 0.94f));
        transitionText = Label(tr.transform, "", 90, Color.white, Vector2.zero, new Vector2(1600, 140));
        transitionGroup = tr.gameObject.AddComponent<CanvasGroup>();
        transitionGroup.alpha = 0f;
    }

    void SetWeatherLabel(string text, Color accent)
    {
        weatherLabel.text = text;
        weatherLabel.color = accent;
    }

    Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>(); img.color = color;
        return img;
    }

    TextMeshProUGUI Label(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = box;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = color;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        return t;
    }

    void OnDestroy()
    {
        if (markerMat != null) Destroy(markerMat);
        if (rainMat != null) Destroy(rainMat);
        if (font != null) Destroy(font);
    }
}

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Day 7 "구조 헬기 탑승" 컷신. 플레이어가 열린 헬리패드(EscapePoint)에 들어가면 Play() → 타이머 없이 영화처럼 자동 진행.
///   Approach (10초): 하늘(헬리패드 위 80m, 수평 100m)에서 헬기 등장 → 곡선으로 헬리패드 위 10m까지 (EaseInOut)
///   Hover    (2초) : 위아래 ±0.3m 흔들림 + 좀비 떼 50마리 소환(ZombieSpawner.SpawnHorde) → 헬리패드로 몰려듦
///   Boarding (4초) : 플레이어가 헬기 바로 아래로 → 로프를 타고 헬기 몸체까지 올라감 → 탑승(숨김)
///   TakeOff  (5초) : 헬기 상승(+60m)하며 멀리(수평 100m) 날아감, 지상 좀비들은 멈춰서 하늘을 올려다봄
///   Done           : 전체 화면 페이드아웃(1.5초) → MissionManager.Escape() → "GAME CLEAR - 생존자 구출 성공"
/// 컷신 중: 플레이어 조작·일시정지·인벤토리 잠금(MenuUI.CutsceneActive), 플레이어 무적, HUD 숨김 + 위아래 검은 띠,
///          시네마틱 카메라(CinematicCamera)로 전환 (끝나면 메인 카메라·HUD 복원).
/// 헬기는 에셋 없이 기본 도형(큐브·구·실린더)으로 조립, 로터 소리도 코드로 만듦.
/// </summary>
public class HelicopterCutscene : MonoBehaviour
{
    public enum Phase { Idle, Approach, Hover, Boarding, TakeOff, Done }

    public static HelicopterCutscene Instance { get; private set; }
    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    /// <summary>컷신 진행 중 (헬리패드 빛기둥 숨김 등)</summary>
    public static bool Playing => Instance != null && Instance.CurrentPhase != Phase.Idle;

    [Header("시간 (초)")]
    public float approachTime = 10f;
    public float hoverTime = 2f;
    public float boardingTime = 4f;
    public float takeOffTime = 5f;
    public float fadeOutTime = 1.5f;

    [Header("헬기 경로 (m)")]
    public float spawnHeight = 80f;
    public float spawnDistance = 100f;
    public float hoverHeight = 10f;
    public float takeOffRise = 60f;
    public float takeOffDistance = 100f;
    public float rotorSpeed = 720f; // 도/초

    [Header("좀비 떼")]
    public float hordeRadius = 50f;
    public int hordeCount = 50;

    Transform heli, mainRotor, tailRotor;
    AudioSource rotorAudio;
    Transform player;
    Vector3 pad, back;
    readonly List<ZombieController> horde = new List<ZombieController>();

    // HUD·화면 연출
    Canvas overlay;
    RectTransform barTop, barBottom;
    Image fade;
    TextMeshProUGUI caption;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    /// <summary>씬에 하나 (없으면 만듦)</summary>
    public static HelicopterCutscene Get()
    {
        if (Instance == null) new GameObject("HelicopterCutscene").AddComponent<HelicopterCutscene>();
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Play(Vector3 landingPos)
    {
        if (CurrentPhase != Phase.Idle) return;
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        player = p.transform;
        // 플레이어 조작 비활성화 (이동·사격·조준·카메라·일시정지·인벤토리)
        var inv = FindFirstObjectByType<InventoryUI>();
        if (inv != null && inv.isInventoryOpen) inv.ToggleInventory();
        MenuUI.CutsceneActive = true;
        MenuUI.RefreshGameplayInput();
        var hp = p.GetComponent<Health>();
        if (hp != null) hp.isInvincible = true; // 좀비 떼가 몰려와도 컷신 중엔 안 죽음
        StartCoroutine(Sequence(landingPos));
    }

    IEnumerator Sequence(Vector3 pos)
    {
        pad = pos;
        back = PickApproachDirection(pos);
        Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
        Vector3 hover = pos + Vector3.up * hoverHeight;

        BuildOverlay();
        StartCoroutine(Letterbox(true));
        StartCoroutine(Caption(Localization.T("구조 헬기 접근 중..."), 3.5f));

        // ── Approach (10초): 하늘에서 곡선으로 접근 ──
        CurrentPhase = Phase.Approach;
        Vector3 from = pos + Vector3.up * spawnHeight + back * spawnDistance;
        Vector3 ctrl = pos + back * 35f + side * 30f + Vector3.up * 45f; // 옆으로 휘어 들어오는 곡선
        heli = BuildHelicopter(from).transform;
        var targets = new CinematicCamera.Targets { heli = heli, player = player, pad = pos, approachFrom = back };
        CinematicCamera.Begin(targets);
        CinematicCamera.SetShot(CinematicCamera.Shot.Approach);
        var ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        Vector3 prev = from;
        for (float t = 0f; t < approachTime; t += Time.deltaTime)
        {
            float k = ease.Evaluate(t / approachTime);
            Vector3 p = Bezier(from, ctrl, hover, k);
            FaceAlong(p - prev, 14f);
            heli.position = p;
            prev = p;
            yield return null;
        }
        heli.position = hover;

        // ── Hover (2초): 흔들리며 떠 있음 + 좀비 떼 ──
        CurrentPhase = Phase.Hover;
        CinematicCamera.SetShot(CinematicCamera.Shot.Wide);
        var spawner = FindFirstObjectByType<ZombieSpawner>();
        if (spawner != null) horde.AddRange(spawner.SpawnHorde(pos, hordeRadius, hordeCount));
        // 평소 AI는 소리 난 곳을 걸어서 확인하러 와서 컷신 안에 안 모임 → 컷신 동안 직접 뛰어오게
        foreach (var z in horde)
            if (z != null && !z.IsDead)
            {
                var rush = z.gameObject.AddComponent<ZombieHordeRush>();
                rush.center = pos;
                rush.speed = Random.Range(5.5f, 7f); // 컷신 6초 안에 대부분 헬리패드에 닿게
            }
        float callAt = 0f;
        for (float t = 0f; t < hoverTime; t += Time.deltaTime)
        {
            Hover(hover, t);
            CallHorde(ref callAt);
            yield return null;
        }

        // ── Boarding (4초): 헬기 바로 아래로 → 로프 타고 올라감 → 탑승 ──
        CurrentPhase = Phase.Boarding;
        CinematicCamera.SetShot(CinematicCamera.Shot.PlayerBoarding);
        // 플레이어 이동 코드(PlayerController → 이동 처리)가 매 프레임 속도를 넣으므로 끄고, 물리도 멈춘 채 위치만 옮김
        // (kinematic인데 속도를 넣으면 "Setting linear velocity of a kinematic body" 경고가 매 프레임 남)
        var pc = player.GetComponent<PlayerController>();
        if (pc != null) pc.enabled = false;
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
        var rope = BuildRope();
        Vector3 startPos = player.position;
        Vector3 under = new Vector3(pos.x, pos.y, pos.z);
        float walkTime = boardingTime * 0.2f;
        for (float t = 0f; t < boardingTime; t += Time.deltaTime)
        {
            Hover(hover, approachTime + t);
            CallHorde(ref callAt);
            Vector3 bottom = heli.position + Vector3.down * 1.3f;
            if (t < walkTime)
                player.position = Vector3.Lerp(startPos, under, Mathf.SmoothStep(0f, 1f, t / walkTime)); // 헬기 바로 아래로
            else
                player.position = Vector3.Lerp(under, bottom - Vector3.up * 1.8f, Mathf.SmoothStep(0f, 1f, (t - walkTime) / (boardingTime - walkTime))); // 위로
            Vector3 f = heli.forward; f.y = 0f;
            if (f.sqrMagnitude > 0.01f) player.rotation = Quaternion.LookRotation(f);
            rope.SetPosition(0, bottom);
            rope.SetPosition(1, player.position + Vector3.up * 1.9f);
            yield return null;
        }
        // 탑승 완료: 플레이어 숨김 (SetActive(false) 대신 모습·충돌만 끔 - 플레이어를 찾는 다른 스크립트가 끊기지 않게)
        SetPlayerVisible(false);
        Destroy(rope.gameObject);

        // ── TakeOff (5초): 상승하며 멀리 날아감, 좀비들은 하늘을 올려다봄 ──
        CurrentPhase = Phase.TakeOff;
        CinematicCamera.SetShot(CinematicCamera.Shot.ZombiesLookUp);
        LookUpZombies();
        Vector3 liftStart = heli.position;
        Vector3 liftEnd = liftStart + Vector3.up * takeOffRise - back * takeOffDistance;
        prev = liftStart;
        for (float t = 0f; t < takeOffTime; t += Time.deltaTime)
        {
            float k = t / takeOffTime;
            Vector3 p = Vector3.Lerp(liftStart, liftEnd, k * k); // 천천히 떠올라 점점 빠르게
            FaceAlong(p - prev, 18f);
            heli.position = p;
            prev = p;
            if (rotorAudio != null) rotorAudio.volume = Mathf.Lerp(1f, 0.4f, k);
            yield return null;
        }

        // ── Done: 페이드아웃 → 클리어 ──
        CurrentPhase = Phase.Done;
        for (float t = 0f; t < fadeOutTime; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(0f, 0f, 0f, t / fadeOutTime);
            if (rotorAudio != null) rotorAudio.volume = Mathf.Lerp(0.4f, 0f, t / fadeOutTime);
            yield return null;
        }
        fade.color = Color.black;
        GameStats.Instance?.Freeze();
        MissionManager.Instance?.Escape(false);
        // 검은 화면 뒤에서 카메라·HUD 원래대로 (클리어 화면이 그 위에 뜸)
        CinematicCamera.End();
        RestoreHud();
        if (heli != null) Destroy(heli.gameObject);
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        yield return ShowClear();
    }

    // ── 헬기 움직임 ─────────────────────────────────
    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    void FaceAlong(Vector3 vel, float maxPitch)
    {
        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        float speed = flat.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        if (flat.sqrMagnitude < 0.000001f) return;
        float pitch = Mathf.Clamp(speed * 0.6f, 0f, maxPitch); // 빠를수록 기수를 숙임
        var want = Quaternion.LookRotation(flat) * Quaternion.Euler(pitch, 0f, 0f);
        heli.rotation = Quaternion.Slerp(heli.rotation, want, 3f * Time.deltaTime);
    }

    void Hover(Vector3 hover, float t)
    {
        heli.position = hover + Vector3.up * Mathf.Sin(t * 2.2f) * 0.3f; // ±0.3m
        Vector3 e = heli.eulerAngles;
        heli.rotation = Quaternion.Slerp(heli.rotation, Quaternion.Euler(0f, e.y, 0f), 2f * Time.deltaTime); // 기수 수평으로
    }

    float nextHudCheck;

    void Update()
    {
        // 컷신 중 새로 켜지는 캔버스(건물 이름표·좀비 머리 위 표시 등)도 숨김
        if (CurrentPhase != Phase.Idle && CurrentPhase != Phase.Done && overlay != null && Time.unscaledTime >= nextHudCheck)
        {
            nextHudCheck = Time.unscaledTime + 0.25f;
            HideHudCanvases();
        }
        if (mainRotor != null) mainRotor.Rotate(0f, rotorSpeed * Time.deltaTime, 0f, Space.Self);
        if (tailRotor != null) tailRotor.Rotate(rotorSpeed * 1.5f * Time.deltaTime, 0f, 0f, Space.Self);
    }

    // 헬기가 들어올 방향: 헬리패드 위 12m에서 출발 지점까지 막힌 게 없는 쪽 (건물을 뚫고 오지 않게)
    Vector3 PickApproachDirection(Vector3 p)
    {
        Vector3 best = Vector3.right;
        float bestScore = -1f;
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            // 실제로 날아올 곡선(Approach)과 떠나갈 직선(TakeOff)을 20구간씩 따라가며 헬기 크기(반경 4m)로 막힘 검사
            // (예전: 출발점→헬리패드 직선 하나만 봐서 높은 건물 사이에선 곡선 일부가 건물을 뚫을 수 있었음)
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            Vector3 from = p + Vector3.up * spawnHeight + d * spawnDistance;
            Vector3 ctrl = p + d * 35f + side * 30f + Vector3.up * 45f;
            Vector3 hover = p + Vector3.up * hoverHeight;
            Vector3 away = hover + Vector3.up * takeOffRise - d * takeOffDistance;
            int clear = 0;
            for (int k = 0; k < 20; k++)
            {
                if (SegmentClear(Bezier(from, ctrl, hover, k / 20f), Bezier(from, ctrl, hover, (k + 1) / 20f))) clear++;
                float t0 = k / 20f, t1 = (k + 1) / 20f;
                if (SegmentClear(Vector3.Lerp(hover, away, t0 * t0), Vector3.Lerp(hover, away, t1 * t1))) clear++;
            }
            // 와이드 샷 카메라 자리(반대편)가 트였는지도 조금 반영
            if (!Physics.Linecast(p + Vector3.up * 3f, p - d * 38f + Vector3.up * 30f, ~0, QueryTriggerInteraction.Ignore)) clear += 2;
            if (clear > bestScore) { bestScore = clear; best = d; }
        }
        return best;
    }

    static bool SegmentClear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        return !Physics.SphereCast(a, 4f, d / len, out _, len, ~0, QueryTriggerInteraction.Ignore) && !Physics.CheckSphere(a, 4f, ~0, QueryTriggerInteraction.Ignore);
    }

    // ── 좀비 ───────────────────────────────────────
    void CallHorde(ref float nextCall)
    {
        if (Time.time < nextCall) return;
        nextCall = Time.time + 1f;
        foreach (var z in horde) if (z != null && !z.IsDead) z.HearNoiseNow(pad); // 계속 헬리패드 쪽으로
    }

    void LookUpZombies()
    {
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None))
        {
            if (z == null || z.IsDead) continue;
            if (!z.enabled && z.GetComponent<ZombieHordeRush>() == null) continue; // 꺼진 좀비 중엔 몰려온 좀비 떼만 (학습장 등 제외)
            if ((z.transform.position - pad).sqrMagnitude > 70f * 70f) continue;
            var rush = z.GetComponent<ZombieHordeRush>();
            if (rush != null) Destroy(rush);
            var look = z.gameObject.AddComponent<ZombieLookUp>();
            look.target = heli;
        }
    }

    void SetPlayerVisible(bool on)
    {
        foreach (var r in player.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        foreach (var c in player.GetComponentsInChildren<Collider>(true)) c.enabled = on;
    }

    // ── 헬기 조립 (기본 도형) ──────────────────────────
    GameObject BuildHelicopter(Vector3 at)
    {
        var root = new GameObject("RescueHelicopter");
        root.transform.position = at;
        Vector3 toPad = pad - at; toPad.y = 0f;
        root.transform.rotation = Quaternion.LookRotation(toPad.sqrMagnitude > 0.01f ? toPad : Vector3.forward);

        Material body = Mat(new Color(0.42f, 0.45f, 0.48f)); // 밤에도 보이게 밝은 회색
        Material dark = Mat(new Color(0.1f, 0.1f, 0.11f));
        Material stripe = Mat(new Color(0.95f, 0.55f, 0.1f));
        Material glass = GlassMat(new Color(0.55f, 0.8f, 1f, 0.55f));

        Part(PrimitiveType.Cube, root.transform, "Body", new Vector3(0f, 0f, 0f), new Vector3(4f, 1.5f, 7f), body);
        Part(PrimitiveType.Cube, root.transform, "Stripe", new Vector3(0f, -0.2f, 0f), new Vector3(4.04f, 0.25f, 6.2f), stripe);
        Part(PrimitiveType.Sphere, root.transform, "Cockpit", new Vector3(0f, 0.1f, 3.4f), new Vector3(3.2f, 1.6f, 2.6f), glass);
        Part(PrimitiveType.Cube, root.transform, "Tail", new Vector3(0f, 0.35f, -5.4f), new Vector3(1f, 0.5f, 4f), body);
        Part(PrimitiveType.Cube, root.transform, "TailFin", new Vector3(0f, 1.1f, -7.1f), new Vector3(0.2f, 1.5f, 1f), body);
        Part(PrimitiveType.Cube, root.transform, "SkidL", new Vector3(-1.5f, -1.3f, 0.3f), new Vector3(0.15f, 0.15f, 5.5f), dark);
        Part(PrimitiveType.Cube, root.transform, "SkidR", new Vector3(1.5f, -1.3f, 0.3f), new Vector3(0.15f, 0.15f, 5.5f), dark);
        foreach (float x in new[] { -1.5f, 1.5f })
            foreach (float z in new[] { -1.2f, 1.8f })
                Part(PrimitiveType.Cube, root.transform, "Strut", new Vector3(x * 0.8f, -0.95f, z), new Vector3(0.12f, 0.6f, 0.12f), dark);

        // 메인 로터: 긴 큐브 2개 교차 (지름 8m)
        mainRotor = new GameObject("MainRotor").transform;
        mainRotor.SetParent(root.transform, false);
        mainRotor.localPosition = new Vector3(0f, 1.15f, 0f);
        Part(PrimitiveType.Cylinder, mainRotor, "Hub", Vector3.zero, new Vector3(0.5f, 0.15f, 0.5f), dark);
        Part(PrimitiveType.Cube, mainRotor, "BladeA", Vector3.zero, new Vector3(8f, 0.06f, 0.35f), dark);
        Part(PrimitiveType.Cube, mainRotor, "BladeB", Vector3.zero, new Vector3(0.35f, 0.06f, 8f), dark);

        // 꼬리 로터: 작은 큐브 교차
        tailRotor = new GameObject("TailRotor").transform;
        tailRotor.SetParent(root.transform, false);
        tailRotor.localPosition = new Vector3(0.18f, 1.1f, -7.2f);
        Part(PrimitiveType.Cube, tailRotor, "BladeA", Vector3.zero, new Vector3(0.05f, 1.6f, 0.18f), dark);
        Part(PrimitiveType.Cube, tailRotor, "BladeB", Vector3.zero, new Vector3(0.05f, 0.18f, 1.6f), dark);

        // 항법등 (빨강/초록) + 아래로 비추는 노란 스포트라이트
        Part(PrimitiveType.Sphere, root.transform, "NavRed", new Vector3(-2.05f, 0.2f, 0.5f), Vector3.one * 0.25f, Glow(new Color(1f, 0.15f, 0.1f)));
        Part(PrimitiveType.Sphere, root.transform, "NavGreen", new Vector3(2.05f, 0.2f, 0.5f), Vector3.one * 0.25f, Glow(new Color(0.2f, 1f, 0.3f)));
        var spotGo = new GameObject("Spotlight");
        spotGo.transform.SetParent(root.transform, false);
        spotGo.transform.localPosition = new Vector3(0f, -0.9f, 1.5f);
        spotGo.transform.localRotation = Quaternion.Euler(80f, 0f, 0f);
        var spot = spotGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.88f, 0.5f);
        spot.range = 45f;
        spot.spotAngle = 45f;
        spot.intensity = 5f;
        spot.shadows = LightShadows.None;

        rotorAudio = root.AddComponent<AudioSource>();
        rotorAudio.clip = RotorClip();
        rotorAudio.loop = true;
        rotorAudio.spatialBlend = 0.6f; // 멀어지면 줄지만 컷신 내내 들리게
        rotorAudio.minDistance = 20f;
        rotorAudio.maxDistance = 250f;
        rotorAudio.outputAudioMixerGroup = AudioManager.GetGroup(AudioManager.Channel.SFX);
        rotorAudio.Play();
        return root;
    }

    static GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>()); // 장식 (플레이어·좀비와 부딪히지 않게)
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    static Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.45f);
        return m;
    }

    static Material Glow(Color c)
    {
        var m = Mat(c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 2f);
        return m;
    }

    // 하늘색 반투명 조종석 (Standard 투명 모드)
    static Material GlassMat(Color c)
    {
        var m = Mat(c);
        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        m.SetFloat("_Glossiness", 0.9f);
        return m;
    }

    LineRenderer BuildRope()
    {
        var go = new GameObject("Rope");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = 0.06f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(0.15f, 0.13f, 0.1f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    // 로터 소리 (에셋 없이): 낮은 바람 소리를 초당 11번 끊어 "두두두" + 55Hz 웅웅 (1초 루프, 이음매 없게 정수 주기)
    static AudioClip RotorClip()
    {
        const int rate = 44100;
        var data = new float[rate];
        float noise = 0f;
        var rnd = new System.Random(7);
        for (int i = 0; i < rate; i++)
        {
            float t = (float)i / rate;
            noise += 0.06f * ((float)rnd.NextDouble() * 2f - 1f - noise);
            float chop = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 11f * t), 4f);
            data[i] = (noise * 2.2f * (0.35f + chop) + 0.18f * Mathf.Sin(2f * Mathf.PI * 55f * t) * (0.5f + chop)) * 0.7f;
        }
        var clip = AudioClip.Create("RotorLoop", rate, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ── 화면 연출 (HUD 숨김·검은 띠·자막·페이드·클리어) ────────
    void BuildOverlay()
    {
        // HUD 전체 숨김 (켜져 있던 캔버스만 기억했다가 복원)
        hiddenCanvases.Clear();
        HideHudCanvases();
        overlay = MenuUI.CreateOverlayCanvas("CutsceneCanvas", 1200); // 일시정지·생존 종료(1100) 위, 씬 전환 페이드 아래
        overlay.transform.SetParent(transform, false);
        // 위아래 검은 띠 (2.35:1 화면비 느낌, 처음엔 화면 밖)
        barTop = Bar("BarTop", new Vector2(0f, 1f));
        barBottom = Bar("BarBottom", new Vector2(0f, 0f));
        // 페이드
        fade = new GameObject("Fade", typeof(RectTransform)).AddComponent<Image>();
        fade.transform.SetParent(overlay.transform, false);
        Stretch(fade.rectTransform);
        fade.color = new Color(0f, 0f, 0f, 0f);
        fade.raycastTarget = false;
        // 자막 (화면 위쪽, 검은 띠 바로 아래)
        caption = Text(overlay.transform, "Caption", new Vector2(0f, -170f), 30f, new Color(1f, 0.82f, 0.35f), false);
        caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        caption.alpha = 0f;
    }

    // 건물 입구 이름표(EntranceMarkers 라벨) 등 카메라를 향하는 3D 글자 - 캔버스가 아니라 따로 숨김
    readonly List<GameObject> hiddenBillboards = new List<GameObject>();

    void HideHudCanvases()
    {
        foreach (var f in FindObjectsByType<FaceCamera>(FindObjectsSortMode.None))
        {
            if (!f.gameObject.activeSelf || f.gameObject.scene != gameObject.scene) continue;
            f.gameObject.SetActive(false);
            hiddenBillboards.Add(f.gameObject);
        }
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || !c.enabled || c == overlay || c.gameObject.scene != gameObject.scene) continue;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }
    }

    RectTransform Bar(string name, Vector2 pivotY)
    {
        var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(overlay.transform, false);
        img.color = Color.black;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, pivotY.y); rt.anchorMax = new Vector2(1f, pivotY.y);
        rt.pivot = new Vector2(0.5f, pivotY.y);
        rt.sizeDelta = new Vector2(0f, 0f);
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    IEnumerator Letterbox(bool show)
    {
        const float height = 130f; // 1080 기준 위아래 각 130px
        for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
        {
            float h = Mathf.SmoothStep(show ? 0f : height, show ? height : 0f, t / 0.8f);
            barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, h);
            yield return null;
        }
        barTop.sizeDelta = barBottom.sizeDelta = new Vector2(0f, show ? height : 0f);
    }

    IEnumerator Caption(string text, float hold)
    {
        caption.text = text;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime) { caption.alpha = t / 0.5f; yield return null; }
        caption.alpha = 1f;
        yield return new WaitForSeconds(hold);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { caption.alpha = 1f - t / 0.6f; yield return null; }
        caption.alpha = 0f;
    }

    void RestoreHud()
    {
        foreach (var g in hiddenBillboards) if (g != null) g.SetActive(true);
        hiddenBillboards.Clear();
        foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
        if (barTop != null) barTop.sizeDelta = barBottom.sizeDelta = Vector2.zero;
        if (caption != null) caption.alpha = 0f;
    }

    // "GAME CLEAR - 생존자 구출 성공" + 기록 + 버튼 (다시 시작 / 메인 메뉴로)
    IEnumerator ShowClear()
    {
        var panel = new GameObject("Clear", typeof(RectTransform), typeof(CanvasGroup));
        panel.transform.SetParent(overlay.transform, false);
        Stretch((RectTransform)panel.transform);
        var group = panel.GetComponent<CanvasGroup>();
        group.alpha = 0f;

        Text(panel.transform, "Title", new Vector2(0f, 120f), 120f, new Color(1f, 0.72f, 0.2f), true).text = "GAME CLEAR";
        Text(panel.transform, "Sub", new Vector2(0f, 20f), 40f, Color.white, true).text = Localization.T("생존자 구출 성공");
        var s = GameStats.Instance;
        string days = Localization.IsEnglish ? $"Day {(s != null ? s.CurrentDay : 7)}" : $"{(s != null ? s.CurrentDay : 7)}일차";
        string stats = $"{days}  ·  {Localization.T("생존 시간")} {GameStats.FormatTime(s != null ? s.PlayTime : 0f)}  ·  {Localization.T("완료한 미션")} {(s != null ? s.CompletedMissions : 0)}";
        Text(panel.transform, "Stats", new Vector2(0f, -45f), 24f, new Color(0.8f, 0.82f, 0.86f), false).text = stats;
        ClearButton(panel.transform, new Vector2(-150f, -160f), Localization.T("다시 시작"), true, MenuUI.RestartGame);
        ClearButton(panel.transform, new Vector2(150f, -160f), Localization.T("메인 메뉴로"), false, MenuUI.LoadMainMenu);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.2f) { group.alpha = t; yield return null; }
        group.alpha = 1f;
    }

    void ClearButton(Transform parent, Vector2 pos, string label, bool primary, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(260f, 64f);
        rt.anchoredPosition = pos;
        var img = go.GetComponent<Image>();
        img.color = primary ? new Color(1f, 0.65f, 0.1f) : new Color(0.12f, 0.13f, 0.16f, 0.95f);
        var b = go.GetComponent<Button>();
        var colors = b.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        b.colors = colors;
        b.onClick.AddListener(() => UIAudio.Play(UIAudio.Sound.Click));
        b.onClick.AddListener(onClick);
        var t = Text(go.transform, "Label", Vector2.zero, 26f, primary ? new Color(0.1f, 0.08f, 0.05f) : Color.white, true);
        t.text = label;
        t.rectTransform.sizeDelta = rt.sizeDelta;
    }

    static TextMeshProUGUI Text(Transform parent, string name, Vector2 pos, float size, Color color, bool bold)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(parent, false);
        var font = Resources.Load<TMP_FontAsset>(bold ? "HUD/Fonts/Pretendard-Bold SDF" : "HUD/Fonts/Pretendard-SemiBold SDF");
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        t.rectTransform.sizeDelta = new Vector2(1600f, size * 1.6f);
        t.rectTransform.anchoredPosition = pos;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

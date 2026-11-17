using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 상인 배치 (상점 3단계). 게임 씬에 자동 생성 —— 씬 수정 없음.
///  - 시작할 때 맵의 바깥 빈터에 상인 traderCount명(기본 3)을 무작위로 세움 (지붕·건물 안·고립 구역 제외,
///    플레이어에게서 40m+, 상인끼리 120m+). 하루 종일 있음
///  - 새 날이 되면 다른 곳으로 옮기고 진열대도 새로 채움 (relocateEachDay)
///  - 진열대는 상인마다 따로 뽑아서, 상인마다 빠진 물건이 다름 (ShopStock.Generate)
///  - 미니맵: 못 찾은 상인은 '이 근처 어딘가' 원만, 가까이 가서 찾으면 그날은 정확한 '$' 핀 (TraderMinimapPins)
///  - 설정: Resources/TraderSettings (Tools ▸ Shop ▸ 상인 설치)
/// </summary>
public class TraderManager : MonoBehaviour
{
    public static TraderManager Instance { get; private set; }
    public readonly List<Trader> traders = new List<Trader>();

    private TraderSettings settings;
    private readonly int sessionSeed = System.Environment.TickCount;
    private int lastDay = -1;

    // ── 자동 생성 ─────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != null) return;
        // 게임 씬에서만 (PlayerController + 지형이 있는 씬)
        if (FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include) == null || Terrain.activeTerrain == null) return;
        new GameObject("TraderManager").AddComponent<TraderManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        settings = TraderSettings.Load();
        if (settings == null) Debug.LogWarning("[상인] Resources/TraderSettings가 없어 상인을 캡슐 모양으로 만듭니다. 메뉴 'Tools ▸ Shop ▸ 상인 설치'를 한 번 실행하세요.");
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        gameObject.AddComponent<TraderMinimapPins>();
        var dnc = DayNightCycle.Instance;
        if (dnc != null) dnc.onNewDay.AddListener(OnNewDay);
        Respawn(dnc != null ? dnc.CurrentDay : 1);
    }

    private void OnNewDay(int day)
    {
        if (settings != null && !settings.relocateEachDay) return;
        Respawn(day);
    }

    /// <summary>상인을 전부 새로 세운다 (자리·진열대 새로)</summary>
    public void Respawn(int day)
    {
        if (day == lastDay && traders.Count > 0) return;
        lastDay = day;
        foreach (var t in traders) if (t != null) Destroy(t.gameObject);
        traders.Clear();

        int count = settings != null ? Mathf.Max(0, settings.traderCount) : 3;
        float fromPlayer = settings != null ? settings.minDistanceFromPlayer : 40f;
        float between = settings != null ? settings.minDistanceBetween : 120f;
        var rng = new System.Random(sessionSeed + day * 7919);
        var player = GameObject.FindWithTag("Player");
        var spots = FindSpots(count, player != null ? player.transform.position : (Vector3?)null, fromPlayer, between, rng);

        for (int i = 0; i < spots.Count; i++)
        {
            var stock = ShopStock.Generate(rng.Next());
            float yaw = (float)rng.NextDouble() * 360f;
            traders.Add(Trader.Create(spots[i], yaw, stock, settings, rng));
        }
        if (spots.Count < count) Debug.LogWarning($"[상인] 자리를 {spots.Count}/{count}곳만 찾았습니다");
    }

    /// <summary>바깥의 빈터 count곳 (지붕·건물 안·고립 구역 제외)</summary>
    private static List<Vector3> FindSpots(int count, Vector3? player, float fromPlayer, float between, System.Random rng)
    {
        var result = new List<Vector3>();
        var terrain = Terrain.activeTerrain;
        if (terrain == null || count <= 0) return result;
        Vector3 size = terrain.terrainData.size, origin = terrain.transform.position;
        float margin = 0.075f;   // 가장자리 7.5%는 빼고

        for (int pass = 0; pass < 2 && result.Count < count; pass++)
        {
            float minBetween = pass == 0 ? between : between * 0.5f;   // 자리가 모자라면 간격을 줄여 한 번 더
            for (int tries = 0; tries < 600 && result.Count < count; tries++)
            {
                float x = origin.x + size.x * (margin + (float)rng.NextDouble() * (1f - 2f * margin));
                float z = origin.z + size.z * (margin + (float)rng.NextDouble() * (1f - 2f * margin));
                float refY = terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y;
                if (!MissionManager.TryGroundPoint(x, z, refY, out Vector3 p)) continue;   // 지붕·건물 안·경사 제외
                if (player.HasValue && Flat(p, player.Value) < fromPlayer) continue;
                bool tooClose = false;
                foreach (var r in result) if (Flat(p, r) < minBetween) { tooClose = true; break; }
                if (tooClose) continue;
                // 노점 들어갈 빈 공간 (땅에서 0.2m 위부터)
                if (Physics.CheckBox(p + Vector3.up * 1.3f, new Vector3(2.0f, 1.1f, 2.0f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                // 앞쪽(손님 자리)도 걸어 다닐 수 있어야
                if (!NavMesh.SamplePosition(p, out NavMeshHit _, 1f, NavMesh.AllAreas)) continue;
                if (!MissionManager.IsConnectedToMap(p)) continue;
                result.Add(p);
            }
        }
        return result;
    }

    private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return (a - b).magnitude; }

    /// <summary>가장 가까운 상인 (디버그·안내용)</summary>
    public Trader Nearest(Vector3 pos)
    {
        Trader best = null;
        float bd = float.MaxValue;
        foreach (var t in traders)
        {
            if (t == null) continue;
            float d = Flat(t.transform.position, pos);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }
}

/// <summary>
/// 미니맵 상인 표시 (MinimapMissionPins와 같은 투영 방식)
///  - 아직 못 찾은 상인: 반투명 금색 원 '이 근처 어딘가' (중심이 실제 위치에서 어긋남).
///    원 중심이 미니맵에 보이는 범위 안에 들어와야 나타남 (멀리 있는 상인은 안 보임 —— 돌아다니다 마주쳐야 함)
///  - 찾은 상인(그날 가까이 가 봤음): 정확한 '$' 핀. 찾는 순간 핀이 톡 튀어나옴. 미니맵 밖이면 테두리에 붙음
///  - 다음 날 상인이 옮겨 가면 다시 원부터
/// </summary>
public class TraderMinimapPins : MonoBehaviour
{
    private static readonly Color PinColor = new Color32(0xFF, 0xD7, 0x00, 0xFF);
    private static readonly Color AreaFill = new Color(1f, 0.84f, 0.2f, 0.13f);
    private static readonly Color AreaRing = new Color(1f, 0.84f, 0.2f, 0.45f);
    private const float PinSize = 16f;
    private const float EdgeInset = 8f;
    private const float MinAreaPx = 18f;

    private class Marker
    {
        public RectTransform area, pin;
        public UnityEngine.UI.Image areaFill;
    }

    private TraderManager manager;
    private Camera mapCam;
    private RectTransform mapImage, pinLayer;
    private readonly List<Marker> pool = new List<Marker>();
    private bool ready;
    private float nextTry;

    private void Start() => manager = GetComponent<TraderManager>();

    private bool Setup()
    {
        var mc = FindFirstObjectByType<MinimapController>();
        if (mc == null || mc.playerMarker == null) return false;
        mapCam = mc.GetComponent<Camera>();
        var raw = mc.playerMarker.parent.GetComponentInChildren<RawImage>(true);
        if (mapCam == null || raw == null) return false;
        mapImage = raw.rectTransform;

        Transform panel = mc.playerMarker.parent;
        pinLayer = new GameObject("TraderPins", typeof(RectTransform)).GetComponent<RectTransform>();
        pinLayer.SetParent(panel, false);
        pinLayer.anchorMin = pinLayer.anchorMax = pinLayer.pivot = new Vector2(0.5f, 0.5f);
        pinLayer.anchoredPosition = mapImage.anchoredPosition;
        pinLayer.sizeDelta = Vector2.zero;
        pinLayer.SetSiblingIndex(mc.playerMarker.GetSiblingIndex());   // 플레이어 마커가 위에
        return true;
    }

    /// <summary>월드 좌표 → 미니맵 UI 좌표 (미니맵 가운데 기준)</summary>
    private Vector2 ToUi(Vector3 world)
    {
        Vector3 vp = mapCam.WorldToViewportPoint(world);
        return new Vector2((vp.x - 0.5f) * mapImage.rect.width, (vp.y - 0.5f) * mapImage.rect.height);
    }

    private void LateUpdate()
    {
        if (manager == null) return;
        if (!ready)
        {
            if (Time.unscaledTime < nextTry) return;
            nextTry = Time.unscaledTime + 1f;
            ready = Setup();
            if (!ready) return;
        }

        float radius = Mathf.Min(mapImage.rect.width, mapImage.rect.height) * 0.5f - EdgeInset;
        Vector3 camRight = mapCam.transform.right; camRight.y = 0f;
        if (camRight.sqrMagnitude < 0.01f) camRight = Vector3.right;
        camRight.Normalize();

        int n = 0;
        foreach (var t in manager.traders)
        {
            if (t == null) continue;
            var m = GetMarker(n++);
            if (t.Discovered)
            {
                // 정확한 핀
                m.area.gameObject.SetActive(false);
                Vector2 ui = ToUi(t.transform.position);
                if (ui.magnitude > radius) ui = ui.normalized * radius;
                m.pin.anchoredPosition = ui;
                float k = Mathf.Clamp01((Time.unscaledTime - t.DiscoveredAt) / 0.35f);   // 찾은 순간 톡
                m.pin.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, k * k);
                m.pin.gameObject.SetActive(true);
            }
            else
            {
                // '이 근처 어딘가' 원 —— 원 중심이 미니맵 범위 밖이면 숨김
                m.pin.gameObject.SetActive(false);
                Vector2 c = ToUi(t.HintCenter);
                if (c.magnitude > radius) { m.area.gameObject.SetActive(false); continue; }
                float r = Mathf.Max(MinAreaPx * 0.5f, (ToUi(t.HintCenter + camRight * t.HintRadius) - c).magnitude);
                r = Mathf.Min(r, radius);
                if (c.magnitude + r > radius) c = c.normalized * Mathf.Max(0f, radius - r);   // 테두리 밖으로 삐져나오지 않게 안쪽으로
                m.area.anchoredPosition = c;
                m.area.sizeDelta = new Vector2(r * 2f, r * 2f);
                // 살짝 숨 쉬듯 (눈에 띄되 튀지 않게)
                var col = AreaFill;
                col.a *= 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 2f);
                m.areaFill.color = col;
                m.area.gameObject.SetActive(true);
            }
        }
        for (int i = n; i < pool.Count; i++)
        {
            pool[i].area.gameObject.SetActive(false);
            pool[i].pin.gameObject.SetActive(false);
        }
    }

    private Marker GetMarker(int i)
    {
        while (pool.Count <= i)
        {
            var m = new Marker();

            // 범위 원: 채움 + 테두리 (크기는 매 프레임 바뀜 → 자식은 부모 크기를 꽉 채움)
            m.area = HudStyle.NewRect(pinLayer, "TraderArea" + pool.Count, Vector2.zero, new Vector2(40f, 40f));
            m.areaFill = HudStyle.NewImage(m.area, "Fill", HudStyle.Circle, AreaFill, Vector2.zero, Vector2.zero);
            Stretch(m.areaFill.rectTransform);
            m.areaFill.raycastTarget = false;
            var ring = HudStyle.NewImage(m.area, "Ring", HudStyle.Ring, AreaRing, Vector2.zero, Vector2.zero);
            Stretch(ring.rectTransform);
            ring.raycastTarget = false;

            // 정확한 핀: 금색 동그라미 + '$'
            var img = HudStyle.NewImage(pinLayer, "TraderPin" + pool.Count, HudStyle.Circle, PinColor, Vector2.zero, new Vector2(PinSize, PinSize));
            img.raycastTarget = false;
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);
            var label = HudStyle.NewText(img.transform, "S", "$", HudStyle.Bold, 13f, new Color(0.15f, 0.12f, 0.05f),
                TMPro.TextAlignmentOptions.Center, Vector2.zero, new Vector2(PinSize, PinSize));
            label.raycastTarget = false;
            m.pin = img.rectTransform;

            m.area.gameObject.SetActive(false);
            m.pin.gameObject.SetActive(false);
            pool.Add(m);
        }
        return pool[i];
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

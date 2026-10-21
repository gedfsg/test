using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 상점/바 네온 간판 (플레이테스트 4순위 분위기).
/// 상가 건물(Kenney building-a~n) 중 signCount곳 + 마트 문 위에 빨강/파랑 네온 글자 + 색 조명.
///  - 글자 재질은 HDR 색(밝기 1 초과) → 포스트 프로세싱 블룸으로 번짐
///  - 탑뷰 카메라에서 읽히게 항상 카메라를 향함 (FaceCamera) - 벽에 붙이면 벽 방향에 따라 안 보이거나 뒤집혀 보임
///  - 낮엔 어둡게(꺼진 네온), 노을~새벽엔 켜짐. 일부는 고장나서 깜빡임
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성.
/// </summary>
public class NeonSigns : MonoBehaviour
{
    public int signCount = 10;
    [Range(0f, 1f)] public float flickerRatio = 0.25f;
    [Tooltip("켜졌을 때 HDR 밝기 배수 (블룸 threshold 1.1보다 충분히 크게)")]
    public float glow = 4f;

    static readonly string[] Words = { "BAR", "OPEN", "24H", "MOTEL", "LIQUOR", "DINER", "HOTEL", "CAFE", "PIZZA", "PAWN", "GUNS", "CLUB" };
    static readonly Color Red = new Color(1f, 0.12f, 0.18f);
    static readonly Color Blue = new Color(0.15f, 0.45f, 1f);

    class Sign { public TextMeshPro text; public Material mat; public Light light; public Color color; public bool flicker; public float seed, nextOff, offEnd; }
    readonly List<Sign> signs = new List<Sign>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (GameObject.FindWithTag("Player") == null || FindFirstObjectByType<NeonSigns>() != null) return;
        new GameObject("NeonSigns").AddComponent<NeonSigns>();
    }

    void Start()
    {
        var shops = new List<BuildingInterior>();
        BuildingInterior mart = null;
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            if (LootDatabase.Classify(b) == LootDatabase.BuildingKind.Mart) mart = b;
            else if (IsShopBuilding(b.name)) shops.Add(b);
        }

        int placed = 0;
        if (mart != null && Place(mart, "MARKET", Red)) placed++;
        while (placed < signCount + (mart != null ? 1 : 0) && shops.Count > 0)
        {
            int i = Random.Range(0, shops.Count);
            var b = shops[i];
            shops.RemoveAt(i);
            if (Place(b, Words[Random.Range(0, Words.Length)], Random.value < 0.5f ? Red : Blue)) placed++;
        }
        Debug.Log($"[NeonSigns] 네온 간판 {placed}개 (깜빡임 {signs.FindAll(s => s.flicker).Count}개)");
    }

    // Kenney City Kit Commercial 상가: building-a ~ building-n (스카이스크래퍼/주택 type-*, low-detail 제외)
    static bool IsShopBuilding(string n)
    {
        if (!n.StartsWith("building-") || n.StartsWith("building-skyscraper") || n.StartsWith("building-type")) return false;
        char c = n.Length > 9 ? n[9] : ' ';
        return c >= 'a' && c <= 'n';
    }

    bool Place(BuildingInterior b, string word, Color color)
    {
        var trig = LootWorldSpawner.FindTrigger(b);
        var doors = EntranceMarkers.DoorCenters(b.GetComponentsInChildren<BuildingWallCollider>(true));
        if (doors.Count == 0 && b.transform.parent != null) doors = EntranceMarkers.DoorCenters(b.transform.parent.GetComponentsInChildren<BuildingWallCollider>(true));
        if (trig == null || doors.Count == 0) return false;
        Vector3 door = Vector3.zero;
        foreach (var d in doors) door += d;
        door /= doors.Count;
        Vector3 outDir = EntranceMarkers.SnapToAxis(door - trig.bounds.center, b.transform);
        if (outDir == Vector3.zero) return false;

        // 문 위 (지면 +3.4m), 벽에서 살짝 떨어뜨림
        var t = Terrain.activeTerrain;
        float ground = Mathf.Max(trig.bounds.min.y, t != null ? t.SampleHeight(door) + t.transform.position.y : trig.bounds.min.y);
        Vector3 pos = new Vector3(door.x, ground + 3.4f, door.z) + outDir * 0.35f;

        var go = new GameObject("Neon_" + word + "_" + b.name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        // 벽에서 위로 기울여 붙였더니 벽 방향에 따라 글자가 180° 뒤집혀 보였음 → 데미지 숫자처럼 항상 카메라를 향하게
        go.AddComponent<FaceCamera>();

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = word;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(4f, 1.2f);
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 3f; tmp.fontSizeMax = 10f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;                       // 색은 재질(_FaceColor HDR)로
        var mat = tmp.fontMaterial;                    // 간판마다 재질 인스턴스 (밝기 따로 조절)
        mat.EnableKeyword("OUTLINE_ON");
        tmp.outlineWidth = 0.18f;                      // 네온관처럼 두꺼운 테두리
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // TextMeshPro가 붙인 렌더러: 글자 그림자 끔

        // 간판 앞 색 조명: 벽/바닥을 그 색으로 물들임
        // 간판 자식으로 두면 FaceCamera 회전에 따라 조명 위치가 돌아감 → 형제로
        var lightGo = new GameObject("Light_" + go.name);
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.position = pos + outDir * 1.2f;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.color = color; l.range = 7f; l.intensity = 1.6f; l.shadows = LightShadows.None;

        signs.Add(new Sign { text = tmp, mat = mat, light = l, color = color, flicker = Random.value < flickerRatio,
                             seed = Random.value * 100f, nextOff = Time.time + Random.Range(1f, 5f) });
        return true;
    }

    void Update()
    {
        var dnc = DayNightCycle.Instance;
        bool night = dnc == null || dnc.CurrentPhase != DayNightCycle.Phase.Day;
        float t = Time.time;
        foreach (var s in signs)
        {
            if (s.text == null) continue;
            // 매번 현재 재질을 가져옴: 생성 직후 잡아둔 fontMaterial은 TMP가 설정 변경(테두리 등) 때 새 인스턴스로 바꿔서 색이 안 먹었음
            s.mat = s.text.fontMaterial;
            float k = night ? 1f : 0f;
            if (night && s.flicker)
            {
                // 고장난 네온: 가끔 0.05~0.4초 꺼졌다 켜짐 (연달아 2~3번 깜빡일 때도)
                if (t >= s.nextOff)
                {
                    s.offEnd = t + Random.Range(0.05f, 0.4f);
                    s.nextOff = s.offEnd + (Random.value < 0.5f ? Random.Range(0.05f, 0.2f) : Random.Range(1.5f, 6f));
                }
                k = t < s.offEnd ? 0.05f : Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(s.seed, t * 6f));
            }
            // 꺼진 네온(낮)도 유리관 색은 보이게 어둡게, 켜지면 HDR 밝기 → 블룸
            Color face = k > 0f ? s.color * (glow * k) : s.color * 0.35f;
            face.a = 1f;
            // 이름으로 지정: ShaderUtilities.ID_* 는 TMP 초기화 전엔 0이라 엉뚱한 속성에 들어가 글자가 흰색으로 나왔음
            s.mat.SetColor("_FaceColor", face);
            s.mat.SetColor("_OutlineColor", new Color(face.r * 0.6f, face.g * 0.6f, face.b * 0.6f, 1f));
            s.light.enabled = k > 0.1f;
            s.light.intensity = 1.6f * k;
        }
    }
}

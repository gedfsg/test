using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 도로 가로등: 씬의 가로등 모델(light-square / LAMPIONE / ParkLight) 중 무작위 30%에만 Point Light를 달고
/// 낮이 아닐 때(노을/밤/새벽)만 켬 → 좀비 시대라 일부만 살아있는 느낌.
/// 분위기(플레이테스트 4순위): 주황(나트륨등) 위주 + 일부 보라(수은등) 빛 + 램프 주변 안개 빛무리(halo),
/// 켜진 것 중 flickerRatio는 고장난 것처럼 깜빡임.
/// 씬에 따로 둘 필요 없음 - 씬이 로드될 때마다 자동 생성.
/// </summary>
public class StreetLightManager : MonoBehaviour
{
    [Range(0f, 1f)] public float workingRatio = 0.3f;
    // public Color lightColor = new Color(1f, 0.82f, 0.45f); // (이전) 노랑
    public Color lightColor = new Color(1f, 0.55f, 0.22f);          // 주황 (나트륨등)
    [Tooltip("이 비율은 보라(수은등) 빛 - 주황/보라 안개가 섞인 밤거리")]
    [Range(0f, 1f)] public float purpleRatio = 0.3f;
    public Color purpleColor = new Color(0.7f, 0.4f, 1f);
    public float intensity = 2.0f;
    [Tooltip("가로등 높이가 약 15m라 바닥까지 닿게")]
    public float range = 22f;

    [Header("안개 빛무리 (램프 주변에 번지는 빛)")]
    public float haloSize = 7f;
    [Range(0f, 1f)] public float haloAlpha = 0.35f;

    [Header("고장난 가로등 (깜빡임)")]
    [Range(0f, 1f)] public float flickerRatio = 0.25f;

    static readonly string[] LampNames = { "light-square", "LAMPIONE", "ParkLight" };

    readonly List<Light> lights = new List<Light>();
    bool shownOn;

    // 깜빡이는 가로등: 조명 + 빛무리 재질 + 랜덤 시드 + 다음 꺼짐 시각
    class Flicker { public Light light; public Material halo; public Color haloColor; public float seed, nextBlackout, blackoutEnd; }
    readonly List<Flicker> flickers = new List<Flicker>();
    readonly List<GameObject> halos = new List<GameObject>();
    static Texture2D haloTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (DayNightCycle.Instance == null && FindFirstObjectByType<DayNightCycle>() == null) return; // 게임 씬에서만
        if (FindFirstObjectByType<StreetLightManager>() != null) return;
        new GameObject("StreetLightManager").AddComponent<StreetLightManager>();
    }

    void Start()
    {
        int total = 0;
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!IsLamp(r.name)) continue;
            total++;
            if (Random.value > workingRatio) continue;
            lights.Add(AddLight(r));
        }
        shownOn = !ShouldBeOn(); // 첫 Update에서 상태 적용되게
        Debug.Log($"[StreetLightManager] 가로등 {total}개 중 {lights.Count}개 켜짐 (밤에만), 그중 {flickers.Count}개 고장(깜빡임)");
    }

    static bool IsLamp(string n)
    {
        foreach (var k in LampNames) if (n.StartsWith(k)) return true;
        return false;
    }

    // 램프 머리 위치: 모델 원점(기둥 밑)에서 외곽(bounds) 중심 쪽으로 더 나간 곳 = 팔 끝, 높이는 꼭대기 바로 아래
    Light AddLight(MeshRenderer r)
    {
        Bounds b = r.bounds;
        Vector3 pivot = r.transform.position;
        Vector3 toCenter = b.center - pivot; toCenter.y = 0f;
        Vector3 head = pivot + toCenter * 1.8f;
        head.y = b.max.y - 0.4f;

        var go = new GameObject("StreetLight");
        go.transform.SetParent(r.transform, true);
        go.transform.position = head;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        Color c = Random.value < purpleRatio ? purpleColor : lightColor;
        l.color = c; l.intensity = intensity; l.range = range;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        l.enabled = false;

        var halo = AddHalo(go.transform, c);
        if (Random.value < flickerRatio)
            flickers.Add(new Flicker { light = l, halo = halo, haloColor = c, seed = Random.value * 100f, nextBlackout = Time.time + Random.Range(1f, 6f) });
        return l;
    }

    // 램프 주변 안개 빛무리: 카메라를 향하는 부드러운 원형 판 (가산 블렌딩 → 블룸과 같이 번짐)
    Material AddHalo(Transform lamp, Color c)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "Halo";
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(lamp, false);
        q.transform.localScale = Vector3.one * haloSize / Mathf.Max(0.01f, lamp.lossyScale.x);
        q.AddComponent<FaceCamera>();
        var r = q.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        // WorldFX 불꽃과 같은 가산 파티클 재질 (Resources라 빌드에서 셰이더가 안 빠짐)
        var src = Resources.Load<Material>("WorldDecor/FireAdditive");
        var m = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = HaloTexture();
        SetHaloColor(m, c, 1f);
        r.sharedMaterial = m;
        q.SetActive(false);
        halos.Add(q);
        return m;
    }

    void SetHaloColor(Material m, Color c, float k)
    {
        var col = new Color(c.r, c.g, c.b, haloAlpha * k);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", col); // 레거시 파티클 셰이더
        m.color = col;
    }

    // 가운데가 밝고 가장자리로 갈수록 0이 되는 원형 그라데이션 (한 번만 생성)
    static Texture2D HaloTexture()
    {
        if (haloTex != null) return haloTex;
        const int N = 64;
        haloTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                haloTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        haloTex.Apply();
        return haloTex;
    }

    static bool ShouldBeOn()
    {
        var d = DayNightCycle.Instance;
        return d != null && d.CurrentPhase != DayNightCycle.Phase.Day;
    }

    void Update()
    {
        bool on = ShouldBeOn();
        if (on != shownOn)
        {
            shownOn = on;
            foreach (var l in lights) if (l != null) l.enabled = on;
            foreach (var h in halos) if (h != null) h.SetActive(on);
        }
        if (on) UpdateFlicker();
    }

    // 고장난 가로등: 평소엔 지글지글 미세하게 흔들리다가, 가끔 0.1~0.6초 툭 꺼짐 (빠르게 2~3번 깜빡이는 것처럼)
    void UpdateFlicker()
    {
        float t = Time.time;
        foreach (var f in flickers)
        {
            if (f.light == null) continue;
            if (t >= f.nextBlackout)
            {
                f.blackoutEnd = t + Random.Range(0.1f, 0.6f);
                f.nextBlackout = f.blackoutEnd + (Random.value < 0.5f ? Random.Range(0.05f, 0.25f) : Random.Range(2f, 8f)); // 연달아 깜빡 or 한동안 정상
            }
            float k = t < f.blackoutEnd
                ? Random.Range(0f, 0.15f)                                                  // 꺼짐 (가끔 살짝 번쩍)
                : Mathf.Lerp(0.65f, 1.05f, Mathf.PerlinNoise(f.seed, t * 9f));            // 지글거림
            f.light.intensity = intensity * k;
            SetHaloColor(f.halo, f.haloColor, k);
        }
    }
}

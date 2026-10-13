using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 도로 가로등: 씬의 가로등 모델(light-square / LAMPIONE / ParkLight) 중 무작위 30%에만 노란 Point Light를 달고
/// 낮이 아닐 때(노을/밤/새벽)만 켬 → 좀비 시대라 일부만 살아있는 느낌.
/// 씬에 따로 둘 필요 없음 - 씬이 로드될 때마다 자동 생성.
/// </summary>
public class StreetLightManager : MonoBehaviour
{
    [Range(0f, 1f)] public float workingRatio = 0.3f;
    public Color lightColor = new Color(1f, 0.82f, 0.45f);
    public float intensity = 2.0f;
    [Tooltip("가로등 높이가 약 15m라 바닥까지 닿게")]
    public float range = 22f;

    static readonly string[] LampNames = { "light-square", "LAMPIONE", "ParkLight" };

    readonly List<Light> lights = new List<Light>();
    bool shownOn;

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
        Debug.Log($"[StreetLightManager] 가로등 {total}개 중 {lights.Count}개 켜짐 (밤에만)");
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
        l.color = lightColor; l.intensity = intensity; l.range = range;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        l.enabled = false;
        return l;
    }

    static bool ShouldBeOn()
    {
        var d = DayNightCycle.Instance;
        return d != null && d.CurrentPhase != DayNightCycle.Phase.Day;
    }

    void Update()
    {
        bool on = ShouldBeOn();
        if (on == shownOn) return;
        shownOn = on;
        foreach (var l in lights) if (l != null) l.enabled = on;
    }
}

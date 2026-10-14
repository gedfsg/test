using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 5 - 쓰러진 오브젝트.
///  가로등 10%: 기둥 밑을 축으로 도로를 따라 쓰러짐 (도로를 가로막지 않게 가까운 도로 방향과 나란히)
///  전봇대 5개: 눕힘
///  쓰레기통 20%: 옆으로 넘어짐
/// 쓰러진 물체는 콜라이더를 끔 (내비메시는 미리 구워져 있어서 길을 막으면 좀비가 걸려 멈춤).
/// 쓰러진 가로등은 불이 안 켜지게 이름을 바꿈 (StreetLightManager가 이름으로 가로등을 찾음 → 그보다 먼저 Awake에서 처리).
/// 게임 씬 로드 시 자동 생성
/// </summary>
public class WorldFallenProps : MonoBehaviour
{
    public int seed = 5150;
    [Range(0f, 1f)] public float streetLightRatio = 0.1f;
    public int powerPoles = 5;
    [Range(0f, 1f)] public float trashBinRatio = 0.2f;

    static readonly string[] LampNames = { "light-square", "LAMPIONE", "ParkLight" }; // StreetLightManager와 같은 이름
    static readonly string[] BinNames = { "Bin-", "Garbage", "trash", "dumpster" };
    public const string FallenPrefix = "Fallen_";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!WorldDecor.IsGameScene || FindFirstObjectByType<WorldFallenProps>() != null) return;
        new GameObject("WorldFallenProps").AddComponent<WorldFallenProps>();
    }

    static bool NameStarts(string n, string[] keys)
    {
        foreach (var k in keys) if (n.StartsWith(k, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void Awake()
    {
        var rng = new System.Random(seed);
        var lamps = new List<Transform>(); var poles = new List<Transform>(); var bins = new List<Transform>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var t = r.transform;
            if (NameStarts(t.name, LampNames)) lamps.Add(t);
            else if (t.name.StartsWith("electricity-pole")) poles.Add(t);
            else if (NameStarts(t.name, BinNames)) bins.Add(t);
        }
        // 고정 순서 (찾는 순서가 실행마다 달라도 같은 것이 쓰러지게)
        System.Comparison<Transform> byPos = (a, b) => (a.position.x * 1000f + a.position.z).CompareTo(b.position.x * 1000f + b.position.z);
        lamps.Sort(byPos); poles.Sort(byPos); bins.Sort(byPos);

        int nl = FellSome(lamps, Mathf.RoundToInt(lamps.Count * streetLightRatio), rng, alongRoad: true, minAngle: 80f, maxAngle: 87f);
        int np = FellSome(poles, Mathf.Min(powerPoles, poles.Count), rng, alongRoad: true, minAngle: 83f, maxAngle: 88f);
        int nb = 0;
        foreach (var b in Pick(bins, Mathf.Max(bins.Count > 0 ? 1 : 0, Mathf.RoundToInt(bins.Count * trashBinRatio)), rng))
        {
            // 쓰레기통: 옆으로 90° 넘어짐
            Vector3 dir = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f) * Vector3.forward;
            Fall(b, dir, 90f);
            nb++;
        }
        Debug.Log($"[WorldFallenProps] 가로등 {nl}/{lamps.Count}, 전봇대 {np}/{poles.Count}, 쓰레기통 {nb}/{bins.Count} 쓰러뜨림");
    }

    static List<Transform> Pick(List<Transform> list, int count, System.Random rng)
    {
        var pool = new List<Transform>(list); var result = new List<Transform>();
        for (int i = 0; i < count && pool.Count > 0; i++) { int k = rng.Next(pool.Count); result.Add(pool[k]); pool.RemoveAt(k); }
        return result;
    }

    int FellSome(List<Transform> list, int count, System.Random rng, bool alongRoad, float minAngle, float maxAngle)
    {
        int n = 0;
        foreach (var t in Pick(list, count, rng))
        {
            Vector3 dir = alongRoad ? AlongNearestRoad(t.position, rng) : Quaternion.Euler(0f, rng.Range(0f, 360f), 0f) * Vector3.forward;
            Fall(t, dir, rng.Range(minAngle, maxAngle));
            t.name = FallenPrefix + t.name; // StreetLightManager가 불을 안 켬
            n++;
        }
        return n;
    }

    /// <summary>가까운 도로와 나란한 방향 (도로 쪽 방향에 수직) + 약간 비스듬히</summary>
    static Vector3 AlongNearestRoad(Vector3 pos, System.Random rng)
    {
        Vector3 toRoad = Vector3.zero; float best = float.MaxValue;
        var roads = WorldDecor.SceneRoot("Roads");
        if (roads != null)
            foreach (var c in roads.GetComponentsInChildren<Collider>())
            {
                Vector3 p = c.ClosestPoint(pos); p.y = pos.y;
                float d = (p - pos).sqrMagnitude;
                if (d < best && d > 0.01f) { best = d; toRoad = p - pos; }
            }
        Vector3 along = toRoad.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toRoad.normalized) : Vector3.forward;
        if (rng.NextDouble() < 0.5) along = -along;
        return Quaternion.Euler(0f, rng.Range(-20f, 20f), 0f) * along;
    }

    /// <summary>원점(밑동)을 축으로 dir 방향으로 angle도 기울임 + 바닥 높이 맞춤 + 콜라이더 끔</summary>
    static void Fall(Transform t, Vector3 dir, float angle)
    {
        float groundY = RendererBounds(t).min.y; // 원래 바닥에 닿아 있던 높이
        dir.y = 0f; dir.Normalize();
        Vector3 axis = Vector3.Cross(Vector3.up, dir); // 위쪽이 dir 쪽으로 넘어가는 회전축
        t.rotation = Quaternion.AngleAxis(angle, axis) * t.rotation;
        // 넘어진 뒤 제일 아래가 원래 바닥에 오게 (원점이 밑동이 아닌 모델은 떠 있거나 묻힘)
        t.position += Vector3.up * (groundY - RendererBounds(t).min.y);
        foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    static Bounds RendererBounds(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}

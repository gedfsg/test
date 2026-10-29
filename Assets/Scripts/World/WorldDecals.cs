using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 1 - 손상 데칼 (반투명 사각형을 바닥/벽에 붙임, 텍스처는 코드 생성).
///  도로: 균열 / 포트홀 / 기름 / 타이어 자국 / 폭발 흔적
///  바닥: 핏자국(좀비 스폰 지점 주변에 몰림) / 그을음
///  벽:   탄흔 (건물 벽에 몇 개씩 무리 지어)
/// 씬에 따로 둘 필요 없음 - 게임 씬이 로드되면 자동 생성. 같은 종류는 메쉬 하나로 합침 (종류당 드로우콜 1)
/// </summary>
public class WorldDecals : MonoBehaviour
{
    public int seed = 20261014;

    [Header("도로")]
    public int cracks = 100;
    public int potholes = 50;
    public int oilStains = 30;
    public int tireMarks = 20;
    public int explosions = 10;

    [Header("바닥/벽")]
    public int bloodStains = 50;
    [Range(0f, 1f)] public float bloodNearZombieSpawns = 0.6f;
    public int scorches = 20;
    public int bulletHoles = 50;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!WorldDecor.IsGameScene || FindFirstObjectByType<WorldDecals>() != null) return;
        new GameObject("WorldDecals").AddComponent<WorldDecals>();
    }

    // 종류마다 한 프레임씩 나눠서 생성 (한 프레임에 몰아서 하면 시작할 때 1초 가까이 멈춤)
    System.Collections.IEnumerator Start()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long worstStep = 0, stepStart = 0; var steps = new System.Text.StringBuilder();
        var rng = new System.Random(seed);
        var group = WorldDecor.Group("Decals");
        var log = new System.Text.StringBuilder();

        // 바닥 데칼: 종류마다 높이를 조금씩 달리 띄우고 그리는 순서(큐)도 나눠서 겹쳐도 깜빡이지 않게
        int order = 0;
        void Ground(string name, Texture2D tex, int count, float minSize, float maxSize, System.Func<(bool ok, RaycastHit hit, Transform tile)> where, bool stretchAlongRoad = false)
        {
            var batch = new WorldDecor.QuadBatch();
            float lift = 0.02f + order * 0.004f;
            for (int i = 0; i < count; i++)
            {
                var (ok, hit, tile) = where();
                if (!ok) continue;
                float size = rng.Range(minSize, maxSize);
                Vector3 n = hit.normal;
                if (stretchAlongRoad && tile != null)
                {
                    // 타이어 자국: 도로 방향(타일 앞/옆 중 하나)으로 길게, 살짝 비스듬히
                    Vector3 along = Quaternion.AngleAxis(rng.Range(-12f, 12f), n) * (rng.NextDouble() < 0.5 ? tile.forward : tile.right);
                    Vector3 across = Vector3.Cross(n, along);
                    batch.Add(hit.point + n * lift, n, -across, size * 0.28f, size, WorldDecor.AtlasCell(rng.Next(4)));
                }
                else
                {
                    Vector3 tangent = Quaternion.AngleAxis(rng.Range(0f, 360f), n) * Vector3.forward;
                    batch.Add(hit.point + n * lift, n, tangent, size, size, WorldDecor.AtlasCell(rng.Next(4)));
                }
            }
            batch.Build(name, group, WorldDecor.FadeMaterial(name, tex, Color.white, 3000 + order));
            log.Append($"{name} {batch.Count}({sw.ElapsedMilliseconds}ms)  ");
            order++;
        }

        (bool, RaycastHit, Transform) OnRoad() { bool ok = WorldDecor.RoadPoint(rng, out var h, out var t); return (ok, h, t); }

        // 좀비 많은 곳 = 야외 스폰 지점 주변
        var spawnPoints = new List<Vector3>();
        var spawner = FindFirstObjectByType<ZombieSpawner>(FindObjectsInactive.Include);
        if (spawner != null && spawner.outdoorSpawnPoints != null)
            foreach (var t in spawner.outdoorSpawnPoints) if (t != null) spawnPoints.Add(t.position);
        (bool, RaycastHit, Transform) NearZombies()
        {
            if (spawnPoints.Count > 0 && rng.NextDouble() < bloodNearZombieSpawns
                && WorldDecor.GroundPointNear(rng.Pick(spawnPoints), 9f, rng, out var h)) return (true, h, null);
            return OnRoad();
        }

        Ground("Cracks", WorldDecorTextures.Crack(seed), cracks, 2f, 4.5f, OnRoad);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        Ground("Potholes", WorldDecorTextures.Pothole(seed + 1), potholes, 1.2f, 2.4f, OnRoad);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        Ground("OilStains", WorldDecorTextures.Oil(seed + 2), oilStains, 1.6f, 3.2f, OnRoad);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        Ground("TireMarks", WorldDecorTextures.TireMarks(seed + 3), tireMarks, 4f, 8f, OnRoad, stretchAlongRoad: true);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        Ground("Scorches", WorldDecorTextures.Scorch(seed + 4), scorches, 2.5f, 4.5f, NearZombies);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        Ground("Explosions", WorldDecorTextures.Explosion(seed + 5), explosions, 5f, 8f, OnRoad);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        Ground("BloodStains", WorldDecorTextures.Blood(seed + 6), bloodStains, 1.4f, 3f, NearZombies);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;

        // 탄흔: 건물 벽에 무리 지어 (건물마다 메쉬 하나 → 건물이 숨겨지면 같이 숨음)
        var holeTex = WorldDecorTextures.BulletHole(seed + 7);
        steps.Append($"{sw.ElapsedMilliseconds - stepStart},"); worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart); yield return null; stepStart = sw.ElapsedMilliseconds;
        int holes = 0;
        yield return PlaceBulletHoles(rng, holeTex, n => holes = n);
        log.Append($"BulletHoles {holes}");

        worstStep = System.Math.Max(worstStep, sw.ElapsedMilliseconds - stepStart);
        Debug.Log($"[WorldDecals] 총 {sw.ElapsedMilliseconds}ms (한 프레임 최대 {worstStep}ms, 단계별 {steps}{sw.ElapsedMilliseconds - stepStart})  {log}");
    }

    // 탄흔 배치 - 무리(건물 하나)마다 한 프레임씩
    System.Collections.IEnumerator PlaceBulletHoles(System.Random rng, Texture2D tex, System.Action<int> done)
    {
        var buildingsRoot = WorldDecor.SceneRoot("Buildings");
        if (buildingsRoot == null || bulletHoles <= 0) { done(0); yield break; }
        var buildings = new List<MeshRenderer>(buildingsRoot.GetComponentsInChildren<MeshRenderer>());
        buildings.RemoveAll(r => { var mf = r.GetComponent<MeshFilter>(); return mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable; });
        if (buildings.Count == 0) { done(0); yield break; }

        var mat = WorldDecor.FadeMaterial("BulletHoles", tex, Color.white, 3010);
        var group = WorldDecor.Group("Decals");
        int placed = 0, guard = 0;
        var batches = new Dictionary<MeshRenderer, WorldDecor.QuadBatch>();
        while (placed < bulletHoles && guard++ < 200)
        {
            var b = rng.Pick(buildings);
            if (!WorldDecor.RandomWallTriangle(b, rng, 0.8f, 4.5f, out var tri)) continue; // 사람 키 높이 근처 벽

            if (!batches.TryGetValue(b, out var batch)) batches[b] = batch = new WorldDecor.QuadBatch();
            int cluster = Mathf.Min(rng.Next(3, 7), bulletHoles - placed);
            Vector3 center = WorldDecor.RandomPointInTriangle(rng, tri.a, tri.b, tri.c);
            Vector3 side = Vector3.Cross(Vector3.up, tri.normal);
            for (int k = 0; k < cluster; k++)
            {
                Vector3 p = center + side * rng.Range(-0.9f, 0.9f) + Vector3.up * rng.Range(-0.6f, 0.6f);
                float s = rng.Range(0.3f, 0.5f);
                batch.Add(p + tri.normal * 0.015f, tri.normal, side, s, s, WorldDecor.AtlasCell(rng.Next(4)));
                placed++;
            }
            yield return null;
        }
        foreach (var kv in batches)
        {
            var go = kv.Value.Build("BulletHoles_" + kv.Key.name, group, mat);
            if (go != null) go.AddComponent<DecorFollowHost>().host = kv.Key;
        }
        done(placed);
    }
}

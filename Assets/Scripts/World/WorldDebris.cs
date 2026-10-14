using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 2 - 파편/잔해 (Unity 기본 도형으로 생성, 콜라이더 없음).
///  건물 바깥 벽 아래 잔해 더미: 벽돌 / 콘크리트 조각 / 깨진 유리
///  도로 위: 종이/쓰레기,  나무 근처: 나뭇가지
/// 같은 재질은 메쉬 하나로 합침 (색 변형마다 드로우콜 1). 게임 씬 로드 시 자동 생성
/// </summary>
public class WorldDebris : MonoBehaviour
{
    public int seed = 777;
    [Tooltip("잔해 더미 개수 (건물 벽 아래). 벽돌/콘크리트/유리는 더미에 나눠 담김")]
    public int rubblePiles = 12;
    public int concreteChunks = 50;
    public int bricks = 100;
    public int glassShards = 30;
    public int paperTrash = 100;
    public int branches = 20;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!WorldDecor.IsGameScene || FindFirstObjectByType<WorldDebris>() != null) return;
        new GameObject("WorldDebris").AddComponent<WorldDebris>();
    }

    // 재질별로 모았다가 마지막에 합침
    readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();
    readonly Dictionary<Material, bool> castsShadow = new Dictionary<Material, bool>();
    Mesh cube, cylinder, quad;

    void Add(Material m, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (!parts.TryGetValue(m, out var list)) parts[m] = list = new List<CombineInstance>();
        list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, rot, scale) });
    }

    static Mesh PrimitiveMesh(PrimitiveType t)
    {
        var go = GameObject.CreatePrimitive(t);
        var m = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return m;
    }

    System.Collections.IEnumerator Start()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new System.Random(seed);
        cube = PrimitiveMesh(PrimitiveType.Cube);
        cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
        quad = PrimitiveMesh(PrimitiveType.Quad);

        // 재질 (색 변형 몇 개씩)
        Material[] concreteMats = { WorldDecor.SolidMaterial("Debris_Concrete1", new Color(0.55f, 0.55f, 0.53f)), WorldDecor.SolidMaterial("Debris_Concrete2", new Color(0.42f, 0.42f, 0.41f)) };
        Material[] brickMats = { WorldDecor.SolidMaterial("Debris_Brick1", new Color(0.55f, 0.24f, 0.16f)), WorldDecor.SolidMaterial("Debris_Brick2", new Color(0.45f, 0.2f, 0.14f)), WorldDecor.SolidMaterial("Debris_Brick3", new Color(0.62f, 0.33f, 0.22f)) };
        var glassMat = WorldDecor.FadeMaterial("Debris_Glass", Texture2D.whiteTexture, new Color(0.75f, 0.88f, 0.95f, 0.55f), 3020);
        glassMat.SetFloat("_Glossiness", 0.9f);
        Material[] paperMats = { WorldDecor.SolidMaterial("Debris_Paper1", new Color(0.86f, 0.84f, 0.78f)), WorldDecor.SolidMaterial("Debris_Paper2", new Color(0.72f, 0.66f, 0.52f)), WorldDecor.SolidMaterial("Debris_Wrapper", new Color(0.62f, 0.18f, 0.16f)), WorldDecor.SolidMaterial("Debris_Wrapper2", new Color(0.2f, 0.35f, 0.6f)) };
        var branchMat = WorldDecor.SolidMaterial("Debris_Branch", new Color(0.3f, 0.21f, 0.13f));
        foreach (var m in concreteMats) castsShadow[m] = true;
        foreach (var m in brickMats) castsShadow[m] = true;
        castsShadow[branchMat] = true;

        // ── 잔해 더미: 건물 바깥 벽 아래 ──
        var piles = new List<(Vector3 pos, Vector3 outward)>();
        var bRoot = WorldDecor.SceneRoot("Buildings");
        if (bRoot != null)
        {
            var bs = bRoot.GetComponentsInChildren<MeshRenderer>();
            for (int guard = 0; piles.Count < rubblePiles && guard < rubblePiles * 10 && bs.Length > 0; guard++)
            {
                var b = rng.Pick(bs);
                if (!WorldDecor.RandomWallTriangle(b, rng, 0f, 3f, out var tri)) continue;
                Vector3 wallPt = WorldDecor.RandomPointInTriangle(rng, tri.a, tri.b, tri.c);
                Vector3 probe = wallPt + tri.normal * rng.Range(1.2f, 2.4f);
                if (WorldDecor.GroundPointNear(probe + Vector3.up * 2f, 0.3f, rng, out var hit) && Mathf.Abs(hit.point.y - b.bounds.min.y) < 2f)
                    piles.Add((hit.point, tri.normal));
            }
        }
        yield return null;

        int nB = 0, nC = 0, nG = 0;
        for (int p = 0; p < piles.Count; p++)
        {
            int left = piles.Count - p;
            int bHere = (bricks - nB) / left, cHere = (concreteChunks - nC) / left, gHere = (glassShards - nG) / left;
            var (center, outward) = piles[p];
            Vector3 along = Vector3.Cross(Vector3.up, outward);
            for (int i = 0; i < bHere + cHere + gHere; i++)
            {
                // 벽 쪽으로 많이, 바깥으로 갈수록 드문드문 (무너져 쌓인 느낌)
                // 더미 가운데로 몰리게 (두 난수 곱 → 가운데 확률 높음)
                Vector3 offset = along * (rng.Range(-1.2f, 1.2f) * rng.Range(0.3f, 1f)) + outward * (rng.Range(-0.9f, 1.1f) * rng.Range(0.3f, 1f)); // 중심이 벽에서 1.2m 이상이라 -0.9여도 벽 밖
                float pileHeight = Mathf.Max(0f, 1f - offset.magnitude / 1.3f) * 0.35f; // 가운데일수록 위에 얹힘 (쌓인 느낌)
                if (!WorldDecor.GroundPointNear(center + offset + Vector3.up * 1.5f, 0.05f, rng, out var hit)) continue;
                Quaternion yaw = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                if (i < bHere)
                {
                    // 벽돌: 실제(0.2×0.06×0.1)보다 크게 - 탑뷰 40m에서 보이게
                    var s = new Vector3(0.36f, 0.12f, 0.18f) * rng.Range(0.85f, 1.15f);
                    var rot = yaw * Quaternion.Euler(rng.Range(-25f, 25f), 0f, rng.Range(-20f, 20f));
                    Add(rng.Pick(brickMats), cube, hit.point + Vector3.up * (s.y * 0.4f + pileHeight * rng.Range(0f, 1f)), rot, s); nB++;
                }
                else if (i < bHere + cHere)
                {
                    // 콘크리트 조각: 납작하고 들쭉날쭉, 반쯤 묻힘
                    float k = rng.Range(0.3f, 0.75f);
                    var s = new Vector3(k * rng.Range(0.8f, 1.4f), k * rng.Range(0.35f, 0.6f), k * rng.Range(0.7f, 1.2f));
                    var rot = yaw * Quaternion.Euler(rng.Range(-30f, 30f), 0f, rng.Range(-30f, 30f));
                    Add(rng.Pick(concreteMats), cube, hit.point + Vector3.up * (s.y * 0.25f + pileHeight * rng.Range(0f, 0.6f)), rot, s); nC++;
                }
                else
                {
                    // 유리 조각: 바닥에 납작하게 누운 작은 판
                    float k = rng.Range(0.15f, 0.35f);
                    var rot = yaw * Quaternion.Euler(90f + rng.Range(-8f, 8f), 0f, rng.Range(0f, 360f));
                    Add(glassMat, quad, hit.point + Vector3.up * 0.025f, rot, new Vector3(k, k * rng.Range(0.4f, 0.9f), 1f)); nG++;
                }
            }
        }
        yield return null;

        // ── 종이/쓰레기: 도로 위 ──
        int nP = 0;
        for (int i = 0; i < paperTrash; i++)
        {
            if (!WorldDecor.RoadPoint(rng, out var hit, out _)) continue;
            float k = rng.Range(0.22f, 0.45f);
            var rot = Quaternion.LookRotation(-hit.normal, Quaternion.Euler(0f, rng.Range(0f, 360f), 0f) * Vector3.forward) * Quaternion.Euler(rng.Range(-10f, 10f), rng.Range(-10f, 10f), 0f);
            // 구겨진 쓰레기(작은 상자) 30%, 나머지는 납작한 종이
            if (rng.NextDouble() < 0.3)
                Add(rng.Pick(paperMats), cube, hit.point + hit.normal * k * 0.15f, Quaternion.Euler(rng.Range(0f, 30f), rng.Range(0f, 360f), rng.Range(0f, 30f)), new Vector3(k * 0.6f, k * 0.3f, k * 0.5f));
            else
                Add(rng.Pick(paperMats), quad, hit.point + hit.normal * 0.03f, rot, new Vector3(k, k * rng.Range(0.6f, 1.3f), 1f));
            nP++;
        }
        yield return null;

        // ── 나뭇가지: 도심 나무 근처 (없으면 도로) ──
        var trees = new List<Vector3>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.name.StartsWith("Tree-")) trees.Add(r.bounds.center);
        int nBr = 0;
        for (int guard = 0; nBr < branches && guard < branches * 8; guard++)
        {
            // 나무 주변 바닥을 못 찾으면(나무 밑이 화단/풀 등) 도로 위로
            RaycastHit hit;
            bool ok = trees.Count > 0 && guard < branches * 5 ? WorldDecor.GroundPointNear(rng.Pick(trees) + Vector3.down * 2f, 5f, rng, out hit) : WorldDecor.RoadPoint(rng, out hit, out _);
            if (!ok) continue;
            float len = rng.Range(0.9f, 2f), thick = rng.Range(0.06f, 0.12f);
            Quaternion lay = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f) * Quaternion.Euler(90f + rng.Range(-6f, 6f), 0f, 0f); // 눕힘
            Vector3 basePos = hit.point + Vector3.up * thick * 0.5f;
            Add(branchMat, cylinder, basePos, lay, new Vector3(thick, len * 0.5f, thick)); // Cylinder 높이 2 → 0.5배
            // 잔가지 1~2개
            int twigs = rng.Next(1, 3);
            for (int t = 0; t < twigs; t++)
            {
                Vector3 along = lay * Vector3.up;
                Vector3 at = basePos + along * rng.Range(-0.3f, 0.35f) * len;
                Quaternion twigRot = Quaternion.AngleAxis(rng.Range(25f, 50f) * (rng.NextDouble() < 0.5 ? 1f : -1f), Vector3.up) * lay;
                float tl = len * rng.Range(0.25f, 0.4f);
                Add(branchMat, cylinder, at + (twigRot * Vector3.up) * tl * 0.5f, twigRot, new Vector3(thick * 0.5f, tl * 0.5f, thick * 0.5f));
            }
            nBr++;
        }
        yield return null;

        // ── 합치기 ──
        var group = WorldDecor.Group("Debris");
        int meshes = 0;
        foreach (var kv in parts)
        {
            var mesh = new Mesh { name = "Debris_" + kv.Key.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(kv.Value.ToArray(), true, true);
            var go = new GameObject(kv.Key.name);
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = kv.Key;
            mr.shadowCastingMode = castsShadow.ContainsKey(kv.Key) ? ShadowCastingMode.On : ShadowCastingMode.Off;
            meshes++;
        }
        Debug.Log($"[WorldDebris] {sw.ElapsedMilliseconds}ms  더미 {piles.Count}곳: 벽돌 {nB} 콘크리트 {nC} 유리 {nG} / 종이·쓰레기 {nP} / 나뭇가지 {nBr} = 총 {nB + nC + nG + nP + nBr}개, 메쉬 {meshes}개");
    }
}

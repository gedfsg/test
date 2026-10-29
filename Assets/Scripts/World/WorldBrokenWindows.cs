using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 4 - 깨진 창문 (건물 창문의 30%).
/// Kenney 건물은 창문이 따로 없고 색상 텍스처(colormap)의 하늘색 칸이라,
/// 건물 메쉬에서 "밝은 푸른색"을 칠한 바깥 벽 삼각형 = 창유리로 보고 그 위에 깨진 유리 데칼을 붙임.
///  - 창 한 장 = 삼각형 2개 → 벽면 기준 사각형으로 묶어서 한 장으로
///  - 금 간 창 / 완전히 깨져 안이 어두운 창 반반
///  - 건물이 숨겨지면(카메라 가림/실내) 같이 숨음
/// 게임 씬 로드 시 자동 생성, 건물 몇 채씩 프레임을 나눠 처리
/// </summary>
public class WorldBrokenWindows : MonoBehaviour
{
    public int seed = 9090;
    [Range(0f, 1f)] public float brokenRatio = 0.3f;
    [Tooltip("이보다 큰 유리면(고층 건물 통유리 외벽)은 창 한 장이 아니라서 제외")]
    public float maxPaneArea = 30f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!WorldDecor.IsGameScene || FindFirstObjectByType<WorldBrokenWindows>() != null) return;
        new GameObject("WorldBrokenWindows").AddComponent<WorldBrokenWindows>();
    }

    // colormap은 읽기 금지 텍스처라 GPU에서 복사해서 읽음 (텍스처마다 한 번)
    static Color32[] ReadPixels(Texture t)
    {
        var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(t, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tx = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
        tx.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var px = tx.GetPixels32();
        Destroy(tx);
        return px;
    }

    /// <summary>창유리 색: 밝은 푸른색 (Kenney colormap의 창문 칸 0.5,0.65,0.9 등)</summary>
    static bool IsGlass(Color32 c) => c.b >= 190 && c.b - c.r >= 50;

    System.Collections.IEnumerator Start()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new System.Random(seed);
        var bRoot = WorldDecor.SceneRoot("Buildings");
        if (bRoot == null) yield break;

        var mat = WorldDecor.FadeMaterial("BrokenWindows", WorldDecorTextures.GlassCrack(seed), Color.white, 3015);
        mat.SetFloat("_Glossiness", 0.6f);
        var group = WorldDecor.Group("BrokenWindows");
        var texPixels = new Dictionary<Texture, Color32[]>();
        int panes = 0, broken = 0, n = 0;

        foreach (var r in bRoot.GetComponentsInChildren<MeshRenderer>())
        {
            var tex = r.sharedMaterial != null ? r.sharedMaterial.mainTexture : null;
            if (tex == null) continue;
            if (!texPixels.TryGetValue(tex, out var px)) texPixels[tex] = px = ReadPixels(tex);
            int w = tex.width, h = tex.height;

            // 창유리 삼각형 → 벽면 사각형(가로=벽 따라, 세로=위)으로 묶기 (같은 창의 삼각형 두 개는 같은 사각형)
            var windows = new Dictionary<Vector3Int, (Vector3 center, Vector3 normal, Vector3 side, float width, float height)>();
            foreach (var tri in WorldDecor.WallTriangles(r, 0f, 1000f))
            {
                if (tri.area > maxPaneArea) continue;
                int ix = Mathf.Clamp((int)(Mathf.Repeat(tri.uv.x, 1f) * w), 0, w - 1), iy = Mathf.Clamp((int)(Mathf.Repeat(tri.uv.y, 1f) * h), 0, h - 1);
                if (!IsGlass(px[iy * w + ix])) continue;
                Vector3 side = Vector3.Cross(Vector3.up, tri.normal).normalized;
                Vector3 up = Vector3.Cross(tri.normal, side);
                float s0 = Mathf.Min(Vector3.Dot(tri.a, side), Mathf.Min(Vector3.Dot(tri.b, side), Vector3.Dot(tri.c, side)));
                float s1 = Mathf.Max(Vector3.Dot(tri.a, side), Mathf.Max(Vector3.Dot(tri.b, side), Vector3.Dot(tri.c, side)));
                float u0 = Mathf.Min(Vector3.Dot(tri.a, up), Mathf.Min(Vector3.Dot(tri.b, up), Vector3.Dot(tri.c, up)));
                float u1 = Mathf.Max(Vector3.Dot(tri.a, up), Mathf.Max(Vector3.Dot(tri.b, up), Vector3.Dot(tri.c, up)));
                float depth = Vector3.Dot(tri.a, tri.normal);
                Vector3 center = side * ((s0 + s1) * 0.5f) + up * ((u0 + u1) * 0.5f) + tri.normal * depth;
                var key = Vector3Int.RoundToInt(center * 10f); // 10cm 단위로 같은 창 판정
                if (!windows.ContainsKey(key)) windows[key] = (center, tri.normal, side, s1 - s0, u1 - u0);
            }

            var batch = new WorldDecor.QuadBatch();
            foreach (var win in windows.Values)
            {
                panes++;
                if (rng.NextDouble() >= brokenRatio) continue;
                // 0,1 = 금 간 창 / 2,3 = 완전히 깨진 창
                batch.Add(win.center + win.normal * 0.03f, win.normal, win.side, win.width * 0.97f, win.height * 0.97f, WorldDecor.AtlasCell(rng.Next(4)));
                broken++;
            }
            var go = batch.Build("BrokenWindows_" + r.name, group, mat);
            if (go != null) go.AddComponent<DecorFollowHost>().host = r;

            if (++n % 8 == 0) yield return null; // 건물 8채마다 한 프레임
        }
        Debug.Log($"[WorldBrokenWindows] {sw.ElapsedMilliseconds}ms  창문 {panes}장 중 {broken}장 깨짐 ({(panes > 0 ? broken * 100 / panes : 0)}%)");
    }
}

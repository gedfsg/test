using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 폐허 꾸미기(데칼/파편/파티클/깨진 유리/쓰러진 오브젝트) 공용 도우미.
///  - 전부 런타임 자동 생성 (씬 파일은 안 건드림 → 팀원과 씬 충돌 없음). 고정 시드라 매번 같은 배치
///  - 같은 종류는 메쉬 하나로 합쳐서 그림 (드로우콜 최소)
///  - 생성물은 씬 루트 "_WorldDecor" 아래에 모임
/// </summary>
public static class WorldDecor
{
    // ── 씬 ─────────────────────────────────────

    // 씬 다시 로드(재시작) 때 이전 씬의 캐시(파괴된 콜라이더 등)가 남지 않게 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterReset()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= ResetCache;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += ResetCache;
    }

    static void ResetCache(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        roadColliders = null; roadWeights = null; roadsRoot = null; container = null; meshArrays.Clear();
    }

    /// <summary>게임 씬(낮밤 사이클 있는 씬)에서만 꾸밈</summary>
    public static bool IsGameScene => Object.FindFirstObjectByType<DayNightCycle>() != null;

    public static GameObject SceneRoot(string name)
    {
        foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (g.name == name) return g;
        return null;
    }

    static Transform container;
    /// <summary>생성물을 모아두는 "_WorldDecor/{name}"</summary>
    public static Transform Group(string name)
    {
        if (container == null)
        {
            var g = SceneRoot("_WorldDecor");
            container = g != null ? g.transform : new GameObject("_WorldDecor").transform;
        }
        var t = container.Find(name);
        if (t == null) { t = new GameObject(name).transform; t.SetParent(container, false); }
        return t;
    }

    // ── 랜덤 ───────────────────────────────────

    public static float Range(this System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
    public static T Pick<T>(this System.Random r, IList<T> list) => list[r.Next(list.Count)];

    // ── 위치 찾기 ──────────────────────────────

    static Collider[] roadColliders;
    static float[] roadWeights;
    static Transform roadsRoot;

    /// <summary>도로 표면 위 랜덤 점 (도로 타일 넓이 비례). 차/소품이 덮고 있는 곳은 제외</summary>
    public static bool RoadPoint(System.Random rng, out RaycastHit hit, out Transform tile)
    {
        hit = default; tile = null;
        if (roadColliders == null)
        {
            var roads = SceneRoot("Roads");
            if (roads == null) return false;
            roadsRoot = roads.transform;
            roadColliders = roads.GetComponentsInChildren<Collider>();
            roadWeights = new float[roadColliders.Length];
            float sum = 0f;
            for (int i = 0; i < roadColliders.Length; i++) { var s = roadColliders[i].bounds.size; sum += s.x * s.z; roadWeights[i] = sum; }
        }
        if (roadColliders.Length == 0) return false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float pick = rng.Range(0f, roadWeights[roadWeights.Length - 1]);
            int idx = System.Array.FindIndex(roadWeights, w => w >= pick);
            var col = roadColliders[Mathf.Max(0, idx)];
            Bounds b = col.bounds;
            var origin = new Vector3(rng.Range(b.min.x, b.max.x), b.max.y + 3f, rng.Range(b.min.z, b.max.z));
            if (Physics.Raycast(origin, Vector3.down, out hit, b.size.y + 8f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.IsChildOf(roadsRoot) && hit.normal.y > 0.9f)
            {
                tile = col.transform;
                return true;
            }
        }
        return false;
    }

    /// <summary>center 주변(반경 radius) 바닥 점. 건물 지붕/차 위 같은 곳 대신 바닥(위를 보는 면)만</summary>
    public static bool GroundPointNear(Vector3 center, float radius, System.Random rng, out RaycastHit hit)
    {
        hit = default;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float a = rng.Range(0f, Mathf.PI * 2f), d = Mathf.Sqrt(rng.Range(0f, 1f)) * radius;
            var origin = center + new Vector3(Mathf.Cos(a) * d, 6f, Mathf.Sin(a) * d);
            if (Physics.Raycast(origin, Vector3.down, out hit, 14f, ~0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.9f && hit.point.y < center.y + 1.5f && hit.collider.attachedRigidbody == null)
                return true;
        }
        return false;
    }

    /// <summary>건물 메쉬의 바깥 벽 삼각형 (월드 좌표). 거의 수직이고 지면 근처~높이 maxHeight까지인 면만</summary>
    public struct WallTri { public Vector3 a, b, c, normal; public Vector2 uv; public float area; }

    public static List<WallTri> WallTriangles(MeshRenderer r, float minHeight, float maxHeight)
    {
        var list = new List<WallTri>();
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return list;
        var mesh = mf.sharedMesh; var tr = r.transform;
        var v = mesh.vertices; var uv = mesh.uv; var tris = mesh.triangles;
        float groundY = r.bounds.min.y;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = tr.TransformPoint(v[tris[i]]), b = tr.TransformPoint(v[tris[i + 1]]), c = tr.TransformPoint(v[tris[i + 2]]);
            Vector3 n = Vector3.Cross(b - a, c - a);
            float area2 = n.magnitude; if (area2 < 1e-4f) continue;
            n /= area2;
            if (Mathf.Abs(n.y) > 0.15f) continue; // 벽(수직)만
            float cy = (a.y + b.y + c.y) / 3f - groundY;
            if (cy < minHeight || cy > maxHeight) continue;
            // 바깥쪽을 보는 면만 (건물 중심에서 멀어지는 방향)
            Vector3 outward = (a + b + c) / 3f - r.bounds.center; outward.y = 0f;
            if (Vector3.Dot(outward, n) <= 0f) continue;
            Vector2 tuv = uv.Length > 0 ? (uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f : Vector2.zero;
            list.Add(new WallTri { a = a, b = b, c = c, normal = n, uv = tuv, area = area2 * 0.5f });
        }
        return list;
    }

    static readonly Dictionary<Mesh, (Vector3[] v, int[] t)> meshArrays = new Dictionary<Mesh, (Vector3[], int[])>();

    /// <summary>
    /// 건물 바깥 벽에서 랜덤 삼각형 하나 (전체를 다 계산하지 않고 무작위로 골라 조건 확인 → 큰 건물도 빠름).
    /// 조건: 거의 수직, 바닥에서 minHeight~maxHeight, 건물 바깥을 봄
    /// </summary>
    public static bool RandomWallTriangle(MeshRenderer r, System.Random rng, float minHeight, float maxHeight, out WallTri tri, int tries = 80)
    {
        tri = default;
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return false;
        var mesh = mf.sharedMesh;
        if (!meshArrays.TryGetValue(mesh, out var arr)) meshArrays[mesh] = arr = (mesh.vertices, mesh.triangles);
        if (arr.t.Length < 3) return false;
        var tr = r.transform; float groundY = r.bounds.min.y; Vector3 center = r.bounds.center;
        for (int k = 0; k < tries; k++)
        {
            int i = rng.Next(arr.t.Length / 3) * 3;
            Vector3 a = tr.TransformPoint(arr.v[arr.t[i]]), b = tr.TransformPoint(arr.v[arr.t[i + 1]]), c = tr.TransformPoint(arr.v[arr.t[i + 2]]);
            Vector3 n = Vector3.Cross(b - a, c - a);
            float area2 = n.magnitude; if (area2 < 0.05f) continue; // 너무 작은 조각 제외
            n /= area2;
            if (Mathf.Abs(n.y) > 0.15f) continue;
            float cy = (a.y + b.y + c.y) / 3f - groundY;
            if (cy < minHeight || cy > maxHeight) continue;
            Vector3 outward = (a + b + c) / 3f - center; outward.y = 0f;
            if (Vector3.Dot(outward, n) <= 0f) continue;
            tri = new WallTri { a = a, b = b, c = c, normal = n, area = area2 * 0.5f };
            return true;
        }
        return false;
    }

    public static Vector3 RandomPointInTriangle(System.Random rng, Vector3 a, Vector3 b, Vector3 c)
    {
        float u = rng.Range(0f, 1f), w = rng.Range(0f, 1f);
        if (u + w > 1f) { u = 1f - u; w = 1f - w; }
        return a + (b - a) * u + (c - a) * w;
    }

    // ── 머티리얼 ───────────────────────────────

    static Material fadeTemplate;
    /// <summary>반투명(Standard Fade) 머티리얼. 조명 받음(밤엔 어두워짐). 템플릿은 Resources에 있어 빌드에서도 셰이더 변형이 남음</summary>
    public static Material FadeMaterial(string name, Texture tex, Color tint, int queue)
    {
        if (fadeTemplate == null) fadeTemplate = Resources.Load<Material>("WorldDecor/DecorFade");
        Material m;
        if (fadeTemplate != null) m = new Material(fadeTemplate);
        else
        {
            m = new Material(Shader.Find("Standard"));
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        m.name = name;
        m.mainTexture = tex; m.color = tint;
        m.SetFloat("_Glossiness", 0.05f); m.SetFloat("_Metallic", 0f);
        m.renderQueue = queue;
        return m;
    }

    /// <summary>불투명 머티리얼 (파편 등)</summary>
    public static Material SolidMaterial(string name, Color color, float smoothness = 0.1f)
    {
        var m = new Material(Shader.Find("Standard")) { name = name, color = color };
        m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    // ── 사각형 묶음 (데칼) ─────────────────────

    /// <summary>많은 사각형(데칼)을 메쉬 하나로 합쳐 그리기 위한 버퍼</summary>
    public class QuadBatch
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        public int Count => verts.Count / 4;

        /// <summary>center에 normal 방향을 보는 w×h 사각형. tangent = 가로 방향. uvRect = 아틀라스 칸</summary>
        public void Add(Vector3 center, Vector3 normal, Vector3 tangent, float w, float h, Rect uvRect)
        {
            normal.Normalize();
            tangent = Vector3.ProjectOnPlane(tangent, normal).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            Vector3 x = tangent * (w * 0.5f), y = bitangent * (h * 0.5f);
            int i0 = verts.Count;
            verts.Add(center - x - y); verts.Add(center + x - y); verts.Add(center + x + y); verts.Add(center - x + y);
            for (int k = 0; k < 4; k++) normals.Add(normal);
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin)); uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax)); uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            // 앞면이 normal 쪽에서 보이게: Cross(v1-v0, v2-v0)가 normal 방향 (bitangent = normal×tangent라 0→1→2 순서)
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }

        public GameObject Build(string name, Transform parent, Material mat)
        {
            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }
    }

    /// <summary>2×2 아틀라스 칸 (변형 4개)</summary>
    public static Rect AtlasCell(int i) => new Rect((i % 2) * 0.5f, (i / 2) * 0.5f, 0.5f, 0.5f);
}

/// <summary>
/// 벽에 붙인 데칼(탄흔/깨진 유리)이 붙은 건물을 따라 숨게 함.
/// 카메라 가림(CameraObstacleFader)/실내(BuildingInterior)는 건물 renderer.enabled를 끄는데,
/// 데칼은 별도 오브젝트라 허공에 떠 보이므로 매 프레임 건물 상태를 따라감 (enabled는 건드리지 않고 forceRenderingOff 사용)
/// </summary>
public class DecorFollowHost : MonoBehaviour
{
    public Renderer host;
    Renderer self;

    void Awake() => self = GetComponent<Renderer>();

    void LateUpdate()
    {
        if (self == null) return;
        bool hide = host == null || !host.enabled || !host.gameObject.activeInHierarchy;
        if (self.forceRenderingOff != hide) self.forceRenderingOff = hide;
    }
}

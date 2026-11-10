#if UNITY_EDITOR
using System.IO;
using Unity.AI.Navigation;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 돌진 학습 씬(노리는 좀비) 생성 도구.
/// 메뉴: Tools → Zombie → 돌진 학습 씬 생성
///
/// 원형 구역(반경 28m, 엄폐용 벽 6개) × 3x3. 구역마다 쏘는 더미 1 + 노리는 좀비 4.
/// 좀비는 캡슐이지만 게임 좀비와 같은 부품을 쓴다: ZombieController(FSM 뇌) + ZombieQuirks(좀비다움) + ZombieLungeAgent.
/// 학습할 것은 '언제 돌진할지' 하나뿐이다 (행동 1분기 2택, 관측 16).
///
/// 1구역(디버그)은 Heuristic Only —— 학습기 없이 예전 규칙(5~9m)으로 돌진하는지 배선만 확인.
/// 3x3(학습)은 Default —— mlagents-learn에 연결된다.
/// </summary>
public static class ZombieLungeSceneBuilder
{
    private const string ScenePath   = "Assets/Scenes/ZombieTrainingLunge.unity";
    private const float  Radius      = 28f;
    private const float  Spacing     = 80f;
    private const float  WallHeight  = 3f;
    private const int    WallSegments = 28;
    private const int    ZombiesPerArena = 4;

    [MenuItem("Tools/Zombie/돌진 학습 씬 생성 (1구역 · 디버그용)")]
    public static void BuildSingle() => Build(1);

    [MenuItem("Tools/Zombie/돌진 학습 씬 생성 (3x3 · 학습용)")]
    public static void BuildGrid() => Build(3);

    private static bool single;

    private static void Build(int grid)
    {
        string path = grid == 1 ? ScenePath.Replace(".unity", "_Debug.unity") : ScenePath;
        if (!EditorUtility.DisplayDialog("돌진 학습 씬 생성",
                $"새 씬을 만들어 {path} 에 저장합니다.\n구역 {grid * grid}개 (구역마다 더미 1 + 좀비 {ZombiesPerArena}).\n\n" +
                "기존 씬이 있으면 덮어씁니다. 계속할까요?", "생성", "취소"))
            return;

        single = grid == 1;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        for (int x = 0; x < grid; x++)
        for (int z = 0; z < grid; z++)
            BuildArena($"LungeArena_{x}_{z}", new Vector3(x * Spacing, 0f, z * Spacing));

        var cam = Camera.main;
        if (cam != null)
        {
            float span = (grid - 1) * Spacing * 0.5f;
            float h = (grid == 1 ? 1.6f : 3.2f) * Radius * 2f;
            cam.transform.position = new Vector3(span, h, span - Radius * 1.4f);
            cam.transform.rotation = Quaternion.Euler(65f, 0f, 0f);
            cam.farClipPlane = 1000f;
        }

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[돌진 학습 씬] 생성 완료 → {path}");
    }

    private static void BuildArena(string name, Vector3 center)
    {
        var root = new GameObject(name);
        root.transform.position = center;

        // 바닥 (원기둥 + 메시 충돌체: NavMesh가 충돌체 모양으로 구워진다)
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform);
        ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale    = new Vector3(Radius * 2f, 0.5f, Radius * 2f);
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.AddComponent<MeshCollider>();
        Paint(ground, Mat("Training_Ground", new Color(0.82f, 0.82f, 0.80f)));

        // 원형 외벽
        float segLen = 2f * Mathf.PI * Radius / WallSegments + 0.6f;
        for (int i = 0; i < WallSegments; i++)
        {
            float deg = 360f / WallSegments * i;
            float rad = deg * Mathf.Deg2Rad;
            var w = Wall(root, $"Wall_{i:00}",
                new Vector3(Mathf.Sin(rad) * Radius, WallHeight * 0.5f, Mathf.Cos(rad) * Radius),
                new Vector3(segLen, WallHeight, 1f));
            w.transform.localRotation = Quaternion.Euler(0f, deg, 0f);
        }

        // 엄폐용 벽 6개 (고정): 더미가 벽 너머 좀비를 못 보고 못 맞힌다 → '보이는 순간'의 판단이 생긴다
        var covers = new[]
        {
            (new Vector3(-10f, 0f,  8f), new Vector3(7f, WallHeight, 1f),  20f),
            (new Vector3( 11f, 0f,  6f), new Vector3(1f, WallHeight, 7f),  0f),
            (new Vector3(  3f, 0f, 15f), new Vector3(6f, WallHeight, 1f), -15f),
            (new Vector3(-13f, 0f, -8f), new Vector3(1f, WallHeight, 6f),  10f),
            (new Vector3(  9f, 0f,-12f), new Vector3(6f, WallHeight, 1f),  30f),
            (new Vector3( -2f, 0f,-16f), new Vector3(4f, WallHeight, 4f),  0f),
        };
        for (int i = 0; i < covers.Length; i++)
        {
            var (pos, size, yaw) = covers[i];
            var c = Wall(root, $"Cover_{i}", new Vector3(pos.x, WallHeight * 0.5f, pos.z), size);
            c.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        var dummy = BuildDummy(root, Vector3.zero);
        var arena = root.AddComponent<LungeTrainingArena>();
        arena.dummy = dummy;
        for (int i = 0; i < ZombiesPerArena; i++)
        {
            float rad = (360f / ZombiesPerArena * i + 45f) * Mathf.Deg2Rad;
            var z = BuildZombie(root, new Vector3(Mathf.Sin(rad) * 18f, 1f, Mathf.Cos(rad) * 18f), i);
            z.targetOverride = dummy.transform;
            z.GetComponent<ZombieLungeAgent>().arena = arena;
            arena.zombies.Add(z);
        }
        dummy.threat = arena.zombies[0].transform;
    }

    private static ZombieController BuildZombie(GameObject parent, Vector3 localPos, int index)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"LungeZombie_{index}";
        Paint(go, Mat("Training_Zombie", new Color(0.85f, 0.15f, 0.15f)));
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = localPos;
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());   // 컨트롤러가 따로 붙인다

        var controller = go.AddComponent<ZombieController>();
        controller.showHealthBar = false;
        controller.drawGizmos    = false;
        controller.moveSpeed     = 2.8f;           // 게임 일반 좀비와 같게 (판마다 학습장이 일반/러너를 고른다)
        controller.zombieQuirks  = true;
        go.AddComponent<ZombieFSMBrain>();          // 쫓는 건 규칙 뇌 그대로

        var quirks = go.AddComponent<ZombieQuirks>();
        quirks.trainingMode = true;                 // 학습용 대상이 있어도 좀비다움을 켠다
        quirks.drawGizmo    = single;

        var bp = go.AddComponent<BehaviorParameters>();
        bp.BehaviorName = ZombieLungeAgent.BehaviorNameDefault;
        bp.BrainParameters.VectorObservationSize = ZombieLungeAgent.ObservationSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(2);   // 0 기다림 / 1 돌진
        bp.BehaviorType = single ? BehaviorType.HeuristicOnly : BehaviorType.Default;

        // ⚠ 에이전트를 DecisionRequester보다 먼저 붙인다 (RequireComponent가 '맨 Agent'를 끼워 넣지 않게)
        var agent = go.AddComponent<ZombieLungeAgent>();
        agent.MaxStep = 0;                          // 판 종료는 학습장이 정한다

        var req = go.AddComponent<DecisionRequester>();
        req.DecisionPeriod = 5;                     // 0.1초마다 "지금 돌진할까?"
        req.TakeActionsBetweenDecisions = true;

        foreach (var a in go.GetComponents<Agent>())
            if (a.GetType() == typeof(Agent)) Object.DestroyImmediate(a);

        return controller;
    }

    private static DummyPlayer BuildDummy(GameObject parent, Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "DummyPlayer";
        go.tag  = "Player";
        Paint(go, Mat("Training_Dummy", new Color(0.15f, 0.75f, 0.30f)));
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = localPos + Vector3.up;

        var nav = go.AddComponent<NavMeshAgent>();
        nav.radius = 0.4f;
        nav.height = 2f;
        // Health는 붙이지 않는다 (Player 태그가 죽으면 게임 전체가 멈춘다)

        var dummy = go.AddComponent<DummyPlayer>();
        dummy.minSpeed        = 2.5f;
        dummy.maxSpeed        = 4.5f;
        dummy.evasionQuality  = 1f;
        dummy.minFleeDistance = 12f;
        dummy.maxFleeDistance = 22f;
        dummy.shootingEnabled = true;               // yaml dummy_shoot로도 켜고 끈다
        dummy.roamRadius      = 10f;
        return dummy;
    }

    // ── 도우미 ──────────────────────────────────
    private static GameObject Wall(GameObject parent, string name, Vector3 localPos, Vector3 scale)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        w.name = name;
        w.transform.SetParent(parent.transform);
        w.transform.localPosition = localPos;
        w.transform.localScale    = scale;
        Paint(w, Mat("Training_Wall", new Color(0.45f, 0.47f, 0.52f)));
        return w;
    }

    private const string MaterialDir = "Assets/Materials/Training";

    private static Material Mat(string assetName, Color color)
    {
        string path = $"{MaterialDir}/{assetName}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        if (!Directory.Exists(MaterialDir)) { Directory.CreateDirectory(MaterialDir); AssetDatabase.Refresh(); }
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void Paint(GameObject go, Material m)
    {
        var r = go.GetComponent<Renderer>();
        if (r != null && m != null) r.sharedMaterial = m;
    }
}
#endif

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
/// 학습 씬을 통째로 생성하는 도구.
/// 메뉴: Tools → Zombie → 학습 씬 생성
///
/// 본 게임 씬(main.unity)으로는 학습할 수 없다. PPO는 수백만 스텝이 필요한데
/// 그러려면 에피소드가 짧고, 자동으로 리셋되고, 병렬로 돌아야 하기 때문이다.
/// 이 도구는 그 조건을 갖춘 작은 구역을 격자로 복제해 하나의 씬을 만든다.
///
/// 좀비와 더미는 일부러 캡슐 프리미티브로 만든다 —— 관측이 추상 벡터라
/// 생김새는 학습에 아무 영향이 없고, 애니메이션/스킨 비용만 빠져서 학습이 빨라진다.
/// </summary>
public static class ZombieTrainingSceneBuilder
{
    private const string ScenePath     = "Assets/Scenes/ZombieTraining.unity";
    private const string OpenScenePath = "Assets/Scenes/ZombieTrainingOpen.unity";

    // 구역 설정 (벽이 있는 사각 구역)
    private const float ArenaSize     = 40f;
    private const float ArenaSpacing  = 60f;
    private const float WallHeight    = 3f;

    // 열린 원형 구역
    // 사각 구역에서는 좀비가 더미를 벽·구석으로 몰아 잡는 법을 배웠다 —— 벽이 동료 역할을 대신해
    // 포위할 필요가 없었다. 구석이 없는 넓은 원형 구역에서는 빠른 도망자를 잡을 방법이
    // 퇴로를 막는 것뿐이므로 포위가 유일한 정답이 된다.
    private const float OpenRadius      = 35f;
    private const float OpenSpacing     = 100f;
    private const int   OpenWallSegments = 32;

    /// <summary>
    /// 구역 1개짜리. 배선 확인·디버깅용 —— 로그가 9배로 쏟아지지 않아 원인을 찾기 쉽다.
    /// </summary>
    [MenuItem("Tools/Zombie/학습 씬 생성 (1구역 · 디버그용)")]
    public static void BuildSingle() => Build(1, false);

    /// <summary>
    /// 구역 9개짜리. 실제 학습용 —— 한 번의 Play로 경험이 9배 쌓인다.
    /// 배선이 확인된 뒤에 이걸로 다시 만들 것.
    /// </summary>
    [MenuItem("Tools/Zombie/학습 씬 생성 (3x3 · 학습용)")]
    public static void BuildGrid() => Build(3, false);

    /// <summary>열린 원형 구역 1개. 배선 확인용.</summary>
    [MenuItem("Tools/Zombie/열린 원형 씬 생성 (1구역 · 디버그용)")]
    public static void BuildOpenSingle() => Build(1, true);

    /// <summary>
    /// 열린 원형 구역 9개. 포위 학습용.
    /// 사각 씬(ZombieTraining.unity)과 다른 파일로 저장되므로 대조 실험용으로 둘 다 남는다.
    /// </summary>
    [MenuItem("Tools/Zombie/열린 원형 씬 생성 (3x3 · 학습용)")]
    public static void BuildOpenGrid() => Build(3, true);

    private static void Build(int gridSize, bool open)
    {
        string path = open ? OpenScenePath : ScenePath;

        if (!EditorUtility.DisplayDialog(
                "학습 씬 생성",
                $"새 씬을 만들어 {path} 에 저장합니다.\n" +
                $"구역 {gridSize * gridSize}개가 생성됩니다.\n\n" +
                $"기존 씬이 있으면 덮어씁니다. 계속할까요?",
                "생성", "취소"))
            return;

        singleArena = gridSize == 1;
        openLayout  = open;
        float spacing = open ? OpenSpacing : ArenaSpacing;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        for (int x = 0; x < gridSize; x++)
        for (int z = 0; z < gridSize; z++)
        {
            Vector3 center = new Vector3(x * spacing, 0f, z * spacing);
            if (open) BuildOpenArena($"Arena_{x}_{z}", center);
            else      BuildArena($"Arena_{x}_{z}", center);
        }

        PlaceOverviewCamera(gridSize, spacing, open ? OpenRadius * 2f : ArenaSize);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, path);

        Debug.Log($"[학습 씬] 생성 완료 → {path}\n" +
                  $"구역 {gridSize * gridSize}개. Behavior Type을 Heuristic Only로 두고 먼저 배선을 확인하세요.");
    }

    // ─────────────────────────────────────────────
    private static bool singleArena;
    private static bool openLayout;

    private static void BuildArena(string name, Vector3 center)
    {
        var root = new GameObject(name);
        root.transform.position = center;

        float half = ArenaSize * 0.5f;

        // 바닥
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform);
        ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale    = new Vector3(ArenaSize, 1f, ArenaSize);
        Paint(ground, GetOrCreateMaterial("Training_Ground", new Color(0.82f, 0.82f, 0.80f)));

        // 외벽 4개 (좀비가 구역 밖으로 못 나가게)
        MakeWall(root, "Wall_N", new Vector3(0f,  WallHeight * 0.5f,  half), new Vector3(ArenaSize, WallHeight, 1f));
        MakeWall(root, "Wall_S", new Vector3(0f,  WallHeight * 0.5f, -half), new Vector3(ArenaSize, WallHeight, 1f));
        MakeWall(root, "Wall_E", new Vector3( half, WallHeight * 0.5f, 0f),  new Vector3(1f, WallHeight, ArenaSize));
        MakeWall(root, "Wall_W", new Vector3(-half, WallHeight * 0.5f, 0f),  new Vector3(1f, WallHeight, ArenaSize));

        // 내부 장애물 —— 차폐를 학습하려면 반드시 필요하다.
        // 탁 트인 바닥만 있으면 시야가 막힐 일이 없어서 Investigate가 영영 안 쓰인다.
        var obstacles = new Transform[5];
        obstacles[0] = MakeWall(root, "Block_0", new Vector3(-8f,  WallHeight * 0.5f,  6f),  new Vector3(10f, WallHeight, 1f)).transform;
        obstacles[1] = MakeWall(root, "Block_1", new Vector3( 9f,  WallHeight * 0.5f, -4f),  new Vector3(1f, WallHeight, 12f)).transform;
        obstacles[2] = MakeWall(root, "Block_2", new Vector3( 2f,  WallHeight * 0.5f, 12f),  new Vector3(8f, WallHeight, 1f)).transform;
        obstacles[3] = MakeWall(root, "Block_3", new Vector3(-12f, WallHeight * 0.5f, -9f),  new Vector3(1f, WallHeight, 9f)).transform;
        obstacles[4] = MakeWall(root, "Block_4", new Vector3( 5f,  WallHeight * 0.5f, -13f), new Vector3(7f, WallHeight, 1f)).transform;
        foreach (var o in obstacles) MakeDynamicObstacle(o.gameObject);

        // NavMesh —— 지오메트리를 다 올린 뒤에 구워야 한다
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        PopulateArena(root, obstacles, half - 3f, 12f, 3000,
                      new Vector3(12f, 1f, 12f),
                      new[] { new Vector3(-13f, 1f, -13f), new Vector3(-13f, 1f, 4f), new Vector3(4f, 1f, -13f) });
    }

    /// <summary>
    /// 구역에 더미 1명 + 좀비 3마리 + Arena 스크립트를 채운다. 사각/원형 구역 공용.
    /// </summary>
    private static void PopulateArena(GameObject root, Transform[] obstacles,
                                      float spawnRadius, float minSeparation, int maxSteps,
                                      Vector3 dummyLocal, Vector3[] zombieLocals)
    {
        var dummy = BuildDummy(root, dummyLocal);

        var zombieList = new System.Collections.Generic.List<ZombieController>();
        for (int i = 0; i < ZombieCount; i++)
            zombieList.Add(BuildZombie(root, zombieLocals[i % zombieLocals.Length], i));

        var arena = root.AddComponent<ZombieTrainingArena>();
        arena.zombies          = zombieList;
        arena.dummy            = dummy;
        arena.spawnRadius      = spawnRadius;
        arena.minSeparation    = minSeparation;
        arena.zombieSeparation = 6f;
        // 중심 거리 1.6이면 몸 사이가 0.6m 떠서 허공을 잡는 것처럼 보였다.
        // NavMeshAgent끼리 밀어내는 최소 거리(약 0.9)보다는 커야 실제로 잡힌다.
        arena.captureRadius    = 1.2f;
        arena.maxSteps         = maxSteps;
        arena.randomizeSpawns  = true;
        arena.movableObstacles = obstacles;
        arena.obstacleJitter   = 2.5f;

        // 구역끼리 섞이지 않도록 빌드 시점에 배선해 씬에 직렬화한다.
        // Arena.Awake에도 같은 배선이 있지만 Awake 순서가 보장되지 않는다.
        foreach (var z in zombieList)
        {
            z.targetOverride = dummy.transform;
            z.GetComponent<ZombieAgent>().arena = arena;
        }
        dummy.threat = zombieList[0].transform;

        if (singleArena && zombieList[0].GetComponent<ZombieWiringDebug>() == null)
            zombieList[0].gameObject.AddComponent<ZombieWiringDebug>();
    }

    /// <summary>
    /// 열린 원형 구역. 구석이 없고 장애물은 작은 기둥 4개뿐이다.
    ///
    /// 사각 구역에서는 벽과 구석이 퇴로를 대신 막아줘서 좀비가 포위 대신 벽 몰이를 배웠다.
    /// 원형 외벽에는 몰아넣을 구석이 없어서, 더미가 벽을 따라 미끄러져 빠져나갈 수 있다 ——
    /// 그 앞을 다른 좀비가 막아야만 잡힌다. 기둥은 시야 차폐가 의미를 잃지 않도록 남겨둔다.
    /// </summary>
    private static void BuildOpenArena(string name, Vector3 center)
    {
        var root = new GameObject(name);
        root.transform.position = center;

        // 바닥: 원기둥. 프리미티브 원기둥의 기본 충돌체는 캡슐이라 바닥으로 쓸 수 없으므로
        // 메시 충돌체로 바꾼다 (NavMesh가 충돌체 모양으로 구워지기 때문).
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform);
        ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale    = new Vector3(OpenRadius * 2f, 0.5f, OpenRadius * 2f);
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.AddComponent<MeshCollider>();
        Paint(ground, GetOrCreateMaterial("Training_Ground", new Color(0.82f, 0.82f, 0.80f)));

        // 원형 외벽: 짧은 벽 조각을 원 둘레에 이어 붙인다
        float segLen = 2f * Mathf.PI * OpenRadius / OpenWallSegments * 1.08f;   // 틈 안 생기게 살짝 겹침
        for (int i = 0; i < OpenWallSegments; i++)
        {
            float deg = 360f / OpenWallSegments * i;
            float rad = deg * Mathf.Deg2Rad;
            var wall = MakeWall(root, $"Wall_{i:00}",
                new Vector3(Mathf.Sin(rad) * OpenRadius, WallHeight * 0.5f, Mathf.Cos(rad) * OpenRadius),
                new Vector3(segLen, WallHeight, 1f));
            wall.transform.localRotation = Quaternion.Euler(0f, deg, 0f);
        }

        // 장애물 20개 —— 도시 블록처럼 길이가 다른 벽 + 건물 같은 덩어리.
        // 실제 배치는 매 에피소드 Arena가 새로 뽑는다(Scatter). 이때 벽끼리, 그리고 외곽과
        // 항상 4m 이상 떨어뜨려서 어느 벽이든 양쪽으로 돌아갈 수 있게 한다.
        // 막다른 곳이 없으면 몰아넣기가 통하지 않고, 양쪽에서 돌아 들어가는 협공만 통한다.
        //
        // 긴 변은 반드시 로컬 x축(스케일 x)에 둔다 —— Arena가 그걸 기준으로 간격을 계산한다.
        var sizes = new[]
        {
            new Vector3(14f, WallHeight, 1f), new Vector3(12f, WallHeight, 1f),
            new Vector3(12f, WallHeight, 1f), new Vector3(10f, WallHeight, 1f),
            new Vector3(10f, WallHeight, 1f), new Vector3( 8f, WallHeight, 1f),
            new Vector3( 8f, WallHeight, 1f), new Vector3( 6f, WallHeight, 1f),
            new Vector3( 6f, WallHeight, 4f), new Vector3( 5f, WallHeight, 5f),
            // 밀도를 높이기 위한 추가분
            new Vector3(14f, WallHeight, 1f), new Vector3(12f, WallHeight, 1f),
            new Vector3(10f, WallHeight, 1f), new Vector3(10f, WallHeight, 1f),
            new Vector3( 8f, WallHeight, 1f), new Vector3( 8f, WallHeight, 1f),
            new Vector3( 6f, WallHeight, 1f), new Vector3( 7f, WallHeight, 4f),
            new Vector3( 5f, WallHeight, 5f), new Vector3( 6f, WallHeight, 6f),
        };
        var obstacles = new Transform[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            float rad = (360f / sizes.Length * i) * Mathf.Deg2Rad;
            float pr  = OpenRadius * 0.4f;
            obstacles[i] = MakeWall(root, $"Obstacle_{i}",
                new Vector3(Mathf.Sin(rad) * pr, WallHeight * 0.5f, Mathf.Cos(rad) * pr),
                sizes[i]).transform;
            MakeDynamicObstacle(obstacles[i].gameObject);
        }

        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        // 구역이 넓고 막다른 곳이 없어 추격이 길다. 4000(80초)으로는 속도 차가 작을 때
        // 거리를 좁힐 시간이 부족했다 → 6000(120초)
        float r = OpenRadius - 8f;
        var arenaRoot = root;
        PopulateArena(root, obstacles, OpenRadius - 5f, 15f, 6000,
                      new Vector3(r * 0.5f, 1f, r * 0.5f),
                      new[]
                      {
                          new Vector3(-r * 0.7f, 1f, -r * 0.3f),
                          new Vector3(-r * 0.2f, 1f, -r * 0.8f),
                          new Vector3( r * 0.3f, 1f, -r * 0.6f),
                      });

        var arena = arenaRoot.GetComponent<ZombieTrainingArena>();
        arena.obstacleMode      = ZombieTrainingArena.ObstacleMode.Scatter;
        arena.scatterMinRadius  = 0f;
        arena.scatterMaxRadius  = OpenRadius - 6f;   // 외곽 벽과 최소 5m 통로
        arena.scatterMinSpacing = 4f;                // 벽끼리 최소 4m (좀비 몸 폭의 5배)
        arena.scatterMinActive  = 14;                // 실제 배치는 평균 13~14개 (10~17)
    }

    /// <summary>
    /// 움직이는 장애물로 만든다.
    ///
    /// NavMesh는 빌드 시점에 한 번 굽고 끝이라, 장애물을 런타임에 옮기면
    /// 옛 자리엔 보이지 않는 구멍이 남고 새 자리는 그냥 통과되는 문제가 있었다.
    /// 굽기에서는 빼고(NavMeshModifier.ignoreFromBuild) NavMeshObstacle의 carving으로
    /// 현재 위치에 맞춰 구멍을 뚫게 한다.
    /// </summary>
    private static void MakeDynamicObstacle(GameObject go)
    {
        var mod = go.AddComponent<NavMeshModifier>();
        mod.ignoreFromBuild = true;

        var obs = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obs.shape               = UnityEngine.AI.NavMeshObstacleShape.Box;
        obs.center              = Vector3.zero;
        obs.size                = Vector3.one;     // 로컬 크기 —— 트랜스폼 스케일을 따라간다
        obs.carving             = true;
        obs.carveOnlyStationary = false;           // 옮기는 즉시 반영 (리셋 때만 움직인다)
    }

    // ── 색상 ──────────────────────────────────────
    private const string MaterialDir = "Assets/Materials/Training";

    /// <summary>
    /// 학습 씬 전용 머티리얼. 에셋으로 저장해 씬을 다시 만들어도 재사용된다.
    /// </summary>
    private static Material GetOrCreateMaterial(string assetName, Color color)
    {
        string path = $"{MaterialDir}/{assetName}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!Directory.Exists(MaterialDir))
        {
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
        }

        // URP 프로젝트이므로 URP/Lit 우선, 없으면 빌트인 Standard로 폴백
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void Paint(GameObject go, Material mat)
    {
        var r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
    }

    private static GameObject MakeWall(GameObject parent, string name, Vector3 localPos, Vector3 scale)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent.transform);
        wall.transform.localPosition = localPos;
        wall.transform.localScale    = scale;
        Paint(wall, GetOrCreateMaterial("Training_Wall", new Color(0.45f, 0.47f, 0.52f)));
        return wall;
    }

    /// <summary>
    /// 구역당 좀비 수. 관측은 '가까운 동료 TeammateSlots명'만 보므로 이 값과 따로 정해도 된다.
    ///
    /// 3마리는 포위가 성립하는 최소 숫자라 셋이 정확히 120°씩 벌어져야만 퇴로가 막힌다.
    /// 무작위 탐색 중에 그런 배치가 우연히 나올 확률이 거의 없어서, 강화할 경험 자체가
    /// 생기지 않았다. 5마리면 흩어져 있기만 해도 반대편에 누군가 있을 확률이 크게 오른다.
    /// </summary>
    private const int ZombieCount = 5;

    private static ZombieController BuildZombie(GameObject parent, Vector3 localPos, int index)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = $"Zombie_{index}";
        Paint(go, GetOrCreateMaterial("Training_Zombie", new Color(0.85f, 0.15f, 0.15f)));
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = localPos;

        // ZombieController가 Awake에서 CapsuleCollider를 따로 붙이므로 프리미티브 것은 제거
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());

        var controller = go.AddComponent<ZombieController>();   // NavMeshAgent 자동 추가
        controller.showHealthBar = false;                       // 학습 중엔 UI 불필요
        controller.drawGizmos    = false;

        // 본 게임의 일반 좀비는 속도 2지만, 더미가 3~5로 도망치므로 그대로 두면
        // 좀비가 영원히 못 잡는다 → captureReward가 한 번도 발생하지 않아
        // "따라붙되 절대 못 잡는" 정책으로 수렴한다.
        // 학습 씬에서만 더미 속도 범위 가운데쯤으로 올려 포획이 가능하게 만든다.
        controller.moveSpeed = 3.5f;

        go.AddComponent<ZombieFSMBrain>();                      // 폴백용
        var mlBrain = go.AddComponent<ZombieMLBrain>();
        controller.brain = mlBrain;

        // ML-Agents 설정 —— 여기가 어긋나면 학습이 시작조차 안 된다
        var bp = go.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "Zombie";
        bp.BrainParameters.VectorObservationSize = ZombieAgent.ObservationSize;    // 자기 10 + 팀 7 + 동료 4x7 = 45
        bp.BrainParameters.NumStackedVectorObservations = 1;
        bp.BrainParameters.ActionSpec = new ActionSpec(2, new[] { 2 });             // 이동 2 + 공격 1분기(2택)
        // 1구역(디버그)은 Heuristic Only —— 트레이너 없이 배선만 확인하는 용도.
        // 3x3(학습)은 Default —— 이래야 mlagents-learn에 연결되어 실제로 학습된다.
        // Heuristic Only로 두고 학습을 돌리면 좀비는 직진 추격만 하고
        // 보상 곡선은 평평한 채 시간만 흘러간다.
        bp.BehaviorType = singleArena ? BehaviorType.HeuristicOnly : BehaviorType.Default;

        // ⚠ 순서가 중요하다. DecisionRequester에는 [RequireComponent(typeof(Agent))]가 붙어 있어서,
        //    ZombieAgent보다 먼저 추가하면 Unity가 "맨 Agent"를 자동으로 끼워 넣는다.
        //    그러면 Agent가 두 개가 되고 DecisionRequester는 먼저 붙은 맨 Agent를 잡는데,
        //    그쪽은 CollectObservations와 Heuristic이 비어 있어 관측 0개·행동 0이 된다.
        //    → ZombieAgent를 먼저 붙여서 RequireComponent를 만족시킨다.
        var agent = go.AddComponent<ZombieAgent>();
        agent.MaxStep = 0;   // 에피소드 종료는 Arena가 그룹 단위로 처리한다

        var requester = go.AddComponent<DecisionRequester>();
        requester.DecisionPeriod = 5;
        requester.TakeActionsBetweenDecisions = true;

        StripPlainAgents(go);

        return controller;
    }

    /// <summary>
    /// 혹시라도 끼어든 "맨 Agent" 컴포넌트를 제거한다.
    /// ZombieAgent 같은 파생 클래스만 남겨야 관측/행동이 제대로 전달된다.
    /// </summary>
    private static void StripPlainAgents(GameObject go)
    {
        foreach (var a in go.GetComponents<Agent>())
        {
            if (a.GetType() == typeof(Agent))
            {
                Debug.LogWarning($"[학습 씬] {go.name}: 자동 추가된 맨 Agent를 제거했습니다.");
                Object.DestroyImmediate(a);
            }
        }
    }

    private static DummyPlayer BuildDummy(GameObject parent, Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "DummyPlayer";
        go.tag  = "Player";
        Paint(go, GetOrCreateMaterial("Training_Dummy", new Color(0.15f, 0.75f, 0.30f)));     // ZombiePerception의 폴백 탐색 경로
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = localPos;

        var navAgent = go.AddComponent<NavMeshAgent>();
        navAgent.radius = 0.4f;
        navAgent.height = 2f;

        // Health는 붙이지 않는다. Player 태그가 죽으면 Health.Die()가
        // Time.timeScale = 0 으로 게임을 멈춰서 학습 전체가 정지한다.

        var dummy = go.AddComponent<DummyPlayer>();

        // 좀비 속도(3.5)를 사이에 두고 걸치게 한다.
        // 느린 에피소드는 정면 추격으로 잡히고, 빠른 에피소드는 몰아붙여야 잡힌다
        // → 한 가지 전략으로 수렴하기 어려워져 행동 다양성에도 도움이 된다.
        dummy.minSpeed = 2.5f;
        dummy.maxSpeed = 4.5f;

        // 열린 쪽을 계산해서 도망친다. 0으로 낮추면 단순 반대방향 도주가 되어
        // 구석에 잘 몰리고, 좀비가 협동을 배울 이유가 사라진다.
        dummy.evasionQuality = 1f;

        // 더미가 좀비를 늦게 알아채면(예전 최소 5m) 코앞까지 몰래 다가가 잡는 게 정답이 되어
        // 포위가 필요 없어진다. 사람 플레이어처럼 멀리서부터 반응하게 한다.
        dummy.minFleeDistance = 12f;
        dummy.maxFleeDistance = 22f;

        return dummy;
    }

    private static void PlaceOverviewCamera(int gridSize, float spacing, float arenaWidth)
    {
        var cam = Camera.main;
        if (cam == null) return;

        float span   = (gridSize - 1) * spacing * 0.5f;
        float height = (gridSize == 1 ? 1.4f : 3.0f) * arenaWidth;
        cam.transform.position = new Vector3(span, height, span - arenaWidth * 0.7f);
        cam.transform.rotation = Quaternion.Euler(65f, 0f, 0f);
        cam.farClipPlane = 1000f;
    }
}
#endif

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트용 좀비 소환기. 게임 안에서 학습장과 비슷한 조건(여러 마리 · 같은 속도 · 주변에서 동시 등장)을
/// 만들어 모델끼리, 그리고 FSM과 직접 비교해 보기 위한 도구. 본 게임 스포너와는 별개로 동작한다.
///
/// 사용법: 게임 씬에 빈 오브젝트를 만들고 이 컴포넌트를 붙인 뒤 프리팹 칸을 채운다.
///   F7 : 소환할 종류 전환 (포위 모델 → 지형·역할 모델 → FSM → 포위 조율기 → 조율기+RL → …)
///   F8 : 플레이어 주변에 좀비 소환
///   F9 : 이 도구로 소환한 좀비 전부 제거
/// (키를 못 쓰는 상황이면 컴포넌트 우클릭 메뉴로도 실행할 수 있다)
///
/// ⚠ 테스트 전용. 커밋하기 전에 씬에서 지우거나 비활성화할 것.
/// </summary>
public class ZombieTestSpawner : MonoBehaviour
{
    public enum Mode { RL, RLTerrain, FSM, Slot, SlotRL }

    [Header("프리팹")]
    [Tooltip("지난번 포위 모델(Zombie.onnx)을 단 _RL 프리팹")]
    public GameObject rlPrefab;
    [Tooltip("지형·역할 모델(이름에 terrain)을 단 _RL_Terrain 프리팹.\n" +
             "'RL 프리팹 만들기 (지형·역할 모델)' 메뉴로 만든다. 센서 구성이 달라서 _RL 프리팹과 섞어 쓸 수 없다.")]
    public GameObject rlTerrainPrefab;
    [Tooltip("FSM 비교용 프리팹. '포위 조율기' 모드도 이 프리팹을 쓴다 (비어 있으면 RL 프리팹을 RL을 끄고 쓴다).")]
    public GameObject fsmPrefab;
    [Tooltip("조율기+RL 모델(이름에 slot)을 단 _RL_Slot 프리팹. 'RL 프리팹 만들기 (조율기+RL 모델)' 메뉴로 만든다.")]
    public GameObject rlSlotPrefab;
    public Mode mode = Mode.RL;

    [Header("포위 조율기 모드")]
    [Tooltip("무리 조율기(HunterPackTactics) 조절값. 플레이 중에 바꿔도 바로 반영된다.")]
    public HunterTacticsSettings slotSettings = new HunterTacticsSettings();

    [Header("소환 조건")]
    [Tooltip("한 번에 소환할 수. 학습은 5마리 무리로 했다")]
    public int count = 5;
    [Tooltip("플레이어로부터의 거리 범위(m). 학습장 시작 거리는 15~30m 정도")]
    public float minDistance = 15f;
    public float maxDistance = 25f;
    [Tooltip("0보다 크면 소환한 좀비의 이동 속도를 이 값으로 덮어쓴다 (학습 때 3.5).\n" +
             "프로필 속도가 느리면 협동이 드러날 틈이 없어서 테스트용으로 쓴다.")]
    public float overrideMoveSpeed = 3.5f;
    [Tooltip("켜면 소환 즉시 플레이어 위치를 알고 시작한다 (학습장의 '첫 목격 보장'과 같은 조건).\n" +
             "끄면 각자 시야로 찾아야 한다.")]
    public bool alertOnSpawn = true;

    [Header("섞인 무리 (지형·역할 모델, 포위 조율기)")]
    [Tooltip("켜면 지형·역할 모델과 포위 조율기는 아래 속도 목록대로 섞인 무리로 나온다 (일반 3.5 / 빠른 4.0 / 탱커 2.5).\n" +
             "포위 조율기는 이 속도로 역할을 정한다: 빠른 좀비 = 앞 막기, 탱커 = 뒤에서 몰기, 일반 = 옆.\n" +
             "끄면 전부 Override Move Speed로 나온다 (같은 조건 비교용).")]
    public bool terrainMixed = true;
    public float[] terrainSpeeds = { 3.5f, 3.5f, 4.0f, 4.0f, 2.5f };
    [Tooltip("섞인 무리일 때 속도별로 색을 입힌다 (달리기 주황, 탱커 보라)")]
    public bool colorByRole = true;

    [Header("비교 환경")]
    [Tooltip("켜면 소환할 때마다 이 도구가 낸 좀비를 뺀 나머지 좀비를 전부 지우고,\n" +
             "게임 스포너(ZombieSpawner, HunterPackSpawner)를 꺼서 새로 나오지 않게 한다.\n" +
             "RL 좀비는 주변 RL 좀비를 동료로 읽기 때문에, 섞여 있으면 비교가 안 된다.\n" +
             "플레이 중에만 적용되고 씬 파일은 바뀌지 않는다.")]
    public bool isolateTest = true;

    [Header("키 / 화면 표시")]
    public bool enableHotkeys = true;
    public bool showLabel = true;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<HunterPackTactics> spawnedPacks = new List<HunterPackTactics>();
    private Transform player;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    void Update()
    {
        if (!enableHotkeys) return;
        if (Pressed(7))
        {
            int modeCount = System.Enum.GetValues(typeof(Mode)).Length;
            mode = (Mode)(((int)mode + 1) % modeCount);
            Debug.Log($"[테스트 소환] 다음 소환: {Label(mode)}");
        }
        if (Pressed(8)) SpawnWave();
        if (Pressed(9)) Clear();
    }

    private static string Label(Mode m) => m switch
    {
        Mode.RL        => "RL (지난번 포위 모델)",
        Mode.RLTerrain => "RL (지형·역할 모델)",
        Mode.Slot      => "포위 조율기 (자리 배정)",
        Mode.SlotRL    => "조율기+RL",
        _              => "FSM",
    };

    private GameObject PrefabFor(Mode m) => m switch
    {
        Mode.RL        => rlPrefab,
        Mode.RLTerrain => rlTerrainPrefab,
        Mode.Slot      => fsmPrefab != null ? fsmPrefab : rlPrefab,
        Mode.SlotRL    => rlSlotPrefab,
        _              => fsmPrefab,
    };

    private static bool Pressed(int f)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        return f == 7 ? kb.f7Key.wasPressedThisFrame
             : f == 8 ? kb.f8Key.wasPressedThisFrame
             :          kb.f9Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(f == 7 ? KeyCode.F7 : f == 8 ? KeyCode.F8 : KeyCode.F9);
#endif
    }

    [ContextMenu("좀비 소환")]
    public void SpawnWave()
    {
        var prefab = PrefabFor(mode);
        if (prefab == null) { Debug.LogWarning($"[테스트 소환] {Label(mode)} 프리팹 칸이 비어 있습니다."); return; }

        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) { Debug.LogWarning("[테스트 소환] Player 태그를 가진 오브젝트가 없습니다."); return; }
            player = go.transform;
        }

        if (isolateTest) IsolateFromGame();

        bool mixed = (mode == Mode.RLTerrain || mode == Mode.Slot || mode == Mode.SlotRL)
                     && terrainMixed && terrainSpeeds != null && terrainSpeeds.Length > 0;

        // 섞인 무리면 속도 목록을 섞어서 종류별로 한쪽에 몰려 나오지 않게 한다
        var speeds = new List<float>();
        for (int i = 0; i < count; i++)
            speeds.Add(mixed ? terrainSpeeds[i % terrainSpeeds.Length] : overrideMoveSpeed);
        for (int i = speeds.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (speeds[i], speeds[j]) = (speeds[j], speeds[i]);
        }

        // 플레이어를 중심으로 고르게 흩어 놓는다 (한쪽에 몰려 시작하면 비교가 안 된다)
        float baseAngle = Random.Range(0f, 360f);
        int made = 0;
        var wave = new List<ZombieController>();
        for (int i = 0; i < count; i++)
        {
            for (int t = 0; t < 8; t++)
            {
                float ang  = baseAngle + 360f / count * i + Random.Range(-20f, 20f);
                float dist = Random.Range(minDistance, maxDistance);
                Vector3 cand = player.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;
                if (!NavMesh.SamplePosition(cand, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;

                var z = Instantiate(prefab, hit.position, Quaternion.LookRotation(Flat(player.position - hit.position)));
                var na = z.GetComponent<NavMeshAgent>();
                if (na != null) na.Warp(hit.position);
                spawned.Add(z);
                var zc = z.GetComponent<ZombieController>();
                if (zc != null) wave.Add(zc);

                float speed = speeds[i];
                if (mixed && colorByRole) Tint(z, speed);
                StartCoroutine(Setup(z, speed));
                made++;
                break;
            }
        }
        // 포위 조율기 / 조율기+RL 모드: 이번에 나온 좀비들을 한 무리로 묶는다
        //   Slot   : 조율기가 FSM 몸을 직접 움직인다
        //   SlotRL : 조율기는 자리만 계산하고, RL 모델이 자리 센서를 보고 움직인다
        if ((mode == Mode.Slot || mode == Mode.SlotRL) && wave.Count > 0)
        {
            var pack = HunterPackTactics.Create(mode == Mode.Slot ? "테스트 포위 조율기" : "테스트 조율기+RL",
                                                null, wave, player, slotSettings, takeControl: mode == Mode.Slot);
            if (alertOnSpawn) pack.Alert(player.position);
            spawnedPacks.Add(pack);
        }

        string speedText = mixed ? string.Join("/", speeds) : (overrideMoveSpeed > 0 ? overrideMoveSpeed.ToString("0.0") : "프로필");
        Debug.Log($"[테스트 소환] {Label(mode)} {made}마리 소환 (속도 {speedText})");
    }

    /// <summary>프로필 적용이 끝난 다음 프레임에 속도를 덮어쓰고, 필요하면 플레이어 위치를 알려준다.</summary>
    private IEnumerator Setup(GameObject z, float speed)
    {
        yield return null;
        if (z == null) yield break;

        var c = z.GetComponent<ZombieController>();
        if (c == null) yield break;

        if (speed > 0f) c.moveSpeed = speed;
        if (alertOnSpawn && player != null) c.HearNoise(player.position);
    }

    /// <summary>달리기(일반보다 빠름)는 주황, 탱커(느림)는 보라. 일반은 그대로.</summary>
    private void Tint(GameObject go, float speed)
    {
        Color tint;
        if      (speed > overrideMoveSpeed + 0.1f) tint = new Color(1f, 0.6f, 0.2f);
        else if (speed < overrideMoveSpeed - 0.1f) tint = new Color(0.6f, 0.35f, 0.9f);
        else return;

        var block = new MaterialPropertyBlock();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mat = r.sharedMaterial;
            if (mat == null) continue;
            int id = mat.HasProperty(BaseColorId) ? BaseColorId : mat.HasProperty(ColorId) ? ColorId : -1;
            if (id == -1) continue;
            r.GetPropertyBlock(block);
            block.SetColor(id, mat.GetColor(id) * tint);
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>게임 스포너를 끄고, 이 도구가 내지 않은 좀비를 전부 지운다.</summary>
    private void IsolateFromGame()
    {
        int spawnersOff = 0;
        foreach (var s in FindObjectsByType<ZombieSpawner>(FindObjectsSortMode.None))
            if (s.enabled) { s.enabled = false; spawnersOff++; }
        foreach (var s in FindObjectsByType<HunterPackSpawner>(FindObjectsSortMode.None))
            if (s.enabled) { s.enabled = false; spawnersOff++; }

        spawned.RemoveAll(z => z == null);
        var mine = new HashSet<GameObject>(spawned);
        int removed = 0;
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None))
        {
            if (mine.Contains(z.gameObject)) continue;
            Destroy(z.gameObject);
            removed++;
        }

        if (spawnersOff > 0 || removed > 0)
            Debug.Log($"[테스트 소환] 비교 환경: 스포너 {spawnersOff}개 끔, 다른 좀비 {removed}마리 제거");
    }

    [ContextMenu("소환한 좀비 제거")]
    public void Clear()
    {
        foreach (var z in spawned) if (z != null) Destroy(z);
        spawned.Clear();
        foreach (var p in spawnedPacks) if (p != null) Destroy(p.gameObject);
        spawnedPacks.Clear();
        Debug.Log("[테스트 소환] 제거 완료");
    }

    void OnGUI()
    {
        if (!showLabel) return;
        GUI.Label(new Rect(12, 12, 600, 24),
            $"테스트 소환: {Label(mode)}   (F7 전환 / F8 소환 / F9 제거)");
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v : Vector3.forward;
    }
}

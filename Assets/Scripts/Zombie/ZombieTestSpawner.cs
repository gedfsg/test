using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트용 좀비 소환기. 게임 안에서 학습장과 비슷한 조건(여러 마리 · 같은 속도 · 주변에서 동시 등장)을
/// 만들어 RL과 FSM을 직접 비교해 보기 위한 도구. 본 게임 스포너와는 별개로 동작한다.
///
/// 사용법: 게임 씬에 빈 오브젝트를 만들고 이 컴포넌트를 붙인 뒤 프리팹 두 칸을 채운다.
///   F7 : RL ↔ FSM 전환
///   F8 : 플레이어 주변에 좀비 소환
///   F9 : 이 도구로 소환한 좀비 전부 제거
/// (키를 못 쓰는 상황이면 컴포넌트 우클릭 메뉴로도 실행할 수 있다)
///
/// ⚠ 테스트 전용. 커밋하기 전에 씬에서 지우거나 비활성화할 것.
/// </summary>
public class ZombieTestSpawner : MonoBehaviour
{
    [Header("프리팹")]
    public GameObject rlPrefab;
    public GameObject fsmPrefab;
    public bool useRL = true;

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

    [Header("키")]
    public bool enableHotkeys = true;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private Transform player;

    void Update()
    {
        if (!enableHotkeys) return;
        if (Pressed(7)) { useRL = !useRL; Debug.Log($"[테스트 소환] 다음 소환: {(useRL ? "RL" : "FSM")}"); }
        if (Pressed(8)) SpawnWave();
        if (Pressed(9)) Clear();
    }

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
        var prefab = useRL ? rlPrefab : fsmPrefab;
        if (prefab == null) { Debug.LogWarning("[테스트 소환] 프리팹 칸이 비어 있습니다."); return; }

        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) { Debug.LogWarning("[테스트 소환] Player 태그를 가진 오브젝트가 없습니다."); return; }
            player = go.transform;
        }

        // 플레이어를 중심으로 고르게 흩어 놓는다 (한쪽에 몰려 시작하면 비교가 안 된다)
        float baseAngle = Random.Range(0f, 360f);
        int made = 0;
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
                StartCoroutine(Setup(z));
                made++;
                break;
            }
        }
        Debug.Log($"[테스트 소환] {(useRL ? "RL" : "FSM")} {made}마리 소환 (속도 {(overrideMoveSpeed > 0 ? overrideMoveSpeed.ToString("0.0") : "프로필")})");
    }

    /// <summary>프로필 적용이 끝난 다음 프레임에 속도를 덮어쓰고, 필요하면 플레이어 위치를 알려준다.</summary>
    private IEnumerator Setup(GameObject z)
    {
        yield return null;
        if (z == null) yield break;

        var c = z.GetComponent<ZombieController>();
        if (c == null) yield break;

        if (overrideMoveSpeed > 0f) c.moveSpeed = overrideMoveSpeed;
        if (alertOnSpawn && player != null) c.HearNoise(player.position);
    }

    [ContextMenu("소환한 좀비 제거")]
    public void Clear()
    {
        foreach (var z in spawned) if (z != null) Destroy(z);
        spawned.Clear();
        Debug.Log("[테스트 소환] 제거 완료");
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v : Vector3.forward;
    }
}

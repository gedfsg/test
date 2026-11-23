using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// (2026-11-23) NPC 스폰 지점: 이 오브젝트 위치에 npcPrefabs 중 하나를 랜덤으로 세움.
///  - 게임(씬)이 열릴 때 한 번만 고름 → 플레이 중에는 같은 모델 유지, 새 게임을 시작하면 다시 랜덤
///  - 바닥에 붙임 (위에서 아래로 레이 → 발이 지면에), 플레이어 쪽을 바라봄 (가까이 오면 천천히 고개(몸)를 돌림)
///  - 프리팹: Tools/NPC/Build NPC Prefabs가 만든 npc_character_01~04 (Assets/Character/NPC/Prefabs)
///    → 이미 4종이 등록된 Assets/Character/NPC/Prefabs/NPCSpawnPoint.prefab을 씬에 끌어다 놓으면 됨
/// 지금은 씬에 배치된 곳이 없음 (상점 상인은 기존 애니메이션 시민 모델 유지 - 사용자 결정). 생존자·미션 NPC가 생기면 이걸로 세우면 됨.
/// </summary>
public class NPCSpawner : MonoBehaviour
{
    [Tooltip("이 중 하나를 랜덤으로 세움")]
    public List<GameObject> npcPrefabs = new List<GameObject>();

    [Tooltip("바닥에 붙이기 (위에서 레이를 쏴서 처음 닿는 면)")]
    public bool snapToGround = true;

    [Tooltip("생길 때 플레이어 쪽을 봄")]
    public bool facePlayerOnSpawn = true;

    [Tooltip("플레이어가 이 거리(m) 안에 있으면 계속 플레이어 쪽으로 몸을 돌림 (0 = 안 돌림)")]
    public float lookAtPlayerRadius = 8f;

    [Tooltip("몸 돌리는 속도 (도/초)")]
    public float turnSpeed = 120f;

    /// <summary>세워진 NPC (없으면 null)</summary>
    public GameObject Spawned { get; private set; }

    Transform player;

    void Start() => Spawn();

    /// <summary>NPC를 세움 (이미 있으면 그대로)</summary>
    public GameObject Spawn()
    {
        if (Spawned != null) return Spawned;
        var valid = npcPrefabs.FindAll(p => p != null);
        if (valid.Count == 0) { Debug.LogWarning($"[NPCSpawner] {name}: NPC 프리팹이 등록되지 않았습니다"); return null; }

        var prefab = valid[Random.Range(0, valid.Count)];   // 이번 게임에서 이 지점의 모델 (한 번만 고름)
        Vector3 pos = transform.position;
        if (snapToGround && TryGround(pos, out Vector3 g)) pos = g;
        Spawned = Instantiate(prefab, pos, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), transform);
        Spawned.name = prefab.name;

        FindPlayer();
        if (facePlayerOnSpawn && player != null) Spawned.transform.rotation = YawTo(player.position);
        return Spawned;
    }

    void Update()
    {
        if (Spawned == null || lookAtPlayerRadius <= 0f) return;
        FindPlayer();
        if (player == null) return;
        Vector3 d = player.position - Spawned.transform.position; d.y = 0f;
        if (d.sqrMagnitude > lookAtPlayerRadius * lookAtPlayerRadius || d.sqrMagnitude < 0.01f) return;
        Spawned.transform.rotation = Quaternion.RotateTowards(Spawned.transform.rotation, YawTo(player.position), turnSpeed * Time.deltaTime);
    }

    void FindPlayer()
    {
        if (player != null) return;
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    Quaternion YawTo(Vector3 target)
    {
        Vector3 d = target - Spawned.transform.position; d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Spawned.transform.rotation;
    }

    /// <summary>위 3m에서 아래로 레이 → 처음 닿는 바닥 (트리거·플레이어 제외)</summary>
    static bool TryGround(Vector3 pos, out Vector3 ground)
    {
        ground = pos;
        var hits = Physics.RaycastAll(pos + Vector3.up * 3f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (h.collider.CompareTag("Player") || h.collider.GetComponentInParent<NPCSpawner>() != null) continue;
            if (h.distance < best) { best = h.distance; ground = h.point; }
        }
        return best < float.MaxValue;
    }

    void OnDrawGizmos()
    {
        // 씬 뷰 표시: 사람 키 캡슐 + 정면 방향
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 1.2f, new Vector3(0.7f, 2.4f, 0.7f));
        Gizmos.DrawLine(transform.position + Vector3.up * 1.2f, transform.position + Vector3.up * 1.2f + transform.forward);
    }
}

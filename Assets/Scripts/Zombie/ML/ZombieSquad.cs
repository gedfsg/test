using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// 게임용 좀비 무리 관리자. 학습장에서 Arena가 하던 일 중 '정보 공유'만 맡는다.
///
/// 하는 일:
///   1. RL 좀비(ZombieAgent)들의 명단을 관리한다 → 각 좀비가 '가까운 동료 4명'을 고를 때 쓴다.
///   2. 누군가 플레이어를 보고 있으면, 그 좀비 근처(shareRadius)의 좀비들에게 위치와 이동 방향을 전한다.
///
/// 씬에 따로 둘 필요는 없다. RL 좀비가 처음 생길 때 자동으로 만들어진다.
/// 값을 조절하고 싶으면 씬에 빈 오브젝트를 만들고 이 컴포넌트를 직접 붙여두면 그걸 쓴다.
/// </summary>
public class ZombieSquad : MonoBehaviour
{
    [Tooltip("동료가 본 플레이어 정보를 이 거리 안의 좀비에게만 전한다(m). 0이면 맵 전체가 공유.\n" +
             "학습장은 반경 35m 원형이라 무리 전체가 공유했다. 맵이 넓으면 전체 공유는\n" +
             "맵의 모든 좀비가 한꺼번에 몰려드는 '군단'이 되니, 난이도에 맞춰 조절할 것.")]
    public float shareRadius = 35f;

    public string playerTag = "Player";

    // ─────────────────────────────────────────────
    private static ZombieSquad instance;

    public static bool HasInstance => instance != null;

    public static ZombieSquad Instance
    {
        get
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<ZombieSquad>();
            if (instance == null) instance = new GameObject("ZombieSquad (auto)").AddComponent<ZombieSquad>();
            return instance;
        }
    }

    private readonly List<ZombieAgent> members = new List<ZombieAgent>();
    private readonly List<Vector3>     spotters = new List<Vector3>();
    private Transform player;

    public IReadOnlyList<ZombieAgent> Members => members;

    public void Register(ZombieAgent a)
    {
        if (a == null || members.Contains(a)) return;
        members.Add(a);
        if (a.intel == null) a.intel = new ZombieTeamIntel();   // 좀비마다 자기 몫 (공유 반경이 달라서)
    }

    public void Unregister(ZombieAgent a) => members.Remove(a);

    // ─────────────────────────────────────────────
    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;

        // 학습 때는 물리 0.02초 × 결정 주기 5 = 0.1초마다 판단했다.
        // 게임의 물리 주기가 다르면 판단 간격이 달라져 움직임이 둔해지거나 성급해진다.
        if (!Mathf.Approximately(Time.fixedDeltaTime, 0.02f))
            Debug.LogWarning($"[ZombieSquad] Fixed Timestep이 {Time.fixedDeltaTime}초입니다. " +
                             "학습 때는 0.02초였습니다 (Project Settings → Time).");
    }

    void OnEnable()  { Academy.Instance.AgentPreStep += OnAgentPreStep; }
    void OnDisable() { if (Academy.IsInitialized) Academy.Instance.AgentPreStep -= OnAgentPreStep; }
    void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>관측 수집 직전에 정보를 갱신한다 (학습장 Arena와 같은 시점).</summary>
    private void OnAgentPreStep(int academyStep)
    {
        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go == null) return;
            player = go.transform;
        }

        members.RemoveAll(m => m == null);

        spotters.Clear();
        foreach (var m in members)
            if (m.isActiveAndEnabled && m.SeesTarget) spotters.Add(m.transform.position);

        float   r2 = shareRadius * shareRadius;
        Vector3 p  = player.position;

        foreach (var m in members)
        {
            if (m.intel == null) continue;

            bool heard = false;
            if (spotters.Count > 0)
            {
                if (shareRadius <= 0f) heard = true;
                else
                {
                    Vector3 me = m.transform.position;
                    foreach (var s in spotters)
                        if ((s - me).sqrMagnitude <= r2) { heard = true; break; }
                }
            }

            // 누군가 실제로 볼 때만 위치가 들어간다 (벽 투시 아님)
            m.intel.Tick(heard, p);
        }
    }
}

using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 돌진 학습장 (노리는 좀비). 메뉴: Tools → Zombie → 돌진 학습 씬 생성
///
/// 구역마다 쏘는 더미 1 + 노리는 좀비 4. 좀비는 게임과 같은 조건으로 움직인다:
/// FSM 뇌로 쫓고, 좀비다움(늦은 반응 · 어슬렁 · '왁!' · 돌진)이 켜져 있다. 강화학습은 '언제 돌진할지'만 고른다.
///
/// 보상 (좀비마다 따로 —— 무리 학습이 아니라 각자 판단):
///   + attack_reward   더미를 때리면 (그 좀비는 새 판: 멀리서 다시 나온다)
///   - hit_penalty     총에 맞을 때마다 (맞으면 잠깐 경직)
///   - kill_penalty    체력이 바닥나면 (그 좀비는 새 판)
///   - time_penalty    초당 (늑장 방지)
/// 구역 전체는 episode_seconds마다 더미 위치·총·속도를 새로 뽑고 모두 다시 시작한다.
///
/// 통계 (텐서보드 Lunge/*): 돌진이 틈을 노리는지 본다.
///   StartWhileReloading vs BaseReloading : 돌진 시작 때 재장전 중이던 비율 vs 평소 재장전 중인 시간 비율
///   StartWhileUntargeted vs BaseUntargeted: 돌진 시작 때 표적이 아니던 비율 vs 평소 (보이는데) 표적이 아닌 비율
///   → 앞이 뒤보다 확실히 크면 '틈을 노린다'를 배운 것.
///   SuccessRate: 돌진이 공격으로 이어진 비율 / StartDistance: 돌진 시작 거리
/// </summary>
public class LungeTrainingArena : MonoBehaviour
{
    [Header("배선 (씬 생성 도구가 채운다)")]
    public DummyPlayer dummy;
    public List<ZombieController> zombies = new List<ZombieController>();

    [Header("판")]
    [Tooltip("구역 전체를 새로 시작하는 간격(초) (yaml: lunge_episode_seconds)")]
    public float episodeSeconds = 60f;
    [Tooltip("좀비가 다시 나올 때 더미에서의 거리(m) 범위 (yaml: lunge_spawn_min / lunge_spawn_max)")]
    public Vector2 respawnDistance = new Vector2(10f, 18f);
    [Tooltip("더미가 판마다 시작하는 곳: 구역 가운데에서 이 반경 안")]
    public float dummyStartRadius = 8f;

    [Header("좀비")]
    [Tooltip("좀비 체력 (yaml: lunge_zombie_hp). 더미 총알 한 발 = dummy_damage")]
    public float zombieHp = 60f;
    [Tooltip("맞았을 때 경직(초) (yaml: lunge_hit_stun)")]
    public float hitStun = 0.25f;
    [Tooltip("일반 좀비 속도 (게임 프로필과 같게) (yaml: lunge_normal_speed)")]
    public float normalSpeed = 2.8f;
    [Tooltip("러너 속도 (yaml: lunge_runner_speed)")]
    public float runnerSpeed = 4f;
    [Tooltip("다시 나올 때 러너일 확률 (yaml: lunge_runner_chance)")]
    [Range(0f, 1f)] public float runnerChance = 0.3f;

    [Header("보상")]
    public float attackReward = 1f;
    public float hitPenalty   = 0.03f;
    public float killPenalty  = 0.5f;
    [Tooltip("초당 벌점")]
    public float timePenalty  = 0.003f;

    [Header("더미 속도 (yaml: lunge_dummy_min / lunge_dummy_max)")]
    [Tooltip("게임 플레이어처럼 대체로 버티고 쏘다가 조금씩 물러난다. 추격 학습장처럼 빠르게 도망치면 어슬렁거리는 좀비가 영영 못 다가온다.")]
    public float dummyMinSpeed = 1.5f;
    public float dummyMaxSpeed = 3f;

    [Header("더미 기본값 (에디터 확인용 —— 학습 때는 yaml의 dummy_* 값이 덮어쓴다)")]
    public bool  dummySetup        = true;
    public float dummyFireMin      = 0.2f;
    public float dummyFireMax      = 0.5f;
    public float dummyDamage       = 15f;
    public int   dummyMag          = 30;
    public float dummyReload       = 1.5f;
    public float dummyRange        = 25f;
    public float dummySpread       = 15f;
    [Tooltip("이 배율 × 도망 거리(12~22m) 안에 좀비가 오면 물러선다. 0.35 → 4~8m")]
    public float dummyFleeScale    = 0.35f;
    [Tooltip("이 거리 안에 좀비가 오면 달린다 (돌진을 피하는 순간)")]
    public float dummySprintDist   = 4f;

    [Tooltip("판마다 더미 탄창 크기를 이 범위에서 뽑는다 (yaml: lunge_mag_min / lunge_mag_max) —— 재장전 틈이 자주 생기게")]
    public Vector2Int dummyMagRange = new Vector2Int(12, 30);

    [Header("노리는 좀비 (ZombieQuirks에 넣어 준다)")]
    [Tooltip("돌진 쿨다운(초) 범위 (yaml: lunge_cooldown_min / lunge_cooldown_max)")]
    public Vector2 agentCooldown = new Vector2(9f, 12f);
    [Tooltip("돌진 실패 뒤 헐떡임(초) 범위 (yaml: lunge_fail_rest_min / lunge_fail_rest_max)")]
    public Vector2 agentFailRest = new Vector2(1.2f, 1.8f);

    [Header("총소리")]
    [Tooltip("더미가 쏘면 이 반경(m) 안의 좀비에게 총소리를 들려준다 (게임의 NoiseSystem과 같은 역할).\n" +
             "없으면 더미가 벽 뒤로 가는 순간 좀비들이 놓치고 흩어진다.")]
    public float gunshotRadius = 35f;
    private int   lastShots;
    private float nextNoiseAt;

    // ── 내부 ────────────────────────────────────
    private readonly List<ZombieLungeAgent> agents = new List<ZombieLungeAgent>();
    private readonly Dictionary<Transform, int> indexOf = new Dictionary<Transform, int>();
    private float[] hp;
    private float   timer;
    private bool    wired;

    // 통계 (구역 판 단위)
    private int   lunges, lungeHits, lungeReload, lungeUntargeted, attacks, kills, hitsTaken;
    private float lungeDistSum;
    private int   baseSteps, baseReload, baseSeenSteps, baseUntargeted;
    private float[] lungeStartTime;
    private int[]   lungeCat;            // 돌진 시작 때 상황: 0 재장전 중 / 1 다른 좀비를 겨눔 / 2 나를 겨눔
    private float[] availableSince;      // 돌진 가능해진 시각 (-1 = 지금 불가)
    private readonly List<ZombieQuirks> quirksList = new List<ZombieQuirks>();
    private readonly int[] catLunges = new int[3], catHits = new int[3];
    private int   lungeFails, waitCount;
    private float waitSum;

    void Start()
    {
        Wire();
        ResetArena();
    }

    private void Wire()
    {
        if (wired) return;
        wired = true;
        agents.Clear();
        indexOf.Clear();
        hp = new float[zombies.Count];
        lungeStartTime = new float[zombies.Count];
        lungeCat       = new int[zombies.Count];
        availableSince = new float[zombies.Count];
        quirksList.Clear();
        var list = new List<Transform>();
        for (int i = 0; i < zombies.Count; i++)
        {
            var z = zombies[i];
            var a = z != null ? z.GetComponent<ZombieLungeAgent>() : null;
            agents.Add(a);
            if (z == null) continue;
            indexOf[z.transform] = i;
            list.Add(z.transform);
            z.targetOverride = dummy != null ? dummy.transform : null;
            if (a != null) a.arena = this;
            z.AttackLanded += OnAttackLanded;
            var q = z.GetComponent<ZombieQuirks>();
            quirksList.Add(q);
            if (q != null)
            {
                q.trainingMode = true;
                int idx = i;
                q.LungeStarted += qq => OnLungeStarted(idx, qq);
                q.LungeFailed  += qq => lungeFails++;
            }
        }
        if (dummy != null)
        {
            dummy.SetThreats(list);
            dummy.ShotHit += OnShot;
            if (dummySetup)
            {
                // 게임 총에 가까운 기본값 (yaml이 있으면 Randomize 때 덮어쓴다)
                dummy.SetGun(dummyFireMin, dummyFireMax, 70f, 150f, dummyMag, dummyReload, dummyRange, dummySpread);
                dummy.bulletDamage      = dummyDamage;
                dummy.shootingEnabled   = true;
                dummy.shootingFleeScale = dummyFleeScale;
                dummy.sprintEnabled     = true;
                dummy.sprintTriggerDist = dummySprintDist;
                dummy.reactionTime      = 0.6f;
                dummy.perceptionDelay   = 0.4f;
                dummy.limitedSight      = true;
            }
        }
    }

    private void ReadParams()
    {
        if (!Academy.IsInitialized) return;
        var ep = Academy.Instance.EnvironmentParameters;
        episodeSeconds = ep.GetWithDefault("lunge_episode_seconds", episodeSeconds);
        zombieHp       = ep.GetWithDefault("lunge_zombie_hp",       zombieHp);
        hitStun        = ep.GetWithDefault("lunge_hit_stun",        hitStun);
        normalSpeed    = ep.GetWithDefault("lunge_normal_speed",    normalSpeed);
        runnerSpeed    = ep.GetWithDefault("lunge_runner_speed",    runnerSpeed);
        runnerChance   = ep.GetWithDefault("lunge_runner_chance",   runnerChance);
        attackReward   = ep.GetWithDefault("lunge_attack_reward",   attackReward);
        hitPenalty     = ep.GetWithDefault("lunge_hit_penalty",     hitPenalty);
        killPenalty    = ep.GetWithDefault("lunge_kill_penalty",    killPenalty);
        timePenalty    = ep.GetWithDefault("lunge_time_penalty",    timePenalty);
        dummyMinSpeed  = ep.GetWithDefault("lunge_dummy_min",       dummyMinSpeed);
        dummyMaxSpeed  = ep.GetWithDefault("lunge_dummy_max",       dummyMaxSpeed);
        respawnDistance.x = ep.GetWithDefault("lunge_spawn_min",    respawnDistance.x);
        respawnDistance.y = ep.GetWithDefault("lunge_spawn_max",    respawnDistance.y);
        gunshotRadius  = ep.GetWithDefault("lunge_gunshot_radius",  gunshotRadius);
        dummyMagRange.x = Mathf.RoundToInt(ep.GetWithDefault("lunge_mag_min", dummyMagRange.x));
        dummyMagRange.y = Mathf.RoundToInt(ep.GetWithDefault("lunge_mag_max", dummyMagRange.y));
        agentCooldown.x = ep.GetWithDefault("lunge_cooldown_min",  agentCooldown.x);
        agentCooldown.y = ep.GetWithDefault("lunge_cooldown_max",  agentCooldown.y);
        agentFailRest.x = ep.GetWithDefault("lunge_fail_rest_min", agentFailRest.x);
        agentFailRest.y = ep.GetWithDefault("lunge_fail_rest_max", agentFailRest.y);
    }

    /// <summary>구역 전체 새 판: 통계 기록 → 더미 새로 뽑기 → 좀비 전부 다시 배치</summary>
    public void ResetArena()
    {
        RecordStats();
        ReadParams();
        timer = 0f;

        if (dummy != null)
        {
            dummy.minSpeed = dummyMinSpeed;
            dummy.maxSpeed = Mathf.Max(dummyMinSpeed, dummyMaxSpeed);
            Vector2 r = Random.insideUnitCircle * dummyStartRadius;
            dummy.TeleportTo(transform.position + new Vector3(r.x, 0f, r.y));
            // 탄창 크기 무작위 (yaml에 dummy_mag가 없으면 이 값이 쓰인다)
            dummy.magazineSize = Random.Range(Mathf.Min(dummyMagRange.x, dummyMagRange.y),
                                              Mathf.Max(dummyMagRange.x, dummyMagRange.y) + 1);
            dummy.Randomize();
        }
        foreach (var q in quirksList)
        {
            if (q == null) continue;
            q.agentLungeCooldown = agentCooldown;
            q.agentFailRest      = agentFailRest;
        }
        for (int i = 0; i < zombies.Count; i++) Respawn(i);
        lastShots = dummy != null ? dummy.ShotsFired : 0;
    }

    /// <summary>좀비 하나를 더미에서 떨어진 곳에 다시 내보낸다 (체력·속도 새로).</summary>
    private void Respawn(int i)
    {
        var z = zombies[i];
        if (z == null) return;
        z.moveSpeed = Random.value < runnerChance ? runnerSpeed : normalSpeed;

        Vector3 center = dummy != null ? dummy.transform.position : transform.position;
        Vector3 pos = z.transform.position;
        for (int k = 0; k < 10; k++)
        {
            Vector2 d = Random.insideUnitCircle.normalized * Random.Range(respawnDistance.x, respawnDistance.y);
            if (NavMesh.SamplePosition(center + new Vector3(d.x, 0f, d.y), out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                pos = hit.position;
                break;
            }
        }
        Vector3 look = center - pos; look.y = 0f;
        var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
        z.ResetForEpisode(pos, rot);
        hp[i] = zombieHp;
        lungeStartTime[i] = -1f;
        availableSince[i] = -1f;
    }

    void FixedUpdate()
    {
        if (!wired) return;
        timer += Time.fixedDeltaTime;

        // 총소리: 더미가 새로 쐈으면 (0.5초에 한 번) 주변 좀비에게 들려준다.
        // HearNoise는 좀비다움을 거친다 —— 일부는 못 들은 척, 멍한 좀비는 못 듣고, 나머지는 반응 지연 뒤 온다.
        if (dummy != null && gunshotRadius > 0f && dummy.ShotsFired != lastShots)
        {
            bool newShot = dummy.ShotsFired > lastShots;
            lastShots = dummy.ShotsFired;
            if (newShot && Time.time >= nextNoiseAt)
            {
                nextNoiseAt = Time.time + 0.5f;
                Vector3 p = dummy.transform.position;
                float r2 = gunshotRadius * gunshotRadius;
                foreach (var z in zombies)
                {
                    if (z == null || z.IsDead) continue;
                    if ((z.transform.position - p).sqrMagnitude <= r2) z.HearNoise(p);
                }
            }
        }

        // 돌진 가능해진 뒤 얼마나 기다렸다 쓰는지
        for (int i = 0; i < quirksList.Count; i++)
        {
            var q = quirksList[i];
            bool avail = q != null && q.LungeAvailableNow;
            if (avail && availableSince[i] < 0f) availableSince[i] = Time.time;
            else if (!avail && (q == null || !q.Lunging)) availableSince[i] = -1f;
        }

        // 기준선 통계: 평소 더미가 재장전 중인 시간, 좀비가 보이는데 표적이 아닌 시간
        if (dummy != null && dummy.ShootingActive)
        {
            baseSteps++;
            if (dummy.IsReloading) baseReload++;
            for (int i = 0; i < zombies.Count; i++)
            {
                var z = zombies[i];
                if (z == null || !z.SeesTarget) continue;
                baseSeenSteps++;
                if (dummy.AimTarget != z.transform) baseUntargeted++;
            }
        }

        if (timer >= episodeSeconds)
        {
            foreach (var a in agents) if (a != null) a.EpisodeInterrupted();
            ResetArena();
        }
    }

    /// <summary>
    /// 에이전트 행동 때마다 —— 시간 벌점. DecisionRequester의 TakeActionsBetweenDecisions가 켜져 있어서
    /// 결정(0.1초) 사이에도 물리 스텝마다 불린다 → 스텝 시간만큼 깎는다.
    /// </summary>
    public void OnAgentStep(ZombieLungeAgent a)
    {
        a.AddReward(-timePenalty * Time.fixedDeltaTime);
    }

    public float HealthFraction(ZombieLungeAgent a)
    {
        int i = agents.IndexOf(a);
        if (i < 0 || hp == null || zombieHp <= 0f) return 1f;
        return Mathf.Clamp01(hp[i] / zombieHp);
    }

    // ── 사건 ────────────────────────────────────
    private void OnShot(Transform t, float damage)
    {
        if (t == null || !indexOf.TryGetValue(t, out int i)) return;
        var a = agents[i];
        hitsTaken++;
        hp[i] -= damage;
        if (a != null) a.AddReward(-hitPenalty);
        zombies[i].Stun(hitStun);

        if (hp[i] <= 0f)
        {
            kills++;
            if (a != null)
            {
                a.AddReward(-killPenalty);
                a.EndEpisode();
            }
            Respawn(i);
        }
    }

    private void OnAttackLanded(ZombieController z, Transform target)
    {
        if (dummy == null || target != dummy.transform) return;
        if (!indexOf.TryGetValue(z.transform, out int i)) return;
        attacks++;
        // 돌진 시작 뒤 3초 안에 때렸으면 돌진 성공으로 친다
        if (lungeStartTime[i] >= 0f && Time.time - lungeStartTime[i] <= 3f)
        {
            lungeHits++;
            catHits[lungeCat[i]]++;
        }
        var a = agents[i];
        if (a != null)
        {
            a.AddReward(attackReward);
            a.EndEpisode();
        }
        Respawn(i);
    }

    private void OnLungeStarted(int i, ZombieQuirks q)
    {
        lunges++;
        lungeStartTime[i] = Time.time;
        if (availableSince[i] >= 0f) { waitSum += Time.time - availableSince[i]; waitCount++; }
        if (dummy == null) return;
        if (dummy.IsReloading) lungeReload++;
        if (dummy.AimTarget != zombies[i].transform) lungeUntargeted++;
        lungeCat[i] = dummy.IsReloading ? 0 : dummy.AimTarget != zombies[i].transform ? 1 : 2;
        catLunges[lungeCat[i]]++;
        Vector3 d = dummy.transform.position - zombies[i].transform.position; d.y = 0f;
        lungeDistSum += d.magnitude;
    }

    private void RecordStats()
    {
        if (!Academy.IsInitialized || baseSteps == 0) { ClearStats(); return; }
        var sr = Academy.Instance.StatsRecorder;
        float minutes = Mathf.Max(0.01f, timer / 60f) * Mathf.Max(1, zombies.Count);   // 좀비-분
        sr.Add("Lunge/PerZombieMinute",    lunges / minutes);
        sr.Add("Result/AttacksPerZombieMinute", attacks / minutes);
        sr.Add("Result/KillsPerZombieMinute",   kills / minutes);
        sr.Add("Result/HitsTakenPerZombieMinute", hitsTaken / minutes);
        sr.Add("Lunge/BaseReloading",   baseReload / (float)baseSteps);
        if (baseSeenSteps > 0) sr.Add("Lunge/BaseUntargeted", baseUntargeted / (float)baseSeenSteps);
        if (lunges > 0)
        {
            sr.Add("Lunge/SuccessRate",          lungeHits / (float)lunges);
            sr.Add("Lunge/StartWhileReloading",  lungeReload / (float)lunges);
            sr.Add("Lunge/StartWhileUntargeted", lungeUntargeted / (float)lunges);
            sr.Add("Lunge/StartDistance",        lungeDistSum / lunges);
            sr.Add("Lunge/FailRate",             lungeFails / (float)lunges);
        }
        // 조건별 돌진 성공률 —— 틈(재장전 / 다른 좀비를 겨눔)에 쓰면 실제로 더 잘 닿는가
        string[] cat = { "Reloading", "Untargeted", "Targeted" };
        for (int c = 0; c < 3; c++)
            if (catLunges[c] > 0) sr.Add($"LungeBy/Success_{cat[c]}", catHits[c] / (float)catLunges[c]);
        // 조건별 돌진 비율 (위 StartWhile*와 같은 정보를 한 번에)
        if (lunges > 0) sr.Add("LungeBy/Share_Targeted", catLunges[2] / (float)lunges);
        if (waitCount > 0) sr.Add("Lunge/WaitSeconds", waitSum / waitCount);
        ClearStats();
    }

    private void ClearStats()
    {
        lunges = lungeHits = lungeReload = lungeUntargeted = attacks = kills = hitsTaken = 0;
        lungeDistSum = 0f;
        baseSteps = baseReload = baseSeenSteps = baseUntargeted = 0;
        lungeFails = waitCount = 0;
        waitSum = 0f;
        for (int c = 0; c < 3; c++) catLunges[c] = catHits[c] = 0;
    }

    void OnDestroy()
    {
        if (dummy != null) dummy.ShotHit -= OnShot;
        foreach (var z in zombies) if (z != null) z.AttackLanded -= OnAttackLanded;
    }
}

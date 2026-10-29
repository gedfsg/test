using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>사냥꾼 무리 조율기의 조절값. 스포너 인스펙터에서 바꾸면 플레이 중에도 바로 반영된다.</summary>
[System.Serializable]
public class HunterTacticsSettings
{
    [Header("포위 자리")]
    [Tooltip("플레이어 둘레 몇 m에 자리를 잡을지.\n좁을수록 빨리 조이지만 플레이어가 틈으로 빠져나가기 쉽다.")]
    public float ringRadius = 7f;
    [Tooltip("자리까지 돌아갈 때 한 번에 도는 최대 각도(°).\n작을수록 플레이어를 크게 돌아서 간다 (가운데를 가로지르지 않음).")]
    public float maxOrbitStep = 50f;
    [Tooltip("플레이어가 최대 몇 초 뒤에 있을 곳을 기준으로 자리를 잡을지. 도망가는 앞쪽을 막는 데 쓴다.")]
    public float maxLeadTime = 1.2f;
    [Tooltip("자리 방향으로 이 거리(m) 안에 벽이 있으면 그쪽은 벽이 막아 준다고 보고 자리를 두지 않는다.\n" +
             "→ 열린 쪽만 좀비가 막으니, 결과적으로 막힌 곳으로 몰아넣는 모양이 된다.")]
    public float wallProbe = 4f;

    [Header("조이기")]
    [Tooltip("플레이어의 가장 큰 빈틈(좀비+벽)이 이 각도 이하가 되면 다 같이 덮친다.")]
    public float closeGapDeg = 120f;
    [Tooltip("좀비 한 마리가 막는다고 보는 각도 (좌우 각각, °).")]
    public float controlHalfAngle = 25f;
    [Tooltip("플레이어와 이 거리(m) 안이면 자리와 상관없이 바로 덮친다.")]
    public float commitDistance = 3f;
    [Tooltip("이 시간(초) 동안 포위가 안 끝나면 일단 덮친다 (빙빙 돌기만 하지 않게).")]
    public float maxEncircleTime = 8f;
    [Tooltip("시간 초과로 덮칠 때 몇 초 동안 덮칠지.")]
    public float forcedCloseTime = 3f;

    [Header("역할 (속도로 판정)")]
    [Tooltip("켜면 빠른 좀비는 앞쪽 자리, 탱커는 뒤쪽 자리, 일반 좀비는 옆 자리를 맡는다.\n" +
             "역할은 무리 안의 중간 속도와 비교해서 정한다 (전부 같은 속도면 전부 일반).")]
    public bool useRoles = true;
    [Tooltip("무리 중간 속도의 이 배 이상이면 '빠른 좀비' (앞을 막으러 멀리 돌아간다)")]
    public float runnerSpeedRatio = 1.1f;
    [Tooltip("무리 중간 속도의 이 배 이하면 '탱커' (뒤에서 밀고 들어와 플레이어를 앞쪽으로 몬다)")]
    public float tankSpeedRatio = 0.85f;
    [Tooltip("역할에 안 맞는 자리(빠른 좀비가 뒤, 탱커가 앞)를 맡으면 몇 초 손해로 칠지.\n클수록 역할을 더 엄격하게 지킨다.")]
    public float roleWeight = 3f;
    [Tooltip("켜면 탱커는 자리에서 기다리지 않고 뒤에서 곧장 밀고 들어간다 (몰이꾼).\n" +
             "플레이어는 탱커를 피해 앞으로 도망치게 되고, 그 앞을 빠른 좀비가 막는다.")]
    public bool tankPushes = true;

    [Header("정보 공유")]
    [Tooltip("무리 중 누군가 플레이어를 보거나(총소리 포함) 들은 뒤 몇 초까지 무리 전체가 위치를 공유할지.\n" +
             "이 시간이 지나면 각자 FSM으로 돌아가 수색/배회한다.")]
    public float packMemory = 4f;

    [Header("기타")]
    [Tooltip("자리 계산 간격(초)")]
    public float updateInterval = 0.2f;
    [Tooltip("Scene 뷰(또는 Gizmos를 켠 Game 뷰)에 선으로 표시:\n" +
             "하늘색 = 자리, 노랑 = 포위하러 가는 길, 주황 = 빠른 좀비, 보라 = 탱커 몰이, 빨강 = 덮치기")]
    public bool debugDraw = true;
    [Tooltip("화면 왼쪽 위에 무리 상태(포위 중/덮치는 중, 최대 빈틈)를 표시")]
    public bool showStatus = true;
}

/// <summary>
/// 사냥꾼 무리 조율기 (자리 배정 방식, 학습 없음).
///
/// 강화학습 무리가 포위를 못 배운 이유는 "다섯 마리가 똑같아서 누가 어디로 갈지 정할 방법이 없다"는 것이었다.
/// 여기서는 그 결정을 코드가 한다:
///   1. 플레이어가 곧 도착할 곳(이동 방향 × 앞서가기 시간)을 중심으로 잡는다.
///   2. 중심 둘레 24방향 중 벽에 막히지 않은 방향에만 자리를 고르게 나눈다 (첫 자리는 도망가는 방향 = 앞 막기).
///   3. 각 좀비에게 돌아가는 거리가 가장 짧은 자리를 하나씩 배정한다 (전체 합 최소).
///   4. 좀비는 플레이어를 가로지르지 않고 둘레를 돌아서 자리로 간다.
///   5. 플레이어의 가장 큰 빈틈(좀비+벽)이 closeGapDeg 이하가 되면 다 같이 덮친다.
/// 역할(useRoles): 무리 안에서 빠른 좀비는 앞쪽 자리(앞 막기), 탱커는 뒤쪽에서 곧장 밀고 들어오는 몰이꾼,
///   일반 좀비는 옆 자리를 맡는다. 배정 비용을 '도착 시간(거리 ÷ 속도) + 역할에 안 맞는 자리 벌점'으로 계산한다.
/// 움직임(길찾기, 공격, 피격, 수색)은 각 좀비의 FSM이 그대로 하고, ZombieFlankBrain이 이동 목표만 바꿔 끼운다.
///
/// 만드는 법: HunterPackTactics.Create(...)  (HunterPackSpawner / ZombieTestSpawner가 알아서 부른다)
/// </summary>
public class HunterPackTactics : MonoBehaviour
{
    public HunterTacticsSettings settings = new HunterTacticsSettings();

    public enum Phase { Searching, Encircling, Closing }
    public enum Role { Normal, Runner, Tank }
    public Phase CurrentPhase { get; private set; } = Phase.Searching;
    /// <summary>플레이어 둘레의 가장 큰 빈틈(°, 좀비+벽). 360이면 아무도 근처에 없음.</summary>
    public float LastGapDeg { get; private set; } = 360f;
    public int MemberCount => members.Count;

    public static readonly List<HunterPackTactics> Active = new List<HunterPackTactics>();

    // ─────────────────────────────────────────────
    private readonly List<ZombieController> members = new List<ZombieController>();
    private readonly List<ZombieController> alive   = new List<ZombieController>();
    private readonly Dictionary<ZombieController, Vector3> orders = new Dictionary<ZombieController, Vector3>();
    private readonly HashSet<ZombieController> rushing = new HashSet<ZombieController>();
    private readonly Dictionary<ZombieController, float> lastSlotAngle = new Dictionary<ZombieController, float>();
    private readonly Dictionary<ZombieController, Role>  roles = new Dictionary<ZombieController, Role>();
    private readonly HashSet<ZombieController> pushing = new HashSet<ZombieController>();
    private readonly List<float> speedBuffer = new List<float>();
    private int runnerCount, tankCount;

    /// <summary>이 좀비의 현재 역할 (무리에 없으면 Normal).</summary>
    public Role RoleOf(ZombieController z) => roles.TryGetValue(z, out Role r) ? r : Role.Normal;

    private Transform player;

    private bool    hasKnown;
    private Vector3 knownPos;
    private Vector3 knownVel;
    private float   lastSeenTime = -999f;
    private bool    hadTrack;
    private Vector3 trackPos;

    private float timer;
    private float lastTickTime;
    private float encircleTimer;
    private float forcedTimer;
    private bool  closing;

    private const int   Bins    = 24;
    private const float BinDeg  = 360f / Bins;
    private const int   GapBins = 72;
    private const float GapDeg  = 360f / GapBins;
    private readonly bool[] wallAtCenter = new bool[Bins];
    private readonly bool[] wallAtTarget = new bool[Bins];
    private readonly bool[] gapBlocked   = new bool[GapBins];

    private readonly List<int>   openBins  = new List<int>(Bins);
    private readonly List<float> slots     = new List<float>();
    private Vector3 lastCenter;

    // ── 소속 (센서/에이전트가 '내 무리'를 찾을 때) ─────────
    private static readonly Dictionary<ZombieController, HunterPackTactics> membership =
        new Dictionary<ZombieController, HunterPackTactics>();

    /// <summary>이 좀비가 속한 무리 조율기. 없으면 null.</summary>
    public static HunterPackTactics PackOf(ZombieController z)
    {
        if (z == null) return null;
        return membership.TryGetValue(z, out var t) && t != null ? t : null;
    }

    /// <summary>
    /// true면 Update에서 스스로 계산하지 않고, 바깥(학습장)이 TickNow()를 부를 때만 계산한다.
    /// 학습장은 에이전트 스텝에 맞춰 부른다 —— 프레임 속도와 상관없이 같은 간격이 되게.
    /// </summary>
    [System.NonSerialized] public bool externalTick;

    /// <summary>true면 좀비의 뇌를 바꾸지 않고 자리만 알려 준다 (강화학습 에이전트가 자리 센서로 읽는다).</summary>
    public bool AdvisorOnly { get; private set; }

    // ─────────────────────────────────────────────
    /// <summary>
    /// 무리 조율기를 만들고 좀비들을 편입시킨다. settings는 참조를 공유한다 (인스펙터 조절이 바로 반영).
    /// takeControl = true  : FSM 몸 + 조율기 명령으로 움직인다 (RL 부품이 있으면 끈다)
    /// takeControl = false : 뇌는 그대로 두고 자리만 계산한다 (조율기 + 강화학습)
    /// </summary>
    public static HunterPackTactics Create(string name, Transform parent, IEnumerable<ZombieController> zombies,
                                           Transform player, HunterTacticsSettings settings, bool takeControl = true)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var t = go.AddComponent<HunterPackTactics>();
        if (settings != null) t.settings = settings;
        t.player = player;
        t.AdvisorOnly = !takeControl;
        foreach (var z in zombies) t.Enlist(z);
        return t;
    }

    /// <summary>좀비를 무리에 넣는다. 조언 모드가 아니면 RL 뇌를 끄고 FSM 몸 + 조율기 명령으로 움직이게 한다.</summary>
    public void Enlist(ZombieController z)
    {
        if (z == null || members.Contains(z)) return;
        members.Add(z);
        membership[z] = this;

        if (AdvisorOnly) return;

        DisableLearnedBrain(z.gameObject);

        var b = z.GetComponent<ZombieFlankBrain>();
        if (b == null) b = z.gameObject.AddComponent<ZombieFlankBrain>();
        b.pack  = this;
        b.enabled = true;
        z.brain = b;
    }

    /// <summary>_RL 프리팹을 넣었을 때를 대비해 강화학습 부품을 끈다 (지우면 RequireComponent 때문에 오류가 난다).</summary>
    private static void DisableLearnedBrain(GameObject go)
    {
        var dr = go.GetComponent<Unity.MLAgents.DecisionRequester>();
        if (dr != null) dr.enabled = false;
        foreach (var a in go.GetComponents<Unity.MLAgents.Agent>()) a.enabled = false;
        var ml = go.GetComponent<ZombieMLBrain>();
        if (ml != null) ml.enabled = false;
    }

    // ── 정보 공유 ─────────────────────────────────────
    /// <summary>등장 즉시 위치를 알려줄 때.</summary>
    public void Alert(Vector3 position) => ReportSighting(position);

    /// <summary>무리 중 누군가 플레이어를 봤거나 총소리를 들었을 때.</summary>
    public void ReportSighting(Vector3 position)
    {
        hasKnown     = true;
        knownPos     = position;
        lastSeenTime = Time.time;
    }

    public bool Knows => hasKnown && Time.time - lastSeenTime <= settings.packMemory;

    /// <summary>ZombieFlankBrain이 매 프레임 묻는다. 명령이 없으면 FSM이 하던 대로 한다.</summary>
    public bool TryGetOrder(ZombieController z, out Vector3 point, out bool rush)
    {
        rush = false;
        if (!orders.TryGetValue(z, out point)) return false;
        rush = rushing.Contains(z);
        return true;
    }

    /// <summary>무리의 현재 명령 단계가 '덮치기'인가.</summary>
    public bool IsClosing => CurrentPhase == Phase.Closing;

    // ─────────────────────────────────────────────
    void OnEnable()  { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void OnDestroy()
    {
        foreach (var m in members)
            if (m != null && membership.TryGetValue(m, out var t) && t == this) membership.Remove(m);
    }

    void Update()
    {
        if (externalTick) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = Mathf.Max(0.02f, settings.updateInterval);
        TickNow();
    }

    /// <summary>지금 한 번 계산한다 (학습장이 에이전트 스텝에 맞춰 부른다).</summary>
    public void TickNow()
    {
        float dt = lastTickTime > 0f ? Time.time - lastTickTime : settings.updateInterval;
        lastTickTime = Time.time;
        Tick(Mathf.Max(dt, 0.01f));
    }

    /// <summary>에피소드가 새로 시작할 때 (학습장). 알고 있던 위치·자리·타이머를 모두 잊는다.</summary>
    public void ResetState()
    {
        hasKnown = false;
        hadTrack = false;
        knownVel = Vector3.zero;
        lastSeenTime  = -999f;
        lastTickTime  = 0f;
        encircleTimer = 0f;
        forcedTimer   = 0f;
        closing       = false;
        CurrentPhase  = Phase.Searching;
        LastGapDeg    = 360f;
        orders.Clear();
        rushing.Clear();
        pushing.Clear();
        lastSlotAngle.Clear();
    }

    private void Tick(float dt)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            var m = members[i];
            if (m != null && !m.IsDead) continue;
            if (m != null && membership.TryGetValue(m, out var t) && t == this) membership.Remove(m);
            members.RemoveAt(i);
        }
        orders.Clear();
        rushing.Clear();
        pushing.Clear();
        slots.Clear();

        if (members.Count == 0) { Destroy(gameObject); return; }

        // 이번 계산에 낄 좀비: 살아 있고 켜져 있는 것만. 학습장에서 총에 맞아 꺼진 좀비는
        // 무리에서 지우지 않는다 (다음 판에 되살아난다) —— 이번 계산에서만 뺀다.
        alive.Clear();
        foreach (var m in members) if (m.gameObject.activeInHierarchy) alive.Add(m);
        if (alive.Count == 0) { CurrentPhase = Phase.Searching; LastGapDeg = 360f; return; }

        AssignRoles();

        if (player == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) return;
            player = go.transform;
        }

        // 0. 누구든 지금 플레이어를 보고 있으면 무리 전체가 안다 (뇌 종류와 상관없이 같은 시야 판정)
        foreach (var m in alive)
            if (m.SeesTarget) { ReportSighting(player.position); break; }

        // 1. 무리가 아는 플레이어 위치 / 속도
        float now = Time.time;
        bool seenNow = hasKnown && now - lastSeenTime < 0.5f;
        if (seenNow)
        {
            Vector3 p = player.position;
            if (hadTrack)
            {
                Vector3 v = (p - trackPos) / dt;
                v.y = 0f;
                knownVel = Vector3.Lerp(knownVel, v, 0.5f);
            }
            trackPos = p;
            hadTrack = true;
            knownPos = p;
        }
        else hadTrack = false;

        if (!Knows)
        {
            // 아무도 모른다 → 각자 FSM (수색/배회)
            CurrentPhase  = Phase.Searching;
            closing       = false;
            encircleTimer = 0f;
            forcedTimer   = 0f;
            LastGapDeg    = 360f;
            return;
        }

        Vector3 target = OnMesh(knownPos);
        if (!seenNow) target = Toward(target, knownPos + knownVel * Mathf.Min(now - lastSeenTime, 1.5f));

        // 2. 앞서가기: 좀비들이 도착할 즈음 플레이어가 있을 곳
        int n = alive.Count;
        float distSum = 0f, speedSum = 0f;
        Vector3 centroid = Vector3.zero;
        foreach (var m in alive)
        {
            distSum  += FlatDistance(m.transform.position, target);
            speedSum += m.moveSpeed;
            centroid += m.transform.position;
        }
        centroid /= n;
        float lead = Mathf.Min(settings.maxLeadTime, (distSum / n) / Mathf.Max(0.5f, speedSum / n));
        Vector3 center = Toward(target, target + knownVel * lead);
        lastCenter = center;

        // 도망가는 방향: 움직이고 있으면 이동 방향, 서 있으면 무리 반대쪽
        float heading = knownVel.sqrMagnitude > 0.25f ? Yaw(knownVel) : Yaw(target - centroid);

        // 3. 벽 / 빈틈
        ProbeWalls(center, settings.wallProbe, wallAtCenter);
        ProbeWalls(target, 3f, wallAtTarget);
        LastGapDeg = ComputeGap(target, out int near);

        // 4. 포위 중인가, 덮칠 때인가
        if (n <= 1) closing = true;
        else if (forcedTimer > 0f) { forcedTimer -= dt; closing = true; }
        else if (closing)
        {
            // 덮치다가 빈틈이 크게 벌어지면 다시 포위로 (잦은 전환을 막는 여유 40°)
            if (LastGapDeg > settings.closeGapDeg + 40f || near < Mathf.Min(2, n)) closing = false;
        }
        else if (near >= Mathf.Min(3, n) && LastGapDeg <= settings.closeGapDeg) closing = true;

        if (closing) encircleTimer = 0f;
        else
        {
            encircleTimer += dt;
            if (encircleTimer >= settings.maxEncircleTime)
            {
                encircleTimer = 0f;
                forcedTimer   = settings.forcedCloseTime;
                closing       = true;
            }
        }

        // 5. 자리: 벽에 막히지 않은 방향에만, 도망가는 방향부터 고르게
        openBins.Clear();
        int headBin = Mathf.RoundToInt(Mathf.Repeat(heading, 360f) / BinDeg) % Bins;
        for (int k = 0; k < Bins; k++)
        {
            int b = (headBin + k) % Bins;
            if (!wallAtCenter[b]) openBins.Add(b);
        }
        if (openBins.Count == 0) closing = true;   // 사방이 벽 = 이미 갇힘

        CurrentPhase = closing ? Phase.Closing : Phase.Encircling;

        if (closing)
        {
            foreach (var m in alive) { orders[m] = target; rushing.Add(m); }
            DebugDraw(target);
            return;
        }

        // [수정 1] 탱커(tankPushes)가 포위 슬롯(slots) 하나를 차지한 뒤 실제로는 target으로 직진해버려서
        // 포위망 한쪽 각도가 텅 비던 문제를 해결: 실제 고리(Ring)에 배치될 좀비들만 슬롯을 나눠 갖도록 분리.
        var ringMembers = new List<ZombieController>(n);
        for (int i = 0; i < n; i++)
        {
            var m = alive[i];
            if (settings.useRoles && settings.tankPushes && RoleOf(m) == Role.Tank)
            {
                orders[m] = target;   // 탱커는 슬롯을 소모하지 않고 곧장 압박해 플레이어를 포위망 쪽으로 몬다
                pushing.Add(m);
            }
            else
            {
                ringMembers.Add(m);
            }
        }

        int rn = ringMembers.Count;
        if (rn == 0)
        {
            DebugDraw(target);
            return;
        }

        for (int i = 0; i < rn; i++)
            slots.Add(openBins[(int)((long)i * openBins.Count / rn)] * BinDeg);

        // 6. 배정: 각자 자리에 도착하는 시간(둘레를 돌아가는 거리 ÷ 속도) + 역할 벌점의 합이 최소가 되게
        var phi = new float[rn];
        var dist = new float[rn];
        var cost = new float[rn, rn];
        for (int i = 0; i < rn; i++)
        {
            Vector3 d = ringMembers[i].transform.position - center;
            phi[i]  = Yaw(d);
            dist[i] = new Vector2(d.x, d.z).magnitude;
            float speed = Mathf.Max(0.5f, ringMembers[i].moveSpeed);
            Role  role  = RoleOf(ringMembers[i]);
            bool hasPrev = lastSlotAngle.TryGetValue(ringMembers[i], out float prev);
            for (int s = 0; s < rn; s++)
            {
                float c = Mathf.Abs(Mathf.DeltaAngle(phi[i], slots[s])) * Mathf.Deg2Rad
                        * Mathf.Max(dist[i], settings.ringRadius) / speed;
                // 직전 자리를 조금 선호 → 자리가 매번 뒤바뀌며 우왕좌왕하지 않게
                if (hasPrev) c += 0.5f * Mathf.Abs(Mathf.DeltaAngle(prev, slots[s])) * Mathf.Deg2Rad * settings.ringRadius / speed;
                if (settings.useRoles) c += settings.roleWeight * RolePenalty(role, slots[s], heading);
                cost[i, s] = c;
            }
        }
        int[] assign = Assign(cost, rn);

        // 7. 각자의 이동 목표
        // [수정 2] 포위 시간이 길어질수록 고리 반경(R)을 서서히 좁혀서, 먼저 도착한 좀비가 7m 거리에서
        // 가만히 서서 더미의 총에 맞아 죽는 현상(Ring Stalling)을 방지한다.
        float tighten = Mathf.Clamp01(encircleTimer / Mathf.Max(1f, settings.maxEncircleTime));
        float R = Mathf.Lerp(settings.ringRadius, settings.commitDistance + 1.0f, tighten * 0.65f);

        for (int i = 0; i < rn; i++)
        {
            var m = ringMembers[i];
            float slot = slots[assign[i]];
            lastSlotAngle[m] = slot;

            if (FlatDistance(m.transform.position, target) <= settings.commitDistance)
            {
                orders[m] = target;   // 이미 코앞 → 바로 덮친다
                rushing.Add(m);
                continue;
            }

            float delta   = Mathf.DeltaAngle(phi[i], slot);
            float step    = Mathf.Clamp(delta, -settings.maxOrbitStep, settings.maxOrbitStep);
            bool  aligned = Mathf.Abs(delta) <= settings.maxOrbitStep;
            float d       = dist[i];

            float r;
            if (aligned)
            {
                // 자기 각도에 도착했으면 제자리에 멈추지 않고 R 안쪽으로 조금씩 포위망을 조여 들어간다
                r = d > R ? R : Mathf.Max(settings.commitDistance + 0.5f, d - 1.2f);
            }
            else
            {
                r = d > R * 1.3f ? Mathf.Max(R * 1.3f, d * 0.75f) : d; // 아직 멀다 → 고리 바깥으로 돌며 좁혀 감
            }
            r = Mathf.Max(r, settings.commitDistance + 0.5f);

            orders[m] = Toward(center, center + Dir(phi[i] + step) * r);
        }

        DebugDraw(target);
    }

    // ── 역할 ───────────────────────────────────────────
    /// <summary>무리 안의 중간 속도와 비교해서 빠른 좀비 / 일반 / 탱커를 정한다.</summary>
    private void AssignRoles()
    {
        roles.Clear();
        runnerCount = tankCount = 0;
        if (!settings.useRoles || alive.Count == 0) return;

        speedBuffer.Clear();
        foreach (var m in alive) speedBuffer.Add(m.moveSpeed);
        speedBuffer.Sort();
        int c = speedBuffer.Count;
        float median = c % 2 == 1 ? speedBuffer[c / 2] : 0.5f * (speedBuffer[c / 2 - 1] + speedBuffer[c / 2]);
        if (median <= 0.01f) return;

        foreach (var m in alive)
        {
            float ratio = m.moveSpeed / median;
            Role r = ratio >= settings.runnerSpeedRatio ? Role.Runner
                   : ratio <= settings.tankSpeedRatio   ? Role.Tank
                   :                                      Role.Normal;
            roles[m] = r;
            if (r == Role.Runner) runnerCount++;
            else if (r == Role.Tank) tankCount++;
        }
    }

    /// <summary>
    /// 역할에 안 맞는 자리일수록 큰 값 (0~1). front = 1이면 도망가는 방향 정면, -1이면 정반대.
    ///   빠른 좀비: 뒤쪽일수록 벌점 / 탱커: 앞쪽일수록 벌점 / 일반: 정면·정후방보다 옆을 조금 선호
    /// </summary>
    private static float RolePenalty(Role role, float slotAngle, float heading)
    {
        float front = Mathf.Cos(Mathf.DeltaAngle(slotAngle, heading) * Mathf.Deg2Rad);
        switch (role)
        {
            case Role.Runner: return (1f - front) * 0.5f;
            case Role.Tank:   return (1f + front) * 0.5f;
            default:          return Mathf.Abs(front) * 0.25f;
        }
    }

    // ── 배정 (작은 무리는 전부 따져 보고, 크면 욕심쟁이 방식) ──────
    private static int[] Assign(float[,] cost, int n)
    {
        var best = new int[n];
        if (n <= 7)
        {
            var perm = new int[n];
            var used = new bool[n];
            float bestCost = float.MaxValue;
            Permute(0, n, cost, perm, used, 0f, ref bestCost, best);
            return best;
        }

        var taken = new bool[n];
        var done  = new bool[n];
        for (int round = 0; round < n; round++)
        {
            int bi = -1, bs = -1;
            float bc = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (done[i]) continue;
                for (int s = 0; s < n; s++)
                    if (!taken[s] && cost[i, s] < bc) { bc = cost[i, s]; bi = i; bs = s; }
            }
            done[bi] = true; taken[bs] = true; best[bi] = bs;
        }
        return best;
    }

    private static void Permute(int i, int n, float[,] cost, int[] perm, bool[] used,
                                float acc, ref float bestCost, int[] best)
    {
        if (acc >= bestCost) return;
        if (i == n)
        {
            bestCost = acc;
            System.Array.Copy(perm, best, n);
            return;
        }
        for (int s = 0; s < n; s++)
        {
            if (used[s]) continue;
            used[s] = true;
            perm[i] = s;
            Permute(i + 1, n, cost, perm, used, acc + cost[i, s], ref bestCost, best);
            used[s] = false;
        }
    }

    // ── 벽 / 빈틈 ─────────────────────────────────────
    private static void ProbeWalls(Vector3 origin, float distance, bool[] result)
    {
        Vector3 o = OnMesh(origin);
        for (int i = 0; i < Bins; i++)
            result[i] = NavMesh.Raycast(o, o + Dir(i * BinDeg) * distance, out _, NavMesh.AllAreas);
    }

    /// <summary>플레이어 둘레(5° 단위)에서 좀비가 막는 각도와 3m 안의 벽을 빼고 남는 가장 큰 연속 빈틈(°).</summary>
    private float ComputeGap(Vector3 target, out int near)
    {
        for (int j = 0; j < GapBins; j++)
            gapBlocked[j] = wallAtTarget[Mathf.RoundToInt(j * GapDeg / BinDeg) % Bins];

        near = 0;
        float nearRadius = settings.ringRadius + 3f;
        foreach (var m in alive)
        {
            Vector3 d = m.transform.position - target;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > nearRadius) continue;
            near++;

            float a    = Yaw(d);
            float half = Mathf.Max(settings.controlHalfAngle, Mathf.Atan2(0.5f, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg);
            int from = Mathf.FloorToInt((a - half) / GapDeg);
            int to   = Mathf.CeilToInt((a + half) / GapDeg);
            for (int k = from; k <= to; k++)
                gapBlocked[((k % GapBins) + GapBins) % GapBins] = true;
        }

        int bestRun = 0, run = 0;
        bool any = false;
        for (int k = 0; k < GapBins * 2; k++)
        {
            if (!gapBlocked[k % GapBins]) { run++; if (run > bestRun) bestRun = run; }
            else { run = 0; any = true; }
        }
        if (!any) return 360f;
        return Mathf.Min(bestRun, GapBins) * GapDeg;
    }

    // ── 보조 ───────────────────────────────────────────
    private static Vector3 OnMesh(Vector3 p)
    {
        return NavMesh.SamplePosition(p, out NavMeshHit h, 2f, NavMesh.AllAreas) ? h.position : p;
    }

    /// <summary>from에서 to로 NavMesh 위를 직선으로 가다가 벽에 막히면 막힌 지점을 돌려준다.</summary>
    private static Vector3 Toward(Vector3 from, Vector3 to)
    {
        Vector3 a = OnMesh(from);
        if (NavMesh.Raycast(a, to, out NavMeshHit hit, NavMesh.AllAreas)) return hit.position;
        return OnMesh(to);
    }

    private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    private static Vector3 Dir(float yawDeg)
    {
        float r = yawDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ── 디버그 ─────────────────────────────────────────
    private void DebugDraw(Vector3 target)
    {
        if (!settings.debugDraw) return;
        float life = Mathf.Max(0.02f, settings.updateInterval);
        Vector3 up = Vector3.up * 0.3f;

        foreach (float s in slots)
            Debug.DrawLine(lastCenter + up, lastCenter + Dir(s) * settings.ringRadius + up, new Color(0.3f, 0.9f, 1f), life);

        foreach (var kv in orders)
        {
            if (kv.Key == null) continue;
            Color c = rushing.Contains(kv.Key)          ? Color.red
                    : pushing.Contains(kv.Key)          ? new Color(0.7f, 0.4f, 1f)
                    : RoleOf(kv.Key) == Role.Runner     ? new Color(1f, 0.6f, 0.2f)
                    :                                     Color.yellow;
            Debug.DrawLine(kv.Key.transform.position + up, kv.Value + up, c, life);
        }
    }

    void OnGUI()
    {
        if (!settings.showStatus) return;
        int i = Active.IndexOf(this);
        string phase = CurrentPhase == Phase.Encircling ? "포위 중"
                     : CurrentPhase == Phase.Closing    ? "덮치는 중"
                     :                                    "플레이어를 찾는 중";
        string gap = LastGapDeg >= 359f ? "-" : $"{LastGapDeg:0}°";
        string roleText = settings.useRoles
            ? $" · 빠른 {runnerCount} / 일반 {alive.Count - runnerCount - tankCount} / 탱커 {tankCount}"
            : "";
        GUI.Label(new Rect(12, 36 + i * 22, 900, 22),
            $"사냥꾼 무리 {i + 1}: {phase} · {alive.Count}마리{roleText} · 최대 빈틈 {gap} (덮치는 기준 {settings.closeGapDeg:0}°)");
    }
}
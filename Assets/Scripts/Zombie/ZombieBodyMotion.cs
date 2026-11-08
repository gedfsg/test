using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 좀비의 몸짓 (보이는 것만 다듬는다 —— 어디로 갈지, 언제 덤빌지는 건드리지 않는다).
/// ZombieQuirks가 붙인다. 학습장(좀비다움이 꺼진 곳, 돌진 학습장 포함)에서는 아무것도 안 한다.
///
///   1. 시선     : 플레이어가 보이면 머리·목·가슴을 플레이어 쪽으로 튼다 (몸은 길을 따라가도 고개는 나를 쫓는다).
///                 '왁!' 때는 홱 돌리고, 헐떡일 때는 고개를 떨군다.
///   2. 부드러운 회전 : NavMeshAgent의 일정한 속도 회전(로봇 같음) 대신, 진행 방향으로 부드럽게 감아 돈다.
///                 회전 속도 상한은 좀비다움의 '둔한 회전'(150~240°/초). 사냥꾼은 건드리지 않는다 (강화학습 좌표계 보호).
///   3. 몸 기울기 : 가속하면 앞으로, 감속하면 뒤로, 돌면 안쪽으로 상체가 살짝 기운다.
///   4. 군중     : 좀비마다 비켜 가는 우선순위를 다르게 → 한 줄로 정렬되지 않고 서로 비집고 온다.
///
/// 뼈대는 Humanoid면 Animator에서, 아니면 이름(Head / Neck / Spine…)으로 찾는다. 못 찾으면 그 부분만 건너뛴다.
/// 애니메이션이 매 프레임 자세를 덮어쓴 '뒤'(LateUpdate)에 살짝 더하는 방식이라 원래 동작은 그대로 살아 있다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Zombie/Zombie Body Motion (몸짓)")]
public class ZombieBodyMotion : MonoBehaviour
{
    [Header("시선")]
    public bool  lookAtPlayer = true;
    [Tooltip("고개를 좌우로 돌릴 수 있는 최대 각도(°)")]
    public float maxYaw   = 75f;
    [Tooltip("고개를 위아래로 돌릴 수 있는 최대 각도(°)")]
    public float maxPitch = 30f;
    [Tooltip("돌린 각도를 나누는 비율: 가슴 / 목 / 머리 (합 1)")]
    public Vector3 yawSplit = new Vector3(0.3f, 0.25f, 0.45f);
    [Tooltip("고개가 따라가는 부드러움(초). 작을수록 빠르게 돌린다")]
    public float lookSmooth = 0.18f;
    [Tooltip("헐떡일 때 고개를 떨구는 각도(°)")]
    public float pantDroop = 18f;

    [Header("부드러운 회전")]
    public bool  smoothTurning = true;
    [Tooltip("진행 방향으로 감아 도는 부드러움(초)")]
    public float turnSmooth = 0.22f;

    [Header("몸 기울기")]
    public bool  lean = true;
    [Tooltip("앞뒤로 기우는 최대 각도(°)")]
    public float maxLeanPitch = 9f;
    [Tooltip("돌 때 옆으로 기우는 최대 각도(°)")]
    public float maxLeanRoll  = 7f;

    [Header("디버그")]
    [Tooltip("켜면 처음 뼈대를 찾을 때 어떤 뼈를 썼는지 콘솔에 한 줄 남긴다")]
    public bool logBones = true;
    private static string N(Transform t) => t != null ? t.name : "없음";

    [Header("군중")]
    [Tooltip("비켜 가는 우선순위 범위 (NavMeshAgent.avoidancePriority, 작을수록 우선)")]
    public Vector2Int avoidancePriority = new Vector2Int(35, 65);

    // ── 내부 ────────────────────────────────────
    private ZombieController body;
    private ZombieQuirks     quirks;
    private NavMeshAgent     agent;
    private Animator         anim;
    private Transform head, neck, chest, spine;
    private bool  bonesSearched;
    private bool  rotationTaken;          // agent.updateRotation을 우리가 껐나
    private float yawVel;                 // 몸 회전 속도 (°/초, SmoothDampAngle)
    private float lookYaw, lookPitch, lookYawVel, lookPitchVel, lookWeight;
    private float lastSpeed, accelSmooth, leanPitch, leanRoll, lastYaw;
    // 애니메이터가 그 뼈를 이번 프레임에 다시 썼는지 확인용 (안 썼으면 우리 회전이 쌓이므로 건너뛴다)
    private readonly System.Collections.Generic.Dictionary<Transform, Quaternion> written =
        new System.Collections.Generic.Dictionary<Transform, Quaternion>();
    private bool  crowdSet;

    void Awake()
    {
        body   = GetComponent<ZombieController>();
        quirks = GetComponent<ZombieQuirks>();
        agent  = GetComponent<NavMeshAgent>();
        anim   = GetComponent<Animator>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
    }

    private bool Active => quirks != null && quirks.ActiveInGame && body != null && !body.IsDead;

    void OnDisable() => GiveBackRotation();

    private void GiveBackRotation()
    {
        if (rotationTaken && agent != null) agent.updateRotation = true;
        rotationTaken = false;
    }

    void LateUpdate()
    {
        if (!Active)
        {
            GiveBackRotation();
            return;
        }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool hunter = quirks.IsHunterNow;

        if (!crowdSet && agent != null)
        {
            crowdSet = true;
            agent.avoidancePriority = Random.Range(Mathf.Min(avoidancePriority.x, avoidancePriority.y),
                                                   Mathf.Max(avoidancePriority.x, avoidancePriority.y) + 1);
        }

        // ── 2. 부드러운 회전 ────────────────────────
        Vector3 vel = agent != null && agent.enabled ? agent.velocity : Vector3.zero;
        vel.y = 0f;
        float speed = vel.magnitude;
        if (smoothTurning && !hunter && agent != null && agent.enabled)
        {
            if (!rotationTaken) { agent.updateRotation = false; rotationTaken = true; }
            if (speed > 0.3f)
            {
                float cur    = transform.eulerAngles.y;
                float goal   = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
                float next   = Mathf.SmoothDampAngle(cur, goal, ref yawVel, turnSmooth, quirks.MyTurnSpeed, dt);
                transform.rotation = Quaternion.Euler(0f, next, 0f);
            }
            else yawVel = 0f;   // 서 있을 땐 컨트롤러가 플레이어 쪽으로 돌린다 (공격 · '왁!')
        }
        else GiveBackRotation();

        // 실제로 돈 속도 (°/초) —— 누가 돌렸든 (기울기용)
        float yawNow   = transform.eulerAngles.y;
        float turnRate = Mathf.DeltaAngle(lastYaw, yawNow) / dt;
        lastYaw = yawNow;

        if (!FindBones()) return;

        // ── 3. 몸 기울기 ───────────────────────────
        float accel = (speed - lastSpeed) / dt;
        lastSpeed   = speed;
        accelSmooth = Mathf.Lerp(accelSmooth, accel, 1f - Mathf.Exp(-6f * dt));
        float wantPitch = lean ? Mathf.Clamp(accelSmooth * 1.5f + speed * 0.8f, -maxLeanPitch * 0.6f, maxLeanPitch) : 0f;
        float wantRoll  = lean ? Mathf.Clamp(-turnRate * speed * 0.004f, -maxLeanRoll, maxLeanRoll) : 0f;
        leanPitch = Mathf.Lerp(leanPitch, wantPitch, 1f - Mathf.Exp(-8f * dt));
        leanRoll  = Mathf.Lerp(leanRoll,  wantRoll,  1f - Mathf.Exp(-8f * dt));
        Transform leanBone = spine != null ? spine : chest;
        if (leanBone != null && (Mathf.Abs(leanPitch) > 0.01f || Mathf.Abs(leanRoll) > 0.01f))
            Apply(leanBone, Quaternion.AngleAxis(leanPitch, transform.right)
                          * Quaternion.AngleAxis(leanRoll, transform.forward));

        // ── 1. 시선 ────────────────────────────────
        if (!lookAtPlayer) return;
        string label = quirks.Label;
        bool panting = label == "헐떡";
        bool roaring = label == "왁!";
        Transform target = body.CurrentTarget;
        bool sees = target != null && body.SeesTarget;

        float wantYaw = 0f, wantPitchLook = 0f, wantWeight = 0f;
        if (sees && !panting)
        {
            Vector3 eye = (head != null ? head.position : transform.position + Vector3.up * 1.6f);
            Vector3 to  = (target.position + Vector3.up * 1.4f) - eye;
            Vector3 local = transform.InverseTransformDirection(to);
            float flat = new Vector2(local.x, local.z).magnitude;
            wantYaw       = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
            wantPitchLook = Mathf.Clamp(-Mathf.Atan2(local.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
            // 정 뒤쪽이면 억지로 꺾지 않는다
            wantWeight = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) > 120f ? 0.3f : 1f;
        }
        if (panting) { wantPitchLook = pantDroop; wantWeight = 1f; }

        float smooth = roaring ? lookSmooth * 0.35f : lookSmooth;     // '왁!' 때는 홱
        lookYaw    = Mathf.SmoothDampAngle(lookYaw,   wantYaw,       ref lookYawVel,   smooth, 720f, dt);
        lookPitch  = Mathf.SmoothDampAngle(lookPitch, wantPitchLook, ref lookPitchVel, smooth, 360f, dt);
        lookWeight = Mathf.MoveTowards(lookWeight, wantWeight, dt * 3f);
        if (lookWeight < 0.001f) return;

        float sum = Mathf.Max(0.01f, yawSplit.x + yawSplit.y + yawSplit.z);
        Twist(chest, yawSplit.x / sum);
        Twist(neck,  yawSplit.y / sum);
        Twist(head,  yawSplit.z / sum);
    }

    private void Twist(Transform bone, float share)
    {
        if (bone == null || share <= 0f) return;
        float w = lookWeight * share;
        Apply(bone, Quaternion.AngleAxis(lookYaw * w, Vector3.up)
                  * Quaternion.AngleAxis(lookPitch * w, transform.right));
    }

    /// <summary>
    /// 뼈에 월드 기준 회전을 더한다. 단, 지난번에 우리가 써 둔 값 그대로라면(= 애니메이터가 이번 프레임에 안 덮어씀:
    /// 화면 밖 컬링 등) 더하지 않는다 —— 그대로 더하면 매 프레임 쌓여서 목이 돌아간다.
    /// 같은 뼈에 두 번(기울기 + 시선) 더할 수 있으므로 이번 프레임에 이미 손댄 뼈는 확인 없이 더한다.
    /// </summary>
    private int frameStamp = -1;
    private readonly System.Collections.Generic.HashSet<Transform> touched = new System.Collections.Generic.HashSet<Transform>();
    private void Apply(Transform bone, Quaternion worldDelta)
    {
        if (frameStamp != Time.frameCount) { frameStamp = Time.frameCount; touched.Clear(); }
        if (!touched.Contains(bone))
        {
            if (written.TryGetValue(bone, out var last) && Quaternion.Angle(last, bone.localRotation) < 0.001f)
                return;   // 애니메이터가 안 덮어씀 → 건너뜀
            touched.Add(bone);
        }
        bone.rotation = worldDelta * bone.rotation;
        written[bone] = bone.localRotation;
    }

    /// <summary>뼈대 찾기 (한 번). 애니메이터가 안 돌고 있으면 쓰지 않는다 (덮어써 주지 않으면 회전이 쌓인다).</summary>
    private bool FindBones()
    {
        if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null) return false;
        if (!bonesSearched)
        {
            bonesSearched = true;
            if (anim.isHuman)
            {
                head  = anim.GetBoneTransform(HumanBodyBones.Head);
                neck  = anim.GetBoneTransform(HumanBodyBones.Neck);
                chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
                spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            }
            // Generic 리그: 이름으로 (믹사모 mixamorig:Head / Spine2, 캐릭터 크리에이터 CC_Base_Head / Spine02 등)
            if (head  == null) head  = FindByName(anim.transform, "head");
            if (neck  == null) neck  = FindByName(anim.transform, "neck", "necktwist01", "neck1");
            if (chest == null) chest = FindByName(anim.transform, "spine2", "spine02", "upperchest", "chest");
            if (spine == null) spine = FindByName(anim.transform, "spine1", "spine01", "spine", "waist");
            if (chest == spine) chest = null;
            if (head == null && neck == null && chest == null && spine == null)
                Debug.Log($"[ZombieBodyMotion] {name}: 머리/척추 뼈대를 못 찾아 시선·기울기는 건너뜀 (회전·군중은 동작)");
            else if (logBones)
                Debug.Log($"[ZombieBodyMotion] {name}: 머리={N(head)} 목={N(neck)} 가슴={N(chest)} 척추={N(spine)} (humanoid={anim.isHuman})");
        }
        return head != null || neck != null || chest != null || spine != null;
    }

    /// <summary>이름 후보를 앞에서부터 차례로 찾는다 (앞 후보가 우선)</summary>
    private static Transform FindByName(Transform root, params string[] keys)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (string key in keys)
            foreach (var t in all)
            {
                string n = t.name.ToLowerInvariant();
                int colon = n.LastIndexOf(':');
                if (colon >= 0) n = n.Substring(colon + 1);   // mixamorig:Head → head
                if (n == key || n.EndsWith("_" + key) || n.EndsWith(" " + key)) return t;
            }
        return null;
    }
}

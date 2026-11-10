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
///   5. 피격 휘청 : 맞으면 상체가 총알(도끼) 방향으로 젖혀졌다가 스프링처럼 돌아온다. 옆구리를 맞으면 몸이 비틀리고,
///                 머리 근처를 맞으면 고개가 크게 꺾인다. (ZombieHitEffect · MeleeWeapon이 Flinch를 부른다)
///   6. 돌진 예고 : '노림'이면 몸을 웅크리고 고개를 숙인 채 노려보고, '왁!'에서 상체를 확 젖히며 포효한다.
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

    [Header("피격 휘청")]
    public bool  hitFlinch = true;
    [Tooltip("기준 피해(25)일 때 상체가 젖혀지는 각도(°). 약한 총은 작게(0.5배), 센 총은 크게(1.6배)")]
    public float flinchAngle = 16f;
    [Tooltip("연사로 쌓여도 이 각도(°) 이상은 안 젖혀진다")]
    public float flinchMax = 32f;
    [Tooltip("옆구리를 맞았을 때 몸이 비틀리는 각도(°)")]
    public float flinchTwist = 14f;
    [Tooltip("머리 근처(이 거리 m 안)를 맞으면 고개가 크게 꺾인다")]
    public float headHitRadius = 0.35f;
    [Tooltip("되돌아오는 빠르기 (스프링 강도). 클수록 빨리 돌아온다 (140 → 약 0.1초에 최대, 0.4초에 복귀)")]
    public float flinchStiffness = 140f;
    [Tooltip("출렁임 감쇠. 1이면 출렁임 없이 돌아오고, 작을수록 한두 번 출렁인다")]
    [Range(0.2f, 1.2f)] public float flinchDamping = 0.55f;
    [Tooltip("느린 좀비(탱커)는 이 배율만큼만 젖혀진다")]
    [Range(0f, 1f)] public float heavyFlinchScale = 0.5f;

    [Header("돌진 예고 자세")]
    public bool  telegraphPose = true;
    [Tooltip("'노림' 때 상체를 앞으로 웅크리는 각도(°)")]
    public float eyeCrouch = 12f;
    [Tooltip("'노림' 때 고개를 숙이는 각도(°) —— 숙인 채 치켜뜨고 노려보는 느낌")]
    public float eyeHeadDown = 10f;
    [Tooltip("'왁!' 때 상체를 뒤로 젖히는 각도(°)")]
    public float roarRear = 10f;
    [Tooltip("'왁!' 때 고개를 드는 각도(°)")]
    public float roarHeadUp = 16f;

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
    // 피격 휘청 (스프링): 상체 젖힘(월드 XZ 방향 × 각도) / 비틀림 / 머리 따로
    private Vector2 bendX, bendV, headX, headV;
    private float   twistX, twistV;
    // 돌진 예고 자세: +1 웅크림(노림) … -1 젖힘(왁!)
    private float   pose;

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

    /// <summary>맞은 좀비를 젖힌다 (몸짓이 없거나 학습장이면 아무 일 없음)</summary>
    public static void Flinch(GameObject zombie, Vector3 dir, Vector3 hitPoint, float scale = 1f)
    {
        if (zombie == null) return;
        var m = zombie.GetComponent<ZombieBodyMotion>();
        if (m != null) m.Flinch(dir, hitPoint, scale);
    }

    /// <summary>dir: 맞은 힘의 방향(총알 진행 방향) / hitPoint: 맞은 곳</summary>
    public void Flinch(Vector3 dir, Vector3 hitPoint, float scale = 1f)
    {
        if (!hitFlinch || !Active) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward;
        dir.Normalize();

        float dmg   = body.LastDamage > 0f ? Mathf.Clamp(body.LastDamage / 25f, 0.5f, 1.6f) : 1f;
        float heavy = body.moveSpeed < quirks.lungeMinBaseSpeed ? heavyFlinchScale : 1f;
        float deg   = flinchAngle * dmg * heavy * scale;
        // 스프링에 속도를 준다: 감쇠 0.55에서 최대 변위 ≈ 속도/ω × 0.52 → 원하는 각도가 나오게 역산
        float w     = Mathf.Sqrt(Mathf.Max(1f, flinchStiffness));
        float kick  = deg * w * PeakGain();
        bendV += new Vector2(dir.x, dir.z) * kick;

        // 옆구리: 맞은 곳이 몸 중심에서 옆으로 떨어져 있으면 그쪽 어깨가 밀려 몸이 비틀린다 (토크 = r × F)
        Vector3 r = hitPoint - transform.position; r.y = 0f;
        float torque = Mathf.Clamp(Vector3.Cross(r, dir).y / 0.25f, -1f, 1f);
        twistV += torque * flinchTwist * dmg * heavy * scale * w * PeakGain();

        // 머리 근처를 맞으면 고개가 크게, 아니면 살짝 늦게 따라 흔들린다
        FindBones();
        bool headShot = head != null && (hitPoint - head.position).sqrMagnitude <= headHitRadius * headHitRadius;
        headV += new Vector2(dir.x, dir.z) * kick * (headShot ? 1.6f : 0.35f);
    }

    private float PeakGain()
    {
        float z = Mathf.Clamp(flinchDamping, 0.2f, 1.2f);
        if (z >= 0.999f) return Mathf.Exp(1f);                      // 임계 감쇠: 최대 = v/ω · 1/e
        float s = Mathf.Sqrt(1f - z * z);
        return 1f / Mathf.Exp(-z / s * Mathf.Atan2(s, z));          // 저감쇠: 최대 = v/ω · e^(-ζ/√(1-ζ²)·atan(√(1-ζ²)/ζ))
    }

    private void StepSpring(ref Vector2 x, ref Vector2 v, float dt)
    {
        float k = Mathf.Max(1f, flinchStiffness), c = 2f * flinchDamping * Mathf.Sqrt(k);
        v += (-k * x - c * v) * dt;
        x += v * dt;
        if (x.magnitude > flinchMax) { x = x.normalized * flinchMax; v *= 0.5f; }
    }

    private void StepSpring(ref float x, ref float v, float dt)
    {
        float k = Mathf.Max(1f, flinchStiffness), c = 2f * flinchDamping * Mathf.Sqrt(k);
        v += (-k * x - c * v) * dt;
        x += v * dt;
        x = Mathf.Clamp(x, -flinchMax, flinchMax);
    }

    void LateUpdate()
    {
        if (!Active)
        {
            GiveBackRotation();
            bendX = bendV = headX = headV = Vector2.zero;
            twistX = twistV = pose = 0f;
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

        // 피격 스프링은 뼈가 없어도 돌린다 (작은 시간 간격으로 나눠서 —— 프레임이 끊겨도 튀지 않게)
        {
            float left = Mathf.Min(dt, 0.1f);
            while (left > 1e-5f)
            {
                float h = Mathf.Min(left, 1f / 120f);
                StepSpring(ref bendX, ref bendV, h);
                StepSpring(ref headX, ref headV, h);
                StepSpring(ref twistX, ref twistV, h);
                left -= h;
            }
        }

        if (!FindBones()) return;

        // ── 6. 돌진 예고 자세 ──────────────────────
        string poseLabel = quirks.Label;
        float wantPose = !telegraphPose ? 0f : poseLabel == "노림" ? 1f : poseLabel == "왁!" ? -1f : 0f;
        float poseRate = wantPose < 0f ? 12f : wantPose > 0f ? 4f : 6f;   // 젖힐 땐 확, 웅크릴 땐 스르륵
        pose = Mathf.MoveTowards(pose, wantPose, poseRate * dt);
        if (Mathf.Abs(pose) > 0.001f)
        {
            float bodyDeg = pose > 0f ? eyeCrouch * pose : roarRear * pose;      // +앞으로 / -뒤로
            float headDeg = pose > 0f ? eyeHeadDown * pose : roarHeadUp * pose;
            Transform torso = spine != null ? spine : chest;
            if (torso != null) Apply(torso, Quaternion.AngleAxis(bodyDeg, transform.right));
            Transform h = head != null ? head : neck;
            if (h != null) Apply(h, Quaternion.AngleAxis(headDeg, transform.right));
        }

        // ── 5. 피격 휘청 ───────────────────────────
        ApplyBend(spine, bendX, 0.3f);
        ApplyBend(chest, bendX, 0.4f);
        ApplyBend(neck,  bendX, 0.1f);
        ApplyBend(head,  bendX, 0.2f);
        ApplyBend(head,  headX, 1f);
        if (Mathf.Abs(twistX) > 0.01f)
        {
            if (spine != null) Apply(spine, Quaternion.AngleAxis(twistX * 0.4f, Vector3.up));
            if (chest != null) Apply(chest, Quaternion.AngleAxis(twistX * 0.6f, Vector3.up));
        }

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

    /// <summary>뼈를 v(월드 XZ 방향 × 각도) 쪽으로 share만큼 기울인다 —— 위쪽이 그 방향으로 넘어간다</summary>
    private void ApplyBend(Transform bone, Vector2 v, float share)
    {
        if (bone == null) return;
        float deg = v.magnitude * share;
        if (deg < 0.01f) return;
        Vector3 d = new Vector3(v.x, 0f, v.y) / v.magnitude;
        Apply(bone, Quaternion.AngleAxis(deg, Vector3.Cross(Vector3.up, d)));
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

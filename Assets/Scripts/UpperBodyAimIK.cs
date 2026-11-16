using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// 상체 조준 IK (2026-11-16) - 척추·가슴·머리가 마우스(조준 방향)를 봄.
/// PlayerController.Awake가 자동으로 붙임. 씬 파일은 안 건드리고 런타임에 Rig를 만듦 (WeaponHandAttacher 오른손 IK와 같은 방식).
///
///  - Animation Rigging Multi-Aim Constraint 3개: Spine 0.3 / Chest 0.5 / Head 0.9 (가중치)
///    각도 제한: 머리 ±80°, 가슴·척추 ±60°. 수평 회전(yaw)만 (고개 숙임/들기는 애니메이션 그대로)
///  - 별도 Rig 레이어("UpperBodyAimRig")를 RigBuilder 맨 앞에 넣음:
///    · Rig는 Animator의 모든 레이어(이동·공격·재장전 Upper Body) 평가가 끝난 뒤 적용 → 애니메이션이 IK를 덮어쓰지 않음
///    · 왼손/오른손 IK Rig(재장전·아이템 사용 때 꺼짐)와 따로라서 재장전 중에도 상체는 계속 조준
///    · 상체를 먼저 돌리고 → 그 다음 손 IK가 돌아간 상체 기준으로 총을 잡음
///  - 조준점은 0.15초 Lerp로 부드럽게 따라감 (마우스를 휙 돌려도 목이 꺾이지 않게)
///  - 달리기: 가중치 70% (팔·어깨 흔들림 유지), 구르기·사망: 끔, 도끼 휘두르는 중: 끔 (내려찍기 몸통 비틀기가 공격 동작 자체라서)
///  - 뒤로 90° 넘게 꺾이는 경우: 루트(하체)가 PlayerController.AimAtMouse로 항상 마우스 쪽으로 돌기 때문에
///    상체가 각도 제한(±60/80°)에 걸리는 동안 하체가 따라 돌아서 자연스럽게 풀림
/// </summary>
public class UpperBodyAimIK : MonoBehaviour
{
    [Header("가중치 (Multi-Aim Constraint)")]
    [Range(0f, 1f)] public float spineWeight = 0.3f;
    [Range(0f, 1f)] public float chestWeight = 0.5f;
    [Range(0f, 1f)] public float headWeight = 0.9f;

    [Header("각도 제한 (±도)")]
    public float spineLimit = 60f;
    public float chestLimit = 60f;
    public float headLimit = 80f;

    [Header("부드럽게")]
    [Tooltip("조준 방향을 따라가는 시간 상수(초)")]
    public float smoothTime = 0.15f;
    [Tooltip("달릴 때 전체 가중치 (팔 흔들림 유지)")]
    [Range(0f, 1f)] public float sprintWeight = 0.7f;

    PlayerController player;
    Locomotion locomotion;
    Rigidbody rb;
    Animator animator;
    RigBuilder builder;
    Rig aimRig;
    Transform aimProxy;     // 실제 제약이 바라보는 점 (부드럽게 움직임)
    Vector3 smoothDir;      // 부드럽게 따라가는 수평 조준 방향
    bool built, failed;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        locomotion = GetComponent<Locomotion>();
        rb = GetComponent<Rigidbody>();
    }

    // 첫 LateUpdate(애니메이션이 한 번 평가된 뒤)에 만듦 → 실제 자세에서 뼈 축을 측정할 수 있음
    void LateUpdate()
    {
        if (failed) return;
        if (!built) { Build(); return; }
        UpdateTargetAndWeight();
    }

    void Build()
    {
        animator = GetComponentInChildren<Animator>();
        builder = GetComponentInChildren<RigBuilder>();
        if (animator == null || !animator.isHuman || builder == null) { failed = true; return; }

        var rigGo = new GameObject("UpperBodyAimRig");
        rigGo.transform.SetParent(builder.transform, false);
        aimRig = rigGo.AddComponent<Rig>();
        aimProxy = new GameObject("UpperBodyAimTarget").transform;
        aimProxy.SetParent(rigGo.transform, false);
        smoothDir = Flat(transform.forward);
        aimProxy.position = transform.position + Vector3.up * 1.5f + smoothDir * 10f;

        int made = 0;
        made += AddAim(rigGo.transform, HumanBodyBones.Spine, spineWeight, spineLimit) ? 1 : 0;
        made += AddAim(rigGo.transform, HumanBodyBones.Chest, chestWeight, chestLimit) ? 1 : 0;
        made += AddAim(rigGo.transform, HumanBodyBones.Head, headWeight, headLimit, yawOnly: false) ? 1 : 0;
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (made == 0) { Destroy(rigGo); failed = true; return; }

        // 기존 손 IK Rig보다 먼저 평가되도록 맨 앞에
        builder.layers.Insert(0, new RigLayer(aimRig, true));
        builder.Build();
        built = true;
    }

    Transform headBone;
    Vector3 headAimLocal; // 머리 조준 축 (로컬)

    static Vector3 AxisVector(MultiAimConstraintData.Axis a) => a switch
    {
        MultiAimConstraintData.Axis.X => Vector3.right, MultiAimConstraintData.Axis.X_NEG => Vector3.left,
        MultiAimConstraintData.Axis.Y => Vector3.up, MultiAimConstraintData.Axis.Y_NEG => Vector3.down,
        MultiAimConstraintData.Axis.Z => Vector3.forward, _ => Vector3.back,
    };

    // yawOnly: 척추·가슴은 위 축 하나만 회전(수평). 머리는 뼈가 앞으로 약 40° 숙여져 있어서 로컬 축 하나로만 돌리면
    // 수평 각도가 과장됨 (Play 실측: 목표 60° → 머리 85°) → 머리는 세 축 모두 + 조준점을 머리 높이에 둬서 수평으로 바라보게
    bool AddAim(Transform parent, HumanBodyBones hb, float weight, float limit, bool yawOnly = true)
    {
        Transform bone = animator.GetBoneTransform(hb);
        if (bone == null) return false;

        var go = new GameObject("Aim_" + hb);
        go.transform.SetParent(parent, false);
        var c = go.AddComponent<MultiAimConstraint>();
        c.data.constrainedObject = bone;
        var sources = new WeightedTransformArray(0);
        sources.Add(new WeightedTransform(aimProxy, 1f));
        c.data.sourceObjects = sources;
        // 뼈의 로컬 축 중 지금 캐릭터 정면과 가장 가까운 축 = 조준 축, 월드 위와 가장 가까운 축 = 비트는 축(yaw)
        Vector3 fwdLocal = bone.InverseTransformDirection(transform.forward);
        Vector3 upLocal = bone.InverseTransformDirection(Vector3.up);
        c.data.aimAxis = ToAxis(fwdLocal);
        c.data.upAxis = ToAxis(upLocal);
        // 수평 회전만: 위 축 하나만 회전 허용
        int upIdx = MaxIndex(upLocal);
        c.data.constrainedXAxis = !yawOnly || upIdx == 0;
        c.data.constrainedYAxis = !yawOnly || upIdx == 1;
        c.data.constrainedZAxis = !yawOnly || upIdx == 2;
        c.data.limits = new Vector2(-limit, limit);
        c.data.maintainOffset = false;
        c.data.worldUpType = MultiAimConstraintData.WorldUpType.None;
        c.weight = weight;
        if (hb == HumanBodyBones.Head) headAimLocal = AxisVector(c.data.aimAxis);
        return true;
    }

    static int MaxIndex(Vector3 v)
    {
        float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
        return ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
    }

    static MultiAimConstraintData.Axis ToAxis(Vector3 v)
    {
        switch (MaxIndex(v))
        {
            case 0: return v.x >= 0f ? MultiAimConstraintData.Axis.X : MultiAimConstraintData.Axis.X_NEG;
            case 1: return v.y >= 0f ? MultiAimConstraintData.Axis.Y : MultiAimConstraintData.Axis.Y_NEG;
            default: return v.z >= 0f ? MultiAimConstraintData.Axis.Z : MultiAimConstraintData.Axis.Z_NEG;
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    void UpdateTargetAndWeight()
    {
        // 조준 방향 = 플레이어 → 마우스 지점 (PlayerController.aimTarget). 너무 가까우면(발밑) 몸 정면
        Vector3 desired = transform.forward;
        if (player != null && player.aimTarget != null)
        {
            Vector3 to = player.aimTarget.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.5f * 0.5f) desired = to;
        }
        desired = Flat(desired);
        float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, smoothTime));
        smoothDir = Flat(Vector3.Slerp(smoothDir, desired, k));
        // 높이: 머리 조준 축의 지금 기울기(숙임)를 그대로 유지하는 높이 → 머리는 좌우만 돌고 숙임은 애니메이션 그대로.
        // (지금 자세 = IK 적용 후라도 가중치 0.9 혼합이라 매 프레임 애니메이션 기울기로 수렴함)
        // 척추·가슴은 수평 회전만이라 높이 영향 없음
        Vector3 basePos = transform.position + Vector3.up * 1.5f;
        float pitchY = 0f;
        if (headBone != null && headAimLocal != Vector3.zero)
        {
            Vector3 ax = headBone.TransformDirection(headAimLocal);
            float flat = new Vector2(ax.x, ax.z).magnitude;
            if (flat > 0.2f) pitchY = Mathf.Clamp(ax.y / flat, -1.5f, 1.5f) * 10f; // 기울기 × 10m
            basePos = headBone.position;
        }
        aimProxy.position = new Vector3(transform.position.x, basePos.y + pitchY, transform.position.z) + smoothDir * 10f;

        // 가중치: 구르기·사망·도끼 휘두르기 = 0, 달리기 = 0.7, 그 외 1
        bool off = player != null && (player.IsRolling || player.IsDead || player.IsMeleeSwinging);
        float planar = rb != null ? new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude : 0f;
        bool sprinting = locomotion != null && locomotion.IsSprinting && planar > 2f;
        float target = off ? 0f : sprinting ? sprintWeight : 1f;
        float speed = off ? 20f : 4f; // 끌 땐 즉시(구르기 시작), 켤 땐 부드럽게
        aimRig.weight = Mathf.MoveTowards(aimRig.weight, target, Time.deltaTime * speed);
    }
}

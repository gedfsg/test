using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 죽을 때 래그돌 —— 사망 애니메이션 대신 물리로 쓰러진다.
///
/// 프리팹을 손댈 필요 없다: 죽는 순간 휴머노이드 뼈(골반·가슴·머리·팔다리 10개)에
/// 리지드바디·콜라이더·관절을 코드로 붙이고 애니메이터를 끈다 (살아 있는 동안은 비용 0).
///   · 달리던 속도를 그대로 이어받아 앞으로 고꾸라진다
///   · 마지막 한 방(총알·도끼)의 방향과 세기로 튕겨 나간다 —— 맞은 부위(가장 가까운 뼈)에 힘을 준다
///   · settleTime초 뒤 굳히고(물리 끔) 땅으로 가라앉아 사라진다
/// 휴머노이드가 아니거나 뼈를 못 찾으면 false → 예전 사망 애니메이션으로 대신한다.
/// </summary>
[DisallowMultipleComponent]
public class ZombieRagdoll : MonoBehaviour
{
    [Tooltip("몸 전체 질량(kg). 부위별로 나눠 준다")]
    public float totalMass = 50f;
    [Tooltip("기준 피해(25)일 때 마지막 한 방이 주는 충격량(N·s). 샷건·도끼처럼 센 한 방은 최대 1.6배")]
    public float hitImpulse = 70f;
    [Tooltip("죽는 순간의 이동 속도를 몸에 얼마나 이어받나 (1 = 그대로 → 달리다 죽으면 고꾸라짐)")]
    [Range(0f, 1.5f)] public float carryVelocity = 1f;
    [Tooltip("쓰러진 뒤 이 시간(초)이 지나면 물리를 끄고 굳힌다")]
    public float settleTime = 4f;

    public bool IsActive { get; private set; }

    private static readonly float[] MassShare = { 0.20f, 0.20f, 0.10f, 0.10f, 0.06f, 0.10f, 0.06f, 0.04f, 0.03f, 0.04f, 0.03f };
    private readonly List<Rigidbody> bodies = new List<Rigidbody>();
    private Rigidbody hipsBody;

    /// <summary>죽은 좀비를 래그돌로 바꾼다. velocity: 죽는 순간의 이동 속도</summary>
    public static bool TryActivate(GameObject zombie, Vector3 velocity)
    {
        if (zombie == null) return false;
        var anim = zombie.GetComponent<Animator>();
        if (anim == null) anim = zombie.GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman || anim.avatar == null) return false;

        var r = zombie.GetComponent<ZombieRagdoll>();
        if (r == null) r = zombie.AddComponent<ZombieRagdoll>();
        if (r.IsActive) return true;
        return r.Build(anim, velocity);
    }

    /// <summary>쓰러진 몸에 맞은 힘을 준다 (ZombieBodyMotion.Flinch가 죽은 좀비면 이리로 넘긴다)</summary>
    public void Hit(Vector3 dir, Vector3 hitPoint, float damage, float scale = 1f)
    {
        if (!IsActive || bodies.Count == 0) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward;
        dir.Normalize();

        float dmg = damage > 0f ? Mathf.Clamp(damage / 25f, 0.5f, 1.6f) : 1f;
        Vector3 force = (dir + Vector3.up * 0.25f).normalized * (hitImpulse * dmg * scale);

        Rigidbody best = null;
        float bestD = float.MaxValue;
        foreach (var b in bodies)
        {
            if (b == null) continue;
            float d = (b.worldCenterOfMass - hitPoint).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        if (best == null) return;
        best.AddForceAtPosition(force * 0.7f, hitPoint, ForceMode.Impulse);
        if (hipsBody != null && hipsBody != best) hipsBody.AddForce(force * 0.3f, ForceMode.Impulse);
    }

    // ─────────────────────────────────────────────
    private bool Build(Animator anim, Vector3 velocity)
    {
        Transform hips  = anim.GetBoneTransform(HumanBodyBones.Hips);
        Transform chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Spine);
        Transform head  = anim.GetBoneTransform(HumanBodyBones.Head);
        Transform lUL = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg),  lLL = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rUL = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg), rLL = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform lUA = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm),  lLA = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rUA = anim.GetBoneTransform(HumanBodyBones.RightUpperArm), rLA = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform lF = anim.GetBoneTransform(HumanBodyBones.LeftFoot),  rF = anim.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform lH = anim.GetBoneTransform(HumanBodyBones.LeftHand),  rH = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck);

        Transform[] bones = { hips, chest, head, lUL, lLL, rUL, rLL, lUA, lLA, rUA, rLA };
        foreach (var b in bones) if (b == null) return false;
        if (lF == null || rF == null || lH == null || rH == null) return false;

        float hipWidth   = Vector3.Distance(lUL.position, rUL.position);
        float shoulderW  = Vector3.Distance(lUA.position, rUA.position);
        float headRadius = Mathf.Max(0.08f, Vector3.Distance(head.position, neck != null ? neck.position : chest.position) * 0.9f);
        if (hipWidth < 1e-3f || shoulderW < 1e-3f) return false;

        anim.enabled = false;   // 이제 뼈는 물리가 움직인다

        // 몸통
        AddCapsule(hips,  hips.position, chest.position, hipWidth * 0.55f);
        AddCapsule(chest, chest.position, neck != null ? neck.position : head.position, shoulderW * 0.32f);
        AddSphere(head, head.position + Vector3.up * headRadius * 0.6f, headRadius);
        // 다리 · 팔
        AddCapsule(lUL, lUL.position, lLL.position, hipWidth * 0.32f);
        AddCapsule(lLL, lLL.position, lF.position,  hipWidth * 0.26f);
        AddCapsule(rUL, rUL.position, rLL.position, hipWidth * 0.32f);
        AddCapsule(rLL, rLL.position, rF.position,  hipWidth * 0.26f);
        AddCapsule(lUA, lUA.position, lLA.position, shoulderW * 0.11f);
        AddCapsule(lLA, lLA.position, lH.position,  shoulderW * 0.09f);
        AddCapsule(rUA, rUA.position, rLA.position, shoulderW * 0.11f);
        AddCapsule(rLA, rLA.position, rH.position,  shoulderW * 0.09f);

        // 리지드바디 (질량 나눔) → 관절 (부모 뼈 리지드바디에 연결)
        float shareSum = 0f;
        foreach (var s in MassShare) shareSum += s;
        for (int i = 0; i < bones.Length; i++)
        {
            var rb = bones[i].gameObject.AddComponent<Rigidbody>();
            rb.mass = totalMass * MassShare[i] / shareSum;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12;
            bodies.Add(rb);
        }
        hipsBody = bodies[0];

        Vector3 right = transform.right, fwd = transform.forward;
        AddJoint(chest, hips,  right, fwd, -20f, 25f, 15f, 15f);
        AddJoint(head,  chest, right, fwd, -35f, 35f, 25f, 25f);
        AddJoint(lUL,   hips,  right, fwd, -70f, 20f, 30f, 10f);
        AddJoint(rUL,   hips,  right, fwd, -70f, 20f, 30f, 10f);
        AddJoint(lLL,   lUL,   right, fwd,   0f, 120f, 5f, 5f);    // 무릎: 한쪽으로만 접힌다
        AddJoint(rLL,   rUL,   right, fwd,   0f, 120f, 5f, 5f);
        AddJoint(lUA,   chest, fwd,  right, -70f, 70f, 60f, 40f);
        AddJoint(rUA,   chest, fwd,  right, -70f, 70f, 60f, 40f);
        AddJoint(lLA,   lUA,   right, fwd, -120f, 0f, 10f, 10f);   // 팔꿈치
        AddJoint(rLA,   rUA,   right, fwd, -120f, 0f, 10f, 10f);

        // 플레이어와는 부딪히지 않게 (시체에 걸려 멈추지 않도록)
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var mine = new List<Collider>();
            foreach (var b in bones) mine.AddRange(b.GetComponents<Collider>());
            foreach (var pc in player.GetComponentsInChildren<Collider>())
                foreach (var c in mine) Physics.IgnoreCollision(pc, c, true);
        }

        Vector3 v = velocity * carryVelocity;
        foreach (var rb in bodies) rb.linearVelocity = v;

        IsActive = true;
        return true;
    }

    /// <summary>a→b 방향의 캡슐 (뼈의 로컬 좌표로 변환 —— 뼈 크기 배율이 0.01이어도 맞게)</summary>
    private static void AddCapsule(Transform bone, Vector3 a, Vector3 b, float worldRadius)
    {
        Vector3 la = bone.InverseTransformPoint(a), lb = bone.InverseTransformPoint(b);
        Vector3 d  = lb - la;
        float scale = Mathf.Max(1e-5f, bone.lossyScale.x);
        var c = bone.gameObject.AddComponent<CapsuleCollider>();
        Vector3 ad = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
        c.direction = ad.x >= ad.y && ad.x >= ad.z ? 0 : ad.y >= ad.z ? 1 : 2;
        c.center = (la + lb) * 0.5f;
        float r = worldRadius / scale;
        c.radius = r;
        c.height = Mathf.Max(d.magnitude + r, r * 2f);
    }

    private static void AddSphere(Transform bone, Vector3 worldCenter, float worldRadius)
    {
        var c = bone.gameObject.AddComponent<SphereCollider>();
        c.center = bone.InverseTransformPoint(worldCenter);
        c.radius = worldRadius / Mathf.Max(1e-5f, bone.lossyScale.x);
    }

    private static void AddJoint(Transform bone, Transform parent, Vector3 worldAxis, Vector3 worldSwing,
                                 float lowTwist, float highTwist, float swing1, float swing2)
    {
        var j = bone.gameObject.AddComponent<CharacterJoint>();
        j.connectedBody = parent.GetComponent<Rigidbody>();
        j.axis      = bone.InverseTransformDirection(worldAxis);
        j.swingAxis = bone.InverseTransformDirection(worldSwing);
        j.lowTwistLimit  = new SoftJointLimit { limit = lowTwist };
        j.highTwistLimit = new SoftJointLimit { limit = highTwist };
        j.swing1Limit    = new SoftJointLimit { limit = swing1 };
        j.swing2Limit    = new SoftJointLimit { limit = swing2 };
        j.enableProjection = true;
        j.enablePreprocessing = false;
    }

    /// <summary>쓰러진 뒤 굳히기 (물리 끔) —— 이후 가라앉히기는 ZombieController가 루트를 내린다</summary>
    public void Freeze()
    {
        foreach (var rb in bodies)
        {
            if (rb == null) continue;
            rb.isKinematic = true;
        }
        foreach (var b in bodies)
        {
            if (b == null) continue;
            foreach (var c in b.GetComponents<Collider>()) c.enabled = false;
        }
    }
}

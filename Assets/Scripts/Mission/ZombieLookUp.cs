using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 탈출 헬기 컷신 마지막: 좀비가 멈춰 서서 날아가는 헬기를 올려다봄 (HelicopterCutscene이 붙임).
///  - AI(ZombieController·뇌)와 이동을 멈추고, 애니메이터 이동 값은 0 → 제자리에 섬
///  - 몸은 헬기 쪽으로 천천히 돌고, 애니메이션이 끝난 뒤(LateUpdate) 목·머리를 위로 젖힘 (휴머노이드만)
/// </summary>
public class ZombieLookUp : MonoBehaviour
{
    public Transform target;
    public float maxHeadPitch = 55f;
    public float turnSpeed = 120f;

    Animator anim;
    Transform head, neck;
    float pitch;

    void Start()
    {
        var zc = GetComponent<ZombieController>();
        if (zc != null) zc.enabled = false;
        foreach (var b in GetComponents<ZombieBrain>()) b.enabled = false;
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }

        anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            foreach (var p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float) anim.SetFloat(p.nameHash, 0f); // 걷기·달리기 값 0 → 제자리
                else if (p.type == AnimatorControllerParameterType.Bool && (p.name == "IsWalking" || p.name == "IsRunning")) anim.SetBool(p.nameHash, false);
            }
            if (anim.isHuman)
            {
                head = anim.GetBoneTransform(HumanBodyBones.Head);
                neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            }
        }
    }

    void Update()
    {
        if (target == null) return;
        Vector3 d = target.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), turnSpeed * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (target == null || head == null) return;
        // 헬기를 올려다보는 각도 (수평 기준 위쪽), 천천히 따라감
        Vector3 to = target.position - head.position;
        float want = Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0f, maxHeadPitch);
        pitch = Mathf.MoveTowards(pitch, want, 60f * Time.deltaTime);
        // 몸 오른쪽 축으로 뒤로 젖힘 (뼈 방향 규칙과 상관없이 동작): 목 40% + 머리 60%
        Quaternion tilt(float a) => Quaternion.AngleAxis(-a, transform.right);
        if (neck != null) neck.rotation = tilt(pitch * 0.4f) * neck.rotation;
        head.rotation = tilt(pitch * (neck != null ? 0.6f : 1f)) * head.rotation;
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 탈출 헬기 컷신 중 좀비 떼가 헬리패드로 뛰어 몰려듦 (HelicopterCutscene이 붙임).
/// 평소 AI는 "소리 난 곳 확인"이라 천천히 걸어와서 컷신 몇 초 안에 안 모임 → 컷신 동안만 AI를 끄고 직접 달리게 함.
///  - 헬리패드 둘레(반경 3.5~8m)의 자기 자리로 달려가 멈춰 서서 헬리패드를 봄
///  - 애니메이터: IsWalking / IsRunning / Speed (ZombieController와 같은 파라미터)
/// 컷신 마지막(이륙)에는 ZombieLookUp으로 바뀜 (이 컴포넌트는 지움).
/// </summary>
public class ZombieHordeRush : MonoBehaviour
{
    public Vector3 center;
    public float speed = 6f;

    static readonly int HashIsWalking = Animator.StringToHash("IsWalking");
    static readonly int HashIsRunning = Animator.StringToHash("IsRunning");
    static readonly int HashSpeed = Animator.StringToHash("Speed");

    NavMeshAgent agent;
    Animator anim;
    Vector3 spot;
    bool arrived;

    void Start()
    {
        var zc = GetComponent<ZombieController>();
        if (zc != null) zc.enabled = false;
        foreach (var b in GetComponents<ZombieBrain>()) b.enabled = false;
        anim = GetComponentInChildren<Animator>();
        agent = GetComponent<NavMeshAgent>();

        // 헬리패드 둘레 자기 자리 (자기가 온 방향 쪽)
        Vector3 from = transform.position - center; from.y = 0f;
        if (from.sqrMagnitude < 0.01f) from = Random.insideUnitSphere;
        from.y = 0f;
        Vector3 want = center + Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * from.normalized * Random.Range(3.5f, 8f);
        spot = NavMesh.SamplePosition(want, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : want;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = speed;
            agent.acceleration = 20f;
            agent.angularSpeed = 540f;
            agent.stoppingDistance = 0.3f;
            agent.SetDestination(spot);
        }
    }

    void Update()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (!arrived && !agent.pathPending && agent.remainingDistance <= 0.6f)
        {
            arrived = true;
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
        if (arrived)
        {
            Vector3 d = center - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
        }
        if (anim == null || anim.runtimeAnimatorController == null) return;
        float v = agent.velocity.magnitude;
        foreach (var p in anim.parameters)
        {
            if (p.nameHash == HashIsWalking) anim.SetBool(HashIsWalking, v > 0.1f);
            else if (p.nameHash == HashIsRunning) anim.SetBool(HashIsRunning, v > 2f);
            else if (p.nameHash == HashSpeed) anim.SetFloat(HashSpeed, v, 0.1f, Time.deltaTime);
        }
    }

    void OnDisable()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
    }
}

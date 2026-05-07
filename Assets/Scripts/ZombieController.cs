using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// 좀비 AI 컨트롤러
/// 상태: Wander(배회) → Chase(추적) → Attack(공격)
/// </summary>
public class ZombieController : MonoBehaviour
{
    // ────────────────────────────────────────────
    // Inspector 설정값
    // ────────────────────────────────────────────

    [Header("Detection Settings")]
    public float soundDetectionRadius = 5f;     // 소리 감지 반경 (즉시 추적)
    public float sightDetectionRadius = 20f;    // 시야 감지 거리
    public float fieldOfView = 120f;            // 시야각
    public float loseAggroTime = 8f;            // 추적 포기까지 대기 시간

    [Header("Wander Settings")]
    public float minWanderRadius = 5f;
    public float maxWanderRadius = 15f;
    public float minWaitTime = 1f;
    public float maxWaitTime = 4f;

    [Header("Melee Attack Settings")]
    public float attackRange = 2.2f;            // 공격 가능 거리
    public float attackDamage = 20f;            // 공격 데미지
    public float attackCooldown = 1.2f;         // 공격 쿨다운 (초)

    [Header("Movement Speed")]
    public float wanderSpeed = 0.8f;            // 배회 속도 (Mixamo Zombie Walk 기준)
    public float chaseSpeed = 2.5f;             // 추적 속도 (Mixamo Zombie Run 기준)

    // ────────────────────────────────────────────
    // 내부 상태
    // ────────────────────────────────────────────

    private enum ZombieState { Wander, Chase, Attack }
    private ZombieState currentState = ZombieState.Wander;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform player;

    private float timeOutOfRange = 0f;
    private float lastAttackTime = -99f;

    private bool isWaiting = false;
    private float waitTimer = 0f;
    private float currentWaitTime = 0f;

    // 애니메이터 파라미터 해시 (문자열 비교보다 빠름)
    private static readonly int AnimSpeed    = Animator.StringToHash("Speed");
    private static readonly int AnimAttack   = Animator.StringToHash("Attack");
    private static readonly int AnimDead     = Animator.StringToHash("IsDead");

    // ────────────────────────────────────────────
    // Unity 생명주기
    // ────────────────────────────────────────────

    void Start()
    {
        agent    = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        // Resources 폴더에서 컨트롤러를 강제 할당
        if (animator != null)
        {
            RuntimeAnimatorController ctrl =
                Resources.Load<RuntimeAnimatorController>("ZombieAnimator");
            if (ctrl != null)
                animator.runtimeAnimatorController = ctrl;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;

        // Health 이벤트 연결 (사망 처리)
        Health health = GetComponent<Health>();
        if (health != null)
            health.onDeath.AddListener(OnDead);

        agent.speed = wanderSpeed;
        agent.stoppingDistance = attackRange * 0.7f;
        SetRandomDestination();
    }

    void Update()
    {
        if (player == null) return;

        switch (currentState)
        {
            case ZombieState.Wander: UpdateWander(); break;
            case ZombieState.Chase:  UpdateChase();  break;
            case ZombieState.Attack: UpdateAttack(); break;
        }

        UpdateAnimator();
    }

    // ────────────────────────────────────────────
    // 상태별 업데이트
    // ────────────────────────────────────────────

    void UpdateWander()
    {
        bool detected = CheckDetection();
        if (detected)
        {
            EnterChase();
            return;
        }

        // 목적지 도착 시 대기 후 다음 목적지 설정
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!isWaiting)
            {
                isWaiting = true;
                waitTimer = 0f;
                currentWaitTime = Random.Range(minWaitTime, maxWaitTime);
            }
        }

        if (isWaiting)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= currentWaitTime)
            {
                isWaiting = false;
                SetRandomDestination();
            }
        }
    }

    void UpdateChase()
    {
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        // 공격 사거리 안에 들어오면 Attack 상태 전환
        if (distToPlayer <= attackRange)
        {
            EnterAttack();
            return;
        }

        bool detected = CheckDetection();
        if (detected)
        {
            timeOutOfRange = 0f;
            agent.SetDestination(player.position);
            FacePlayer();
        }
        else
        {
            // 감지 실패 시 포기 타이머 증가
            timeOutOfRange += Time.deltaTime;
            if (timeOutOfRange >= loseAggroTime)
                EnterWander();
        }
    }

    void UpdateAttack()
    {
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        FacePlayer();
        agent.SetDestination(transform.position); // 제자리 정지

        // 사거리를 벗어나면 다시 추적
        if (distToPlayer > attackRange + 0.5f)
        {
            EnterChase();
            return;
        }

        // 쿨다운이 지났으면 공격
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            PerformAttack();
        }
    }

    // ────────────────────────────────────────────
    // 상태 전환
    // ────────────────────────────────────────────

    void EnterWander()
    {
        currentState   = ZombieState.Wander;
        timeOutOfRange = 0f;
        agent.speed    = wanderSpeed;
        agent.ResetPath();
        isWaiting      = true;
        waitTimer      = 0f;
        currentWaitTime = Random.Range(minWaitTime, maxWaitTime);
    }

    void EnterChase()
    {
        currentState          = ZombieState.Chase;
        timeOutOfRange        = 0f;
        isWaiting             = false;
        agent.speed           = chaseSpeed;
        agent.stoppingDistance = 0f;
        agent.SetDestination(player.position);
    }

    void EnterAttack()
    {
        currentState = ZombieState.Attack;
        agent.ResetPath();
    }

    // ────────────────────────────────────────────
    // 핵심 로직
    // ────────────────────────────────────────────

    void PerformAttack()
    {
        lastAttackTime = Time.time;

        if (animator != null)
            animator.SetTrigger(AnimAttack);

        Health playerHealth = player.GetComponent<Health>();
        if (playerHealth != null)
            playerHealth.TakeDamage(attackDamage);
    }

    bool CheckDetection()
    {
        float dist = Vector3.Distance(transform.position, player.position);

        // 소리 감지: 짧은 거리는 무조건 탐지
        if (dist <= soundDetectionRadius)
            return true;

        // 시야 감지: FOV + 레이캐스트
        if (dist <= sightDetectionRadius)
        {
            Vector3 dirToPlayer = (player.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToPlayer);

            if (angle <= fieldOfView / 2f)
            {
                RaycastHit hit;
                Vector3 origin = transform.position + Vector3.up * 1f;
                Vector3 target = player.position + Vector3.up * 1f;

                if (Physics.Raycast(origin, (target - origin).normalized, out hit, sightDetectionRadius))
                {
                    if (hit.collider.CompareTag("Player"))
                        return true;
                }
            }
        }

        return false;
    }

    void SetRandomDestination()
    {
        float radius = Random.Range(minWanderRadius, maxWanderRadius);
        Vector3 randomDir = Random.insideUnitSphere * radius + transform.position;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDir, out hit, radius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    void FacePlayer()
    {
        Vector3 dir = (player.position - transform.position).normalized;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
    }

    void UpdateAnimator()
    {
        if (animator == null) return;
        // desiredVelocity: 가속 딜레이 없이 목표 속도를 즉시 반영
        float normalizedSpeed = agent.desiredVelocity.magnitude / chaseSpeed;
        animator.SetFloat(AnimSpeed, normalizedSpeed, 0.05f, Time.deltaTime);
    }

    // ────────────────────────────────────────────
    // 외부 호출 함수
    // ────────────────────────────────────────────

    /// <summary>
    /// 공격을 받았을 때 Health.onHurt에서 호출. 즉시 추적 시작.
    /// </summary>
    public void OnAttacked()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }

        if (currentState == ZombieState.Wander)
            EnterChase();

        timeOutOfRange = 0f;
    }

    /// <summary>
    /// 사망 시 Health.onDeath에서 호출.
    /// </summary>
    private void OnDead()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        if (animator != null)
            animator.SetBool(AnimDead, true);

        StartCoroutine(DeathSequence());
        enabled = false;
    }

    private IEnumerator DeathSequence()
    {
        // 사망 애니메이션 재생 대기
        yield return new WaitForSeconds(1.8f);

        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        // 재질을 투명 모드로 전환
        foreach (Renderer r in renderers)
        {
            foreach (Material m in r.materials)
            {
                if (m.HasProperty("_Surface"))
                {
                    m.SetFloat("_Surface", 1f);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = 3000;
                }
                else if (m.HasProperty("_Mode"))
                {
                    m.SetFloat("_Mode", 2f);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_ALPHABLEND_ON");
                    m.renderQueue = 3000;
                }
            }
        }

        // 1.5초에 걸쳐 서서히 투명하게 (재처럼 사라짐)
        float duration = 1.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);

            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.materials)
                {
                    if (m.HasProperty("_BaseColor"))
                    {
                        Color c = m.GetColor("_BaseColor");
                        c.a = alpha;
                        m.SetColor("_BaseColor", c);
                    }
                    else if (m.HasProperty("_Color"))
                    {
                        Color c = m.GetColor("_Color");
                        c.a = alpha;
                        m.SetColor("_Color", c);
                    }
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    // ────────────────────────────────────────────
    // 에디터 기즈모
    // ────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        // 소리 감지 반경 (빨강)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, soundDetectionRadius);

        // 시야 감지 반경 (노랑)
        Gizmos.color = Color.yellow;
        Vector3 left  = Quaternion.Euler(0, -fieldOfView / 2f, 0) * transform.forward;
        Vector3 right = Quaternion.Euler(0,  fieldOfView / 2f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position, left  * sightDetectionRadius);
        Gizmos.DrawRay(transform.position, right * sightDetectionRadius);

        // 공격 사거리 (파랑)
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}

using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class EnemyController : MonoBehaviour
{
    [Header("Detection Settings")]
    public float soundDetectionRadius = 3f;   // 소리 인식 반경 (작은 원)
    public float sightDetectionRadius = 15f;  // 시야 인식 거리
    public float fieldOfView = 90f;           // 시야 각도 (부채꼴의 벌어진 정도)

    [Header("Random Wander Settings")]
    public float minWanderRadius = 5f;
    public float maxWanderRadius = 15f;
    public float minWaitTime = 1f;
    public float maxWaitTime = 4f;

    [Header("Burst Fire Settings")]
    public int minBurstCount = 1;
    public int maxBurstCount = 3;
    public float timeBetweenBursts = 1.5f;


    public float loseAggroTime = 3f;

    private NavMeshAgent agent;
    private Transform player;
    
    private bool isChasing = false;
    private float timeOutOfRange = 0f;

    private bool isWaiting = false;
    private float waitTimer = 0f;
    private float currentWaitTime = 0f;

    private Weapon myWeapon;
    private bool isFireingBurst = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        myWeapon = GetComponent<Weapon>();

        if(myWeapon != null)
        {
            myWeapon.shooterTag = "Enemy";
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }

        SetRandomDestination();

        myWeapon = GetComponent<Weapon>();
        myWeapon.shooterTag = "Enemy";
    }

    void Update()
    {
        if (player == null) return;

        // 1. 인식 조건 검사 함수를 실행하여 결과를 저장한다.
        bool detected = CheckDetection();

        if (detected)
        {
            isChasing = true;
            timeOutOfRange = 0f;
            isWaiting = false;

            FacePlayer();

            if (!isFireingBurst)
            {
                StartCoroutine(ShootBurst());
            }
        }
        else if (isChasing == true)
        {
            timeOutOfRange += Time.deltaTime;

            if (timeOutOfRange >= loseAggroTime)
            {
                isChasing = false;
                agent.ResetPath();
                isWaiting = true;
                waitTimer = 0f;
                currentWaitTime = Random.Range(minWaitTime, maxWaitTime);
            }
        }

        if (isChasing == true)
        {
            agent.SetDestination(player.position);
        }
        else
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                if (isWaiting == false)
                {
                    isWaiting = true;
                    waitTimer = 0f;
                    currentWaitTime = Random.Range(minWaitTime, maxWaitTime);
                }
            }

            if (isWaiting == true)
            {
                waitTimer += Time.deltaTime;
                if (waitTimer >= currentWaitTime)
                {
                    isWaiting = false;
                    SetRandomDestination();
                }
            }
        }
    }

    bool CheckDetection()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // [소리 인식] 거리가 설정된 소리 반경보다 작으면 즉시 true(인식)를 반환.
        if (distanceToPlayer <= soundDetectionRadius)
        {
            return true;
        }

        // [시야 인식] 거리가 시야 거리 안쪽에 있을 때만 추가 검사를 진행한다.
        if (distanceToPlayer <= sightDetectionRadius)
        {
            // 적의 위치에서 플레이어를 향하는 방향 벡터를 계산한다.
            Vector3 directionToPlayer = (player.position - transform.position).normalized;
            
            // 적이 바라보는 정면(forward)과 플레이어가 있는 방향 사이의 각도를 산출한다.
            float angle = Vector3.Angle(transform.forward, directionToPlayer);

            // 각도가 시야각의 절반(예: 90도일 경우 좌우 45도) 이내인지 확인한다.
            if (angle <= fieldOfView / 2f)
            {
                // 적의 중심점에서 플레이어를 향해 가상의 직선(Ray)을 발사해 벽이 있는지 검사한다.
                // 바닥을 긁지 않도록 높이(Y축)를 1만큼 더함.
                RaycastHit hit;
                Vector3 rayOrigin = transform.position + Vector3.up * 1f;
                Vector3 rayTarget = player.position + Vector3.up * 1f;
                Vector3 rayDirection = (rayTarget - rayOrigin).normalized;

                if (Physics.Raycast(rayOrigin, rayDirection, out hit, sightDetectionRadius))
                {
                    if (hit.collider.CompareTag("Player"))
                    {
                        return true;
                    }
                }
            }
        }

        // 아무 조건에도 맞지 않으면 false(인식 실패)를 반환한다.
        return false; 
    }

    void SetRandomDestination()
    {
        float currentWanderRadius = Random.Range(minWanderRadius, maxWanderRadius);
        Vector3 randomDirection = Random.insideUnitSphere * currentWanderRadius;
        randomDirection += transform.position;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, currentWanderRadius, 1))
        {
            agent.SetDestination(hit.position);
        }
    }

    // 에디터 화면에서 인식 반경과 각도를 시각적으로 그려준다.
    void OnDrawGizmosSelected()
    {
        // 1. 소리 인식 범위 (빨간색)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, soundDetectionRadius);

        // 2. 시야 인식 범위 (노란색 선으로 부채꼴의 양 끝 경계를 표시한다)
        Gizmos.color = Color.yellow;
        Vector3 leftBoundary = Quaternion.Euler(0, -fieldOfView / 2f, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, fieldOfView / 2f, 0) * transform.forward;

        Gizmos.DrawRay(transform.position, leftBoundary * sightDetectionRadius);
        Gizmos.DrawRay(transform.position, rightBoundary * sightDetectionRadius);
    }

    void HandleShooting()
    {
        myWeapon.TryFire();
    }

    void FacePlayer()
    {
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0; // 수평 방향으로만 회전하도록 Y축 고정
        Quaternion lookRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 10f);
    }

    IEnumerator ShootBurst()
    {
        isFireingBurst = true;
        int burstCount = Random.Range(minBurstCount, maxBurstCount + 1);

        // WeaponData 할당 여부를 확인하여 연사 속도를 결정함.
        float currentFireRate = (myWeapon.weaponData != null) ? myWeapon.weaponData.attackRate : 0.2f;

        for (int i = 0; i < burstCount; i++)
        {
            myWeapon.TryFire();
            
            // 산출된 연사 속도를 대기 시간으로 사용함.
            yield return new WaitForSeconds(currentFireRate); 
        }

        yield return new WaitForSeconds(timeBetweenBursts); 
        isFireingBurst = false;
    }
    public void OnAttacked()
    {
        if(player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }

        isChasing = true;
        timeOutOfRange = 0f;

        Debug.Log("적이 공격당했습니다! 플레이어를 추적합니다.");
    }
}
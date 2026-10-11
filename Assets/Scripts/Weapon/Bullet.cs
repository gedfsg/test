using UnityEngine;

public class Bullet : MonoBehaviour
{
    public string shooterTag;
    public float damage;
    public float speed = 20f; 
    public float effectiveRange = 50f;

    public bool penetrating = false;

    [Header("크리티컬")]
    public float criticalChance = 0.15f;
    public float criticalMultiplier = 2f;

    private Vector3 startPosition;
    private float currentDamage;
    private TrailRenderer trail;
    private Vector3 moveDirection;

    void Start()
    {
        // 발사된 초기 위치와 초기 데미지를 저장함.
        startPosition = transform.position;
        currentDamage = damage;

        moveDirection = transform.forward;

        SetupTrail();
    }

    void SetupTrail()
    {
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.08f;
        trail.startWidth = 0.05f;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.01f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;

        // 노란색 → 투명 그라디언트
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.yellow, 0f),
                new GradientColorKey(Color.yellow, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        trail.colorGradient = gradient;

        // Trail 전용 머티리얼 (Sprites/Default 는 색상을 그대로 표현함)
        trail.material = new Material(Shader.Find("Sprites/Default"));
    }

    private bool destroyed = false;

    void Update()
    {
        if (destroyed) return;

        Vector3 oldPos = transform.position;
        float step = speed * Time.deltaTime;
        Vector3 newPos = oldPos + moveDirection * step;

        // 총알이 빠르고(초당 수십~수백 유닛) Transform을 직접 움직이기 때문에,
        // 한 프레임 이동 거리가 좀비 콜라이더보다 커서 OnTriggerEnter가 그냥 지나쳐버리는
        // "터널링"이 생김. 그래서 이전 위치→새 위치 구간을 매 프레임 레이캐스트로 훑어서
        // 맞았는지 직접 확인함 (OnTriggerEnter는 저속 보조용으로만 남겨둠).
        //
        // 프로젝트의 Physics.autoSyncTransforms가 꺼져있어서(Edit > Project Settings > Physics),
        // 이 프레임에 다른 스크립트(좀비 NavMeshAgent 등)가 transform.position을 직접 옮겨도
        // 물리엔진 내부 콜라이더 위치는 다음 동기화 시점까지 안 바뀜 - 레이캐스트가 좀비를
        // "그 자리에 없는 것"처럼 놓칠 수 있음. 그래서 쏘기 직전에 강제로 동기화함.
        Physics.SyncTransforms();
        if (step > 0f)
        {
            // RaycastAll + 거리순 정렬: 단발 Raycast는 가장 가까운 콜라이더 하나만 주는데,
            // 그게 무시 대상(다른 총알 등)이면 바로 뒤에 있는 진짜 타겟을 놓쳐버림.
            // 그래서 경로상의 모든 충돌을 가까운 순으로 보면서 처리 대상이 나올 때까지 건너뜀.
            RaycastHit[] hits = Physics.RaycastAll(oldPos, moveDirection, step, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (TryProcessHit(h.collider))
                {
                    transform.position = h.point;
                    return;
                }
            }
        }

        transform.position = newPos;

        // 시작 위치로부터 이동한 누적 거리를 계산함.
        float distanceTraveled = Vector3.Distance(startPosition, transform.position);

        // 누적 거리가 유효 사거리의 절반을 초과했을 경우 데미지를 50%로 감소시킴.
        if (distanceTraveled > effectiveRange / 2f)
        {
            currentDamage = damage / 2f;
        }

        // 누적 거리가 유효 사거리를 초과했을 경우 투사체 객체를 파괴함.
        if (distanceTraveled > effectiveRange)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // 레이캐스트가 못 잡는 경우(저속/근접)를 위한 보조 경로.
        TryProcessHit(other);
    }

    // true를 반환하면 이 충돌로 총알이 소모됨(관통이 아니면 파괴).
    bool TryProcessHit(Collider other)
    {
        if (destroyed) return false;
        if (other.CompareTag(shooterTag)) return false;
        if (other.GetComponent<Bullet>() != null) return false;
        if (other.GetComponent<PickupItem>() != null) return false;

        bool hitSomething = false;
        bool isCritical = UnityEngine.Random.value < criticalChance;
        float finalDamage = isCritical ? currentDamage * criticalMultiplier : currentDamage;

        // Health 검색 - 자식/부모까지
        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth == null) targetHealth = other.GetComponentInParent<Health>();
        if (targetHealth == null) targetHealth = other.GetComponentInChildren<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(finalDamage, isCritical);
            hitSomething = true;
        }

        // 좀비 직접 데미지 (Health 없는 경우 백업)
        ZombieController zombie = other.GetComponent<ZombieController>();
        if (zombie == null) zombie = other.GetComponentInParent<ZombieController>();
        if (zombie != null && targetHealth == null)
        {
            zombie.TakeDamage(finalDamage);
            hitSomething = true;
        }

        DestructibleObstacle obstacle = other.GetComponent<DestructibleObstacle>();
        if (obstacle == null) obstacle = other.GetComponentInParent<DestructibleObstacle>();
        if (obstacle != null)
        {
            obstacle.TakeDamage(currentDamage);
            hitSomething = true;
        }

        if (!hitSomething) { Destroy(gameObject); destroyed = true; return true; }

        if (!penetrating) { Destroy(gameObject); destroyed = true; }
        return true;
    }
}
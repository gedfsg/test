using UnityEngine;

public class Bullet : MonoBehaviour
{
    public string shooterTag;
    public float damage;
    public float speed = 20f; 
    public float effectiveRange = 50f;

    private Vector3 startPosition;
    private float currentDamage;
    private TrailRenderer trail;

    void Start()
    {
        // 발사된 초기 위치와 초기 데미지를 저장함.
        startPosition = transform.position;
        currentDamage = damage;

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

    void Update()
    {
        // 매 프레임마다 지정된 속도로 투사체를 전진시킴.
        transform.Translate(Vector3.forward * speed * Time.deltaTime);

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
        if (other.CompareTag(shooterTag)) return;

        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth != null)
        {
            // 계산된 현재 데미지를 대상에게 전달함.
            targetHealth.TakeDamage(currentDamage); 
        }

        DestructibleObstacle obstacle = other.GetComponent<DestructibleObstacle>();
        if (obstacle != null)
        {
            obstacle.TakeDamage(currentDamage);
        }

        Destroy(gameObject);
    }
}
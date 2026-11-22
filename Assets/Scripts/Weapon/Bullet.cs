using UnityEngine;

public class Bullet : MonoBehaviour
{
    public string shooterTag;
    public float damage;
    public float speed = 20f; 
    public float effectiveRange = 50f;

    public bool penetrating = false;

    [Header("관통 횟수 (2026-11-16)")]
    [Tooltip("뚫고 지나갈 수 있는 좀비 수. 0이면 penetrating 값대로(켜져 있으면 무제한). 통과해도 데미지 그대로")]
    public int penetrationCount = 0;
    private int penetratedSoFar;
    // 같은 좀비를 여러 콜라이더(몸통·머리 등)로 두 번 맞히지 않게
    private readonly System.Collections.Generic.HashSet<GameObject> alreadyHit = new System.Collections.Generic.HashSet<GameObject>();

    [Header("특수 탄 (2026-11-16) - Weapon.SpawnBullet이 WeaponData 값으로 채움")]
    [Tooltip("RPG: 처음 맞은 곳에서 폭발 (반경 안 좀비 광역 데미지 + 넉백)")]
    public float splashDamage;
    public float splashRadius;
    public float knockback;
    [Tooltip("화염방사기: 맞은 좀비에 점화 DoT")]
    public float burnDps;
    public float burnDuration;
    [Tooltip("(2026-11-22) 폭발(splash) 소리. 비우면 산탄 소리를 낮게")]
    public AudioClip explodeSound;
    [Tooltip("궤적 색 (기본 노랑). 화염 = 주황, 레이저 = 청록")]
    public Color trailColor = Color.yellow;
    [Tooltip("궤적 굵기 (기본 0.05)")]
    public float trailWidth = 0.05f;

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
        trail.startWidth = trailWidth; // (이전) 0.05f 고정
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.01f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;

        // 노란색 → 투명 그라디언트
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(trailColor, 0f),   // (이전) Color.yellow 고정
                new GradientColorKey(trailColor, 1f)
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
                if (TryProcessHit(h.collider, h.point))
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
            if (splashRadius > 0f && !destroyed) Explode(transform.position); // RPG: 사거리 끝에서도 터짐
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // 레이캐스트가 못 잡는 경우(저속/근접)를 위한 보조 경로.
        TryProcessHit(other, GameUtils.ClosestPointSafe(other, transform.position));
    }

    // true를 반환하면 이 충돌로 총알이 소모됨(관통이 아니면 파괴).
    bool TryProcessHit(Collider other, Vector3 hitPoint)
    {
        if (destroyed) return false;
        if (other.CompareTag(shooterTag)) return false;
        if (other.GetComponent<Bullet>() != null) return false;
        // 죽은 좀비(래그돌 시체)는 총알이 그냥 지나간다 —— 예전처럼 시체가 뒤의 좀비를 막아 주지 않게
        var deadZombie = other.GetComponentInParent<ZombieController>();
        if (deadZombie != null && deadZombie.IsDead) return false;
        // 관통 중 이미 맞힌 좀비의 다른 콜라이더는 건너뜀 (한 좀비 = 관통 1회)
        if (deadZombie != null && alreadyHit.Contains(deadZombie.gameObject)) return false;
        if (other.GetComponent<PickupItem>() != null) return false;
        if (other.GetComponent<InvisibleWall>() != null) return false; // 다리 가장자리 투명 벽은 총알 통과 (2026-11-12)

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

        // 좀비 피격 이펙트 (피 파티클 / 빨간 플래시 / 넉백 / 사운드)
        if (zombie != null && hitSomething)
        {
            ZombieHitEffect.Play(zombie.gameObject, hitPoint, moveDirection);
            alreadyHit.Add(zombie.gameObject);
            if (burnDps > 0f && burnDuration > 0f) BurnDoT.Apply(zombie.gameObject, burnDps, burnDuration); // 화염방사기 점화
        }

        DestructibleObstacle obstacle = other.GetComponent<DestructibleObstacle>();
        if (obstacle == null) obstacle = other.GetComponentInParent<DestructibleObstacle>();
        if (obstacle != null)
        {
            obstacle.TakeDamage(currentDamage);
            hitSomething = true;
        }

        // 데미지를 줄 수 없는 트리거(건물 진입 감지 영역 등)는 통과시킴.
        // 이게 없으면 바깥에서 문 안으로 쏜 총알이 진입 트리거 경계에서 그냥 사라졌음.
        if (!hitSomething && other.isTrigger) return false;

        // RPG: 처음 맞은 곳(좀비든 벽이든)에서 폭발하고 탄은 사라짐
        if (splashRadius > 0f) { Explode(hitPoint); Destroy(gameObject); destroyed = true; return true; }

        if (!hitSomething) { Destroy(gameObject); destroyed = true; return true; }

        // (이전) if (!penetrating) { Destroy(gameObject); destroyed = true; }
        // 관통 횟수 (2026-11-16): penetrationCount > 0이면 좀비를 그 수만큼 뚫고 (데미지 그대로) 다음 좀비에서 멈춤
        //  - Mosin 2 = 좀비 2마리까지 맞히고 사라짐 (1마리 관통 후 2번째에서 멈춤이 아니라 "2마리까지")
        //  - 장애물(DestructibleObstacle)은 관통하지 않음
        bool hitZombie = zombie != null;
        if (penetrationCount > 0)
        {
            if (hitZombie) penetratedSoFar++;
            if (!hitZombie || penetratedSoFar >= penetrationCount) { Destroy(gameObject); destroyed = true; }
            return true;
        }
        if (!penetrating) { Destroy(gameObject); destroyed = true; }
        return true;
    }

    // ── RPG 폭발 (2026-11-16) ──
    // 반경 안 모든 Health(쏜 쪽 태그 제외)에 광역 데미지 + 좀비 넉백. 직격 대상도 광역을 같이 받음 (300 + 150)
    // (2026-11-22) 자해: 쏜 플레이어도 폭발 반경 안이면 다침 — 가까울수록 세게, 중심 = 광역 데미지의 40% (기본 60) → 가장자리 0
    const float SelfSplashRatio = 0.4f;

    void Explode(Vector3 center)
    {
        var done = new System.Collections.Generic.HashSet<Health>();
        foreach (var c in Physics.OverlapSphere(center, splashRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            // if (!string.IsNullOrEmpty(shooterTag) && c.CompareTag(shooterTag)) continue; // (이전) 쏜 쪽은 무시
            Health h = c.GetComponentInParent<Health>();
            if (h == null || !done.Add(h)) continue;
            if (!string.IsNullOrEmpty(shooterTag) && (c.CompareTag(shooterTag) || h.CompareTag(shooterTag)))
            {
                if (shooterTag == "Player") SelfSplash(h, center);   // (2026-11-22) 자해
                continue;
            }
            var z = h.GetComponent<ZombieController>();
            if (z != null && z.IsDead) continue;
            h.TakeDamage(splashDamage);
            if (z != null)
            {
                Vector3 dir = h.transform.position - center; dir.y = 0f;
                var fx = z.GetComponent<ZombieHitEffect>();
                if (fx == null) fx = z.gameObject.AddComponent<ZombieHitEffect>(); // Unity 오브젝트라 ?? 대신 == null
                float keep = fx.knockbackDistance;
                fx.knockbackDistance = keep * Mathf.Max(1f, knockback); // 넉백 배수만큼 멀리
                fx.PlayHit(h.transform.position + Vector3.up, dir.sqrMagnitude > 1e-4f ? dir.normalized : moveDirection);
                fx.knockbackDistance = keep;
            }
        }
        WorldFX_Explosion(center);
        NoiseSystem.Emit(center, splashRadius * 6f); // 폭발음은 크게 들림
    }

    /// <summary>(2026-11-22) RPG 자해 — 폭발 중심에서 멀수록 줄어듦 (구르기 무적 중엔 Health가 알아서 무시)</summary>
    void SelfSplash(Health h, Vector3 center)
    {
        Vector3 d = h.transform.position - center; d.y = 0f;
        float falloff = Mathf.Clamp01(1f - d.magnitude / Mathf.Max(0.01f, splashRadius));
        float dmg = splashDamage * SelfSplashRatio * falloff;
        if (dmg >= 1f) h.TakeDamage(dmg);
    }

    static Material explosionMat;

    // 간단한 폭발 표시: 주황 점광 0.25초 + 파티클 (전용 이펙트 에셋 없음)
    void WorldFX_Explosion(Vector3 p)
    {
        var go = new GameObject("RPG_Explosion");
        go.transform.position = p + Vector3.up * 0.5f;
        var li = go.AddComponent<Light>();
        li.type = LightType.Point; li.color = new Color(1f, 0.55f, 0.15f); li.range = splashRadius * 1.5f; li.intensity = 6f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.duration = 0.3f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, splashRadius * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.1f), new Color(0.3f, 0.3f, 0.3f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)40) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (explosionMat == null) explosionMat = new Material(Shader.Find("Sprites/Default")) { name = "RPGExplosion(Runtime)" };
        r.sharedMaterial = explosionMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Destroy(go, 1f);
        Destroy(li, 0.25f);
        // (2026-11-22) RPG: rpg.mp3의 폭발 구간 (Weapon.SpawnBullet이 넣어 줌). 없으면 예전처럼 산탄 소리를 낮게 (총소리처럼 들렸음)
        if (explodeSound != null)
            AudioManager.PlaySFX(explodeSound, p, 1f, AudioManager.Channel.SFX, 80f);
        else if (AudioManager.Library != null)
            AudioManager.PlaySFX(SoundLibrary.Pick(AudioManager.Library.FireFor(WeaponType.Shotgun)), p, AudioManager.Vol.GunshotShotgun,
                                 AudioManager.Channel.SFX, 80f, 0.6f); // 산탄 소리를 낮게 → 폭발음 대용
    }
}

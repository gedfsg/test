using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 수류탄 (2026-11-16). ThrowableController가 던질 때 붙임 (Rigidbody 투사체).
///  - 신관 3초(ThrowableData.fuseTime) 또는 좀비에 맞으면 즉시 폭발
///  - 폭발: explosin.mp3 + 연기·불꽃 파티클(1초) + 섬광
///  - 광역 데미지: 반경 innerRadius(3m) 안 = damage(500, 즉사), 3~radius(8m) 500 → 0 선형 감소. 플레이어도 맞음
///  - 좀비 넉백 + 시체(래그돌) AddExplosionForce, 소음 반경 100m, 화면 흔들림 0.3초
///  (2026-11-18) 사실적인 신관: ThrowableController가 핀을 뽑은 순간부터 5초를 재고 남은 시간을 fuseOverride로 넘김.
///   공중이든 땅이든 그 시간 뒤 폭발 (좀비에 맞아도 바로 안 터짐)
/// </summary>
public class Grenade : MonoBehaviour
{
    [HideInInspector] public ThrowableData data;
    [HideInInspector] public GameObject thrower; // 던진 사람 (닿아도 터지지 않음)
    [HideInInspector] public float fuseOverride = -1f; // (2026-11-18) 0 이상이면 이 시간(초) 뒤 폭발 (핀 뽑은 뒤 남은 시간)

    bool exploded;

    void Start()
    {
        StartCoroutine(Fuse());
    }

    IEnumerator Fuse()
    {
        // yield return new WaitForSeconds(data != null ? data.fuseTime : 3f); // (이전)
        yield return new WaitForSeconds(fuseOverride >= 0f ? fuseOverride : data != null ? data.fuseTime : 3f);
        Explode();
    }

    void OnCollisionEnter(Collision c)
    {
        if (exploded || c.collider == null) return;
        if (thrower != null && c.collider.transform.IsChildOf(thrower.transform)) return;
        // (이전 2026-11-16) 적(좀비)에 직접 맞으면 즉시 → 2026-11-18 신관 시간으로만 폭발
        // var z = c.collider.GetComponentInParent<ZombieController>();
        // if (z != null && !z.IsDead) Explode();
    }

    public void Explode()
    {
        if (exploded) return;
        exploded = true;
        Vector3 p = transform.position;
        float outer = data != null ? data.radius : 8f, inner = data != null ? data.innerRadius : 3f, maxDmg = data != null ? data.damage : 500f;

        // ── 데미지 (Health 단위로 한 번씩) ──
        var done = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(p, outer, ~0, QueryTriggerInteraction.Collide))
        {
            Health h = col.GetComponentInParent<Health>();
            if (h == null || !done.Add(h)) continue;
            var z = h.GetComponent<ZombieController>();
            if (z != null && z.IsDead) continue;
            float d = Vector3.Distance(p, h.transform.position + Vector3.up);
            float dmg = d <= inner ? maxDmg : maxDmg * (1f - Mathf.Clamp01((d - inner) / Mathf.Max(0.01f, outer - inner)));
            if (dmg <= 0f) continue;
            h.TakeDamage(dmg);
            if (z != null)
            {
                Vector3 dir = h.transform.position - p; dir.y = 0f;
                var fx = z.GetComponent<ZombieHitEffect>();
                if (fx == null) fx = z.gameObject.AddComponent<ZombieHitEffect>();
                float keep = fx.knockbackDistance;
                fx.knockbackDistance = keep * 6f; // 폭발에 크게 밀림
                fx.PlayHit(h.transform.position + Vector3.up, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward);
                fx.knockbackDistance = keep;
            }
        }

        NoiseSystem.Emit(p, data != null ? data.noiseRadius : 100f);
        CameraFollow.Shake(0.3f, 0.6f);
        if (data != null && data.explodeSound != null)
            AudioManager.PlaySFX(data.explodeSound, p, 1f, AudioManager.Channel.SFX, 120f);
        SpawnBlastFX(p, outer);

        // 숨기고 → 다음 물리 프레임에 날아가는 시체(죽으며 켜진 래그돌)에 폭발력
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        var rb = GetComponent<Rigidbody>(); if (rb != null) { rb.isKinematic = true; }
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        StartCoroutine(PushBodies(p, outer));
    }

    IEnumerator PushBodies(Vector3 p, float r)
    {
        yield return new WaitForFixedUpdate();
        foreach (var col in Physics.OverlapSphere(p, r))
        {
            var body = col.attachedRigidbody;
            if (body == null || body.isKinematic || (thrower != null && body.transform.IsChildOf(thrower.transform))) continue;
            body.AddExplosionForce(900f, p, r, 1.5f);
        }
        yield return new WaitForSeconds(1.2f);
        Destroy(gameObject);
    }

    static Material fxMat;

    // 연기 + 불꽃 (1초) + 섬광 0.2초. 전용 이펙트 에셋이 없어서 코드로 생성
    static void SpawnBlastFX(Vector3 p, float r)
    {
        if (fxMat == null) fxMat = new Material(Shader.Find("Sprites/Default")) { name = "GrenadeFX(Runtime)" };
        var go = new GameObject("GrenadeBlast");
        go.transform.position = p + Vector3.up * 0.3f;

        var li = go.AddComponent<Light>();
        li.type = LightType.Point; li.color = new Color(1f, 0.6f, 0.2f); li.range = r * 2f; li.intensity = 8f; li.shadows = LightShadows.None;
        Object.Destroy(li, 0.2f);

        Burst(go.transform, "Sparks", 60, new Color(1f, 0.75f, 0.2f), new Color(1f, 0.3f, 0.05f), 0.2f, 0.6f, 6f, 16f, 0.15f, 0.4f, 0f);
        Burst(go.transform, "Smoke", 30, new Color(0.35f, 0.33f, 0.3f, 0.8f), new Color(0.15f, 0.15f, 0.15f, 0.6f), 0.7f, 1.0f, 1f, 4f, 1.2f, 2.8f, -0.05f);
        Object.Destroy(go, 1.6f);
    }

    static void Burst(Transform parent, string name, int count, Color a, Color b, float lifeMin, float lifeMax,
                      float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.duration = 1f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.4f; sh.rotation = new Vector3(-90f, 0f, 0f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = fxMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 화염병 (2026-11-16). ThrowableController가 던질 때 붙임 (Rigidbody 투사체).
///  - 지면·벽에 닿으면: 유리 깨짐 + 불 붙는 소리 → 화염 지대 생성 (병은 사라짐)
///  - 화염 지대: 반경 radius(5m), fireDuration(8초) 지속, 오렌지 불꽃 파티클 + 일렁이는 빛 + fire_loop 반복
///    · 안에 있는 좀비 초당 damage(30), 그리고 점화(BurnDoT: 밖에 나가도 igniteDuration 3초간 초당 10)
///    · 플레이어는 자기 불에 피해 없음
///  - 끝나기 1초 전부터 빛·불꽃·소리가 서서히 꺼짐
/// </summary>
public class Molotov : MonoBehaviour
{
    [HideInInspector] public ThrowableData data;
    [HideInInspector] public GameObject thrower;

    bool broken;

    void OnCollisionEnter(Collision c)
    {
        if (broken || c.collider == null) return;
        if (thrower != null && c.collider.transform.IsChildOf(thrower.transform)) return;
        broken = true;

        Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        // 벽에 맞아도 불은 그 아래 바닥에 깔림
        if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;

        if (data != null)
        {
            AudioManager.PlaySFX(data.breakSound, p, 1f, AudioManager.Channel.SFX, 40f);
            AudioManager.PlaySFX(data.igniteSound, p, 1f, AudioManager.Channel.SFX, 50f);
        }
        NoiseSystem.Emit(p, data != null ? data.noiseRadius : 40f);
        FireZone.Create(p, data, thrower);
        Destroy(gameObject);
    }

    /// <summary>화염 지대 (병이 깨진 자리에 남음)</summary>
    public class FireZone : MonoBehaviour
    {
        const float Tick = 0.5f;
        const float FadeTime = 1f;

        ThrowableData data;
        GameObject thrower;
        float radius, duration, dps, start;
        Light glow;
        ParticleSystem flames;
        AudioSource loop;

        static Material fxMat;

        public static FireZone Create(Vector3 p, ThrowableData data, GameObject thrower)
        {
            var go = new GameObject("MolotovFire");
            go.transform.position = p + Vector3.up * 0.05f;
            var z = go.AddComponent<FireZone>();
            z.data = data; z.thrower = thrower;
            z.radius = data != null ? data.radius : 5f;
            z.duration = data != null ? data.fireDuration : 8f;
            z.dps = data != null ? data.damage : 30f;
            z.Build();
            return z;
        }

        void Build()
        {
            start = Time.time;
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.45f, 0.1f); glow.range = radius * 2.2f; glow.intensity = 3f;
            glow.shadows = LightShadows.None;

            if (fxMat == null) fxMat = new Material(Shader.Find("Sprites/Default")) { name = "MolotovFX(Runtime)" };
            var fgo = new GameObject("Flames");
            fgo.transform.SetParent(transform, false);
            fgo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 위로 솟음
            flames = fgo.AddComponent<ParticleSystem>();
            flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flames.main; main.loop = true; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.3f, 0.02f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = flames.emission; em.rateOverTime = radius * radius * 12f;
            var sh = flames.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 1f;
            var col = flames.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = flames.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var r = fgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = fxMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flames.Play();

            if (data != null && data.loopSound != null)
            {
                loop = gameObject.AddComponent<AudioSource>();
                loop.clip = data.loopSound; loop.loop = true; loop.spatialBlend = 1f;
                loop.rolloffMode = AudioRolloffMode.Linear; loop.minDistance = 3f; loop.maxDistance = 35f; loop.volume = 0.8f;
                var mixer = AudioManager.Mixer;
                if (mixer != null) { var gs = mixer.FindMatchingGroups("SFX"); if (gs.Length > 0) loop.outputAudioMixerGroup = gs[0]; }
                loop.Play();
            }
            StartCoroutine(Burn());
        }

        IEnumerator Burn()
        {
            var hitThisTick = new HashSet<Health>();
            while (Time.time < start + duration)
            {
                hitThisTick.Clear();
                foreach (var c in Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Collide))
                {
                    Health h = c.GetComponentInParent<Health>();
                    if (h == null || !hitThisTick.Add(h)) continue;
                    if (h.CompareTag("Player") || (thrower != null && h.gameObject == thrower)) continue; // 자기 불엔 안 다침
                    var z = h.GetComponent<ZombieController>();
                    if (z == null || z.IsDead) continue; // 좀비만
                    Vector3 d = h.transform.position - transform.position; d.y = 0f;
                    if (d.magnitude > radius) continue; // 원기둥 판정 (위·아래로 멀면 구가 잡아도 수평으로 재확인)
                    h.TakeDamage(dps * Tick);
                    BurnDoT.Apply(h.gameObject, data != null ? data.igniteDps : 10f, data != null ? data.igniteDuration : 3f);
                }
                yield return new WaitForSeconds(Tick);
            }
            Destroy(gameObject);
        }

        void Update()
        {
            // 끝나기 FadeTime 전부터 서서히 꺼짐 + 일렁임
            float left = start + duration - Time.time;
            float k = Mathf.Clamp01(left / FadeTime);
            glow.intensity = (2.4f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 1.2f) * k;
            if (loop != null) loop.volume = 0.8f * k;
            if (left < FadeTime && flames != null && flames.isEmitting) flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}

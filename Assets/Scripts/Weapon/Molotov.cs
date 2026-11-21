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
        // (2026-11-21) 자해: 플레이어도 불 위에 서 있으면 다침 (좀비보다 약하게) + 나와도 잠깐 탐
        const float PlayerDamageRatio = 0.4f;     // 좀비 초당 피해(data.damage)의 40% (기본 30 → 초당 12)
        const float PlayerIgniteDps = 5f, PlayerIgniteDuration = 2f;

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
            // glow.type = LightType.Point; glow.color = new Color(1f, 0.45f, 0.1f); glow.range = radius * 2.2f; glow.intensity = 3f; // (이전)
            glow.type = LightType.Point; glow.color = new Color(1f, 0.45f, 0.1f); glow.range = radius * 2.2f; glow.intensity = 2f; // (2026-11-22 C3) 덜 눈부시게
            glow.shadows = LightShadows.None;

            // if (fxMat == null) fxMat = new Material(Shader.Find("Sprites/Default")) { name = "MolotovFX(Runtime)" }; // (이전) 텍스처 없어서 네모 파티클
            // (2026-11-22 C3) 불꽃 스프라이트(코드 생성) + 알파 블렌딩 (가산이 아니라 덜 눈부시고, 끝 색 검정이 연기처럼 보임)
            if (fxMat == null)
            {
                var shd = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
                fxMat = new Material(shd) { name = "MolotovFX(Runtime)", mainTexture = FlameTexture() };
                if (fxMat.HasProperty("_TintColor")) fxMat.SetColor("_TintColor", new Color(0.62f, 0.62f, 0.62f, 1f)); // ×2 셰이더 → 약 1.25배 (Bloom 살짝)
            }
            var fgo = new GameObject("Flames");
            fgo.transform.SetParent(transform, false);
            fgo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 위로 솟음
            flames = fgo.AddComponent<ParticleSystem>();
            flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flames.main; main.loop = true; main.duration = 1f;
            // (이전) 수명 0.5~1.1 / 속도 0.8~2.2 / 크기 0.4~1.1 / 원(Circle) 모양 / 노랑→진빨강
            // (2026-11-22 C3) 수명 0.8~1.2 / 크기 0.3~0.7 / 랜덤 회전 / 원뿔(Cone, 반경 = 화염 반경 그대로) / 노이즈 / 흰→노랑→빨강→검정
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); // (Stretch 길이에 쓰임)
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = -0.15f; // 위로 살짝 떠오름
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            var em = flames.emission; em.rateOverTime = radius * radius * 30f; // 작고 부드러운 입자를 많이 겹쳐 불덩이처럼
            var sh = flames.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = radius; sh.radiusThickness = 1f;
            var col = flames.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.97f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.25f), 0.2f),
                              new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 0.55f), new GradientColorKey(new Color(0.08f, 0.05f, 0.04f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.12f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = flames.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.25f)); // 퍼지며 연기처럼
            var rot = flames.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            var nz = flames.noise; nz.enabled = true; nz.strength = 0.6f; nz.frequency = 1.4f; nz.scrollSpeed = 0.8f; nz.damping = true; // 일렁임
            var r = fgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = fxMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // (2026-11-22 C3) 위로 올라가는 방향으로 늘려 그림 → 둥근 방울이 아니라 불꽃 혀 모양 (2회차: 둥근 방울만 따로따로 보였음)
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.35f;
            r.lengthScale = 1.4f;
            r.sortMode = ParticleSystemSortMode.Distance;
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

        /// <summary>(2026-11-22 C3) 불꽃 스프라이트: 위로 길쭉한 부드러운 방울 + 가장자리 일렁임 (흰색, 색은 파티클 색)</summary>
        static Texture2D FlameTexture()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MolotovFlame" };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    // (1회차) 위로 가는 불꽃 모양 + 가장자리 일렁임 → 윤곽이 또렷해 낙엽처럼 보였음 (Play 확인)
                    // 둥글고 가장자리가 완전히 부드러운 빛망울 (회전해도 티 안 남, 여러 개가 겹쳐 불덩이 모양을 만듦)
                    float d2 = u * u + v * v;
                    float a = Mathf.Exp(-d2 * 3.2f) * Mathf.Clamp01((1f - Mathf.Sqrt(d2)) * 4f);
                    a *= 0.85f + 0.15f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f); // 살짝 얼룩
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
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
                    // (이전) if (h.CompareTag("Player") || (thrower != null && h.gameObject == thrower)) continue; // 자기 불엔 안 다침
                    bool isPlayer = h.CompareTag("Player");
                    if (!isPlayer)
                    {
                        var z = h.GetComponent<ZombieController>();
                        if (z == null || z.IsDead) continue; // 플레이어 말고는 좀비만
                    }
                    Vector3 d = h.transform.position - transform.position; d.y = 0f;
                    if (d.magnitude > radius) continue; // 원기둥 판정 (위·아래로 멀면 구가 잡아도 수평으로 재확인)
                    if (isPlayer)
                    {
                        // 자해 (2026-11-21): 자기가 던진 불도 밟으면 다침. 구르기 무적 중엔 Health가 알아서 무시
                        h.TakeDamage(dps * Tick * PlayerDamageRatio);
                        BurnDoT.Apply(h.gameObject, PlayerIgniteDps, PlayerIgniteDuration);
                        continue;
                    }
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
            // glow.intensity = (2.4f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 1.2f) * k; // (이전)
            glow.intensity = (1.6f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 0.8f) * k;  // (2026-11-22 C3) 평균 2
            if (loop != null) loop.volume = 0.8f * k;
            if (left < FadeTime && flames != null && flames.isEmitting) flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}

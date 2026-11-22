using UnityEngine;

/// <summary>
/// 화염방사기 불길 (2026-11-22). 피드백 "화염방사기 불 이미지를 불꽃 모양이나 불 모양으로 잘 만들어줘"
/// 예전: 0.05초마다 나가는 탄의 주황 궤적 선(TrailRenderer, 폭 0.35) + 총구 플레어 → 불이 아니라 주황 선 다발처럼 보였음
/// 지금: 총구에서 월드 공간 파티클로 분사 (Weapon.Shoot가 한 발마다 Emit 호출, 탄은 데미지·점화 판정만 하고 안 보임)
///  - 불길(Flames): 부드럽고 얼룩진 불 방울을 속도 방향으로 늘려 그림(Stretch) → 불 혀 모양.
///    처음엔 빠르게(18~24m/s) 뿜어지다 감속하며 부풀고(0.3 → 2.2m) 위로 피어오름 + 난류(Noise)로 일렁임.
///    색: 흰빛 노랑(총구) → 주황 → 붉은색 → 검붉게 사라짐. 사거리 약 10m (탄 effectiveRange와 비슷)
///  - 심지(Core): 총구 바로 앞 짧고 밝은 가산 불꽃 (뜨거운 중심)
///  - 연기(Smoke): 불길 끝에서 어두운 연기가 이어서 퍼짐
/// 씬 루트에 하나만 만들어 공유 (무기 교체해도 날아가던 불길은 그대로 사라짐)
/// </summary>
public class FlameStream : MonoBehaviour
{
    static FlameStream instance;
    ParticleSystem flames, core, smoke;

    [Header("불길")]
    // (1차 Play 확인) 18~24m/s · 0.38~0.5초 · 6° · 5개 → 가늘고 듬성듬성, 화면 밖으로 날아감 → 느리고 굵고 많이
    // (2026-11-22 2차) 사거리 10 → 13m, 부채꼴 ±12°에 맞춰 더 멀리·넓게 (이전 13~17m/s · 0.55~0.7초 · 9°)
    public Vector2 speed = new Vector2(16f, 21f);
    public Vector2 lifetime = new Vector2(0.65f, 0.8f);
    public float spreadAngle = 13f;
    public int flamesPerShot = 10;
    public int corePerShot = 2;
    public int smokePerShot = 1;

    /// <summary>총구 위치·발사 방향으로 불길 한 번 분사 (화염방사기 한 발)</summary>
    public static void Emit(Vector3 muzzle, Vector3 dir, Vector3 shooterVelocity)
    {
        if (instance == null) instance = new GameObject("FlameStream").AddComponent<FlameStream>();
        instance.DoEmit(muzzle, dir.normalized, shooterVelocity);
    }

    void Awake()
    {
        var blob = WorldDecorTextures.SoftDot("FX_FlameBlob", true); // 가장자리 부드럽고 얼룩진 방울 (회전·늘림에 자연스러움)
        flames = Build("Flames", blob, "Legacy Shaders/Particles/Alpha Blended", 0.75f, 600);
        core = Build("Core", blob, "Legacy Shaders/Particles/Additive", 0.5f, 200);
        smoke = Build("Smoke", blob, "Legacy Shaders/Particles/Alpha Blended", 0.5f, 200);

        // ── 불길 ──
        {
            var m = flames.main; m.gravityModifier = -0.35f; // 뜨거워서 위로
            var col = flames.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { (0f, new Color(1f, 0.96f, 0.75f)), (0.18f, new Color(1f, 0.78f, 0.25f)), (0.45f, new Color(1f, 0.42f, 0.08f)),
                                     (0.75f, new Color(0.75f, 0.14f, 0.03f)), (1f, new Color(0.2f, 0.06f, 0.03f)) },
                             new[] { (0f, 0.95f), (0.5f, 0.85f), (0.8f, 0.45f), (1f, 0f) });
            var sz = flames.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.6f), new Keyframe(1f, 3.2f)));
            var lv = flames.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 3f; lv.dampen = 0.13f; // 감속 → 끝에서 뭉쳐 부풂
            var nz = flames.noise; nz.enabled = true; nz.strength = 2f; nz.frequency = 1.3f; nz.scrollSpeed = 1.5f; nz.damping = true; nz.quality = ParticleSystemNoiseQuality.Medium;
            var rot = flames.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var r = flames.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.3f; // 빠를 때(총구 쪽) 길쭉한 불 혀
        }
        // ── 심지 ──
        {
            var col = core.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { (0f, new Color(1f, 0.95f, 0.8f)), (0.5f, new Color(1f, 0.7f, 0.25f)), (1f, new Color(1f, 0.35f, 0.05f)) },
                             new[] { (0f, 0.9f), (0.6f, 0.6f), (1f, 0f) });
            var sz = core.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.2f));
            var r = core.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.6f;
        }
        // ── 연기 ──
        {
            var m = smoke.main; m.gravityModifier = -0.15f;
            var col = smoke.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { (0f, new Color(0.25f, 0.2f, 0.18f)), (1f, new Color(0.12f, 0.11f, 0.1f)) },
                             new[] { (0f, 0f), (0.35f, 0f), (0.55f, 0.35f), (1f, 0f) }); // 불길이 사그라질 즈음부터 보임
            var sz = smoke.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 3.2f));
            var lv = smoke.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 1.5f; lv.dampen = 0.15f;
            var nz = smoke.noise; nz.enabled = true; nz.strength = 0.8f; nz.frequency = 0.8f; nz.scrollSpeed = 0.6f; nz.damping = true;
            var rot = smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
        }
    }

    ParticleSystem Build(string name, Texture2D tex, string shader, float tint, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        var em = ps.emission; em.enabled = false; // 직접 Emit만
        var sh = ps.shape; sh.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        var sd = Shader.Find(shader) ?? Shader.Find("Sprites/Default");
        var mat = new Material(sd) { name = "FlameStream_" + name, mainTexture = tex };
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(tint, tint, tint, 1f));
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.YoungestInFront;
        ps.Play();
        return ps;
    }

    static ParticleSystem.MinMaxGradient Grad((float t, Color c)[] cols, (float t, float a)[] alphas)
    {
        var g = new Gradient();
        var ck = new GradientColorKey[cols.Length]; for (int i = 0; i < cols.Length; i++) ck[i] = new GradientColorKey(cols[i].c, cols[i].t);
        var ak = new GradientAlphaKey[alphas.Length]; for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
        g.SetKeys(ck, ak);
        return new ParticleSystem.MinMaxGradient(g);
    }

    void DoEmit(Vector3 muzzle, Vector3 dir, Vector3 shooterVel)
    {
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < flamesPerShot; i++)
        {
            Vector3 d = Quaternion.Euler(Random.Range(-spreadAngle, spreadAngle) * 0.5f, Random.Range(-spreadAngle, spreadAngle), 0f) * dir;
            p.position = muzzle + dir * Random.Range(0f, 0.3f);
            p.velocity = d * Random.Range(speed.x, speed.y) + shooterVel;
            p.startLifetime = Random.Range(lifetime.x, lifetime.y);
            p.startSize = Random.Range(0.45f, 0.75f);
            p.rotation = Random.Range(0f, 360f);
            p.startColor = Color.white;
            flames.Emit(p, 1);
        }
        for (int i = 0; i < corePerShot; i++)
        {
            p.position = muzzle;
            p.velocity = dir * Random.Range(8f, 12f) + shooterVel;
            p.startLifetime = Random.Range(0.08f, 0.13f);
            p.startSize = Random.Range(0.18f, 0.28f);
            p.rotation = Random.Range(0f, 360f);
            core.Emit(p, 1);
        }
        for (int i = 0; i < smokePerShot; i++)
        {
            Vector3 d = Quaternion.Euler(0f, Random.Range(-spreadAngle, spreadAngle), 0f) * dir;
            p.position = muzzle + dir * 0.5f;
            p.velocity = d * Random.Range(12f, 16f) + shooterVel;
            p.startLifetime = Random.Range(0.9f, 1.3f);
            p.startSize = Random.Range(0.6f, 0.9f);
            p.rotation = Random.Range(0f, 360f);
            smoke.Emit(p, 1);
        }
    }
}

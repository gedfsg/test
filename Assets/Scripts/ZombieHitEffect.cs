using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// 좀비 피격 이펙트 - 에셋 없이 코드로만 만든 PUBG풍 피격 피드백
//  1) 피 파티클: 피격 지점에서 빨간 작은 입자가 총알 진행 방향 쪽으로 흩뿌려짐
//  2) 몸 빨간색 플래시 (flashDuration초) - MaterialPropertyBlock이라 머티리얼 원본은 안 건드림
//  3) 작은 넉백: 총알 방향으로 살짝 밀림 (NavMeshAgent.Move라 벽/내비 밖으로는 안 밀림)
//  4) 피격 사운드: 절차적으로 만든 짧은 "퍽" 소리 (나중에 hitClip에 실제 사운드 넣으면 그걸 씀)
// 사용: ZombieHitEffect.Play(좀비 GameObject, 피격 지점, 총알 방향) - Bullet.TryProcessHit에서 호출.
// 좀비에 미리 붙여둘 필요 없음 - 첫 피격 때 자동으로 붙음 (프리팹에 붙여두면 인스펙터로 값 조절 가능).
public class ZombieHitEffect : MonoBehaviour
{
    [Header("피 파티클")]
    public int bloodCount = 14;              // 한 번에 튀는 입자 수
    public float bloodSpeed = 3.5f;          // 입자 초기 속도
    public float bloodSize = 0.08f;          // 입자 크기
    public Color bloodColor = new Color(0.55f, 0.02f, 0.02f);

    [Header("플래시")]
    public float flashDuration = 0.1f;
    public Color flashColor = new Color(1f, 0.15f, 0.15f);

    [Header("넉백")]
    public float knockbackDistance = 0.3f;   // 총 밀리는 거리(m)
    public float knockbackTime = 0.1f;       // 밀리는 데 걸리는 시간

    [Header("사운드 (비우면 절차적 기본음)")]
    public AudioClip hitClip;
    [Range(0f, 1f)] public float hitVolume = 0.6f;

    static Material bloodMaterial;
    static AudioClip defaultClip;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    NavMeshAgent agent;
    Coroutine flashRoutine, knockRoutine;

    // 외부 호출 진입점 - 컴포넌트가 없으면 붙이고 재생
    public static void Play(GameObject zombie, Vector3 hitPoint, Vector3 bulletDir)
    {
        if (zombie == null) return;
        var fx = zombie.GetComponent<ZombieHitEffect>();
        if (fx == null) fx = zombie.AddComponent<ZombieHitEffect>();
        fx.PlayHit(hitPoint, bulletDir);
    }

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        agent = GetComponent<NavMeshAgent>();
    }

    public void PlayHit(Vector3 hitPoint, Vector3 bulletDir)
    {
        Vector3 dir = bulletDir; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward;
        dir.Normalize();

        SpawnBlood(hitPoint, dir);

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(Flash());

        if (knockRoutine != null) StopCoroutine(knockRoutine);
        knockRoutine = StartCoroutine(Knockback(dir));

        // 실제 피격음은 Health(좀비 피격 → AudioManager 살 맞는 소리)가 재생 → 사운드 라이브러리가 있으면 여기선 생략 (이중 재생 방지)
        if (hitClip != null || AudioManager.Library == null)
            AudioSource.PlayClipAtPoint(hitClip != null ? hitClip : GetDefaultClip(), hitPoint, hitVolume);
    }

    // ── 피 파티클 ───────────────────────────────
    void SpawnBlood(Vector3 point, Vector3 dir)
    {
        var go = new GameObject("BloodFX");
        go.transform.position = point;
        go.transform.rotation = Quaternion.LookRotation(dir); // 총알이 뚫고 나가는 방향으로 튐

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 설정 바꾸기 전에 정지 필요

        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(bloodSpeed * 0.4f, bloodSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(bloodSize * 0.5f, bloodSize);
        main.startColor = new ParticleSystem.MinMaxGradient(bloodColor, bloodColor * 0.6f + Color.black * 0.4f);
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy; // 다 끝나면 오브젝트 자동 삭제

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)bloodCount) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.02f;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GetBloodMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ps.Play();
    }

    static Material GetBloodMaterial()
    {
        if (bloodMaterial == null)
        {
            // Sprites/Default는 빌드에 항상 포함되는 내장 셰이더 - 정점 색(startColor)이 그대로 나옴
            bloodMaterial = new Material(Shader.Find("Sprites/Default")) { name = "BloodParticle(Runtime)" };
        }
        return bloodMaterial;
    }

    // ── 플래시 ─────────────────────────────────
    IEnumerator Flash()
    {
        SetFlash(true);
        yield return new WaitForSeconds(flashDuration);
        SetFlash(false);
        flashRoutine = null;
    }

    void SetFlash(bool on)
    {
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (on)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor(ColorId, flashColor);
                r.SetPropertyBlock(mpb);
            }
            else r.SetPropertyBlock(null); // 블록 제거 = 원래 머티리얼 색으로 복귀
        }
    }

    void OnDisable()
    {
        // 플래시 도중 비활성/파괴돼도 빨간색이 남지 않게
        if (renderers != null) SetFlash(false);
    }

    // ── 넉백 ─────────────────────────────────
    IEnumerator Knockback(Vector3 dir)
    {
        float t = 0f;
        while (t < knockbackTime)
        {
            float dt = Time.deltaTime;
            Vector3 delta = dir * (knockbackDistance * dt / knockbackTime);
            // NavMeshAgent.Move는 내비메시 경계(벽)를 넘지 않음. 에이전트가 없거나 꺼져있으면 직접 이동
            // 죽으면 ZombieController가 에이전트를 끄므로 그때는 넉백 생략 (쓰러지는 연출과 충돌 방지)
            if (agent != null && !agent.enabled) break;
            if (agent != null && agent.isOnNavMesh) agent.Move(delta);
            else transform.position += delta;
            t += dt;
            yield return null;
        }
        knockRoutine = null;
    }

    // ── 사운드 ─────────────────────────────────
    // 0.12초짜리 감쇠 노이즈 + 저음 사인 = 둔탁한 "퍽"
    static AudioClip GetDefaultClip()
    {
        if (defaultClip != null) return defaultClip;
        const int rate = 22050;
        int n = (int)(rate * 0.12f);
        var data = new float[n];
        var rng = new System.Random(1234);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Exp(-t * 40f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float thump = Mathf.Sin(2f * Mathf.PI * 90f * t);
            data[i] = env * (noise * 0.5f + thump * 0.7f);
        }
        defaultClip = AudioClip.Create("ZombieHit(Procedural)", n, 1, rate, false);
        defaultClip.SetData(data, 0);
        return defaultClip;
    }
}

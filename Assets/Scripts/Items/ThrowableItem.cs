using System.Collections;
using UnityEngine;

/// <summary>
/// 투척 아이템 월드 오브젝트 동작.
/// PlayerController에서 Instantiate 후 Throw()를 호출한다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ThrowableItem : MonoBehaviour
{
    [HideInInspector] public ThrowableData data;

    private Rigidbody rb;
    private bool triggered = false;

    void Awake() => rb = GetComponent<Rigidbody>();

    /// <summary>던질 방향과 힘을 설정하고 퓨즈를 점화한다.</summary>
    public void Throw(Vector3 direction, float force)
    {
        rb.AddForce(direction * force, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.Impulse);

        if (data.throwableType == ThrowableType.Grenade)
            StartCoroutine(GrenadeRoutine());
        // 화염병·섬광탄은 충돌 시 발동 (OnCollisionEnter)
    }

    void OnCollisionEnter(Collision collision)
    {
        if (triggered || data == null) return;

        switch (data.throwableType)
        {
            case ThrowableType.Molotov:
                triggered = true;
                StartCoroutine(MolotovRoutine());
                break;
            case ThrowableType.Flashbang:
                triggered = true;
                FlashbangExplode();
                break;
        }
    }

    // ── 수류탄 ──────────────────────────────────────

    private IEnumerator GrenadeRoutine()
    {
        yield return new WaitForSeconds(data.fuseTime);
        if (triggered) yield break;
        triggered = true;
        GrenadeExplode();
    }

    private void GrenadeExplode()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, data.radius);
        foreach (var col in hits)
        {
            Health h = col.GetComponent<Health>() ?? col.GetComponentInParent<Health>();
            if (h != null)
            {
                float dist    = Vector3.Distance(transform.position, col.transform.position);
                float falloff = 1f - Mathf.Clamp01(dist / data.radius);
                h.TakeDamage(data.damage * falloff);
            }
        }
        Debug.Log($"[Grenade] 폭발! 반경 {data.radius}m, 데미지 {data.damage}");
        Destroy(gameObject);
    }

    // ── 화염병 ──────────────────────────────────────

    private IEnumerator MolotovRoutine()
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        rb.isKinematic = true;

        float elapsed = 0f;
        float nextTick = 0f;
        float tickInterval = 0.5f;

        Debug.Log($"[Molotov] 불길 시작! {data.fireDuration}초 지속");

        while (elapsed < data.fireDuration)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= nextTick)
            {
                nextTick += tickInterval;
                Collider[] hits = Physics.OverlapSphere(transform.position, data.radius);
                foreach (var col in hits)
                {
                    Health h = col.GetComponent<Health>() ?? col.GetComponentInParent<Health>();
                    if (h != null) h.TakeDamage(data.damage * tickInterval);
                }
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    // ── 섬광탄 ──────────────────────────────────────

    private void FlashbangExplode()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, data.flashRadius);
        foreach (var col in hits)
        {
            ZombieController zombie = col.GetComponent<ZombieController>()
                                  ?? col.GetComponentInParent<ZombieController>();
            if (zombie != null) zombie.Stun(data.stunDuration);
        }
        Debug.Log($"[Flashbang] 섬광! 반경 {data.flashRadius}m, 기절 {data.stunDuration}초");
        Destroy(gameObject);
    }
}
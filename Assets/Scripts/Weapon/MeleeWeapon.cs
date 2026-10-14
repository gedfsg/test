using UnityEngine;
using System.Collections;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Melee Settings")]
    public Transform attackPoint;
    public float attackRange = 1.5f;
    public string shooterTag;
    public Transform slashPivot;
    [Tooltip("예전 가짜 휘두르기 궤적(slashPivot을 180° 돌리는 하얀 트레일). 실제 휘두르기 애니메이션이 생겨서 경로가 안 맞아 기본 끔")]
    public bool useSlashTrail = false;
    [Tooltip("클릭 후 데미지가 들어가기까지(초). 내려찍기 애니에서 도끼가 내려와 맞는 순간(Play 실측 약 0.35초)에 맞춤. 0이면 예전처럼 클릭 즉시")]
    public float hitDelay = 0.35f;

    [Header("판정: 플레이어 앞 부채꼴")]
    [Tooltip("켜면 플레이어 위치에서 바라보는 방향 부채꼴로 판정 (여러 마리 동시에). 끄면 예전 방식(attackPoint 중심 구, 앞쪽 반원)")]
    public bool useFanHit = true;
    [Tooltip("부채꼴 반경(수평 m, 플레이어 중심 → 대상 몸 표면). WeaponData.effectiveRange가 있으면 그 값을 씀. 좀비 공격 범위(중심~중심 1.8m, 뚱뚱이 2.5m)보다 길게")]
    public float fanRange = 2.9f;
    [Tooltip("부채꼴 전체 각도 (90 = 정면 좌우 45°씩)")]
    public float fanAngle = 90f;
    [Tooltip("가까운 거리(몸 표면까지 이 반경 안)는 더 넓은 각도로 판정. 좀비가 옆으로 붙으면(실측 64°, 중심 1.8m) 90° 부채꼴로는 못 때리고 일방적으로 맞기만 했음")]
    public float closeRange = 2.0f;
    [Tooltip("가까운 거리 판정 전체 각도 (160 = 좌우 80°씩)")]
    public float closeAngle = 160f;

    // 추가된 데이터 연동 변수임.
    [Header("Weapon Data")]
    public WeaponData weaponData;

    private float lastAttackTime;

    /// <summary>쿨타임이 지나서 지금 휘두를 수 있는지 (스테미나를 쓰기 전에 확인용)</summary>
    public bool IsReady => Time.time >= lastAttackTime + ((weaponData != null) ? weaponData.attackRate : 0.5f);

    /// <summary>쿨타임이 지났으면 공격하고 true (PlayerController가 휘두르기 애니메이션 트리거)</summary>
    public bool TryAttack()
    {
        // 무기 데이터의 공격 주기를 참조함.
        float currentAttackRate = (weaponData != null) ? weaponData.attackRate : 0.5f;

        if (Time.time >= lastAttackTime + currentAttackRate)
        {
            // Attack();   // (이전) 클릭 즉시 판정 → 도끼가 아직 머리 위인데 맞아서 타격감 없음
            if (hitDelay > 0f && isActiveAndEnabled) StartCoroutine(AttackAfterDelay(hitDelay));
            else Attack();
            lastAttackTime = Time.time;
            return true;
        }
        return false;
    }

    private IEnumerator AttackAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Attack();
    }

    private void Attack()
    {
        if (useSlashTrail && slashPivot != null)
        {
            StartCoroutine(SwingEffect());
        }

        if (useFanHit) { FanAttack(); return; }

        Collider[] hitColliders = Physics.OverlapSphere(attackPoint.position, attackRange);
        
        // 무기 데이터의 타격 데미지를 참조함.
        float currentDamage = (weaponData != null) ? weaponData.damage : 50f;

        foreach (Collider hit in hitColliders)
        {
            if (hit.CompareTag(shooterTag)) continue;

            Vector3 directionToTarget = (hit.transform.position - attackPoint.position).normalized;
            directionToTarget.y = 0f;

            Vector3 forwardDirection = attackPoint.forward;
            forwardDirection.y = 0f;

            if (Vector3.Angle(forwardDirection, directionToTarget) > 90f) continue;

            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth == null) targetHealth = hit.GetComponentInParent<Health>();
            if (targetHealth == null) targetHealth = hit.GetComponentInChildren<Health>();
            if (targetHealth != null) targetHealth.TakeDamage(currentDamage);
            else
            {
                ZombieController zc = hit.GetComponent<ZombieController>();
                if (zc == null) zc = hit.GetComponentInParent<ZombieController>();
                if (zc != null) zc.TakeDamage(currentDamage);
            }

            DestructibleObstacle obstacle = hit.GetComponent<DestructibleObstacle>();
            if (obstacle == null) obstacle = hit.GetComponentInParent<DestructibleObstacle>();
            if (obstacle != null) obstacle.TakeDamage(currentDamage);
        }
    }

    /// <summary>
    /// 부채꼴 판정: 플레이어(attackPoint의 루트) 위치에서 바라보는 방향 ±fanAngle/2, 수평 거리 fanRange 안의 대상 전부.
    /// 거리는 대상 몸(콜라이더 표면)까지. 한 대상은 한 번만 맞음
    /// (예전: attackPoint가 플레이어 머리 위 2m라 실제 사거리 약 1.7m → 좀비 공격 범위 1.8m 안에서 싸워야 했음)
    /// </summary>
    private void FanAttack()
    {
        Transform owner = attackPoint != null ? attackPoint.root : transform.root;
        Vector3 origin = owner.position;
        Vector3 forward = owner.forward; forward.y = 0f; forward.Normalize();
        float range = (weaponData != null && weaponData.effectiveRange > 0f) ? weaponData.effectiveRange : fanRange;
        float currentDamage = (weaponData != null) ? weaponData.damage : 50f;

        var done = new System.Collections.Generic.HashSet<Object>();
        foreach (Collider hit in Physics.OverlapSphere(origin + Vector3.up, range + 1f))
        {
            if (hit.CompareTag(shooterTag) || hit.transform.IsChildOf(owner)) continue;

            Health targetHealth = hit.GetComponentInParent<Health>();
            if (targetHealth == null) targetHealth = hit.GetComponentInChildren<Health>();
            ZombieController zombie = hit.GetComponentInParent<ZombieController>();

            // 거리: 도끼가 몸에 닿는 거리 = 플레이어 중심 → 대상 콜라이더 표면(가장 가까운 점)까지 수평 거리
            //  (좀비 콜라이더 반경 0.8m(뚱뚱이 1.0m)라 2.9m면 좀비 중심 기준 약 3.7m. 좀비는 중심~중심 1.8m에서 때림)
            //  예전 판정(머리 위 2m 높이 구, 반경 2m)도 실측으로 좀비 중심 3.6m까지 맞았음 → 사거리는 비슷하게 유지, 판정을 명확한 부채꼴로
            Transform body = zombie != null ? zombie.transform : (targetHealth != null ? targetHealth.transform : null);
            Vector3 to = hit.ClosestPoint(origin + Vector3.up) - origin; to.y = 0f;
            if (to.magnitude > range) continue;
            Vector3 toCenter = (body != null ? body.position : hit.bounds.center) - origin; toCenter.y = 0f;
            float allowedAngle = to.magnitude <= closeRange ? Mathf.Max(fanAngle, closeAngle) : fanAngle;
            if (toCenter.sqrMagnitude > 0.01f && Vector3.Angle(forward, toCenter) > allowedAngle * 0.5f) continue;

            if (targetHealth != null) { if (done.Add(targetHealth)) targetHealth.TakeDamage(currentDamage); }
            else
            {
                if (zombie != null && done.Add(zombie)) zombie.TakeDamage(currentDamage);
            }

            DestructibleObstacle obstacle = hit.GetComponentInParent<DestructibleObstacle>();
            if (obstacle != null && done.Add(obstacle)) obstacle.TakeDamage(currentDamage);
        }
    }

    // 중심축을 180도 회전시켜 트레일 렌더러의 궤적을 생성하는 코루틴임.
    private IEnumerator SwingEffect()
    {
        TrailRenderer trail = slashPivot.GetComponentInChildren<TrailRenderer>();
        if (trail != null)
        {
            trail.Clear(); 
            trail.emitting = true; 
        }

        float duration = 0.15f;
        float elapsed = 0f;
        
        Quaternion startRotation = Quaternion.Euler(0, -90f, 0);
        Quaternion endRotation = Quaternion.Euler(0, 90f, 0);

        slashPivot.localRotation = startRotation;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            slashPivot.localRotation = Quaternion.Slerp(startRotation, endRotation, t);
            yield return null;
        }

        slashPivot.localRotation = Quaternion.identity;

        if (trail != null)
        {
            trail.emitting = false; 
        }
    }

    void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }

    // 외부에서 새로운 무기 데이터를 덮어씌울 때 호출하는 함수
    public void ChangeWeaponData(WeaponData newData)
    {
        weaponData = newData;
        if (newData.effectiveRange > 0f) attackRange = newData.effectiveRange; // 근접 사거리 (도끼 2m)
        Debug.Log("근접 무기가 [" + newData.itemName + "]으로 교체됨!");
    }
}
using UnityEngine;
using System.Collections;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Melee Settings")]
    public Transform attackPoint;
    public float attackRange = 1.5f;
    public string shooterTag;
    public Transform slashPivot;

    // 추가된 데이터 연동 변수임.
    [Header("Weapon Data")]
    public WeaponData weaponData;

    private float lastAttackTime;

    public void TryAttack()
    {
        // 무기 데이터의 공격 주기를 참조함.
        float currentAttackRate = (weaponData != null) ? weaponData.attackRate : 0.5f;

        if (Time.time >= lastAttackTime + currentAttackRate)
        {
            Attack();
            lastAttackTime = Time.time;
        }
    }

    private void Attack()
    {
        if (slashPivot != null)
        {
            StartCoroutine(SwingEffect());
        }

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
            if (targetHealth != null) targetHealth.TakeDamage(currentDamage);

            DestructibleObstacle obstacle = hit.GetComponent<DestructibleObstacle>();
            if (obstacle != null) obstacle.TakeDamage(currentDamage);
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
}
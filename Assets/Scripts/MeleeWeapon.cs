using UnityEngine;
using System.Collections;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Melee Settings")]
    public Transform attackPoint;
    public float attackRange = 1.5f;
    public float meleeDamage = 50f;
    public float attackRate = 0.5f;
    public string shooterTag;

    // 시각 효과를 위해 회전시킬 중심축 오브젝트임.
    public Transform slashPivot;

    private float lastAttackTime;

    public void TryAttack()
    {
        if (Time.time >= lastAttackTime + attackRate)
        {
            Attack();
            lastAttackTime = Time.time;
        }
    }

    private void Attack()
    {
        // 시각 효과를 위한 코루틴을 실행함.
        if (slashPivot != null)
        {
            StartCoroutine(SwingEffect());
        }

        Collider[] hitColliders = Physics.OverlapSphere(attackPoint.position, attackRange);

        foreach (Collider hit in hitColliders)
        {
            if (hit.CompareTag(shooterTag)) continue;

            Vector3 directionToTarget = (hit.transform.position - attackPoint.position).normalized;
            directionToTarget.y = 0f;

            Vector3 forwardDirection = attackPoint.forward;
            forwardDirection.y = 0f;

            if (Vector3.Angle(forwardDirection, directionToTarget) > 90f) continue;

            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null) targetHealth.TakeDamage(meleeDamage);

            DestructibleObstacle obstacle = hit.GetComponent<DestructibleObstacle>();
            if (obstacle != null) obstacle.TakeDamage(meleeDamage);
        }
    }

    // 중심축을 180도 회전시켜 트레일 렌더러의 궤적을 생성하는 코루틴임.
    private IEnumerator SwingEffect()
    {
        TrailRenderer trail = slashPivot.GetComponentInChildren<TrailRenderer>();
        if (trail != null)
        {
            trail.Clear(); // 이전 궤적 데이터를 삭제함.
            trail.emitting = true; // 궤적 생성을 활성화함.
        }

        float duration = 0.15f;
        float elapsed = 0f;
        
        // Y축 기준 -90도에서 90도로 회전하도록 목표 각도를 설정함.
        Quaternion startRotation = Quaternion.Euler(0, -90f, 0);
        Quaternion endRotation = Quaternion.Euler(0, 90f, 0);

        slashPivot.localRotation = startRotation;

        // 지정된 시간(duration) 동안 두 각도 사이를 보간(Slerp)하여 회전시킴.
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            slashPivot.localRotation = Quaternion.Slerp(startRotation, endRotation, t);
            yield return null;
        }

        // 회전 완료 후 중심축의 각도를 기본값으로 초기화함.
        slashPivot.localRotation = Quaternion.identity;

        if (trail != null)
        {
            trail.emitting = false; // 궤적 생성을 비활성화함.
        }
    }

    void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}
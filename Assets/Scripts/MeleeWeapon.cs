using UnityEngine;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Weapon Settings")]
    public Transform attackPoint;
    public float attackRange = 1.5f;
    public float meleeDamage = 50f;
    public float attackRate = 0.5f;
    public string shooterTag;
    public ParticleSystem slashEffect;

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
        if (slashEffect != null)
        {
            slashEffect.Play();
        }

        // attackPoint 위치를 중심으로 반경 내의 모든 콜라이더를 1차적으로 탐색함.
        Collider[] hitColliders = Physics.OverlapSphere(attackPoint.position, attackRange);

        foreach (Collider hit in hitColliders)
        {
            // 공격 주체(나)와 동일한 태그를 가진 오브젝트는 충돌 처리에서 제외함.
            if (hit.CompareTag(shooterTag)) 
            {
                continue;
            }

            // 타겟을 향하는 방향 벡터를 계산함. (평면 기준 판정을 위해 Y축 값을 0으로 일치시킴)
            Vector3 directionToTarget = (hit.transform.position - attackPoint.position).normalized;
            directionToTarget.y = 0f;
            
            // 무기(캐릭터)의 정면 방향 벡터를 가져옴.
            Vector3 forwardDirection = attackPoint.forward;
            forwardDirection.y = 0f;

            // 정면 방향과 타겟 방향 사이의 절대 각도를 계산함.
            // 계산된 각도가 90도를 초과할 경우(정면 180도 밖일 경우) 타격 판정을 무시함.
            if (Vector3.Angle(forwardDirection, directionToTarget) > 90f)
            {
                continue;
            }

            // 적중한 오브젝트의 Health 컴포넌트를 탐색하여 데미지를 전달함.
            Health targetHealth = hit.GetComponent<Health>();
            if (targetHealth != null)
            {
                targetHealth.TakeDamage(meleeDamage);
            }

            // 적중한 오브젝트의 파괴 가능 장애물 컴포넌트를 탐색하여 데미지를 전달함.
            DestructibleObstacle obstacle = hit.GetComponent<DestructibleObstacle>();
            if (obstacle != null)
            {
                obstacle.TakeDamage(meleeDamage);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (attackPoint == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}

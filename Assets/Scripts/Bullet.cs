using UnityEngine;
using UnityEngine.InputSystem;

public class Bullet : MonoBehaviour
{
    public float speed = 50f;
    public float damage = 20f;
    public string shooterTag;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.linearVelocity = transform.forward * speed;

        Destroy(gameObject, 0.5f);
    }

    void OnTriggerEnter(Collider other)
    {
        // 발사자 객체와의 충돌을 무시하고 함수를 종료함.
        if (other.CompareTag(shooterTag))
        {
            return;
        }

        // 높은 장애물 객체와 충돌 시 총알 객체를 파괴하고 함수를 종료함.
        if (other.CompareTag("HighObstacle"))
        {
            Destroy(gameObject);
            return;
        }

        // 낮은 장애물 객체와 충돌 시 데미지를 전달하지 않음.
        // 총알 객체를 파괴하지 않고 관통 처리함.
        if (other.CompareTag("LowObstacle"))
        {
            return; 
        }

        // 대상 객체의 Health 컴포넌트를 확인하여 데미지를 전달함.
        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(damage);
        }
        
        // 대상이 발사자나 낮은 장애물이 아닌 경우 총알 객체를 파괴함.
        Destroy(gameObject);
    }
}

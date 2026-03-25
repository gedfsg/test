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
        if (other.CompareTag(shooterTag))
        {
            return;
        }

        Health targetHealth = other.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(damage);
        }
        Destroy(gameObject);
    }
}

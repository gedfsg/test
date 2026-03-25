using UnityEngine;
using System.Collections;

public class Locomotion : MonoBehaviour
{
    [Header("Movement Stats")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float rollSpeed = 15f;
    public float rollDuration = 0.2f;

    [Header("Stamina Stats")]
    public float maxStamina = 100f;
    public float sprintCost = 25f;
    public float rollCost = 30f;
    public float staminaRegen = 15f;

    private float currentStamina;
    private bool isSprinting = false;
    private bool isRolling = false;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        currentStamina = maxStamina;
    }
    public void Move(Vector3 direction)
    {
        if (isRolling) return;

        // 달리기 조건: 달리기 버튼이 눌려 있고, 움직이고 있으며, 스테미나가 있을 때
        float targetSpeed = (isSprinting && direction.magnitude > 0.1f && currentStamina > 0) ? sprintSpeed : walkSpeed;
        
        rb.linearVelocity = direction.normalized * targetSpeed;

        // 스테미나 소모 로직
        if (isSprinting && direction.magnitude > 0.1f)
        {
            currentStamina -= sprintCost * Time.deltaTime;
            if (currentStamina <= 0) isSprinting = false;
        }
    }

    public void TryRoll(Vector3 direction)
    {
        if (!isRolling && currentStamina >= rollCost && direction.magnitude > 0.1f)
        {
            StartCoroutine(RollRoutine(direction));
        }
    }

    private IEnumerator RollRoutine(Vector3 direction)
    {
        isRolling = true;
        currentStamina -= rollCost;

        Vector3 rollDir = direction.normalized;
        float startTime = Time.time;

        while (Time.time < startTime + rollDuration)
        {
            rb.linearVelocity = rollDir * rollSpeed;
            yield return null;
        }

        isRolling = false;
    }

    void Update()
    {
        if (!isSprinting && !isRolling && currentStamina < maxStamina)
        {
            currentStamina += staminaRegen * Time.deltaTime;
        }
        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
    }

    public void SetSprinting(bool state) => isSprinting = state;
    public float GetStaminaNormalized() => currentStamina / maxStamina;
}
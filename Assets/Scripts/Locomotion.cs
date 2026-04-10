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
    private Animator anim; // [추가] 애니메이터 변수

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // 자식 오브젝트(male01_1 등)에 있는 Animator를 가져옴
        anim = GetComponentInChildren<Animator>(); 
        currentStamina = maxStamina;
    }

    public void Move(Vector3 direction)
    {
        if (isRolling) return;

        // 1. 실제 물리 이동 속도 계산
        float targetSpeed = (isSprinting && direction.magnitude > 0.1f && currentStamina > 0) ? sprintSpeed : walkSpeed;
        rb.linearVelocity = direction.normalized * targetSpeed;

        // 2. [핵심] 애니메이션 파라미터 업데이트
        if (anim != null)
        {
            // 월드 좌표계의 이동 방향을 캐릭터의 로컬 좌표계로 변환해.
            // 이걸 해야 마우스를 보고 있을 때 옆걸음(Horizontal)인지 앞걸음(Vertical)인지 알아내거든.
            Vector3 localMove = transform.InverseTransformDirection(direction);

            // 걷기(1/-1)와 달리기(2/-2)를 구분하기 위한 배율이야.
            float multiplier = (isSprinting && currentStamina > 0) ? 2f : 1f;

            // 블렌드 트리에 만든 파라미터 이름이랑 똑같이 맞춰야 해! (대소문자 주의)
            // 0.1f는 댐핑 값으로, 모션 전환을 부드럽게 만들어줘.
            anim.SetFloat("Horizontal", localMove.x * multiplier, 0.1f, Time.deltaTime);
            anim.SetFloat("Vertical", localMove.z * multiplier, 0.1f, Time.deltaTime);
        }

        // 3. 스테미나 소모 로직
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
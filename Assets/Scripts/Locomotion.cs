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

    [Header("스텝업 (낮은 턱/계단 자동으로 올라감 - L4D/좀보이드 스타일)")]
    public float maxStepHeight = 1.2f;      // 이보다 낮은 턱만 자동으로 올라감
    public float stepCheckDistance = 0.4f;  // 앞으로 이 거리 안에 막힌 게 있는지 감지
    public float stepSmoothSpeed = 6f;      // 초당 밀어올리는 속도(부드럽게)

    private float currentStamina;
    private bool isSprinting = false;
    private bool isRolling = false;
    private Rigidbody rb;
    private Animator anim; // [추가] 애니메이터 변수
    private CapsuleCollider capsule;
    private float stepUpAccumulated; // 이번 장애물에서 지금까지 밀어올린 누적 높이

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        // 자식 오브젝트(male01_1 등)에 있는 Animator를 가져옴
        anim = GetComponentInChildren<Animator>();
        currentStamina = maxStamina;
    }

    public void Move(Vector3 direction)
    {
        if (isRolling) return;

        TryStepUp(direction);

        // 1. 실제 물리 이동 속도 계산
        float targetSpeed = (isSprinting && direction.magnitude > 0.1f && currentStamina > 0) ? sprintSpeed : walkSpeed;
        float yVel = rb.linearVelocity.y; // 중력(Y속도) 보존
        rb.linearVelocity = direction.normalized * targetSpeed + Vector3.up * yVel;

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

    // 진행 방향 발밑 높이에서 막혀있는데, maxStepHeight 높이에서는 안 막혀있으면
    // "낮은 턱"으로 보고 그 프레임만큼 살짝 밀어올림 - 여러 프레임 누적되며 자연스럽게 올라감.
    // 벽은 maxStepHeight 높이에서도 막혀있어서 그대로 안 올라감(유지).
    //
    // 안전장치 2단:
    //  1) stepUpAccumulated로 "이번 장애물에서 총 몇 유닛 밀어올렸는지" 누적 추적 -
    //     maxStepHeight만큼 이미 다 올렸는데도 계속 막혀있으면 더 이상 절대 안 올림.
    //     레이 판정이 어떤 이유로 몇 프레임 잘못돼도 실제로 밀려 올라가는 높이는
    //     물리적으로 maxStepHeight를 못 넘음 - 이게 "벽을 뚫고 올라가는" 문제의 근본 차단.
    //  2) maxStepHeight 레이 하나만 믿지 않고, 사람 키 정도(1.8) 높이에서도 한 번 더
    //     체크(safetyOrigin) - 진짜 벽이면 이 레이도 거의 확실히 맞음.
    // 턱의 앞면(세로 면)이 없고 바닥판 모서리만 있는 경우(외부 모델의 높은 바닥 등):
    // 앞으로 쏘는 레이에는 아무것도 안 걸리는데 캐릭터는 모서리에 걸려 멈춤.
    // → 발 바로 앞 바닥 높이를 위에서 아래로 재서, 0.15m ~ maxStepHeight 만큼 높으면 턱으로 봄
    bool LedgeAhead(Vector3 origin, Vector3 dir)
    {
        float feet = rb.position.y + (capsule != null ? capsule.center.y - capsule.radius : -0.1f);
        Vector3 top = origin + dir * 0.1f;
        top.y = feet + maxStepHeight + 0.05f;
        if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, maxStepHeight + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        float rise = hit.point.y - feet;
        if (rise <= 0.15f || rise > maxStepHeight || hit.normal.y < 0.7f) return false; // 평평한 윗면만

        // 0.5m 더 앞에서도 비슷한 높이여야 '올라설 바닥'. 울타리 가로대처럼 얇은 건 그 너머가 다시 낮아서 제외
        Vector3 further = top + dir * 0.5f;
        if (!Physics.Raycast(further, Vector3.down, out RaycastHit hit2, maxStepHeight + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        return Mathf.Abs(hit2.point.y - hit.point.y) < 0.15f;
    }

    void TryStepUp(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f) { stepUpAccumulated = 0f; return; }

        float radius = capsule != null ? capsule.radius : 0.5f;
        Vector3 dir = direction.normalized;
        Vector3 origin = rb.position + dir * (radius + 0.05f);

        Vector3 lowOrigin = origin + Vector3.up * 0.05f; // 발밑 바로 위
        if (!Physics.Raycast(lowOrigin, dir, stepCheckDistance, ~0, QueryTriggerInteraction.Ignore)
            && !LedgeAhead(origin, dir))
        {
            stepUpAccumulated = 0f; // 앞이 안 막혀있으면 리셋(스텝업 필요 없음)
            return;
        }

        if (stepUpAccumulated >= maxStepHeight) return; // 이미 최대치만큼 올렸는데 아직도 막힘 = 진짜 벽

        Vector3 highOrigin = origin + Vector3.up * maxStepHeight;
        if (Physics.Raycast(highOrigin, dir, stepCheckDistance, ~0, QueryTriggerInteraction.Ignore))
            return; // maxStepHeight 높이에서 막혀있으면 진짜 벽 - 그대로 막힘

        Vector3 safetyOrigin = origin + Vector3.up * 1.8f; // 사람 키 높이 2차 확인
        if (Physics.Raycast(safetyOrigin, dir, stepCheckDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        float rise = Mathf.Min(stepSmoothSpeed * Time.fixedDeltaTime, maxStepHeight - stepUpAccumulated);
        rb.position += Vector3.up * rise;
        stepUpAccumulated += rise;
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
        float speed = rollSpeed * WeatherManager.RollDistanceMultiplier; // 비: 젖은 바닥에서 미끄러져 더 멀리 (같은 시간 × 빠른 속도 = 1.3배 거리)

        while (Time.time < startTime + rollDuration)
        {
            rb.linearVelocity = rollDir * speed;
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
    public void RecoverStamina(float amount) => currentStamina = Mathf.Min(currentStamina + amount, maxStamina);
    public float GetStaminaNormalized() => currentStamina / maxStamina;

    /// <summary>스테미나가 amount 이상이면 쓰고 true, 모자라면 안 쓰고 false (근접 공격용)</summary>
    public bool TryUseStamina(float amount)
    {
        if (amount <= 0f) return true;
        if (currentStamina < amount) return false;
        currentStamina -= amount;
        return true;
    }
}
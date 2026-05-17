using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 버프 상태를 관리한다.
/// InventoryManager.UseConsumable() 에서 이 컴포넌트를 통해 버프를 적용한다.
/// 플레이어 GameObject에 붙여두면 된다.
/// </summary>
[RequireComponent(typeof(Locomotion))]
public class PlayerBuffManager : MonoBehaviour
{
    private Locomotion locomotion;
    private Weapon rangedWeapon;

    private Coroutine speedBuffCoroutine;
    private Coroutine healOverTimeCoroutine;

    // 기본 스탯 (버프 종료 후 복원용)
    private float baseWalkSpeed;
    private float baseSprintSpeed;
    private float baseAttackRate;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        baseWalkSpeed = locomotion.walkSpeed;
        baseSprintSpeed = locomotion.sprintSpeed;
    }

    void Start()
    {
        var pc = GetComponent<PlayerController>();
        if (pc != null && pc.rangedWeaponObject != null)
        {
            rangedWeapon = pc.rangedWeaponObject.GetComponent<Weapon>();
            if (rangedWeapon != null && rangedWeapon.weaponData != null)
                baseAttackRate = rangedWeapon.weaponData.attackRate;
        }
    }

    /// <summary>소비품 데이터를 받아 해당 효과를 적용한다.</summary>
    public void ApplyConsumable(ConsumableData data, Health health)
    {
        // 1. 즉시 회복
        if (data.healAmount > 0f)
            health.Heal(data.healAmount);

        // 2. 지속 회복 (진통제 등)
        if (data.healPerSecond > 0f && data.healDuration > 0f)
        {
            if (healOverTimeCoroutine != null) StopCoroutine(healOverTimeCoroutine);
            healOverTimeCoroutine = StartCoroutine(HealOverTime(health, data.healPerSecond, data.healDuration));
        }

        // 3. 스태미나 즉시 회복
        if (data.staminaAmount > 0f)
            locomotion.RecoverStamina(data.staminaAmount);

        // 4. 버프
        if (data.buffType != BuffType.None && data.buffDuration > 0f)
        {
            if (speedBuffCoroutine != null) StopCoroutine(speedBuffCoroutine);
            speedBuffCoroutine = StartCoroutine(BuffRoutine(data));
        }
    }

    private IEnumerator HealOverTime(Health health, float perSec, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            health.Heal(perSec * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator BuffRoutine(ConsumableData data)
    {
        locomotion.walkSpeed = baseWalkSpeed * data.speedMultiplier;
        locomotion.sprintSpeed = baseSprintSpeed * data.speedMultiplier;

        if (data.buffType == BuffType.Adrenaline && rangedWeapon?.weaponData != null)
            rangedWeapon.weaponData.attackRate = baseAttackRate / data.attackRateMultiplier;

        yield return new WaitForSeconds(data.buffDuration);

        locomotion.walkSpeed = baseWalkSpeed;
        locomotion.sprintSpeed = baseSprintSpeed;

        if (data.buffType == BuffType.Adrenaline && rangedWeapon?.weaponData != null)
            rangedWeapon.weaponData.attackRate = baseAttackRate;

        Debug.Log($"[Buff] {data.buffType} 버프 종료");
    }
}
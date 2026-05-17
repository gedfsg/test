using UnityEngine;

public enum BuffType
{
    None,
    SpeedBoost,      // 이동속도 증가
    Adrenaline,      // 이동속도 + 공격속도 대폭 증가 (단기)
    StaminaRegen,    // 스태미나 즉시 회복
}

/// <summary>
/// 소비 아이템 데이터
/// - 즉시 회복 (붕대, 응급 키트)
/// - 시간 회복 (진통제)
/// - 버프 (에너지 드링크, 아드레날린)
/// </summary>
[CreateAssetMenu(fileName = "New Consumable Data", menuName = "Inventory/Consumable Data")]
public class ConsumableData : ItemData
{
    [Header("Heal Settings")]
    [Tooltip("즉시 회복량 (0이면 즉시 회복 없음)")]
    public float healAmount = 0f;

    [Tooltip("초당 회복량 (진통제 등 지속 회복)")]
    public float healPerSecond = 0f;

    [Tooltip("지속 회복 지속 시간 (초)")]
    public float healDuration = 0f;

    [Header("Stamina Settings")]
    [Tooltip("즉시 회복할 스태미나 양 (0이면 없음)")]
    public float staminaAmount = 0f;

    [Header("Buff Settings")]
    public BuffType buffType = BuffType.None;

    [Tooltip("버프 지속 시간 (초)")]
    public float buffDuration = 0f;

    [Tooltip("이동속도 배율 (예: 1.3 = 30% 증가)")]
    public float speedMultiplier = 1f;

    [Tooltip("공격속도 배율 (예: 1.5 = 50% 증가) — Adrenaline 전용")]
    public float attackRateMultiplier = 1f;
}
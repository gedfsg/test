using UnityEngine;

public enum ThrowableType { Grenade, Molotov, Flashbang }

/// <summary>
/// 투척 아이템 데이터 (수류탄 / 화염병 / 섬광탄)
/// </summary>
[CreateAssetMenu(fileName = "New Throwable Data", menuName = "Inventory/Throwable Data")]
public class ThrowableData : ItemData
{
    [Header("Throwable Settings")]
    public ThrowableType throwableType;

    [Tooltip("투척 프리팹 (ThrowableItem 컴포넌트 포함)")]
    public GameObject throwablePrefab;

    [Header("Grenade / Molotov")]
    [Tooltip("폭발/화염 범위 (반경, 미터)")]
    public float radius = 5f;

    [Tooltip("폭발 데미지 (수류탄) / 초당 화염 데미지 (화염병)")]
    public float damage = 80f;

    [Tooltip("투척 후 폭발까지 딜레이 (수류탄)")]
    public float fuseTime = 2.5f;

    [Header("Molotov Only")]
    [Tooltip("불길이 지속되는 시간 (초)")]
    public float fireDuration = 5f;

    [Header("Flashbang Only")]
    [Tooltip("기절 지속 시간 (초)")]
    public float stunDuration = 3f;

    [Tooltip("섬광 범위 (반경, 미터)")]
    public float flashRadius = 8f;
}
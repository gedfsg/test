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

    [Header("2026-11-16 투척 시스템 (ThrowableController / Grenade / Molotov)")]
    [Tooltip("수류탄: 이 반경 안은 최대 데미지(damage), 바깥 radius까지 선형 감소해 0")]
    public float innerRadius = 3f;
    [Tooltip("터질 때 좀비를 부르는 소음 반경 (m)")]
    public float noiseRadius = 100f;
    [Tooltip("화염병: 불 지대를 벗어나도 남는 점화 (초당 데미지 / 초)")]
    public float igniteDps = 10f;
    public float igniteDuration = 3f;
    [Tooltip("조준 궤적 끝 위험 범위 원 색 (수류탄 빨강, 화염병 주황)")]
    public Color rangeColor = Color.red;

    [Header("소리 (Assets/Audio/throwable)")]
    public AudioClip pinSound;      // 수류탄 핀 뽑기 (던지기 시작)
    public AudioClip throwSound;    // 던지기
    public AudioClip explodeSound;  // 수류탄 폭발
    public AudioClip breakSound;    // 화염병 유리 깨짐
    public AudioClip igniteSound;   // 화염병 불 붙음
    public AudioClip loopSound;     // 화염병 불 지속
}
using UnityEngine;

/// <summary>
/// 좀비 종류 정의. 일반 / 달리기 / 탱커를 이 에셋 하나로 가른다.
///
/// 만들기: Project 창 우클릭 → Create → Zombie → Zombie Profile
///         (또는 메뉴 Tools → Zombie → 기본 프로필 3종 생성)
/// 쓰기:   좀비 프리팹의 ZombieController 인스펙터 "Profile" 슬롯에 꽂는다.
///
/// 비워두면 아무것도 덮어쓰지 않으므로, 기존 프리팹은 지금 값 그대로 돌아간다.
/// </summary>
[CreateAssetMenu(fileName = "ZombieProfile", menuName = "Zombie/Zombie Profile")]
public class ZombieProfile : ScriptableObject
{
    [Header("식별")]
    public string displayName = "일반 좀비";
    [Tooltip("디버그 HUD에서 이 종류를 구분할 색")]
    public Color debugColor = Color.white;

    [Header("능력치")]
    public float maxHealth      = 50f;
    public float moveSpeed      = 2f;
    public float attackRange    = 1.8f;
    public float attackDamage   = 10f;
    public float attackCooldown = 1.5f;

    [Header("감지")]
    public float detectionRange = 30f;
    public ZombieVision vision  = new ZombieVision();

    [Header("FSM 성향")]
    [Tooltip("시야에서 사라진 뒤 계속 추적하는 시간")]
    public float sightMemory = 1.2f;
    [Tooltip("마지막 목격 지점을 기억하고 수색하는 시간")]
    public float investigateMemory = 6f;
    [Tooltip("피격/소음 후 경계 지속 시간")]
    public float alertDuration = 5f;
    [Tooltip("경계 중 감지 반경 배율")]
    public float alertRangeMultiplier = 2f;

    [Header("배회")]
    public bool  enableWander      = true;
    public float wanderRadius      = 8f;
    public float wanderSpeedFactor = 0.5f;

    // ─────────────────────────────────────────────
    /// <summary>ZombieController에 수치를 적용.</summary>
    public void ApplyTo(ZombieController z)
    {
        z.maxHealth      = maxHealth;
        z.moveSpeed      = moveSpeed;
        z.attackRange    = attackRange;
        z.attackDamage   = attackDamage;
        z.attackCooldown = attackCooldown;
        z.detectionRange = detectionRange;

        // ⚠ z.vision = vision 으로 대입하면 안 된다.
        //    ZombieVision은 class라서 같은 프로필을 쓰는 좀비들이 인스턴스 하나를
        //    공유하게 되고, 한 마리가 값을 바꾸면 전부 같이 바뀐다.
        if (z.vision == null) z.vision = new ZombieVision();
        CopyVisionTo(z.vision);
    }

    private void CopyVisionTo(ZombieVision dst)
    {
        dst.viewAngle          = vision.viewAngle;
        dst.nearSenseRadius    = vision.nearSenseRadius;
        dst.occlusionMask      = vision.occlusionMask;
        dst.eyeHeight          = vision.eyeHeight;
        dst.targetHeight       = vision.targetHeight;
        dst.hearingSensitivity = vision.hearingSensitivity;
        dst.checkInterval      = vision.checkInterval;
        // debugDrawSight는 프리팹별 디버그 설정이므로 덮어쓰지 않는다
    }

    /// <summary>FSM 브레인에 성향을 적용. (RL 브레인에는 적용하지 않는다)</summary>
    public void ApplyTo(ZombieFSMBrain brain)
    {
        brain.sightMemory          = sightMemory;
        brain.investigateMemory    = investigateMemory;
        brain.alertDuration        = alertDuration;
        brain.alertRangeMultiplier = alertRangeMultiplier;
        brain.enableWander         = enableWander;
        brain.wanderRadius         = wanderRadius;
        brain.wanderSpeedFactor    = wanderSpeedFactor;
    }
}
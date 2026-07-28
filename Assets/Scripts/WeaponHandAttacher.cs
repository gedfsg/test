using UnityEngine;

/// <summary>
/// 게임 시작 시 RangedWeapon을 캐릭터 오른손 뼈(hand.R)에 자동 부착
/// Player 오브젝트에 추가 후 Inspector에서 RangedWeapon 연결
///
/// [왼손 IK 동기화]
/// - 각 무기 비주얼 프리팹 내부에 "LeftHandPoint"라는 빈 오브젝트를 만들어두면
///   (예: AR_A_1 > LeftHandPoint), 이 스크립트가 매 프레임 leftHandIKTarget의
///   위치/회전을 그 LeftHandPoint 값으로 갱신해준다.
/// - leftHandIKTarget 자체는 씬에 고정된 오브젝트(Rig 1 > LeftHandIK > LeftHandIKTarget)여야
///   하며, 총의 자식으로 만들면 안 된다. (총은 런타임에 Instantiate되므로
///   RigBuilder가 인식 못해 TransformStreamHandle 에러가 남)
///
/// [총구 정밀 조준 - 최종본]
/// - 총은 항상 완전한 수평(pitch = 0, roll = 0)을 유지한 채, 좌우(Yaw)만
///   aimTarget을 향하도록 회전한다.
/// - 마우스가 캐릭터 바로 위(수평 거리 0에 가까움)에 있어도 각도가 급격히
///   튀지 않도록 최소 거리를 클램프한다.
/// - 목표 회전으로 순간 스냅하지 않고 초당 최대 각도(aimRotationSpeed)로만
///   회전시켜, 마우스 좌표의 프레임 단위 노이즈가 그대로 총에 반영되어
///   떠는 것을 막는다.
/// </summary>
public class WeaponHandAttacher : MonoBehaviour
{
    [Header("무기 오브젝트")]
    public GameObject rangedWeapon;

    [Header("손 위치/회전 오프셋 (플레이 중 Inspector에서 실시간 조정 가능)")]
    public Vector3 positionOffset = new Vector3(0.04f, 0.02f, 0.08f);
    public Vector3 rotationOffset = new Vector3(-90f, 0f, 0f);
    public Vector3 scaleOffset = Vector3.one;

    [Header("Animation Rigging - 왼손 IK")]
    [Tooltip("Rig 1 > LeftHandIK > LeftHandIKTarget. 씬에 고정된 오브젝트를 연결할 것 (총의 자식 X)")]
    public Transform leftHandIKTarget;

    [Tooltip("무기 프리팹 안에서 찾을 왼손 포인트 오브젝트 이름")]
    public string leftHandPointName = "LeftHandPoint";

    [Header("총구 정밀 조준")]
    [Tooltip("무기 프리팹 안에서 찾을 총구 오브젝트 이름")]
    public string muzzlePointName = "MuzzlePoint";

    [Tooltip("마우스 월드 좌표를 매 프레임 담아주는 Transform. PlayerController의 aimTarget과 같은 오브젝트를 연결할 것.")]
    public Transform aimTarget;

    [Tooltip("총구와 마우스의 수평(XZ) 거리가 이 값보다 가까워지면 이 거리만큼 떨어진 것으로 취급한다. 마우스가 캐릭터 바로 위에 있을 때 방향 벡터 길이가 0에 가까워져 각도가 급격히 튀는 것을 방지.")]
    public float minAimHorizontalDistance = 1.0f;

    [Tooltip("초당 최대 회전 각도(도). 목표 방향으로 즉시 스냅하지 않고 이 속도로 서서히 따라가게 해서, 마우스 좌표의 미세한 프레임 단위 노이즈가 총에 그대로 반영되어 떠는 것을 막는다.")]
    public float aimRotationSpeed = 720f;

    [Header("발사 위치 동기화")]
    [Tooltip("Weapon(Weapon.cs)의 Fire Point로 연결된 바로 그 오브젝트. 씬에 고정된 오브젝트를 연결할 것 (총의 자식 X)")]
    public Transform firePointTarget;

    // 현재 장착된 무기 비주얼에서 찾은 포인트 캐시 (디버그용으로 Inspector에 노출)
    [SerializeField] private Transform currentLeftHandPoint;
    [SerializeField] private Transform currentMuzzlePoint;
    private Transform currentWeaponVisualRoot;

    // 총구가 무기 루트 기준으로 갖는 "고정된" 회전 오프셋. 무기 장착 시점에 한 번만 계산해서
    // 캐싱해두고, 매 프레임 이 값을 그대로 재사용한다. 매 프레임 currentMuzzlePoint.rotation을
    // 다시 읽어서 델타를 계산하면, 애니메이션의 미세한 흔들림이 계속 누적되는
    // 되먹임(feedback) 루프가 생겨 시간이 지나며 떨림이 증폭된다.
    private Quaternion muzzleLocalOffset = Quaternion.identity;

    const string RIGHT_HAND_BONE = "hand.R";

    void Start()
    {
        AttachToHand();
    }

    void AttachToHand()
    {
        if (rangedWeapon == null)
        {
            Debug.LogWarning("[WeaponHandAttacher] rangedWeapon이 비어있어요.");
            return;
        }

        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone == null)
        {
            Debug.LogWarning($"[WeaponHandAttacher] '{RIGHT_HAND_BONE}' 뼈대를 찾지 못했어요.");
            return;
        }

        rangedWeapon.transform.SetParent(handBone);
        rangedWeapon.transform.localPosition = positionOffset;
        rangedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);

        CacheLeftHandPoint(handBone);
    }

    public void ApplyOffset()
    {
        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone == null) return;
        foreach (Transform child in handBone)
        {
            child.localPosition = positionOffset;
            child.localRotation = Quaternion.Euler(rotationOffset);
            child.localScale = scaleOffset;
        }
    }

    Transform FindDeep(Transform parent, string boneName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == boneName) return child;
            Transform found = FindDeep(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    public void HideVisual()
    {
        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone == null) return;

        foreach (Transform child in handBone)
        {
            if (child.GetComponentInChildren<Weapon>() == null)
                Destroy(child.gameObject);
        }

        if (rangedWeapon != null) rangedWeapon.SetActive(false);

        currentLeftHandPoint = null;
        currentMuzzlePoint = null;
        currentWeaponVisualRoot = null;
    }

    public void SwapVisual(GameObject newWeaponPrefab, Vector3 posOffset, Vector3 rotOffset)
    {
        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone == null) return;

        foreach (Transform child in handBone)
        {
            if (child.GetComponentInChildren<Weapon>() == null)
                Destroy(child.gameObject);
        }

        GameObject newVisual = Instantiate(newWeaponPrefab, handBone);
        newVisual.transform.localPosition = posOffset;
        newVisual.transform.localRotation = Quaternion.Euler(rotOffset);

        positionOffset = posOffset;
        rotationOffset = rotOffset;

        CacheLeftHandPoint(newVisual.transform);
    }

    void CacheLeftHandPoint(Transform searchRoot)
    {
        currentLeftHandPoint = FindDeep(searchRoot, leftHandPointName);
        currentMuzzlePoint = FindDeep(searchRoot, muzzlePointName);

        currentWeaponVisualRoot = currentMuzzlePoint != null ? currentMuzzlePoint.parent
                                 : currentLeftHandPoint != null ? currentLeftHandPoint.parent
                                 : null;

        // 총구가 무기 루트 기준으로 갖는 회전 오프셋을 지금 이 시점에 딱 한 번만 계산해서 고정한다.
        // 무기 내부 구조(총구와 루트 사이)는 이후 절대 변하지 않으므로, 매 프레임 다시 계산할
        // 필요가 없고, 오히려 매 프레임 다시 읽으면 되먹임 루프가 생겨 떨림의 원인이 된다.
        if (currentMuzzlePoint != null && currentWeaponVisualRoot != null)
        {
            muzzleLocalOffset = Quaternion.Inverse(currentWeaponVisualRoot.rotation) * currentMuzzlePoint.rotation;
        }

        if (currentLeftHandPoint == null)
        {
            Debug.LogWarning($"[WeaponHandAttacher] '{searchRoot.name}'에서 '{leftHandPointName}'을 찾지 못했어요. 왼손 IK가 적용되지 않습니다.");
        }

        if (currentMuzzlePoint == null)
        {
            Debug.LogWarning($"[WeaponHandAttacher] '{searchRoot.name}'에서 '{muzzlePointName}'을 찾지 못했어요. 총구 정밀 조준이 적용되지 않습니다.");
        }
    }

    void LateUpdate()
    {
        if (currentLeftHandPoint == null || currentMuzzlePoint == null)
        {
            Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
            if (handBone != null) CacheLeftHandPoint(handBone);
        }

        AimMuzzleAtTarget();
        SyncLeftHandIK();
        SyncFirePoint();
    }

    /// <summary>
    /// 총구가 항상 완전한 수평(pitch=0)을 유지한 채, 좌우로만 aimTarget을 향하도록
    /// 무기 비주얼 루트를 회전시킨다.
    ///
    /// [되먹임 방지] currentMuzzlePoint.rotation을 매 프레임 다시 읽어서 델타를
    /// 계산하지 않는다. 그 값은 우리가 지난 프레임에 준 회전 + 부모 뼈의 미세한
    /// 애니메이션 흔들림이 겹쳐 있어서, 이걸 기준으로 다시 계산하면 흔들림이
    /// 계속 누적되는 되먹임 루프가 생긴다. 대신 "부모 뼈(hand.R_end)의 이번 프레임
    /// 회전 + 무기 장착 시 고정해둔 총구 오프셋(muzzleLocalOffset)"만으로 목표
    /// 로컬 회전을 매번 새로 계산하고, 그 목표로 서서히(RotateTowards) 다가간다.
    /// 이러면 우리가 준 회전값이 다시 계산에 섞여 들어갈 여지가 없어서 안정적이다.
    /// </summary>
    /// <summary>
    /// 총구가 항상 완전한 수평(pitch=0)을 유지한 채, 좌우로만 aimTarget을 향하도록
    /// 무기 비주얼 루트를 회전시킨다.
    ///
    /// 마우스와 총구의 수평 거리가 minAimHorizontalDistance보다 가까우면, 방향을
    /// 억지로 계산하지 않고 이번 프레임 갱신 자체를 건너뛴다(마지막 방향 유지).
    /// 거리를 강제로 늘려서 방향을 계산하면 그 "늘린 방향" 자체가 마우스의 미세한
    /// 위치 변화에 따라 계속 흔들릴 수 있는데, 아예 계산을 안 하면 그럴 일이 없다.
    /// </summary>
    void AimMuzzleAtTarget()
    {
        if (currentMuzzlePoint == null || currentWeaponVisualRoot == null || aimTarget == null)
            return;

        Transform parentBone = currentWeaponVisualRoot.parent;
        if (parentBone == null) return;

        Vector3 toTarget = aimTarget.position - currentMuzzlePoint.position;
        toTarget.y = 0f;

        // 너무 가까우면 이번 프레임은 그냥 아무것도 하지 않는다 (마지막 방향 유지).
        if (toTarget.magnitude < minAimHorizontalDistance) return;

        // 이번 프레임에 총구가 향해야 할 월드 회전
        Quaternion desiredMuzzleWorldRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        // 총구 기준 목표를, 무기 루트가 가져야 할 월드 회전으로 환산 (고정 오프셋 사용)
        Quaternion desiredRootWorldRotation = desiredMuzzleWorldRotation * Quaternion.Inverse(muzzleLocalOffset);

        // 부모 뼈의 "이번 프레임" 회전을 기준으로, 무기 루트가 가져야 할 로컬 회전을 계산
        Quaternion desiredLocalRotation = Quaternion.Inverse(parentBone.rotation) * desiredRootWorldRotation;

        currentWeaponVisualRoot.localRotation = Quaternion.RotateTowards(
            currentWeaponVisualRoot.localRotation,
            desiredLocalRotation,
            aimRotationSpeed * Time.deltaTime);
    }

    void SyncLeftHandIK()
    {
        if (currentLeftHandPoint == null || leftHandIKTarget == null) return;

        leftHandIKTarget.position = currentLeftHandPoint.position;
        leftHandIKTarget.rotation = currentLeftHandPoint.rotation;
    }

    void SyncFirePoint()
    {
        if (currentMuzzlePoint == null || firePointTarget == null) return;

        firePointTarget.position = currentMuzzlePoint.position;
        firePointTarget.rotation = currentMuzzlePoint.rotation;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
        if (handBone != null && rangedWeapon != null &&
            rangedWeapon.transform.parent == handBone)
        {
            rangedWeapon.transform.localPosition = positionOffset;
            rangedWeapon.transform.localRotation = Quaternion.Euler(rotationOffset);
        }
    }
#endif
}
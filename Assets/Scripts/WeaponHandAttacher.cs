using UnityEngine;
using UnityEngine.Animations.Rigging;

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
///
/// [권총 양손 그립 - 가슴 중앙] (2026-11)
/// - Mixamo "pistol idle" 클립은 상체를 90° 가까이 틀고 총을 어깨/눈높이 바깥쪽(캐릭터 중심에서 약 0.44m)으로 들어서 어색함.
/// - 권총을 들면(pistolHeld, PlayerController가 설정) 시작 때 런타임으로 만든 오른팔 Two Bone IK가
///   오른손을 가슴 앞 중앙(pistolGripOffset / pistolGripHeight)으로 끌어오고, 왼손은 기존 왼손 IK가
///   총의 LeftHandPoint(손잡이)를 감싸 쥠 → 양손 그립. 소총은 원래 자세가 자연스러워 그대로 둠.
/// - IK 목표는 Rig 1 아래(캐릭터 계층 안)에 둬서, 이동 중에도 몸과 같이 움직임 (한 프레임 늦게 따라오는 문제 없음).
///
/// [AR·소총·샷건 어깨 견착 + 달리기 Low Ready] (2026-11-12)
/// - 피드백: 권총·AR·샷건 모두 잡는 자세가 부자연스러움 → 같은 오른팔 IK를 모든 총에 사용.
///   권총 = 가슴 중앙 양손 / AR·소총 = 오른쪽 어깨 견착(손이 어깨 앞) / 샷건 = 어깨 견착 (조금 더 몸 쪽)
///   왼손은 기존 왼손 IK가 무기의 LeftHandPoint(소총은 앞 손잡이)를 잡음 → 양손 그립.
/// - 무기별 위치는 WeaponData.handGripOffset (0이면 아래 종류별 기본값).
/// - 달릴 때(gripLowReady, PlayerController가 설정)는 Low Ready: 손을 내리고 총구를 아래로 숙임.
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

    [Tooltip("false면 총구 조준 회전과 왼손 IK 위치 동기화를 멈춤 (도끼처럼 애니메이션 손을 그대로 따라가야 할 때)")]
    public bool aimAtTarget = true;

    [Header("권총 양손 그립 (가슴 중앙)")]
    [Tooltip("권총을 들면 오른손을 IK로 가슴 앞 중앙에 고정하고 왼손으로 손잡이를 감싸 쥠. 끄면 예전처럼 애니메이션 클립 자세 그대로")]
    public bool pistolCenterGrip = true;
    [Tooltip("오른손 목표 수평 위치: 가슴 뼈 기준 (x = 오른쪽, y = 앞) (m)")]
    public Vector2 pistolGripOffset = new Vector2(0.03f, 0.42f);
    [Tooltip("오른손 목표 높이: 가슴 뼈 높이 + 이 값 (m)")]
    public float pistolGripHeight = 0.17f; // 실측: 0.05는 배 높이, 0.17이 가슴 높이
    [Tooltip("손가락 방향을 앞에서 아래로 숙이는 정도 (0 = 수평)")]
    public float pistolWristDown = 0.35f;

    /// <summary>지금 권총을 들고 있는지 (PlayerController.RefreshIKAndAim이 매 프레임 설정)</summary>
    [System.NonSerialized] public bool pistolHeld;

    [Header("AR·소총·샷건 어깨 견착 (2026-11-12)")]
    [Tooltip("총 종류별 기본 오른손 위치 (가슴 뼈 기준 x 오른쪽, y 위, z 앞). WeaponData.handGripOffset이 0이 아니면 그 값 사용")]
    public Vector3 rifleGripOffset = new Vector3(0.11f, 0.12f, 0.30f);
    public Vector3 shotgunGripOffset = new Vector3(0.11f, 0.11f, 0.27f);
    [Tooltip("소총·샷건 손목 숙임 (권총은 pistolWristDown)")]
    public float rifleWristDown = 0.05f;
    [Header("달리기 Low Ready")]
    [Tooltip("달릴 때 손을 내리는 높이(m) / 몸 쪽으로 당기는 거리(m)")]
    public float lowReadyDrop = 0.28f, lowReadyPullBack = 0.08f;
    [Tooltip("달릴 때 총구를 아래로 숙이는 각도(도)")]
    public float lowReadyPitch = 38f;

    /// <summary>지금 든 총 (근접 무기/맨손이면 null) - PlayerController.RefreshIKAndAim이 매 프레임 설정</summary>
    [System.NonSerialized] public WeaponData gripWeapon;
    /// <summary>달리는 중 → Low Ready (PlayerController가 설정)</summary>
    [System.NonSerialized] public bool gripLowReady;
    float lowReady01; // Low Ready 섞는 정도 0~1 (부드럽게)

    TwoBoneIKConstraint rightHandIK;     // 런타임 생성 (씬 파일은 안 건드림)
    Transform rightHandIKTarget, rightHandIKHint;
    Animator bodyAnimator;

    [Header("발사 위치 동기화")]
    [Tooltip("Weapon(Weapon.cs)의 Fire Point로 연결된 바로 그 오브젝트. 씬에 고정된 오브젝트를 연결할 것 (총의 자식 X)")]
    public Transform firePointTarget;

    // 현재 장착된 무기 비주얼에서 찾은 포인트 캐시 (디버그용으로 Inspector에 노출)
    [SerializeField] private Transform currentLeftHandPoint;
    [SerializeField] private Transform currentMuzzlePoint;
    private Transform currentWeaponVisualRoot;
    private Transform currentVisual; // 마지막으로 손에 붙인 무기 모델

    /// <summary>지금 손에 붙어 있는 무기 모델 (구르기 동안 허리/등으로 옮기거나 숨길 때 사용)</summary>
    public Transform CurrentVisual => currentVisual;

    /// <summary>손에 든 무기 모델의 로컬 회전 직접 지정 (도끼: 들고 다닐 때 ↔ 휘두를 때 전환용)</summary>
    public void SetVisualLocalRotation(Quaternion rot)
    {
        if (currentVisual != null) currentVisual.localRotation = rot;
    }

    /// <summary>손에 든 무기 모델의 로컬 위치 직접 지정</summary>
    public void SetVisualLocalPosition(Vector3 pos)
    {
        if (currentVisual != null) currentVisual.localPosition = pos;
    }

    // 총구가 무기 루트 기준으로 갖는 "고정된" 회전 오프셋. 무기 장착 시점에 한 번만 계산해서
    // 캐싱해두고, 매 프레임 이 값을 그대로 재사용한다. 매 프레임 currentMuzzlePoint.rotation을
    // 다시 읽어서 델타를 계산하면, 애니메이션의 미세한 흔들림이 계속 누적되는
    // 되먹임(feedback) 루프가 생겨 시간이 지나며 떨림이 증폭된다.
    private Quaternion muzzleLocalOffset = Quaternion.identity;

    const string RIGHT_HAND_BONE = "hand.R";

    void Start()
    {
        AttachToHand();
        SetupPistolGripIK();
    }

    /// <summary>
    /// 오른팔 Two Bone IK를 기존 왼손 IK와 같은 Rig에 런타임으로 추가 (upper_arm.R → forearm.R → hand.R).
    /// 평소엔 weight 0이라 아무 영향 없음. Rig/RigBuilder를 못 찾으면 조용히 끔 (예전 자세 유지).
    /// </summary>
    void SetupPistolGripIK()
    {
        if (!pistolCenterGrip || leftHandIKTarget == null) return;
        bodyAnimator = GetComponentInChildren<Animator>();
        var rig = leftHandIKTarget.GetComponentInParent<Rig>();
        var builder = GetComponentInChildren<RigBuilder>();
        if (bodyAnimator == null || !bodyAnimator.isHuman || rig == null || builder == null) return;
        Transform upper = bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform lower = bodyAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform hand = bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        if (upper == null || lower == null || hand == null) return;

        var go = new GameObject("RightHandIK_Pistol");
        go.transform.SetParent(rig.transform, false);
        go.transform.SetAsFirstSibling(); // 오른손(총)을 먼저 옮기고 → 왼손 IK가 그 총 손잡이를 잡음
        rightHandIKTarget = new GameObject("RightHandIKTarget").transform;
        rightHandIKTarget.SetParent(go.transform, false);
        rightHandIKTarget.SetPositionAndRotation(hand.position, hand.rotation);
        rightHandIKHint = new GameObject("RightHandIKHint").transform;
        rightHandIKHint.SetParent(go.transform, false);
        rightHandIKHint.position = lower.position;

        rightHandIK = go.AddComponent<TwoBoneIKConstraint>();
        rightHandIK.data.root = upper;
        rightHandIK.data.mid = lower;
        rightHandIK.data.tip = hand;
        rightHandIK.data.target = rightHandIKTarget;
        rightHandIK.data.hint = rightHandIKHint;
        rightHandIK.data.targetPositionWeight = 1f;
        rightHandIK.data.targetRotationWeight = 1f;
        rightHandIK.data.hintWeight = 1f;
        rightHandIK.weight = 0f;
        builder.Build(); // 새 제약을 애니메이션 그래프에 반영
    }

    /// <summary>
    /// 권총 그립 목표 갱신 (LateUpdate). 오른손 = 가슴 앞 중앙, 손바닥은 안쪽(왼쪽), 손가락은 앞·약간 아래.
    /// 측정한 hand.R 축: 손가락 -Z, 엄지 +Y, 손바닥 +X.
    /// </summary>
    void UpdatePistolGrip()
    {
        if (rightHandIK == null) return;
        // (이전) 권총만: rightHandIK.weight → pistolHeld ? 1 : 0
        bool gunHeld = pistolHeld || (gripWeapon != null && gripWeapon.type != WeaponType.Melee);
        rightHandIK.weight = Mathf.MoveTowards(rightHandIK.weight, gunHeld && pistolCenterGrip ? 1f : 0f, Time.deltaTime * 8f);
        lowReady01 = Mathf.MoveTowards(lowReady01, gunHeld && gripLowReady ? 1f : 0f, Time.deltaTime * 5f);
        if (rightHandIK.weight <= 0f) return;

        Transform chest = bodyAnimator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = bodyAnimator.GetBoneTransform(HumanBodyBones.Spine);
        Vector3 f = transform.forward; f.y = 0f; // 플레이어 루트 = 마우스 방향 (PlayerController.AimAtMouse)
        if (f.sqrMagnitude < 1e-6f) return;
        f.Normalize();
        Vector3 r = Vector3.Cross(Vector3.up, f);
        Vector3 c = chest.position;
        // rightHandIKTarget.position = c + r * pistolGripOffset.x + f * pistolGripOffset.y + Vector3.up * pistolGripHeight; // (이전) 권총만
        Vector3 g = GripOffset(out float wristDown); // (오른쪽, 위, 앞)
        g.y -= lowReadyDrop * lowReady01;
        g.z -= lowReadyPullBack * lowReady01;
        wristDown = Mathf.Lerp(wristDown, Mathf.Tan(lowReadyPitch * Mathf.Deg2Rad), lowReady01);
        rightHandIKTarget.position = c + r * g.x + f * g.z + Vector3.up * g.y;

        Vector3 palm = -r;                                              // +X
        Vector3 fingers = (f - Vector3.up * wristDown).normalized;      // -Z
        Vector3 z = -fingers;
        Vector3 y = Vector3.Cross(z, palm);                              // +Y (엄지: 위·앞)
        rightHandIKTarget.rotation = Quaternion.LookRotation(z, y);

        // 팔꿈치는 오른쪽 아래로 (안쪽으로 접혀 몸을 파고들지 않게)
        rightHandIKHint.position = c + r * 0.45f - Vector3.up * 0.35f - f * 0.05f;
    }

    /// <summary>오른손 목표 (가슴 기준 오른쪽, 위, 앞)와 손목 숙임. 무기에 handGripOffset이 있으면 그 값, 없으면 종류별 기본값</summary>
    Vector3 GripOffset(out float wristDown)
    {
        var type = gripWeapon != null ? gripWeapon.type : WeaponType.Pistol;
        bool pistol = pistolHeld || type == WeaponType.Pistol;
        wristDown = pistol ? pistolWristDown : rifleWristDown;
        if (gripWeapon != null && gripWeapon.handGripOffset != Vector3.zero) return gripWeapon.handGripOffset;
        if (pistol) return new Vector3(pistolGripOffset.x, pistolGripHeight, pistolGripOffset.y);
        return type == WeaponType.Shotgun ? shotgunGripOffset : rifleGripOffset;
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
        currentVisual = newVisual.transform;
        newVisual.transform.localPosition = posOffset;
        newVisual.transform.localRotation = Quaternion.Euler(rotOffset);

        positionOffset = posOffset;
        rotationOffset = rotOffset;

        CacheLeftHandPoint(newVisual.transform);
    }

    // warn: 무기를 새로 쥐었을 때만 경고. LateUpdate의 매 프레임 재검색(맨손이면 늘 못 찾음)은 경고 안 함 - 초당 수십 개 경고로 콘솔이 덮였음
    void CacheLeftHandPoint(Transform searchRoot, bool warn = true)
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

        if (currentLeftHandPoint == null && warn)
        {
            Debug.LogWarning($"[WeaponHandAttacher] '{searchRoot.name}'에서 '{leftHandPointName}'을 찾지 못했어요. 왼손 IK가 적용되지 않습니다.");
        }

        if (currentMuzzlePoint == null && warn)
        {
            Debug.LogWarning($"[WeaponHandAttacher] '{searchRoot.name}'에서 '{muzzlePointName}'을 찾지 못했어요. 총구 정밀 조준이 적용되지 않습니다.");
        }
    }

    void LateUpdate()
    {
        if (currentLeftHandPoint == null || currentMuzzlePoint == null)
        {
            Transform handBone = FindDeep(transform, RIGHT_HAND_BONE);
            // if (handBone != null) CacheLeftHandPoint(handBone);  // (이전) 맨손일 때 매 프레임 경고 2개
            if (handBone != null) CacheLeftHandPoint(handBone, false);
        }

        UpdatePistolGrip(); // 오른손 목표 먼저 (총 위치가 정해진 뒤 왼손이 손잡이를 따라감)
        if (aimAtTarget)
        {
            AimMuzzleAtTarget();
            SyncLeftHandIK();
        }
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
        // 달리기 Low Ready: 총구를 아래로 숙임 (2026-11-12). 평소엔 수평 유지
        if (lowReady01 > 0f) desiredMuzzleWorldRotation = Quaternion.AngleAxis(lowReadyPitch * lowReady01, Vector3.Cross(Vector3.up, toTarget.normalized)) * desiredMuzzleWorldRotation;

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
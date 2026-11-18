using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;

/// <summary>
/// 투척 시스템 (2026-11-16) - 핫바 3번(투척 전용 칸)의 수류탄·화염병. PlayerController.Awake가 자동 부착.
/// PlayerController.HandleFire가 투척물을 들고 있을 때 매 프레임 Tick(data, 좌클릭 누름 여부)을 부름.
///
/// [조작] (이전 2026-11-16)
///  - 좌클릭 짧게(0.2초 안에 뗌): 바로 던짐 (커서 방향·거리, 궤적 표시 없음)
///  - 좌클릭 꾹(0.2초 이상): 조준 모드 → 포물선 궤적 + 착지 원(노랑 1m) + 위험 범위 원(수류탄 빨강 8m / 화염병 주황 5m)
///  - 조준 중 우클릭: 거리 모드 전환 — 멀리(던지는 힘 15, 최대 15m) ↔ 가까이(힘 7, 최대 5m)
///  - 좌클릭 뗌: 보이던 궤적 그대로 던짐
///
/// [조작 - 2026-11-18 리워크]
///  - 좌클릭 꾹 = 조준(점선 궤적 + 착지 원) → 떼면 던짐 / 짧게 클릭해도 바로 던짐 (그대로)
///  - 우클릭 거리 모드(가까이/멀리) 삭제 → 항상 "멀리" 값(최대 15m)으로 커서 거리에 맞춤
///  - 우클릭 = 투척 취소: 들고 있던 투척물을 넣고 마지막 총 칸으로.
///    수류탄은 핀을 뽑기 전(좌클릭 누르고 0.3초 안)에만, 화염병은 아무 때나
/// [수류탄 신관] 좌클릭 누르고 0.3초에 핀이 뽑힘(소리) → 5초 카운트다운. 머리 위에 "⏱ 3.2s" (빨강, 굵게)
///  - 던지면 공중이든 땅이든 남은 시간 뒤 폭발 (좀비에 맞아도 바로 안 터짐)
///  - 쥔 채 5초가 지나면 손 안에서 폭발 → 플레이어 즉사
///  - 핀 뽑힌 채 다른 칸으로 바꾸면 발밑에 떨어뜨림 (남은 시간 뒤 폭발)
/// [투척 자세] Animation Rigging 두 팔 IK (런타임 Rig 레이어 "ThrowPoseRig")
///  - 조준 중: 오른손을 머리 뒤로 들어올림(위팔 약 80° 들고 팔꿈치 90°), 왼손은 앞으로 내밀어 균형, 상체는 UpperBodyAimIK가 커서 쪽
///  - 던지는 순간: 오른팔이 앞으로 휘둘러짐(약 0.12초) 후 풀림
/// [던지기] 손 높이에서 Rigidbody 투사체 + 초기 속도. 커서까지 거리에 맞춰 40° 각도로 속도를 계산 (최대 = 던지는 힘)
/// 던지면 핫바 개수 -1, 0개가 되면 이전 총 칸(1 또는 2)으로 자동 전환.
/// </summary>
public class ThrowableController : MonoBehaviour
{
    [Header("조작")]
    public float holdThreshold = 0.2f;

    [Header("거리 모드")]
    public float farForce = 15f, farRange = 15f;
    public float nearForce = 7f, nearRange = 5f; // (2026-11-18부터 안 씀 - 우클릭 거리 모드 삭제)
    [Tooltip("던지는 각도 (도)")]
    public float launchAngle = 40f;
    public float minRange = 1.5f;

    [Header("손")]
    public float handHeight = 1.5f;

    [Header("수류탄 신관 (2026-11-18)")]
    [Tooltip("좌클릭을 누르고 이 시간(초)이 지나면 핀이 뽑힘. 그 전에만 우클릭 취소 가능")]
    public float pinDelay = 0.3f;
    [Tooltip("핀을 뽑은 뒤 폭발까지 (초)")]
    public float grenadeFuse = 5f;

    [Header("궤적 색 (2026-11-18)")]
    public Color grenadeColor = new Color32(0xFF, 0xA0, 0x30, 0xFF);
    public Color molotovColor = new Color32(0xFF, 0x40, 0x20, 0xFF);

    PlayerController player;
    Animator animator;
    bool wasHeld;
    float pressTime;
    bool aiming;
    bool nearMode; // (2026-11-18부터 항상 false)
    ThrowableData heldData;
    GameObject handVisual;
    TrajectoryRenderer traj;

    // 수류탄 신관
    bool pinPulled;
    float fuseEnd;
    GrenadeFuseLabel fuseLabel;
    bool waitRelease; // 취소 후 좌클릭을 뗄 때까지 무시

    // 투척 자세 IK
    Rig poseRig;
    TwoBoneIKConstraint rightIK, leftIK;
    Transform rTarget, rHint, lTarget, lHint;
    Transform rShoulder, lShoulder;
    float armLen = 0.8f;
    bool poseBuilt, poseFailed;
    float swingUntil = -1f;

    /// <summary>조준 중(궤적 표시 중)인지</summary>
    public bool IsAiming => aiming;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        traj = new GameObject("ThrowTrajectory").AddComponent<TrajectoryRenderer>();
        traj.Hide();
    }

    void OnDestroy()
    {
        if (traj != null) Destroy(traj.gameObject);
        if (fuseLabel != null) Destroy(fuseLabel.gameObject);
    }

    bool IsGrenade(ThrowableData d) => d != null && d.throwableType == ThrowableType.Grenade;
    Color TrajColor(ThrowableData d) => d != null && d.throwableType == ThrowableType.Molotov ? molotovColor : grenadeColor;

    /// <summary>투척물을 들고 있는 동안 매 프레임 (held = 좌클릭 누르고 있음)</summary>
    public void Tick(ThrowableData data, bool held)
    {
        ShowInHand(data);
        if (data == null) { Cancel(); return; }

        // 취소한 뒤엔 좌클릭을 한 번 뗄 때까지 아무것도 안 함
        if (waitRelease) { if (!held) waitRelease = false; wasHeld = held; return; }

        if (held && !wasHeld) // 누름 시작
        {
            pressTime = Time.time;
            // (이전) 누르자마자 핀 소리 → (2026-11-18) 0.3초 뒤 핀이 뽑힐 때 (PullPin)
            // if (data.pinSound != null) AudioManager.PlaySFX(data.pinSound, transform.position + Vector3.up, 1f, AudioManager.Channel.SFX, 20f);
        }
        // 수류탄: 누르고 0.3초 → 핀 뽑힘 (이후 취소 불가, 5초 카운트다운)
        if (held && IsGrenade(data) && !pinPulled && Time.time - pressTime >= pinDelay) PullPin(data);
        if (held && !aiming && Time.time - pressTime >= holdThreshold) aiming = true;

        // 우클릭 = 투척 취소 (2026-11-18). 수류탄은 핀 뽑기 전만
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            if (IsGrenade(data) && pinPulled)
                PickupFeedbackUI.Instance?.Show("핀을 뽑았습니다 - 취소 불가! 던지세요");
            else
            {
                CancelThrow(data, held);
                return;
            }
        }

        if (aiming && held)
        {
            // (이전 2026-11-16) 조준 중 우클릭 = 가까이/멀리 전환 → 2026-11-18 삭제 (우클릭은 취소)
            // if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            // {
            //     nearMode = !nearMode;
            //     PickupFeedbackUI.Instance?.Show(nearMode ? "가까이 던지기 (~5m)" : "멀리 던지기 (~15m)");
            // }
            Vector3 v = ComputeVelocity(out Vector3 origin);
            // traj.Show(origin, v, data.radius, data.rangeColor); // (이전) 흰 실선 + 노랑 1m 원 + 위험 범위 원
            traj.Show(origin, v, data.radius, TrajColor(data));
        }

        if (!held && wasHeld) // 뗌 → 던짐 (짧게 = 즉시, 길게 = 궤적대로. 둘 다 같은 계산)
        {
            if (IsGrenade(data) && !pinPulled) PullPin(data); // 짧게 클릭: 던지는 순간 핀
            Vector3 v = ComputeVelocity(out Vector3 origin);
            Throw(data, origin, v);
            aiming = false;
            traj.Hide();
        }
        wasHeld = held;
    }

    void PullPin(ThrowableData data)
    {
        pinPulled = true;
        fuseEnd = Time.time + grenadeFuse;
        if (data.pinSound != null) AudioManager.PlaySFX(data.pinSound, transform.position + Vector3.up, 1f, AudioManager.Channel.SFX, 20f);
        if (fuseLabel == null) fuseLabel = GrenadeFuseLabel.Create();
        fuseLabel.Follow(transform, Vector3.up * 3.1f, fuseEnd); // 머리(약 2.2m) 위 - 탑뷰에서 머리와 안 겹치게
    }

    // 우클릭 취소: 궤적·자세 정리하고 투척물을 넣음 (마지막 총 칸으로)
    void CancelThrow(ThrowableData data, bool held)
    {
        aiming = false;
        traj.Hide();
        waitRelease = held;
        wasHeld = held;
        PickupFeedbackUI.Instance?.Show((data != null ? data.itemName : "투척") + " 취소");
        WeaponHotbarUI.Instance?.ReturnToLastGun();
    }

    /// <summary>투척물을 내려놓음 (다른 칸 선택, 구르기 등) - 궤적/손 모델 정리</summary>
    public void Cancel()
    {
        // (2026-11-18) 핀 뽑힌 수류탄을 쥔 채 내려놓으면 발밑에 떨어뜨림 (남은 시간 뒤 폭발)
        if (pinPulled && heldData != null) DropCooked(heldData);
        aiming = false; wasHeld = false; waitRelease = false;
        if (traj != null) traj.Hide();
        ShowInHand(null);
    }

    void Update()
    {
        // 수류탄을 쥔 채 신관이 다 됨 → 손 안에서 폭발, 플레이어 즉사
        if (pinPulled && heldData != null && Time.time >= fuseEnd) ExplodeInHand(heldData);
    }

    void LateUpdate() => UpdateThrowPose();

    Vector3 ComputeVelocity(out Vector3 origin)
    {
        origin = transform.position + Vector3.up * handHeight + transform.forward * 0.3f;
        Vector3 target = player != null && player.aimTarget != null ? player.aimTarget.position : transform.position + transform.forward * 8f;
        Vector3 flat = target - origin; flat.y = 0f;
        float maxRange = nearMode ? nearRange : farRange, maxSpeed = nearMode ? nearForce : farForce;
        float d = Mathf.Clamp(flat.magnitude, minRange, maxRange);
        Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward;
        // 발밑 높이(손 높이만큼 아래)에 d만큼 날아가는 속도: v² = g d² / (2 cos²θ (d tanθ + h))
        float th = launchAngle * Mathf.Deg2Rad, g = -Physics.gravity.y, h = handHeight;
        float v2 = g * d * d / (2f * Mathf.Cos(th) * Mathf.Cos(th) * (d * Mathf.Tan(th) + h));
        float speed = Mathf.Min(Mathf.Sqrt(Mathf.Max(0.01f, v2)), maxSpeed);
        return (dir * Mathf.Cos(th) + Vector3.up * Mathf.Sin(th)) * speed;
    }

    GameObject SpawnProjectile(ThrowableData data, Vector3 origin, Vector3 velocity)
    {
        var obj = Instantiate(data.throwablePrefab, origin, velocity.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(velocity) : Quaternion.identity);
        obj.name = data.itemName + "(던짐)";
        var rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        if (obj.GetComponent<Collider>() == null) obj.AddComponent<SphereCollider>().radius = 0.08f;
        rb.mass = 0.6f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 8f;
        // 던진 사람 몸에 부딪히지 않게
        foreach (var mine in GetComponentsInChildren<Collider>())
            foreach (var c in obj.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, c);
        var old = obj.GetComponent<ThrowableItem>(); if (old != null) Destroy(old); // 예전 투척 스크립트 (있으면)
        return obj;
    }

    void Throw(ThrowableData data, Vector3 origin, Vector3 velocity)
    {
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null || data.throwablePrefab == null) return;

        var obj = SpawnProjectile(data, origin, velocity);
        if (data.throwableType == ThrowableType.Molotov)
        {
            var m = obj.AddComponent<Molotov>(); m.data = data; m.thrower = gameObject;
        }
        else
        {
            var gr = obj.AddComponent<Grenade>(); gr.data = data; gr.thrower = gameObject;
            gr.fuseOverride = Mathf.Max(0.05f, fuseEnd - Time.time); // (2026-11-18) 핀 뽑은 뒤 남은 시간
            if (fuseLabel != null) { fuseLabel.Follow(obj.transform, Vector3.up * 0.7f, fuseEnd); fuseLabel = null; } // 이제 수류탄을 따라감
        }
        pinPulled = false;
        swingUntil = Time.time + 0.12f; // 오른팔 앞으로 휘두르기

        if (data.throwSound != null) AudioManager.PlaySFX(data.throwSound, origin, 1f, AudioManager.Channel.SFX, 25f);
        // Debug.Log($"[투척] {data.itemName} {(nearMode ? "가까이" : "멀리")} 속도 {velocity.magnitude:F1}"); // (이전)
        Debug.Log($"[투척] {data.itemName} 속도 {velocity.magnitude:F1}");

        hotbar.ConsumeThrowable(hotbar.GetActiveSlot()); // 0개면 핫바가 이전 총 칸으로 전환
    }

    // 핀 뽑힌 수류탄을 쥔 채 다른 칸으로 → 발밑에 떨어뜨림
    void DropCooked(ThrowableData data)
    {
        pinPulled = false;
        var hotbar = WeaponHotbarUI.Instance;
        if (data.throwablePrefab == null) return;
        Vector3 origin = transform.position + Vector3.up * 1.0f + transform.forward * 0.4f;
        var obj = SpawnProjectile(data, origin, transform.forward * 1f);
        var gr = obj.AddComponent<Grenade>(); gr.data = data; gr.thrower = gameObject;
        gr.fuseOverride = Mathf.Max(0.05f, fuseEnd - Time.time);
        if (fuseLabel != null) { fuseLabel.Follow(obj.transform, Vector3.up * 0.7f, fuseEnd); fuseLabel = null; }
        PickupFeedbackUI.Instance?.Show("핀 뽑힌 수류탄을 떨어뜨렸습니다!");
        if (hotbar != null)
        {
            int slot = hotbar.IndexOf(data);
            if (slot >= 0) hotbar.ConsumeThrowable(slot);
        }
    }

    // 쥔 채로 신관 끝 → 손 안에서 폭발 + 플레이어 즉사
    void ExplodeInHand(ThrowableData data)
    {
        pinPulled = false;
        aiming = false; traj.Hide();
        Vector3 p = handVisual != null ? handVisual.transform.position : transform.position + Vector3.up * handHeight;
        if (data.throwablePrefab != null)
        {
            var obj = SpawnProjectile(data, p, Vector3.zero);
            obj.GetComponent<Rigidbody>().isKinematic = true;
            var gr = obj.AddComponent<Grenade>(); gr.data = data; gr.thrower = null;
            gr.Explode();
        }
        if (fuseLabel != null) { Destroy(fuseLabel.gameObject); fuseLabel = null; }
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar != null) { int slot = hotbar.IndexOf(data); if (slot >= 0) hotbar.ConsumeThrowable(slot); }
        var h = GetComponent<Health>();
        if (h != null && h.GetCurrentHealth() > 0f) h.TakeDamage(h.GetCurrentHealth() + 1000f); // 즉사
        Debug.Log("[투척] 수류탄이 손 안에서 폭발 - 즉사");
    }

    // 손에 쥔 투척물 모델 (오른손 뼈에 붙임). 총 모델은 PlayerController가 숨김
    void ShowInHand(ThrowableData data)
    {
        if (data == heldData && (data == null || handVisual != null)) return;
        if (handVisual != null) Destroy(handVisual);
        handVisual = null;
        heldData = data;
        if (data == null || data.throwablePrefab == null) return;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        Transform hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand == null) return;
        handVisual = Instantiate(data.throwablePrefab, hand);
        handVisual.name = "HeldThrowable";
        // 손바닥 쪽(+X)·손가락 방향(-Z)으로 약간 (hand.R 실측 축: 손가락 -Z, 엄지 +Y, 손바닥 +X)
        handVisual.transform.localPosition = new Vector3(0.04f, 0f, -0.08f);
        handVisual.transform.localRotation = Quaternion.identity;
        foreach (var c in handVisual.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var r in handVisual.GetComponentsInChildren<Rigidbody>()) Destroy(r);
    }

    // ── 투척 자세 IK (2026-11-18) ──
    void BuildPose()
    {
        poseBuilt = true;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        var builder = GetComponentInChildren<RigBuilder>();
        if (animator == null || !animator.isHuman || builder == null) { poseFailed = true; return; }
        Transform ru = animator.GetBoneTransform(HumanBodyBones.RightUpperArm), rl = animator.GetBoneTransform(HumanBodyBones.RightLowerArm), rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lu = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), ll = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (ru == null || rl == null || rh == null || lu == null || ll == null || lh == null) { poseFailed = true; return; }
        rShoulder = ru; lShoulder = lu;
        armLen = Vector3.Distance(ru.position, rl.position) + Vector3.Distance(rl.position, rh.position);

        var rigGo = new GameObject("ThrowPoseRig");
        rigGo.transform.SetParent(builder.transform, false);
        poseRig = rigGo.AddComponent<Rig>();
        poseRig.weight = 0f;
        rightIK = MakeIK(rigGo.transform, "RightThrowIK", ru, rl, rh, out rTarget, out rHint);
        leftIK = MakeIK(rigGo.transform, "LeftBalanceIK", lu, ll, lh, out lTarget, out lHint);
        builder.layers.Add(new RigLayer(poseRig, true)); // 맨 뒤 = 다른 Rig(상체 조준·손 IK) 다음에 적용
        builder.Build();
    }

    static TwoBoneIKConstraint MakeIK(Transform parent, string name, Transform root, Transform mid, Transform tip, out Transform target, out Transform hint)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        target = new GameObject(name + "_Target").transform; target.SetParent(go.transform, false); target.position = tip.position;
        hint = new GameObject(name + "_Hint").transform; hint.SetParent(go.transform, false); hint.position = mid.position;
        var c = go.AddComponent<TwoBoneIKConstraint>();
        c.data.root = root; c.data.mid = mid; c.data.tip = tip;
        c.data.target = target; c.data.hint = hint;
        c.data.targetPositionWeight = 1f;
        c.data.targetRotationWeight = 0f; // 손목 방향은 애니메이션 그대로
        c.data.hintWeight = 1f;
        return c;
    }

    void UpdateThrowPose()
    {
        bool swinging = Time.time < swingUntil;
        bool want = (aiming || swinging) && heldData != null || swinging;
        if (!poseBuilt) { if (!want) return; BuildPose(); }
        if (poseFailed || poseRig == null) return;

        float targetW = want ? 1f : 0f;
        poseRig.weight = Mathf.MoveTowards(poseRig.weight, targetW, Time.deltaTime / (want ? 0.15f : 0.2f));
        if (poseRig.weight <= 0f) return;

        // 방향: 커서 쪽 (몸 루트가 이미 커서를 봄)
        Vector3 f = transform.forward;
        if (player != null && player.aimTarget != null) { Vector3 to = player.aimTarget.position - transform.position; to.y = 0f; if (to.sqrMagnitude > 1f) f = to; }
        f.y = 0f; f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        Vector3 r = Vector3.Cross(Vector3.up, f), up = Vector3.up;
        float L = armLen;
        Vector3 rs = rShoulder.position, ls = lShoulder.position;

        if (!swinging)
        {
            // 던질 준비: 오른손을 머리 뒤 위로 (위팔 약 80° 들고 팔꿈치 90°), 팔꿈치는 옆·어깨 높이
            rTarget.position = rs + up * 0.55f * L - f * 0.30f * L + r * 0.10f * L;
            rHint.position = rs + r * 0.65f * L + up * 0.10f * L - f * 0.25f * L;
        }
        else
        {
            // 던지는 순간: 오른팔을 앞으로 쭉
            rTarget.position = rs + f * 0.90f * L + up * 0.15f * L - r * 0.15f * L;
            rHint.position = rs + r * 0.45f * L - up * 0.15f * L + f * 0.2f * L;
        }
        // 왼손: 앞으로 내밀어 균형
        lTarget.position = ls + f * 0.80f * L + up * 0.05f * L - r * 0.05f * L;
        lHint.position = ls - r * 0.45f * L - up * 0.30f * L;
    }
}

/// <summary>
/// 수류탄 신관 표시 (2026-11-18): "⏱ 3.2s" 빨강 굵은 글자, 카메라를 향함. 쥐고 있을 땐 플레이어 머리 위, 던지면 수류탄 위를 따라감.
/// 시간이 다 되거나 따라갈 대상이 사라지면 스스로 지움.
/// </summary>
public class GrenadeFuseLabel : MonoBehaviour
{
    Transform follow;
    Vector3 offset;
    float endTime;
    TMPro.TextMeshPro tmp;
    string prefix = "⏱ ";

    public static GrenadeFuseLabel Create()
    {
        var go = new GameObject("GrenadeFuseLabel");
        go.AddComponent<FaceCamera>();
        var l = go.AddComponent<GrenadeFuseLabel>();
        l.tmp = go.AddComponent<TMPro.TextMeshPro>();
        l.tmp.font = HudStyle.Bold;
        l.tmp.fontSize = 3.2f;
        l.tmp.fontStyle = TMPro.FontStyles.Bold;
        l.tmp.color = new Color(1f, 0.18f, 0.12f);
        l.tmp.alignment = TMPro.TextAlignmentOptions.Center;
        l.tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        l.tmp.outlineWidth = 0.2f;
        l.tmp.outlineColor = new Color32(0, 0, 0, 200);
        l.tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 폰트에 ⏱(U+23F1)이 없으면 네모로 나오므로 빼고, 대신 코드로 그린 스톱워치 아이콘을 글자 왼쪽에 (Pretendard에 없음 - Play 확인)
        if (l.tmp.font != null && !l.tmp.font.HasCharacter('⏱', true, true))
        {
            l.prefix = "";
            var icon = new GameObject("StopwatchIcon");
            icon.transform.SetParent(go.transform, false);
            icon.transform.localPosition = new Vector3(-0.66f, 0.02f, 0f);
            icon.transform.localScale = Vector3.one * 0.55f;
            var sr = icon.AddComponent<SpriteRenderer>();
            sr.sprite = StopwatchSprite();
            sr.color = l.tmp.color;
            sr.sortingOrder = 1;
        }
        return l;
    }

    static Sprite stopwatch;
    // 64px 스톱워치: 둥근 테 + 위 꼭지 + 바늘 2개 (흰색, SpriteRenderer.color로 물들임)
    static Sprite StopwatchSprite()
    {
        if (stopwatch != null) return stopwatch;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Stopwatch", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var c = new Vector2(32f, 28f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Vector2.Distance(p, c);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 22f) / 3.5f + 0.5f) * (d > 17.5f && d < 26.5f ? 1f : 0f); // 테
                if (Mathf.Abs(p.x - 32f) < 4f && p.y > 50f && p.y < 60f) a = 1f;                                 // 위 꼭지
                if (Mathf.Abs(p.x - 32f) < 2.2f && p.y > c.y && p.y < c.y + 15f) a = 1f;                          // 바늘 (12시)
                Vector2 h = (p - c); float along = Vector2.Dot(h, new Vector2(0.7071f, 0.7071f)), perp = Mathf.Abs(Vector2.Dot(h, new Vector2(-0.7071f, 0.7071f)));
                if (along > 0f && along < 11f && perp < 2.2f) a = 1f;                                              // 바늘 (2시 반)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        stopwatch = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return stopwatch;
    }

    public void Follow(Transform t, Vector3 off, float end)
    {
        follow = t; offset = off; endTime = end;
        LateUpdate();
    }

    void LateUpdate()
    {
        float remain = endTime - Time.time;
        if (follow == null || remain <= 0f) { Destroy(gameObject); return; }
        transform.position = follow.position + offset;
        tmp.text = prefix + remain.ToString("0.0") + "s";
    }
}

/// <summary>
/// 투척 궤적 (2026-11-16): 포물선 30점 Line Renderer (흰색 → 끝으로 투명, 두께 0.1m)
/// + 착지 지점 노란 원(반경 1m) + 위험 범위 원(수류탄 빨강 8m / 화염병 주황 5m). 지형·벽에 닿는 곳에서 끝남.
/// (2026-11-18 리폴리싱) 점선(0.1m 선 + 0.05m 틈), 색 수류탄 #FFA030 / 화염병 #FF4020 알파 0.8, 30점,
///   착지 위치에 같은 색 반투명(알파 0.3) 원판 (반경 = 폭발/화염 범위). 벽·지면에 닿으면 그 점에서 끊김
/// </summary>
public class TrajectoryRenderer : MonoBehaviour
{
    const int Points = 30;
    const int CircleSegs = 48;
    const float MaxTime = 4f;
    const float Dash = 0.1f, Gap = 0.05f;

    LineRenderer arc, landRing, rangeRing;
    static Material mat;
    Material dashMat;            // 점선 (텍스처 반복)
    MeshRenderer disc;           // 착지 원판
    Material discMat;

    void Awake()
    {
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default")) { name = "Trajectory(Runtime)" };
        arc = NewLine("Arc", 0.1f);
        arc.positionCount = Points;
        // (이전) 흰색 → 끝으로 투명 실선
        // var g = new Gradient();
        // g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        //           new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.05f, 1f) });
        // arc.colorGradient = g;
        landRing = NewLine("LandRing", 0.06f); landRing.loop = true; landRing.positionCount = CircleSegs;
        rangeRing = NewLine("RangeRing", 0.12f); rangeRing.loop = true; rangeRing.positionCount = CircleSegs;
        landRing.enabled = rangeRing.enabled = false; // (2026-11-18) 원 테두리 대신 원판

        // 점선: 선 길이 전체에 텍스처 한 번(Stretch) → 재질 반복 횟수를 길이/0.15m로 (텍스처 앞 2/3 불투명 = 0.1m 선, 뒤 1/3 = 0.05m 틈)
        var src = Resources.Load<Material>("FX/TrajectoryDash"); // 셰이더(Legacy Particles/Alpha Blended)를 빌드에 포함시키는 재질
        dashMat = src != null ? new Material(src) : new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        dashMat.name = "TrajectoryDash(Runtime)";
        dashMat.mainTexture = DashTexture();
        if (dashMat.HasProperty("_TintColor")) dashMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // ×2 = 정점 색 그대로
        arc.sharedMaterial = dashMat;
        arc.textureMode = LineTextureMode.Stretch;
        arc.widthMultiplier = 0.08f;
        arc.numCapVertices = 0;

        // 착지 원판
        var dgo = new GameObject("LandDisc");
        dgo.transform.SetParent(transform, false);
        dgo.AddComponent<MeshFilter>().sharedMesh = DiscMesh();
        disc = dgo.AddComponent<MeshRenderer>();
        discMat = new Material(mat) { name = "LandDisc(Runtime)", renderQueue = 3000 };
        disc.sharedMaterial = discMat;
        disc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        disc.receiveShadows = false;
    }

    LineRenderer NewLine(string name, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat; lr.widthMultiplier = width; lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.numCapVertices = 2;
        return lr;
    }

    static Texture2D dashTex;
    static Texture2D DashTexture()
    {
        if (dashTex != null) return dashTex;
        const int w = 24; // 16px 선 + 8px 틈 = 0.1 : 0.05
        dashTex = new Texture2D(w, 4, TextureFormat.RGBA32, false) { name = "TrajectoryDashTex", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < w; x++)
            for (int y = 0; y < 4; y++)
                dashTex.SetPixel(x, y, x < w * Dash / (Dash + Gap) ? Color.white : new Color(1f, 1f, 1f, 0f));
        dashTex.Apply();
        return dashTex;
    }

    static Mesh discMesh;
    static Mesh DiscMesh()
    {
        if (discMesh != null) return discMesh;
        var v = new Vector3[CircleSegs + 1];
        var t = new int[CircleSegs * 3];
        v[0] = Vector3.zero;
        for (int i = 0; i < CircleSegs; i++)
        {
            float a = i * Mathf.PI * 2f / CircleSegs;
            v[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % CircleSegs + 1; t[i * 3 + 2] = i + 1; // 위에서 보이게 (시계 방향)
        }
        discMesh = new Mesh { name = "LandDisc", vertices = v, triangles = t };
        discMesh.RecalculateNormals();
        discMesh.RecalculateBounds();
        return discMesh;
    }

    public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

    /// <summary>origin에서 velocity로 던졌을 때의 궤적 표시 (중력만, 공기저항 없음 = Rigidbody 기본)</summary>
    public void Show(Vector3 origin, Vector3 velocity, float dangerRadius, Color dangerColor)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        Vector3 g = Physics.gravity;

        // 1) 착지 시간: 0.02초 간격으로 진행하며 이전 점→다음 점 레이캐스트
        float landT = MaxTime; Vector3 landPos = origin + velocity * MaxTime + 0.5f * g * MaxTime * MaxTime;
        Vector3 prev = origin;
        for (float t = 0.02f; t <= MaxTime; t += 0.02f)
        {
            Vector3 p = origin + velocity * t + 0.5f * g * t * t;
            Vector3 seg = p - prev;
            if (Physics.Raycast(prev, seg.normalized, out RaycastHit hit, seg.magnitude, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.CompareTag("Player"))
            {
                landT = t - 0.02f + 0.02f * (hit.distance / Mathf.Max(1e-4f, seg.magnitude));
                landPos = hit.point;
                break;
            }
            prev = p;
        }

        // 2) 30점 균등 샘플 (+ 점선 반복 횟수 = 길이 / 0.15m)
        float length = 0f; Vector3 last = origin;
        for (int i = 0; i < Points; i++)
        {
            float t = landT * i / (Points - 1);
            Vector3 p = origin + velocity * t + 0.5f * g * t * t;
            arc.SetPosition(i, p);
            length += Vector3.Distance(last, p); last = p;
        }
        dashMat.mainTextureScale = new Vector2(Mathf.Max(1f, length / (Dash + Gap)), 1f);
        Color lc = dangerColor; lc.a = 0.8f;
        arc.startColor = arc.endColor = lc;

        // 3) (이전) 착지 원 (노랑 1m) + 위험 범위 원
        // Color yellow = new Color(1f, 0.9f, 0.2f, 0.95f);
        // landRing.startColor = landRing.endColor = yellow;
        // Color dc = dangerColor; dc.a = 0.8f;
        // rangeRing.startColor = rangeRing.endColor = dc;
        // Circle(landRing, landPos, 1f);
        // Circle(rangeRing, landPos, dangerRadius);
        // (2026-11-18) 같은 색 반투명 원판 (반경 = 폭발/화염 범위)
        Color dcol = dangerColor; dcol.a = 0.3f;
        discMat.color = dcol;
        disc.transform.position = landPos + Vector3.up * 0.12f;
        disc.transform.rotation = Quaternion.identity;
        disc.transform.localScale = new Vector3(dangerRadius, 1f, dangerRadius);
    }

    static void Circle(LineRenderer lr, Vector3 c, float r)
    {
        for (int i = 0; i < CircleSegs; i++)
        {
            float a = i * Mathf.PI * 2f / CircleSegs;
            lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0.12f, Mathf.Sin(a) * r));
        }
    }
}

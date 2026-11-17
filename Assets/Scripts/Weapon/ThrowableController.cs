using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 투척 시스템 (2026-11-16) - 핫바 3번(투척 전용 칸)의 수류탄·화염병. PlayerController.Awake가 자동 부착.
/// PlayerController.HandleFire가 투척물을 들고 있을 때 매 프레임 Tick(data, 좌클릭 누름 여부)을 부름.
///
/// [조작]
///  - 좌클릭 짧게(0.2초 안에 뗌): 바로 던짐 (커서 방향·거리, 궤적 표시 없음)
///  - 좌클릭 꾹(0.2초 이상): 조준 모드 → 포물선 궤적 + 착지 원(노랑 1m) + 위험 범위 원(수류탄 빨강 8m / 화염병 주황 5m)
///  - 조준 중 우클릭: 거리 모드 전환 — 멀리(던지는 힘 15, 최대 15m) ↔ 가까이(힘 7, 최대 5m)
///  - 좌클릭 뗌: 보이던 궤적 그대로 던짐
/// [던지기] 손 높이에서 Rigidbody 투사체 + 초기 속도. 커서까지 거리에 맞춰 40° 각도로 속도를 계산 (최대 = 모드의 힘)
/// 던지면 핫바 개수 -1, 0개가 되면 이전 총 칸(1 또는 2)으로 자동 전환.
/// </summary>
public class ThrowableController : MonoBehaviour
{
    [Header("조작")]
    public float holdThreshold = 0.2f;

    [Header("거리 모드")]
    public float farForce = 15f, farRange = 15f;
    public float nearForce = 7f, nearRange = 5f;
    [Tooltip("던지는 각도 (도)")]
    public float launchAngle = 40f;
    public float minRange = 1.5f;

    [Header("손")]
    public float handHeight = 1.5f;

    PlayerController player;
    Animator animator;
    bool wasHeld;
    float pressTime;
    bool aiming;
    bool nearMode;
    ThrowableData heldData;
    GameObject handVisual;
    TrajectoryRenderer traj;

    /// <summary>조준 중(궤적 표시 중)인지</summary>
    public bool IsAiming => aiming;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        traj = new GameObject("ThrowTrajectory").AddComponent<TrajectoryRenderer>();
        traj.Hide();
    }

    void OnDestroy() { if (traj != null) Destroy(traj.gameObject); }

    /// <summary>투척물을 들고 있는 동안 매 프레임 (held = 좌클릭 누르고 있음)</summary>
    public void Tick(ThrowableData data, bool held)
    {
        ShowInHand(data);
        if (data == null) { Cancel(); return; }

        if (held && !wasHeld) // 누름 시작
        {
            pressTime = Time.time;
            if (data.pinSound != null) AudioManager.PlaySFX(data.pinSound, transform.position + Vector3.up, 1f, AudioManager.Channel.SFX, 20f);
        }
        if (held && !aiming && Time.time - pressTime >= holdThreshold) aiming = true;

        if (aiming && held)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                nearMode = !nearMode;
                PickupFeedbackUI.Instance?.Show(nearMode ? "가까이 던지기 (~5m)" : "멀리 던지기 (~15m)");
            }
            Vector3 v = ComputeVelocity(out Vector3 origin);
            traj.Show(origin, v, data.radius, data.rangeColor);
        }

        if (!held && wasHeld) // 뗌 → 던짐 (짧게 = 즉시, 길게 = 궤적대로. 둘 다 같은 계산)
        {
            Vector3 v = ComputeVelocity(out Vector3 origin);
            Throw(data, origin, v);
            aiming = false;
            traj.Hide();
        }
        wasHeld = held;
    }

    /// <summary>투척물을 내려놓음 (다른 칸 선택, 구르기 등) - 궤적/손 모델 정리</summary>
    public void Cancel()
    {
        aiming = false; wasHeld = false;
        if (traj != null) traj.Hide();
        ShowInHand(null);
    }

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

    void Throw(ThrowableData data, Vector3 origin, Vector3 velocity)
    {
        var hotbar = WeaponHotbarUI.Instance;
        if (hotbar == null || data.throwablePrefab == null) return;

        var obj = Instantiate(data.throwablePrefab, origin, Quaternion.LookRotation(velocity));
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
        if (data.throwableType == ThrowableType.Molotov)
        {
            var m = obj.AddComponent<Molotov>(); m.data = data; m.thrower = gameObject;
        }
        else
        {
            var gr = obj.AddComponent<Grenade>(); gr.data = data; gr.thrower = gameObject;
        }

        if (data.throwSound != null) AudioManager.PlaySFX(data.throwSound, origin, 1f, AudioManager.Channel.SFX, 25f);
        Debug.Log($"[투척] {data.itemName} {(nearMode ? "가까이" : "멀리")} 속도 {velocity.magnitude:F1}");

        hotbar.ConsumeThrowable(hotbar.GetActiveSlot()); // 0개면 핫바가 이전 총 칸으로 전환
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
}

/// <summary>
/// 투척 궤적 (2026-11-16): 포물선 30점 Line Renderer (흰색 → 끝으로 투명, 두께 0.1m)
/// + 착지 지점 노란 원(반경 1m) + 위험 범위 원(수류탄 빨강 8m / 화염병 주황 5m). 지형·벽에 닿는 곳에서 끝남.
/// </summary>
public class TrajectoryRenderer : MonoBehaviour
{
    const int Points = 30;
    const int CircleSegs = 48;
    const float MaxTime = 4f;

    LineRenderer arc, landRing, rangeRing;
    static Material mat;

    void Awake()
    {
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default")) { name = "Trajectory(Runtime)" };
        arc = NewLine("Arc", 0.1f);
        arc.positionCount = Points;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.05f, 1f) });
        arc.colorGradient = g;
        landRing = NewLine("LandRing", 0.06f); landRing.loop = true; landRing.positionCount = CircleSegs;
        rangeRing = NewLine("RangeRing", 0.12f); rangeRing.loop = true; rangeRing.positionCount = CircleSegs;
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

        // 2) 30점 균등 샘플
        for (int i = 0; i < Points; i++)
        {
            float t = landT * i / (Points - 1);
            arc.SetPosition(i, origin + velocity * t + 0.5f * g * t * t);
        }

        // 3) 착지 원 (노랑 1m) + 위험 범위 원
        Color yellow = new Color(1f, 0.9f, 0.2f, 0.95f);
        landRing.startColor = landRing.endColor = yellow;
        Color dc = dangerColor; dc.a = 0.8f;
        rangeRing.startColor = rangeRing.endColor = dc;
        Circle(landRing, landPos, 1f);
        Circle(rangeRing, landPos, dangerRadius);
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

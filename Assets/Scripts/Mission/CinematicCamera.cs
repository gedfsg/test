using System.Collections;
using UnityEngine;

/// <summary>
/// 컷신용 카메라 (탈출 헬기 컷신 - HelicopterCutscene).
///  - Begin(): 메인 카메라 설정을 복사한 시네마틱 카메라를 켜고 메인 카메라는 끔 (MainCamera 태그·오디오 리스너도 넘김 →
///    컷신 중 Camera.main을 쓰는 스크립트가 끊기지 않음)
///  - SetShot(): 샷 종류마다 매 프레임 목표 위치·방향을 계산하고, 샷이 바뀌면 코루틴으로 1.2초 동안 위치·각도를 부드럽게 옮김
///  - End(): 메인 카메라 복원
/// </summary>
public class CinematicCamera : MonoBehaviour
{
    public enum Shot { Approach, Wide, PlayerBoarding, ZombiesLookUp }

    /// <summary>샷이 따라갈 대상 (컷신이 채움)</summary>
    public class Targets
    {
        public Transform heli;
        public Transform player;
        public Vector3 pad;          // 헬리패드 바닥
        public Vector3 approachFrom; // 헬기가 날아온 쪽 (수평 단위 벡터)
    }

    public static CinematicCamera Instance { get; private set; }
    public static bool Active => Instance != null;
    public Camera Cam { get; private set; }

    const float BlendTime = 1.2f;

    Camera mainCam;
    AudioListener mainListener;
    bool mainListenerWasOn;           // AudioManager가 카메라 리스너를 꺼 두므로 원래 상태 그대로 되돌림
    Targets targets;
    Shot shot;
    float blend = 1f;                 // 1 = 샷 목표를 그대로 따라감
    Vector3 fromPos; Quaternion fromRot;
    Coroutine blendRoutine;
    float yaw;                        // 가려질 때 샷을 대상 둘레로 돌리는 각도
    float near = 1f;                  // 그래도 가리면 대상 쪽으로 당김 (1 = 원래 거리)
    float lift;                       // 어느 각도도 트이지 않으면 카메라를 위로 올림 (m)
    float nextClearCheck;
    static readonly float[] YawTries = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
    // 콜라이더 없는 장식(숲 나무·덤불 등)도 화면을 가림 → 컷신 동안만 같은 모양의 가림 검사용 콜라이더를
    // 아무것과도 안 부딪히는 레이어에 임시 생성 (범위(AABB)로 검사하면 큰 나무 범위가 헬리패드까지 덮어서 모든 각도가 막힘)
    readonly System.Collections.Generic.List<GameObject> proxies = new System.Collections.Generic.List<GameObject>();
    const int ProxyLayer = 31;

    public static CinematicCamera Begin(Targets targets)
    {
        if (Instance != null) { Instance.targets = targets; return Instance; }
        var main = Camera.main;
        var go = new GameObject("CinematicCamera");
        var cc = go.AddComponent<CinematicCamera>();
        Instance = cc;
        cc.targets = targets;
        cc.Cam = go.AddComponent<Camera>();
        if (main != null)
        {
            cc.Cam.CopyFrom(main);
            // 게임 카메라는 직교(orthographic) 탑뷰라 그대로 복사하면 화면이 좁고 원근감이 없음 → 영화처럼 원근 카메라로
            cc.Cam.orthographic = false;
            cc.Cam.fieldOfView = 55f;
            cc.Cam.nearClipPlane = 0.3f;
            // 탑뷰 카메라는 하늘을 볼 일이 없어 배경이 검정 → 올려다보는 샷용 하늘 (스카이박스가 있으면 그것, 없으면 안개색 기반)
            if (RenderSettings.skybox != null) cc.Cam.clearFlags = CameraClearFlags.Skybox;
            else
            {
                cc.Cam.clearFlags = CameraClearFlags.SolidColor;
                cc.Cam.backgroundColor = RenderSettings.fogColor; // 낮엔 밝은 안개색, 밤엔 어두운 안개색 → 먼 건물과 자연스럽게 이어짐
            }
            cc.Cam.farClipPlane = Mathf.Max(main.farClipPlane, 400f); // 80m 위 헬기 + 멀리 날아가는 모습까지
            go.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            cc.mainCam = main;
            cc.mainListener = main.GetComponent<AudioListener>();
            cc.mainListenerWasOn = cc.mainListener != null && cc.mainListener.enabled;
            main.enabled = false;
            main.tag = "Untagged";
            if (cc.mainListener != null) cc.mainListener.enabled = false;
        }
        go.tag = "MainCamera";
        // 소리 듣는 위치: 메인 카메라에 있던 리스너만 넘겨받음 (플레이어 등 다른 곳에 리스너가 있으면 그대로 둠 - 2개면 경고)
        bool otherListener = false;
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.isActiveAndEnabled) otherListener = true;
        if (!otherListener) go.AddComponent<AudioListener>();
        cc.CollectOccluders();
        return cc;
    }

    public static void SetShot(Shot type)
    {
        if (Instance == null) return;
        Instance.StartShot(type);
    }

    public static void End()
    {
        if (Instance == null) return;
        var cc = Instance;
        if (cc.mainCam != null)
        {
            cc.mainCam.tag = "MainCamera";
            cc.mainCam.enabled = true;
            if (cc.mainListener != null) cc.mainListener.enabled = cc.mainListenerWasOn;
        }
        Instance = null;
        Destroy(cc.gameObject);
    }

    void StartShot(Shot type)
    {
        shot = type;
        yaw = PickYaw();
        StartBlendFromHere();
    }

    void StartBlendFromHere()
    {
        fromPos = transform.position;
        fromRot = transform.rotation;
        if (blendRoutine != null) StopCoroutine(blendRoutine);
        blendRoutine = StartCoroutine(Blend());
    }

    // 헬리패드가 숲·건물 옆이면 정해진 자리가 나무 속이거나 벽에 가려짐 → 대상 둘레로 돌려 트인 각도를 고름
    float PickYaw()
    {
        foreach (float up in new[] { 0f, 20f })
            foreach (float n in new[] { 1f, 0.6f })
                foreach (float y in YawTries)
                {
                    near = n; lift = up;
                    Pose(shot, y, out Vector3 pos, out Vector3 look);
                    if (Clear(pos, look)) return y;
                }
        near = 1f; lift = 20f; // 다 막히면 높이 올린 원래 각도 (나무 숲 위에서 내려다봄)
        return 0f;
    }

    void CollectOccluders()
    {
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(ProxyLayer, i, true); // 검사 전용 (몸·총알과 안 부딪힘)
        Vector3 pad = targets.pad;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r is SkinnedMeshRenderer || r is ParticleSystemRenderer || r is LineRenderer) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.5f) continue;                                               // 바닥·납작한 것 제외
            if ((b.ClosestPoint(pad) - pad).sqrMagnitude > 60f * 60f) continue;
            if (r.GetComponentInParent<EscapePoint>() != null) continue;
            if (r.GetComponent<Collider>() != null) continue;                            // 콜라이더가 있으면 그대로 검사됨
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var proxy = new GameObject("CinematicOccluder");
            proxy.layer = ProxyLayer;
            proxy.transform.SetParent(r.transform, false);
            proxy.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            proxies.Add(proxy);
        }
    }

    bool Clear(Vector3 pos, Vector3 look)
    {
        if (Physics.CheckSphere(pos, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return false;

        foreach (var h in Physics.RaycastAll(pos, (look - pos).normalized, Vector3.Distance(pos, look) - 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<ZombieController>() != null || h.collider.CompareTag("Player")) continue; // 사람·좀비는 가려도 됨
            return false;
        }
        return true;
    }

    void Pose(Shot s, float yawDeg, out Vector3 pos, out Vector3 look)
    {
        ShotPose(s, out pos, out look);
        if (yawDeg != 0f) pos = look + Quaternion.Euler(0f, yawDeg, 0f) * (pos - look);
        if (near < 1f) pos = look + (pos - look) * near;
        pos.y += lift;
    }

    // 이전 카메라 자리에서 새 샷 자리로 위치·각도 Lerp (부드럽게 시작·끝)
    IEnumerator Blend()
    {
        blend = 0f;
        for (float t = 0f; t < BlendTime; t += Time.deltaTime)
        {
            blend = Mathf.SmoothStep(0f, 1f, t / BlendTime);
            yield return null;
        }
        blend = 1f;
        blendRoutine = null;
    }

    void LateUpdate()
    {
        if (targets == null) return;
        // 움직이는 샷은 가는 도중 가려질 수 있음 → 가끔 확인해서 가려졌으면 다른 각도로 부드럽게 옮김
        if (blend >= 1f && Time.time >= nextClearCheck)
        {
            nextClearCheck = Time.time + 0.5f;
            Pose(shot, yaw, out Vector3 p0, out Vector3 l0);
            if (!Clear(p0, l0)) { float oldNear = near, oldLift = lift; float y = PickYaw(); if (y != yaw || near != oldNear || lift != oldLift) { yaw = y; StartBlendFromHere(); } }
        }
        Pose(shot, yaw, out Vector3 pos, out Vector3 look);
        Quaternion rot = Quaternion.LookRotation((look - pos).sqrMagnitude > 0.01f ? look - pos : Vector3.forward);
        transform.SetPositionAndRotation(Vector3.Lerp(fromPos, pos, blend), Quaternion.Slerp(fromRot, rot, blend));
    }

    void ShotPose(Shot s, out Vector3 pos, out Vector3 look)
    {
        Vector3 pad = targets.pad;
        Vector3 heli = targets.heli != null ? targets.heli.position : pad + Vector3.up * 10f;
        Vector3 back = targets.approachFrom;                         // 헬기가 온 쪽
        Vector3 side = Vector3.Cross(Vector3.up, back).normalized;   // 그 옆
        switch (s)
        {
            case Shot.Approach:
            {
                // 헬기 뒤 위쪽에서 따라가며 헬기와 헬리패드를 같이 봄
                Vector3 fwd = targets.heli != null ? targets.heli.forward : -back; fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = -back;
                fwd.Normalize();
                pos = heli - fwd * 22f + Vector3.up * 7f + side * 6f;
                look = Vector3.Lerp(heli, pad, 0.35f);
                break;
            }
            case Shot.Wide:
                // 헬리패드 반대편 높은 곳에서 넓게: 플레이어·호버링 헬기·몰려오는 좀비 떼
                pos = pad - back * 38f + side * 14f + Vector3.up * 30f;
                look = pad + Vector3.up * 3f;
                break;
            case Shot.PlayerBoarding:
            {
                // 올라가는 플레이어 옆 가까이 (플레이어 높이를 따라 올라감)
                Vector3 p = targets.player != null ? targets.player.position : pad;
                pos = new Vector3(pad.x, p.y + 1.5f, pad.z) + side * 9f - back * 4f;
                look = p + Vector3.up * 1.4f;
                break;
            }
            default: // ZombiesLookUp
                // 땅 높이에서 좀비들 사이로 하늘의 헬기를 올려다봄
                // 헬기가 날아가는 반대편 땅에서 헬리패드 너머를 봄 → 앞쪽엔 올려다보는 좀비 떼, 하늘엔 멀어지는 헬기
                pos = pad + back * 14f + side * 4f + Vector3.up * 1.8f;
                look = Vector3.Lerp(pad + Vector3.up * 1.5f, heli, 0.5f);
                break;
        }
    }

    void OnDestroy()
    {
        foreach (var p in proxies) if (p != null) Destroy(p);
        if (Instance == this) Instance = null;
    }
}

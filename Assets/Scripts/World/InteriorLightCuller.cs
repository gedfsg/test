using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실내 조명 거리 컬링 (2026-11-20).
/// InteriorDecorBuilder가 건물 98채에 놓은 방 조명(Point, 약 180개)이 전부 켜져 있으면 근처 가구마다 조명 패스가 늘어
/// (Play 실측: 실내 장식 켬/끔 비교 SetPass +960, 프레임 +7ms) → 플레이어에서 radius(32m) 안의 실내 조명만 켬.
/// 탑뷰 카메라가 보는 범위가 대략 40m라 화면 안 조명은 그대로 켜져 있음.
///  - 대상: 씬 루트 이름이 "Building_Interior_"로 시작하는 오브젝트 아래 Light (Exterior 그룹의 불 조명 등은 제외 안 해도 거리로 같이 처리)
///  - 0.25초마다 갱신, 씬에 따로 둘 필요 없음 (Player가 있는 씬이면 자동 생성)
/// </summary>
public class InteriorLightCuller : MonoBehaviour
{
    public float radius = 32f;
    public float interval = 0.25f;

    readonly List<Light> lights = new List<Light>();
    Transform player;
    float next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (GameObject.FindWithTag("Player") == null || FindFirstObjectByType<InteriorLightCuller>() != null) return;
        new GameObject("InteriorLightCuller").AddComponent<InteriorLightCuller>();
    }

    void Start()
    {
        foreach (var g in gameObject.scene.GetRootGameObjects())
            if (g.name.StartsWith("Building_Interior_"))
                lights.AddRange(g.GetComponentsInChildren<Light>(true));
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        Refresh();
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + interval;
        Refresh();
    }

    void Refresh()
    {
        if (player == null) return;
        Vector3 pp = player.position;
        float r2 = radius * radius;
        foreach (var l in lights)
        {
            if (l == null) continue;
            Vector3 d = l.transform.position - pp; d.y = 0f;
            bool on = d.sqrMagnitude < r2;
            if (l.enabled != on) l.enabled = on;
        }
    }
}

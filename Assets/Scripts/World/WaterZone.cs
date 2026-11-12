using UnityEngine;

/// <summary>
/// 강·물 영역 판정 (2026-11-12).
///
/// 맵 구조 (에디터 실측): 수면은 맵 전체를 덮는 판 하나("WaterBlock_50m", y ≈ 10.9)이고,
/// 지형(Ground) 바닥은 보통 y ≈ 15인데 강줄기만 y ≈ 0까지 파여 있음 → 지면이 수면보다 낮은 곳 = 물속.
/// 강 바닥에도 NavMesh가 깔려 있어서 다리 아래 물속에 좀비가 생기고(ZombieSpawner), 플레이어도 물속으로 걸어 들어갔음.
///
///  - IsUnderwater(p): 그 자리의 바닥이 수면 아래인지 (스폰 거부, NavMesh 굽기 제외 영역 계산)
///  - BlockVelocity(): 플레이어가 물속으로 들어가는 이동 성분을 0으로 (보이지 않는 벽처럼 물가에서 멈춤, 수면 셰이더는 그대로)
/// 다리 위는 레이가 다리 상판(y ≈ 15.6~17.8)에 먼저 닿으므로 물로 판정되지 않음.
/// </summary>
public static class WaterZone
{
    const string WaterSurfaceName = "WaterBlock_50m";
    const float FallbackSurfaceY = 10.9f;

    // 수면보다 이만큼 높은 바닥까지도 물가로 봄 (물에 발을 담그기 직전에 멈추게)
    public const float ShoreMargin = 0.3f;

    static bool searched;
    static bool hasWater;
    static float surfaceY = FallbackSurfaceY;
    static Bounds surfaceBounds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ResetStatics()
    {
        searched = false;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => searched = false;

    /// <summary>수면 높이 (수면 판이 없는 씬이면 물 판정 없음)</summary>
    public static bool TryGetSurface(out float y)
    {
        Find();
        y = surfaceY;
        return hasWater;
    }

    static void Find()
    {
        if (searched) return;
        searched = true;
        hasWater = false;
        var go = GameObject.Find(WaterSurfaceName);
        if (go == null) return;
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        surfaceBounds = r.bounds;
        surfaceY = surfaceBounds.max.y;
        hasWater = true;
    }

    /// <summary>(x, z) 자리의 걸을 수 있는 바닥이 수면 아래인지. fromY = 레이를 쏘기 시작할 높이 (플레이어/후보 위치 + 여유)</summary>
    public static bool IsUnderwater(Vector3 pos, float rayStartAbove = 3f)
    {
        Find();
        if (!hasWater) return false;
        if (pos.x < surfaceBounds.min.x || pos.x > surfaceBounds.max.x || pos.z < surfaceBounds.min.z || pos.z > surfaceBounds.max.z) return false;
        // 위에서 아래로: 처음 닿는 면(다리 상판, 지면)이 수면보다 낮으면 물
        Vector3 from = new Vector3(pos.x, Mathf.Max(pos.y, surfaceY) + rayStartAbove, pos.z);
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, rayStartAbove + 60f, ~0, QueryTriggerInteraction.Ignore))
            return pos.y < surfaceY; // 바닥이 없으면 높이로만 판단
        return hit.point.y < surfaceY + ShoreMargin;
    }

    /// <summary>
    /// 플레이어 이동 속도에서 물속으로 들어가는 수평 성분을 뺌 (X/Z 따로 검사 → 물가를 따라 미끄러지듯 이동 가능).
    /// 이미 물속이면(밀려 들어간 경우 등) 막지 않음 → 걸어서 나올 수 있게.
    /// </summary>
    public static Vector3 BlockVelocity(Vector3 position, Vector3 velocity, float probe = 0.6f)
    {
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        if (flat.sqrMagnitude < 0.0001f) return velocity;
        Find();
        if (!hasWater || IsUnderwater(position)) return velocity;

        Vector3 dir = flat.normalized;
        if (!IsUnderwater(position + dir * probe)) return velocity;

        // 축별로 다시 검사해서 물 쪽 성분만 제거 (물가를 따라 걷기)
        Vector3 v = velocity;
        if (Mathf.Abs(dir.x) > 0.01f && IsUnderwater(position + new Vector3(Mathf.Sign(dir.x) * probe, 0f, 0f))) v.x = 0f;
        if (Mathf.Abs(dir.z) > 0.01f && IsUnderwater(position + new Vector3(0f, 0f, Mathf.Sign(dir.z) * probe))) v.z = 0f;
        // 대각선만 물인 경우 (두 축 각각은 괜찮음) → 전부 멈춤
        if (v.x == velocity.x && v.z == velocity.z) { v.x = 0f; v.z = 0f; }
        return v;
    }
}

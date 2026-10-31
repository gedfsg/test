using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 스폰 보호: 게임 시작(재시작) 후 Duration초 동안 플레이어 Radius 안 좀비는 공격하지 않고 천천히 어슬렁거림 (ZombieRules).
/// 플레이어가 먼저 공격하면 즉시 해제. 화면 중앙에 남은 시간 표시.
/// 씬에 따로 둘 필요 없음 - Player가 있는 씬이면 자동 생성.
/// </summary>
public class SpawnProtection : MonoBehaviour
{
    public const float Duration = 10f, Radius = 30f;

    static float until = -1f;
    static Transform player;

    TextMeshProUGUI countdown;
    bool relocationChecked;

    public static bool Active => player != null && Time.time < until;

    /// <summary>pos의 좀비가 지금 보호에 묶여 있는지</summary>
    public static bool Holds(Vector3 pos)
    {
        if (!Active) return false;
        float dx = pos.x - player.position.x, dz = pos.z - player.position.z;
        return dx * dx + dz * dz < Radius * Radius;
    }

    /// <summary>플레이어가 먼저 공격함 → 즉시 해제</summary>
    public static void Cancel()
    {
        if (!Active) return;
        until = -1f;
        Debug.Log("[SpawnProtection] 플레이어 공격으로 해제");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null || FindFirstObjectByType<SpawnProtection>() != null) return;
        player = p.transform;
        until = Time.time + Duration;
        new GameObject("SpawnProtection").AddComponent<SpawnProtection>();
    }

    void Start()
    {
        var canvasGo = new GameObject("SpawnProtectionUI", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // MissionHUD와 같은 층
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1681f, 936f);
        scaler.matchWidthOrHeight = 0.5f;
        // 화면 중앙보다 조금 위 (플레이어 캐릭터를 가리지 않게)
        countdown = HudStyle.NewText(canvasGo.transform, "Countdown", "", HudStyle.Bold, 40f, new Color(0.45f, 1f, 0.55f), TextAlignmentOptions.Center, new Vector2(0f, 170f), new Vector2(400f, 60f));
    }

    void Update()
    {
        // 건물 이벤트 좀비 등은 각자의 Start에서 생기므로, 전부 끝난 첫 프레임에 시작 지점 주변을 확인
        if (!relocationChecked)
        {
            relocationChecked = true;
            PlayerRandomSpawn.RelocateIfCrowded();
        }

        if (!Active)
        {
            Destroy(gameObject);
            return;
        }
        countdown.text = $"안전 {Mathf.CeilToInt(until - Time.time)}";
    }
}

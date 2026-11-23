using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// (2026-11-24) 빌드 점검용 자동 테스트. 개발 빌드(Development Build)에서 실행 인자 -autotest 를 줬을 때만 동작 (일반 빌드·에디터에선 아무것도 안 함).
///   "My First Game.exe" -autotest -autoshot "C:\경로" -logFile log.txt
///  - 메인 메뉴 → 바로 게임 시작 → 시작 화면(튜토리얼 말풍선) / 다음 안내 / 학교 교실 안 / 경찰서 앞 스크린샷 → 종료
///  - 로그에 [AutoTest] 줄을 남김 (셰이더·텍스처 누락 같은 빌드 전용 문제를 사람이 클릭 없이 확인하려고)
/// </summary>
public class DevAutoTest : MonoBehaviour
{
    static string shotDir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Application.isEditor || !Debug.isDebugBuild) return;
        var args = System.Environment.GetCommandLineArgs();
        bool on = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-autotest") on = true;
            if (args[i] == "-autoshot" && i + 1 < args.Length) shotDir = args[i + 1];
        }
        if (!on) return;
        var go = new GameObject("DevAutoTest");
        DontDestroyOnLoad(go);
        go.AddComponent<DevAutoTest>();
    }

    IEnumerator Start()
    {
        Debug.unityLogger.filterLogType = LogType.Log;   // LogFilter가 일반 로그를 숨기므로 점검 중엔 다시 보이게
        Debug.Log("[AutoTest] 시작 quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()] + " api=" + SystemInfo.graphicsDeviceType);
        yield return new WaitForSecondsRealtime(4f);
        if (SceneManager.GetActiveScene().name == MenuUI.MainMenuScene) { Shot("0_menu"); yield return new WaitForSecondsRealtime(1f); MenuUI.StartGame(); }
        while (FindFirstObjectByType<PlayerController>() == null) yield return null;
        yield return new WaitForSecondsRealtime(4f);
        Shot("1_start");                                             // 시작 튜토리얼 말풍선 + 스포트라이트
        yield return new WaitForSecondsRealtime(1f);
        Debug.Log($"[AutoTest] 튜토리얼={(TutorialManager.Instance != null)} 열림={MenuUI.TutorialOpen} timeScale={Time.timeScale}");

        // 이후엔 튜토리얼 끄고 장소 확인
        TutorialManager.Enabled = false;
        yield return new WaitForSecondsRealtime(1.5f);
        Time.timeScale = 1f;
        foreach (var zs in FindObjectsByType<ZombieSpawner>(FindObjectsSortMode.None)) zs.enabled = false;
        foreach (var z in FindObjectsByType<ZombieController>(FindObjectsSortMode.None)) Destroy(z.gameObject);

        Teleport(new Vector3(-295.2f, 16.8f, 143.6f));             // 학교 교실 B (책상 사이)
        yield return new WaitForSecondsRealtime(3f);
        Diag("school");
        Shot("2_school");

        // 다른 건물 실내도 같은지 (경찰서 안)
        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
            if (LootDatabase.Classify(b) == LootDatabase.BuildingKind.Police)
            { var t = b.GetComponent<Collider>().bounds; if (MissionManager.TryGroundPoint(t.center.x, t.center.z, 15f, out var gi)) Teleport(gi + Vector3.up * 0.3f); break; }
        yield return new WaitForSecondsRealtime(3f);
        Diag("police-inside");
        Shot("2b_police_inside");

        foreach (var b in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
            if (LootDatabase.Classify(b) == LootDatabase.BuildingKind.Police && EntranceMarkers.TryGetDoor(b, out var door, out var outDir)
                && MissionManager.TryGroundPoint(door.x + outDir.x * 6f, door.z + outDir.z * 6f, 15f, out var g))
            { Teleport(g + Vector3.up * 0.3f); break; }
        yield return new WaitForSecondsRealtime(3f);
        Shot("3_police");
        yield return new WaitForSecondsRealtime(1.5f);
        Debug.Log("[AutoTest] 끝");
        Application.Quit();
    }

    static void Diag(string tag)
    {
        var p = GameObject.FindWithTag("Player");
        var cur = BuildingInterior.Current;
        string masks = "";
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.name.StartsWith("Indoor")) masks += $"{r.name}(on={r.enabled && r.gameObject.activeInHierarchy}, sh={(r.sharedMaterial != null ? r.sharedMaterial.shader.name + "/" + r.sharedMaterial.shader.isSupported : "null")}, b={r.bounds.min:F0}..{r.bounds.max:F0}) ";
        Debug.Log($"[AutoTest] {tag}: player={(p != null ? p.transform.position.ToString("F1") : "-")} cam={Camera.main.transform.position:F1} inside={(cur != null ? cur.name : "없음")} masks={masks}");
    }

    static void Teleport(Vector3 pos)
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        var rb = p.GetComponent<Rigidbody>();
        if (rb != null) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
        p.transform.position = pos;
    }

    static void Shot(string name)
    {
        if (string.IsNullOrEmpty(shotDir)) return;
        string path = System.IO.Path.Combine(shotDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("[AutoTest] 스크린샷 " + path);
    }
}

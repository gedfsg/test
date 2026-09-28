using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 좀비 FSM 상태를 화면에 직접 표시하는 디버그 오버레이.
/// 빈 GameObject 하나 만들어서 붙이고 Play하면 됨. Scene 뷰도 선택도 필요 없음.
///
/// 배포 전에 이 컴포넌트를 끄거나 오브젝트를 지울 것.
/// </summary>
public class ZombieDebugHUD : MonoBehaviour
{
    [Header("표시")]
    public bool  showList       = true;   // 좌상단 상태 목록
    public bool  showWorldLabel = true;   // 좀비 머리 위 라벨
    public float refreshInterval = 0.25f;
    public float labelMaxDistance = 60f;

    private readonly List<ZombieController> zombies = new List<ZombieController>();
    private float refreshTimer;
    private Camera cam;
    private GUIStyle boxStyle, labelStyle;

    void Update()
    {
        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = refreshInterval;
            zombies.Clear();
            zombies.AddRange(Object.FindObjectsByType<ZombieController>(FindObjectsSortMode.None));
        }
    }

    void OnGUI()
    {
        if (cam == null) cam = Camera.main;

        if (boxStyle == null)
        {
            boxStyle   = new GUIStyle(GUI.skin.box)   { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, richText = true };
        }

        if (showList) DrawList();
        if (showWorldLabel && cam != null) DrawWorldLabels();
    }

    void DrawList()
    {
        var counts = new Dictionary<ZombieState, int>();
        foreach (var z in zombies)
        {
            if (z == null) continue;
            counts.TryGetValue(z.State, out int c);
            counts[z.State] = c + 1;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b>좀비 {zombies.Count}마리</b>");
        foreach (ZombieState s in System.Enum.GetValues(typeof(ZombieState)))
        {
            counts.TryGetValue(s, out int c);
            if (c == 0) continue;
            sb.AppendLine($"<color=#{ColorOf(s)}>■</color> {s}  {c}");
        }

        GUI.Box(new Rect(10, 10, 170, 22 * (counts.Count + 1) + 12), sb.ToString(), boxStyle);
    }

    void DrawWorldLabels()
    {
        foreach (var z in zombies)
        {
            if (z == null) continue;

            Vector3 world = z.transform.position + Vector3.up * 2.2f;
            if (Vector3.Distance(cam.transform.position, world) > labelMaxDistance) continue;

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) continue;   // 카메라 뒤

            labelStyle.normal.textColor = Color.white;
            GUI.Label(
                new Rect(sp.x - 50f, Screen.height - sp.y - 10f, 100f, 20f),
                $"<color=#{ColorOf(z.State)}>{z.State}</color>",
                labelStyle);
        }
    }

    static string ColorOf(ZombieState s)
    {
        switch (s)
        {
            case ZombieState.Chase:       return "FF4040";
            case ZombieState.Attack:      return "FF40FF";
            case ZombieState.Investigate: return "FFA000";
            case ZombieState.Wander:      return "40FF40";
            case ZombieState.Stunned:     return "40C0FF";
            case ZombieState.Dead:        return "606060";
            default:                      return "C0C0C0";
        }
    }
}

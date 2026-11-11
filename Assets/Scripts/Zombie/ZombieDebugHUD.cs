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

            // 좀비다움(ZombieQuirks): 지금 하는 일(반응 중 / 멈칫 / 가속)과 멍한 개체 표시
            string extra = "";
            var q = z.GetComponent<ZombieQuirks>();
            if (q != null && q.isActiveAndEnabled)
            {
                if (q.IsDull) extra += " <color=#A0A0A0>(멍)</color>";
                if (q.IsLunger) extra += " <color=#FF8080>(돌진형)</color>";
                if (q.IsHesitant) extra += " <color=#80E0FF>(머뭇형)</color>";
                if (!q.Herds)   extra += " <color=#80C0FF>(외톨이)</color>";
                if (!string.IsNullOrEmpty(q.Label)) extra += $" <color=#{QuirkColor(q.Label)}>{q.Label}</color>";
            }

            labelStyle.normal.textColor = Color.white;
            GUI.Label(
                new Rect(sp.x - 90f, Screen.height - sp.y - 10f, 180f, 20f),
                $"<color=#{ColorOf(z.State)}>{z.State}</color>{extra}",
                labelStyle);
        }
    }

    static string QuirkColor(string label)
    {
        switch (label)
        {
            case "반응 중": return "FFFF40";
            case "멍함":   return "A0A0A0";
            case "멈칫":   return "40FFFF";
            case "가속":   return "FF9020";
            case "왁!":    return "FF2020";
            case "노림":   return "FF8C8C";
            case "포위":   return "E64D4D";
            case "돌진":   return "FF40FF";
            case "머뭇":   return "66CCFF";
            case "급발진": return "FF4D99";
            case "헐떡":   return "8080FF";
            case "휘청":   return "FFFFFF";
            default:      return "FFFFFF";
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

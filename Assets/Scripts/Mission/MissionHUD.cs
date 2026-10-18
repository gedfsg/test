using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미션 HUD (오른쪽 위 - 다른 HUD와 안 겹치는 빈 자리) + 목표 방향 화살표.
///
///   메인 미션
///   ○ 7일 생존                3/7일
///   ○ 열쇠 카드 수집           1/4
///      남은 곳: 병원 · 마트 · 교회
///   ○ 탈출 지점 도달        7일차 개방
///   서브 미션                 점수 300
///   ● 좀비 15마리 처치  15/15  점수 300
///
/// 화살표: 다음 목표(가장 가까운 열쇠 카드 → 다 모으면 탈출 지점)가 화면 밖이면 화면 가장자리에 방향 + 거리 표시.
/// MissionManager가 AddComponent로 붙임. 기준 해상도는 다른 HUD와 같은 1681x936.
/// </summary>
public class MissionHUD : MonoBehaviour
{
    const float Width = 340f, LineH = 24f;
    const float ArrowRadius = 130f; // 플레이어 중심에서 화살표까지 (UI px)

    static readonly Color DoneColor = new Color(0.45f, 1f, 0.55f);
    static readonly Color KeyColor = new Color(0.2f, 0.85f, 1f);

    MissionManager mm;
    RectTransform panel;
    Image panelBg;
    readonly List<(Image icon, TextMeshProUGUI label, TextMeshProUGUI value)> rows = new List<(Image, TextMeshProUGUI, TextMeshProUGUI)>();
    TextMeshProUGUI remainText, scoreText;
    RectTransform arrow;
    Image arrowImg;
    TextMeshProUGUI arrowDist;

    void Start()
    {
        mm = GetComponent<MissionManager>();
        Build();
    }

    void Build()
    {
        var canvasGo = new GameObject("MissionHUD", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // 기본 HUD(0) 위, 사망 화면(1000) 아래
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1681f, 936f);
        scaler.matchWidthOrHeight = 0.5f;

        // 패널: 오른쪽 위 고정
        int lines = 1 + 3 + 1 + 1 + mm.subMissions.Count; // 제목 + 메인3 + 남은 곳 + 서브 제목 + 서브들
        float height = lines * LineH + 24f;
        panelBg = HudStyle.NewImage(canvasGo.transform, "Panel", HudStyle.RoundedRect, HudStyle.PanelBg, Vector2.zero, new Vector2(Width, height));
        panel = panelBg.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-20f, -20f);

        float y = -12f - LineH / 2f;
        Header("메인 미션", ref y);
        for (int i = 0; i < 3; i++)
        {
            Row(ref y);
            if (i == 1) { remainText = Text("Remain", "", HudStyle.Regular, 14f, HudStyle.TextDim, TextAlignmentOptions.MidlineLeft, new Vector2(34f, y), Width - 44f); y -= LineH; }
        }
        scoreText = Header("서브 미션", ref y);
        foreach (var _ in mm.subMissions) Row(ref y);

        // 목표 방향 화살표
        arrowImg = HudStyle.NewImage(canvasGo.transform, "ObjectiveArrow", HudStyle.NavArrow != null ? HudStyle.NavArrow : HudStyle.Triangle, KeyColor, Vector2.zero, new Vector2(34f, 34f));
        arrow = arrowImg.rectTransform;
        arrowDist = HudStyle.NewText(canvasGo.transform, "ObjectiveDist", "", HudStyle.SemiBold, 15f, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(120f, 22f));
    }

    TextMeshProUGUI Header(string title, ref float y)
    {
        Text("H_" + title, title, HudStyle.Bold, 17f, HudStyle.Highlight, TextAlignmentOptions.MidlineLeft, new Vector2(14f, y), Width - 28f);
        var right = Text("H_R_" + title, "", HudStyle.SemiBold, 15f, HudStyle.Highlight, TextAlignmentOptions.MidlineRight, new Vector2(14f, y), Width - 28f);
        y -= LineH;
        return right;
    }

    void Row(ref float y)
    {
        var icon = HudStyle.NewImage(panel, "Icon", HudStyle.Ring, HudStyle.TextDim, Vector2.zero, new Vector2(12f, 12f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
        icon.rectTransform.anchoredPosition = new Vector2(22f, y);
        var label = Text("Label", "", HudStyle.Regular, 15f, HudStyle.Text, TextAlignmentOptions.MidlineLeft, new Vector2(34f, y), Width - 44f);
        var value = Text("Value", "", HudStyle.SemiBold, 14f, HudStyle.TextDim, TextAlignmentOptions.MidlineRight, new Vector2(34f, y), Width - 48f);
        rows.Add((icon, label, value));
        y -= LineH;
    }

    // 패널 왼쪽 위 기준 텍스트 (x = 왼쪽 여백, 폭 w)
    TextMeshProUGUI Text(string name, string s, TMP_FontAsset font, float size, Color c, TextAlignmentOptions align, Vector2 pos, float w)
    {
        var t = HudStyle.NewText(panel, name, s, font, size, c, align, Vector2.zero, new Vector2(w, LineH));
        var r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = pos;
        return t;
    }

    void SetRow(int i, string label, string value, bool done, Color? valueColor = null)
    {
        var (icon, l, v) = rows[i];
        icon.sprite = done ? HudStyle.Circle : HudStyle.Ring;
        icon.color = done ? DoneColor : HudStyle.TextDim;
        l.text = label;
        l.color = done ? DoneColor : HudStyle.Text;
        v.text = value;
        v.color = done ? DoneColor : (valueColor ?? HudStyle.TextDim);
    }

    void Update()
    {
        if (mm == null || panel == null) return;

        int escapeDay = mm.EscapeDay;
        SetRow(0, $"{escapeDay}일 생존", $"{Mathf.Min(mm.CurrentDay, escapeDay)}/{escapeDay}일", mm.SurvivedEnough);
        SetRow(1, "열쇠 카드 수집", $"{mm.KeyCards}/{mm.keyCardsRequired}", mm.HasAllKeyCards, KeyColor);
        var remain = new List<string>();
        foreach (var k in mm.keyCardsInWorld) if (k != null) remain.Add(k.buildingLabel);
        remainText.text = remain.Count > 0 ? "남은 곳: " + string.Join(" · ", remain) : "";
        SetRow(2, "탈출 지점 도달", mm.Escaped ? "탈출!" : mm.CanEscape ? "열림!" : $"{escapeDay}일차 개방", mm.Escaped,
               mm.CanEscape ? DoneColor : (Color?)null);

        scoreText.text = $"점수 {mm.Score}";
        for (int i = 0; i < mm.subMissions.Count; i++)
        {
            var s = mm.subMissions[i];
            string prog = s.target > 1 ? $"{Mathf.Min(s.progress, s.target)}/{s.target}  " : "";
            SetRow(3 + i, s.title, prog + s.RewardText, s.Done);
        }

        UpdateArrow();
    }

    /// <summary>다음 목표가 화면 밖이면 화면 가장자리에 방향 화살표 + 거리</summary>
    void UpdateArrow()
    {
        var cam = Camera.main;
        var player = GameObject.FindWithTag("Player");
        Transform target = null;
        Color color = KeyColor;
        if (!mm.HasAllKeyCards)
        {
            float best = float.MaxValue;
            foreach (var k in mm.keyCardsInWorld)
            {
                if (k == null || player == null) continue;
                float d = (k.transform.position - player.transform.position).sqrMagnitude;
                if (d < best) { best = d; target = k.transform; }
            }
        }
        else if (mm.EscapePointObj != null)
        {
            target = mm.EscapePointObj.transform;
            color = mm.CanEscape ? DoneColor : new Color(1f, 0.35f, 0.3f);
        }

        bool show = target != null && cam != null && player != null && !mm.Finished;
        Vector3 vp = show ? cam.WorldToViewportPoint(target.position) : Vector3.zero;
        if (show && vp.z > 0f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f) show = false; // 화면 안에 보이면 숨김
        arrow.gameObject.SetActive(show);
        arrowDist.gameObject.SetActive(show);
        if (!show) return;

        // 플레이어 둘레(arrowRadius) 위에 목표 방향으로 표시 (화면 가장자리에 두면 상단 날짜 HUD/미니맵과 겹침)
        // 방향 = 플레이어 → 목표 수평 방향을 화면에 투영 (멀리 있는 목표도 안정적으로)
        Vector3 pp = player.transform.position;
        Vector3 flat = target.position - pp; flat.y = 0f;
        Vector3 s0 = cam.WorldToScreenPoint(pp), s1 = cam.WorldToScreenPoint(pp + flat.normalized * 3f);
        Vector2 dir = new Vector2(s1.x - s0.x, s1.y - s0.y);
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
        dir.Normalize();

        var canvasRt = (RectTransform)arrow.parent;
        Vector2 playerUi = new Vector2(s0.x / Screen.width - 0.5f, s0.y / Screen.height - 0.5f);
        playerUi.Scale(canvasRt.rect.size);
        Vector2 pos = playerUi + dir * ArrowRadius;
        arrow.anchoredPosition = pos;
        arrow.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f); // 스프라이트 기본 방향 = 위
        arrowImg.color = color;
        arrowDist.rectTransform.anchoredPosition = pos - dir * 32f;
        arrowDist.text = $"{Vector3.Distance(player.transform.position, target.position):F0}m";
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미션 HUD (오른쪽 위 - 다른 HUD와 안 겹치는 빈 자리) + 목표 방향 화살표.
///
///   상단 중앙: 총 진행률: 68% + 진행률 바 (파랑, 100%면 초록)
///
///   메인 미션
///   ○ 7일 생존                3/7일
///   ○ 열쇠 카드 수집           1/4
///   ○ 탈출 지점 도달      100% 필요
///   서브 미션 (Day 3)          오늘 100%
///   ○ 좀비 15마리 처치     7/15 (46%)
///
/// 인벤토리를 열면 패널(가려짐)을 숨기고 한 줄 요약만 표시.
/// 화살표: 열쇠 카드를 다 모으면 탈출 지점이 화면 밖일 때 방향 + 거리 표시 (열쇠 카드 위치는 플레이어가 직접 탐색).
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
    TextMeshProUGUI subHeaderText, subRightText;
    InventoryUI inventoryUI;
    TextMeshProUGUI compactText;  // 인벤토리 열림: 메인 미션 한 줄 요약
    TextMeshProUGUI progressText; // 상단 중앙 총 진행률
    Image progressFill;
    static readonly Color ProgressColor = new Color(0.25f, 0.6f, 1f);
    RectTransform arrow;
    Image arrowImg;
    TextMeshProUGUI arrowDist;

    void Start()
    {
        mm = GetComponent<MissionManager>();
        inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
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

        // 패널: 오른쪽 위 고정 (높이는 오늘 서브 미션 수에 맞춰 Update에서 조절)
        float height = PanelHeight(0);
        panelBg = HudStyle.NewImage(canvasGo.transform, "Panel", HudStyle.RoundedRect, HudStyle.PanelBg, Vector2.zero, new Vector2(Width, height));
        panel = panelBg.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-20f, -20f);

        float y = -12f - LineH / 2f;
        Header("메인 미션", ref y);
        for (int i = 0; i < 3; i++)
        {
            Row(ref y);
        }
        subRightText = Header("서브 미션", ref y);
        subHeaderText = panel.Find("H_서브 미션").GetComponent<TextMeshProUGUI>();
        for (int i = 0; i < MissionManager.MaxDailySubMissions; i++) Row(ref y); // 최대 수만큼 미리 만들고 오늘 것만 보임

        BuildToast(canvasGo.transform);
        BuildProgressBar(canvasGo.transform);

        // 인벤토리 열림: 패널 대신 한 줄 요약 (인벤토리 창 위 빈 자리)
        compactText = HudStyle.NewText(canvasGo.transform, "MissionCompact", "", HudStyle.SemiBold, 15f, HudStyle.Highlight, TextAlignmentOptions.MidlineRight, Vector2.zero, new Vector2(Width, LineH));
        var cr = compactText.rectTransform;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 1f);
        cr.anchoredPosition = new Vector2(-20f, -14f);
        compactText.gameObject.SetActive(false);

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

        UpdateProgressBar();
        // 인벤토리 열림: 패널이 인벤토리를 가리지 않게 숨기고 한 줄만
        bool inventoryOpen = inventoryUI != null && inventoryUI.isInventoryOpen;
        panel.gameObject.SetActive(!inventoryOpen);
        compactText.gameObject.SetActive(inventoryOpen);
        if (inventoryOpen) compactText.text = $"진행률 {Mathf.FloorToInt(mm.TotalProgress)}% · 열쇠 {mm.KeyCards}/{mm.keyCardsRequired}";

        int goalDays = mm.SurvivalGoalDays;
        SetRow(0, $"{goalDays}일 생존", $"{mm.CompletedDays}/{goalDays}일", mm.SurvivedEnough);
        SetRow(1, "열쇠 카드 수집", $"{mm.KeyCards}/{mm.keyCardsRequired}", mm.HasAllKeyCards, KeyColor);
        SetRow(2, "탈출 지점 도달", mm.Escaped ? "탈출!" : mm.CanEscape ? "열림!" : "100% 필요", mm.Escaped,
               mm.CanEscape ? DoneColor : (Color?)null);

        subHeaderText.text = $"서브 미션 (Day {mm.CurrentDay})";
        subRightText.text = mm.TodayAllDone ? "오늘 100%" : "";
        var shown = mm.subMissions;
        for (int i = 0; i < MissionManager.MaxDailySubMissions; i++)
        {
            bool visible = i < shown.Count;
            var (icon, l, v) = rows[3 + i];
            icon.gameObject.SetActive(visible); l.gameObject.SetActive(visible); v.gameObject.SetActive(visible);
            if (!visible) continue;
            var s = shown[i];
            SetRow(3 + i, s.title, $"{Mathf.Min(s.progress, s.target)}/{s.target} ({s.Percent}%)", s.Done);
        }
        panel.sizeDelta = new Vector2(Width, PanelHeight(shown.Count));

        UpdateToast();
        UpdateArrow();
    }

    // 제목 + 메인3 + 서브 제목 + 서브
    static float PanelHeight(int subRows) => (1 + 3 + 1 + subRows) * LineH + 24f;

    // ── 상단 중앙 총 진행률 (날짜/시간 HUD 바로 아래) ──
    void BuildProgressBar(Transform canvas)
    {
        var root = new GameObject("TotalProgress", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvas, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(-25f, -150f); // SurvivalDayHUD와 같은 중심선
        progressText = HudStyle.NewText(root, "Label", "", HudStyle.SemiBold, 16f, Color.white, TextAlignmentOptions.Center, new Vector2(0f, 8f), new Vector2(300f, 22f));
        var bg = HudStyle.NewImage(root, "Bar", null, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -10f), new Vector2(300f, 10f));
        progressFill = HudStyle.NewImage(bg.rectTransform, "Fill", null, ProgressColor, Vector2.zero, Vector2.zero);
        var f = progressFill.rectTransform;
        f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(0f, 1f); f.pivot = new Vector2(0f, 0.5f);
        f.anchoredPosition = Vector2.zero; f.sizeDelta = Vector2.zero;
    }

    void UpdateProgressBar()
    {
        float p = mm.TotalProgress;
        progressText.text = $"총 진행률: {Mathf.FloorToInt(p)}%";
        progressFill.rectTransform.anchorMax = new Vector2(p / 100f, 1f);
        progressFill.color = p >= 100f ? DoneColor : ProgressColor;
    }

    // ── 토스트: 서브 미션 활성화 알림 (화면 위 가운데, 2초 후 사라짐) ──
    const float ToastDuration = 2f, ToastFade = 0.3f;
    TextMeshProUGUI toastTitle, toastSub;
    CanvasGroup toastGroup;
    readonly Queue<(string title, string sub)> toastQueue = new Queue<(string, string)>();
    float toastUntil = -1f;

    /// <summary>토스트 표시 (여러 개가 한꺼번에 오면 2초씩 차례로)</summary>
    public void ShowToast(string title, string sub) => toastQueue.Enqueue((title, sub));

    void BuildToast(Transform canvas)
    {
        var bg = HudStyle.NewImage(canvas, "MissionToast", HudStyle.RoundedRect, HudStyle.PanelBg, Vector2.zero, new Vector2(420f, 64f));
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -185f); // 상단 날짜/시간 HUD + 진행률 바 아래
        toastGroup = bg.gameObject.AddComponent<CanvasGroup>();
        toastTitle = HudStyle.NewText(rt, "Title", "", HudStyle.Bold, 19f, HudStyle.Highlight, TextAlignmentOptions.Center, new Vector2(0f, 11f), new Vector2(400f, 26f));
        toastSub = HudStyle.NewText(rt, "Sub", "", HudStyle.Regular, 15f, HudStyle.Text, TextAlignmentOptions.Center, new Vector2(0f, -14f), new Vector2(400f, 22f));
        bg.gameObject.SetActive(false);
    }

    void UpdateToast()
    {
        if (toastGroup == null) return;
        if (Time.time >= toastUntil && toastQueue.Count > 0)
        {
            var (title, sub) = toastQueue.Dequeue();
            toastTitle.text = title;
            toastSub.text = sub;
            toastUntil = Time.time + ToastDuration;
        }
        float left = toastUntil - Time.time;
        toastGroup.gameObject.SetActive(left > 0f);
        toastGroup.alpha = Mathf.Clamp01(left / ToastFade); // 마지막 0.3초 동안 사라짐
    }

    /// <summary>다음 목표가 화면 밖이면 화면 가장자리에 방향 화살표 + 거리</summary>
    void UpdateArrow()
    {
        var cam = Camera.main;
        var player = GameObject.FindWithTag("Player");
        Transform target = null;
        Color color = KeyColor;
        // 열쇠 카드 방향은 안 알려줌 (직접 탐색) - 다 모은 뒤에만 탈출 지점 안내
        if (mm.HasAllKeyCards && mm.EscapePointObj != null)
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

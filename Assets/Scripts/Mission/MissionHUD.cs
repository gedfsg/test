using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미션 HUD (오른쪽 위, 레퍼런스 ui.jpg 레이아웃) + 토스트(화면 중앙 하단) + 목표 방향 화살표.
///
///   ┌──────────────────────────────┐
///   │ 진행률                    40% │  ← 주황, % 크게
///   │ ████████░░░░░░░░░░░░░░░░░░░  │  ← 주황 그라데이션 바 (오늘 진행률, 실시간)
///   │                    목표 100% │
///   ├──────────────────────────────┤
///   │ ◆ 메인 미션                   │
///   │ 비상 무전기 가동            ○ │  ← ○ 미완료 / ✓ 완료 (% 없음)
///   ├──────────────────────────────┤
///   │ 🔍 탐색 서브 미션              │
///   │ 📍 주유소 수색          [발견] │  ← 발견된 것만 표시. 발견 / ○ / ✓
///   ├──────────────────────────────┤
///   │    2일차 시작 시 새 미션 개방   │
///   └──────────────────────────────┘
/// 인벤토리를 열면 패널(가려짐)을 숨기고 한 줄 요약만 표시.
/// MissionManager가 AddComponent로 붙임. 기준 해상도는 다른 HUD와 같은 1681x936.
/// (이전: 메인 3줄(7일 생존/열쇠 카드/탈출) + 서브 % 표시 + 상단 중앙 총 진행률 바 → git 기록 참고)
/// </summary>
public class MissionHUD : MonoBehaviour
{
    public enum ToastKind { Discovered, MainComplete, SubComplete, NewDay, Info }

    const float Width = 340f, Pad = 18f, RowH = 26f;
    const float ArrowRadius = 130f; // 플레이어 중심에서 화살표까지 (UI px)

    // 색상 (요청 스펙)
    static readonly Color Orange = new Color32(0xFF, 0xA5, 0x00, 0xFF);   // 제목 / % / 바
    static readonly Color Gray = new Color32(0x9E, 0x9E, 0x9E, 0xFF);     // 미완료 ○ / 안내
    static readonly Color Green = new Color32(0x5C, 0xE6, 0x73, 0xFF);    // 완료 ✓
    static readonly Color PanelFill = new Color(0.06f, 0.07f, 0.09f, 0.84f);
    static readonly Color Gold = new Color32(0xC9, 0xA2, 0x55, 0xE6);      // 테두리
    static readonly Color Divider = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color Cyan = new Color32(0x4F, 0xC3, 0xF7, 0xFF);      // 발견 토스트

    MissionManager mm;
    RectTransform panel;
    Image panelFrame;
    TextMeshProUGUI percentText, mainTitle, footerText, emptySubText;
    Image barFill, mainStatus;
    RectTransform div2, subHeader, div3;
    class SubRow { public RectTransform root; public Image pin, status; public TextMeshProUGUI title; public GameObject badge; }
    readonly List<SubRow> subRows = new List<SubRow>();
    InventoryUI inventoryUI;
    TextMeshProUGUI compactText;  // 인벤토리 열림: 한 줄 요약
    RectTransform arrow;
    Image arrowImg;
    TextMeshProUGUI arrowDist;

    void Start()
    {
        mm = GetComponent<MissionManager>();
        inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        Build();
    }

    // ── 생성 ──
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

        // 패널: 오른쪽 위 고정 (높이는 발견된 서브 미션 수에 맞춰 Update에서 조절)
        var bg = HudStyle.NewImage(canvasGo.transform, "Panel", PanelSprite, PanelFill, Vector2.zero, new Vector2(Width, 300f));
        panel = bg.rectTransform;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-20f, -20f);
        panelFrame = HudStyle.NewImage(panel, "Frame", FrameSprite, Gold, Vector2.zero, Vector2.zero);
        bg.pixelsPerUnitMultiplier = panelFrame.pixelsPerUnitMultiplier = FrameScale; // 테두리를 레퍼런스처럼 얇게
        Stretch(panelFrame.rectTransform);

        // 진행률 + 큰 %
        Text(panel, "ProgressLabel", "진행률", HudStyle.Bold, 18f, Orange, TextAlignmentOptions.MidlineLeft, Pad, -30f, 150f);
        percentText = Text(panel, "Percent", "0%", HudStyle.Bold, 32f, Orange, TextAlignmentOptions.MidlineRight, Width - Pad - 150f, -30f, 150f);
        // 진행 바 (어두운 홈 + 주황 그라데이션 채움)
        var track = HudStyle.NewImage(panel, "BarTrack", HudStyle.RoundedRect, new Color(1f, 1f, 1f, 0.1f), Vector2.zero, new Vector2(Width - Pad * 2f, 10f));
        TopLeft(track.rectTransform, Pad, -60f);
        track.pixelsPerUnitMultiplier = 3f;
        barFill = HudStyle.NewImage(track.rectTransform, "Fill", HudStyle.BarOrange, Color.white, Vector2.zero, Vector2.zero);
        if (barFill.sprite == null) barFill.color = Orange;
        var f = barFill.rectTransform;
        f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(0f, 1f); f.pivot = new Vector2(0f, 0.5f);
        f.anchoredPosition = Vector2.zero; f.sizeDelta = Vector2.zero;
        Text(panel, "Goal", "목표 100%", HudStyle.Regular, 12f, Gray, TextAlignmentOptions.MidlineRight, Width - Pad - 150f, -78f, 150f);

        Line(panel, "Div1", -94f);
        // ◆ 메인 미션
        HeaderRow(panel, "MainHeader", HudStyle.Diamond, "메인 미션", -114f);
        mainTitle = Text(panel, "MainTitle", "", HudStyle.SemiBold, 16f, Color.white, TextAlignmentOptions.MidlineLeft, Pad, -142f, Width - Pad * 2f - 30f);
        mainStatus = HudStyle.NewImage(panel, "MainStatus", HudStyle.Ring, Gray, Vector2.zero, new Vector2(16f, 16f));
        TopLeft(mainStatus.rectTransform, Width - Pad - 16f, -142f, centerY: true);

        div2 = Line(panel, "Div2", -164f);
        // 🔍 탐색 서브 미션
        subHeader = HeaderRow(panel, "SubHeader", HudStyle.Search, "탐색 서브 미션", -184f);
        for (int i = 0; i < MissionManager.MaxDailySubMissions; i++) subRows.Add(SubRowAt(-212f - i * RowH));
        emptySubText = Text(panel, "SubEmpty", "주변을 탐색해 서브 미션을 찾으세요", HudStyle.Regular, 13f, Gray, TextAlignmentOptions.MidlineLeft, Pad, -212f, Width - Pad * 2f);

        div3 = Line(panel, "Div3", -236f);
        footerText = Text(panel, "Footer", "", HudStyle.Regular, 13f, Gray, TextAlignmentOptions.Center, 0f, -256f, Width);

        BuildToast(canvasGo.transform);

        // 인벤토리 열림: 패널 대신 한 줄 요약 (인벤토리 창 위 빈 자리)
        compactText = HudStyle.NewText(canvasGo.transform, "MissionCompact", "", HudStyle.SemiBold, 15f, Orange, TextAlignmentOptions.MidlineRight, Vector2.zero, new Vector2(Width, 24f));
        var cr = compactText.rectTransform;
        cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 1f);
        cr.anchoredPosition = new Vector2(-20f, -14f);
        compactText.gameObject.SetActive(false);

        // 목표 방향 화살표
        arrowImg = HudStyle.NewImage(canvasGo.transform, "ObjectiveArrow", HudStyle.NavArrow != null ? HudStyle.NavArrow : HudStyle.Triangle, Orange, Vector2.zero, new Vector2(34f, 34f));
        arrow = arrowImg.rectTransform;
        arrowDist = HudStyle.NewText(canvasGo.transform, "ObjectiveDist", "", HudStyle.SemiBold, 15f, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(120f, 22f));
    }

    // 모서리 깎인 패널 스프라이트 (없으면 둥근 사각형 - Tools/HUD/Build Mission HUD Sprites)
    const float FrameScale = 1.6f; // 9-slice 테두리 배율 (클수록 얇은 테두리 / 작은 모서리)
    static Sprite PanelSprite => HudStyle.ChamferPanel != null ? HudStyle.ChamferPanel : HudStyle.RoundedRect;
    static Sprite FrameSprite => HudStyle.ChamferFrame != null ? HudStyle.ChamferFrame : HudStyle.RoundedFrame;

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    // 패널 왼쪽 위 기준 배치 (x = 왼쪽 끝, y = 중심 높이)
    static void TopLeft(RectTransform r, float x, float y, bool centerY = true)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, centerY ? 0.5f : 1f);
        r.anchoredPosition = new Vector2(x, y);
    }

    static TextMeshProUGUI Text(Transform parent, string name, string s, TMP_FontAsset font, float size, Color c, TextAlignmentOptions align, float x, float y, float w)
    {
        var t = HudStyle.NewText(parent, name, s, font, size, c, align, Vector2.zero, new Vector2(w, RowH));
        TopLeft(t.rectTransform, x, y);
        return t;
    }

    static RectTransform Line(Transform parent, string name, float y)
    {
        var img = HudStyle.NewImage(parent, name, null, Divider, Vector2.zero, new Vector2(Width - Pad * 2f + 8f, 1f));
        TopLeft(img.rectTransform, Pad - 4f, y);
        return img.rectTransform;
    }

    // 아이콘 + 주황 소제목 (행 전체를 묶은 RectTransform 반환 → 위치 이동용)
    static RectTransform HeaderRow(Transform parent, string name, Sprite icon, string title, float y)
    {
        var row = HudStyle.NewRect(parent, name, Vector2.zero, new Vector2(Width, RowH));
        TopLeft(row, 0f, y);
        var ic = HudStyle.NewImage(row, "Icon", icon, Orange, Vector2.zero, new Vector2(14f, 14f));
        TopLeft(ic.rectTransform, Pad, -RowH / 2f);
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0f, 1f);
        Text(row, "Title", title, HudStyle.Bold, 16f, Orange, TextAlignmentOptions.MidlineLeft, Pad + 20f, -RowH / 2f, Width - Pad * 2f);
        return row;
    }

    SubRow SubRowAt(float y)
    {
        var r = new SubRow { root = HudStyle.NewRect(panel, "SubRow", Vector2.zero, new Vector2(Width, RowH)) };
        TopLeft(r.root, 0f, y);
        r.pin = HudStyle.NewImage(r.root, "Pin", HudStyle.Pin, Orange, Vector2.zero, new Vector2(13f, 13f));
        TopLeft(r.pin.rectTransform, Pad + 1f, -RowH / 2f);
        r.title = Text(r.root, "Title", "", HudStyle.Regular, 15f, Color.white, TextAlignmentOptions.MidlineLeft, Pad + 20f, -RowH / 2f, Width - Pad * 2f - 80f);
        r.status = HudStyle.NewImage(r.root, "Status", HudStyle.Ring, Gray, Vector2.zero, new Vector2(16f, 16f));
        TopLeft(r.status.rectTransform, Width - Pad - 16f, -RowH / 2f);
        // "발견" 라벨: 주황 배경 + 흰 글씨
        var badge = HudStyle.NewImage(r.root, "Badge", PanelSprite, Orange, Vector2.zero, new Vector2(42f, 20f));
        TopLeft(badge.rectTransform, Width - Pad - 42f, -RowH / 2f);
        badge.pixelsPerUnitMultiplier = 3f;
        HudStyle.NewText(badge.rectTransform, "Text", "발견", HudStyle.Bold, 12f, Color.white, TextAlignmentOptions.Center, Vector2.zero, new Vector2(42f, 20f));
        r.badge = badge.gameObject;
        return r;
    }

    // ── 갱신 ──
    void Update()
    {
        if (mm == null || panel == null) return;

        // 인벤토리 열림: 패널이 인벤토리를 가리지 않게 숨기고 한 줄만
        bool inventoryOpen = inventoryUI != null && inventoryUI.isInventoryOpen;
        panel.gameObject.SetActive(!inventoryOpen);
        compactText.gameObject.SetActive(inventoryOpen);
        float p = mm.DayProgress;
        if (inventoryOpen) compactText.text = $"진행률 {Mathf.FloorToInt(p)}% · 메인 미션 {(mm.MainDone ? "완료" : "진행 중")}";

        percentText.text = $"{Mathf.FloorToInt(p)}%";
        barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(p / 100f), 1f);

        // 메인 미션: 미션명 (+ 진행 수) + ○/✓
        var main = mm.CurrentMain;
        string detail = string.IsNullOrEmpty(mm.MainDetail) || mm.MainDone ? "" : $"  <size=13><color=#9E9E9E>{mm.MainDetail}</color></size>";
        mainTitle.text = main != null ? main.title + detail : "-";
        SetStatus(mainStatus, mm.MainDone);

        // 서브 미션: 발견된 것만
        int shown = 0;
        foreach (var s in mm.subMissions)
        {
            if (!s.Discovered || shown >= subRows.Count) continue;
            var row = subRows[shown++];
            row.root.gameObject.SetActive(true);
            bool countable = s.target > 1 && !s.Done;
            row.title.text = s.title + (countable && s.progress > 0 ? $"  <size=12><color=#9E9E9E>{s.progress}/{s.target}</color></size>" : "");
            row.title.color = s.Done ? Green : Color.white;
            row.pin.sprite = s.Done ? HudStyle.Check : HudStyle.Pin;
            row.pin.color = s.Done ? Green : Orange;
            bool fresh = !s.Done && s.progress == 0; // 막 발견됨 (아직 진행 없음) → "발견" 라벨
            row.badge.SetActive(fresh);
            row.status.gameObject.SetActive(!fresh);
            SetStatus(row.status, s.Done);
        }
        for (int i = shown; i < subRows.Count; i++) subRows[i].root.gameObject.SetActive(false);
        emptySubText.gameObject.SetActive(shown == 0);

        // 아래쪽 줄 위치 (서브 줄 수에 맞춰)
        int lines = Mathf.Max(1, shown);
        float subEnd = -212f - (lines - 1) * RowH;
        TopLeft(div3, Pad - 4f, subEnd - 22f);
        TopLeft(footerText.rectTransform, 0f, subEnd - 42f);
        footerText.text = mm.FooterText;
        panel.sizeDelta = new Vector2(Width, -(subEnd - 42f) + 22f);

        UpdateToast();
        UpdateArrow();
    }

    static void SetStatus(Image img, bool done)
    {
        img.sprite = done ? HudStyle.Check : HudStyle.Ring;
        img.color = done ? Green : Gray;
    }

    // ── 토스트: 화면 중앙 하단 (핫바 위), 2초 후 사라짐 ──
    const float ToastDuration = 2f, ToastFade = 0.3f;
    Image toastFrame, toastIcon;
    TextMeshProUGUI toastTitle, toastSub;
    CanvasGroup toastGroup;
    readonly Queue<(ToastKind kind, string title, string sub)> toastQueue = new Queue<(ToastKind, string, string)>();
    float toastUntil = -1f;

    /// <summary>토스트 표시 (여러 개가 한꺼번에 오면 2초씩 차례로)</summary>
    public void ShowToast(ToastKind kind, string title, string sub) => toastQueue.Enqueue((kind, title, sub));
    public void ShowToast(string title, string sub) => ShowToast(ToastKind.Info, title, sub); // 기존 호출 호환

    void BuildToast(Transform canvas)
    {
        var bg = HudStyle.NewImage(canvas, "MissionToast", PanelSprite, PanelFill, Vector2.zero, new Vector2(300f, 62f));
        var rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 150f); // 핫바 위
        toastGroup = bg.gameObject.AddComponent<CanvasGroup>();
        toastFrame = HudStyle.NewImage(rt, "Frame", FrameSprite, Cyan, Vector2.zero, Vector2.zero);
        bg.pixelsPerUnitMultiplier = toastFrame.pixelsPerUnitMultiplier = FrameScale;
        Stretch(toastFrame.rectTransform);
        toastIcon = HudStyle.NewImage(rt, "Icon", HudStyle.Pin, Cyan, new Vector2(-118f, 0f), new Vector2(24f, 24f));
        HudStyle.NewImage(rt, "IconDivider", null, new Color(1f, 1f, 1f, 0.2f), new Vector2(-96f, 0f), new Vector2(1f, 34f));
        toastTitle = HudStyle.NewText(rt, "Title", "", HudStyle.SemiBold, 13f, Cyan, TextAlignmentOptions.MidlineLeft, new Vector2(42f, 11f), new Vector2(256f, 20f));
        toastSub = HudStyle.NewText(rt, "Sub", "", HudStyle.Bold, 17f, Color.white, TextAlignmentOptions.MidlineLeft, new Vector2(42f, -10f), new Vector2(256f, 24f));
        bg.gameObject.SetActive(false);
    }

    void UpdateToast()
    {
        if (toastGroup == null) return;
        if (Time.time >= toastUntil && toastQueue.Count > 0)
        {
            var (kind, title, sub) = toastQueue.Dequeue();
            Color c = kind switch { ToastKind.Discovered => Cyan, ToastKind.SubComplete => Green, ToastKind.Info => Color.white, _ => Orange };
            toastIcon.sprite = kind switch { ToastKind.Discovered => HudStyle.Pin, ToastKind.NewDay => HudStyle.Diamond, ToastKind.Info => HudStyle.Diamond, _ => HudStyle.Check };
            toastIcon.color = toastTitle.color = c;
            toastFrame.color = new Color(c.r, c.g, c.b, 0.85f);
            toastTitle.text = title;
            toastSub.text = sub;
            // 긴 문구는 폭을 늘림
            float w = Mathf.Clamp(Mathf.Max(toastSub.GetPreferredValues(sub).x, toastTitle.GetPreferredValues(title).x) + 110f, 300f, 560f);
            ((RectTransform)toastGroup.transform).sizeDelta = new Vector2(w, 62f);
            toastIcon.rectTransform.anchoredPosition = new Vector2(-w / 2f + 32f, 0f);
            toastGroup.transform.Find("IconDivider").GetComponent<RectTransform>().anchoredPosition = new Vector2(-w / 2f + 54f, 0f);
            toastTitle.rectTransform.anchoredPosition = new Vector2(-w / 2f + 66f + 128f, 11f);
            toastSub.rectTransform.anchoredPosition = new Vector2(-w / 2f + 66f + 128f, -10f);
            toastUntil = Time.time + ToastDuration;
        }
        float left = toastUntil - Time.time;
        toastGroup.gameObject.SetActive(left > 0f);
        toastGroup.alpha = Mathf.Clamp01(left / ToastFade); // 마지막 0.3초 동안 사라짐
    }

    /// <summary>메인 미션 목표가 화면 밖이면 플레이어 둘레에 방향 화살표 + 거리 (열쇠 카드/아이템 수집은 직접 탐색이라 없음)</summary>
    void UpdateArrow()
    {
        var cam = Camera.main;
        var player = GameObject.FindWithTag("Player");
        bool show = mm.TryGetGuideTarget(out Vector3 target) && cam != null && player != null;
        Color color = mm.CanEscape ? Green : Orange;

        Vector3 vp = show ? cam.WorldToViewportPoint(target) : Vector3.zero;
        if (show && vp.z > 0f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f) show = false; // 화면 안에 보이면 숨김
        arrow.gameObject.SetActive(show);
        arrowDist.gameObject.SetActive(show);
        if (!show) return;

        // 방향 = 플레이어 → 목표 수평 방향을 화면에 투영 (멀리 있는 목표도 안정적으로)
        Vector3 pp = player.transform.position;
        Vector3 flat = target - pp; flat.y = 0f;
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
        arrowDist.text = $"{Vector3.Distance(pp, target):F0}m";
    }
}

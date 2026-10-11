using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 레퍼런스 UI 이미지(1681x936) 기준으로 씬 HUD Canvas를 배치하는 툴.
/// 섹션별 메뉴(Tools → HUD → Apply Layout → ...)로 나눠져 있고, 재실행해도 안전(같은 값으로 덮어씀).
/// main.unity가 팀원과 병합 충돌 나면 충돌 해결 후 이 메뉴를 다시 실행하면 HUD 배치가 복구됨.
///
/// 좌표는 전부 "레퍼런스 이미지 픽셀" 단위. Canvas Scaler를 레퍼런스 해상도로 맞춰두기 때문에
/// 이미지에서 잰 위치/크기를 그대로 쓰면 됨.
/// </summary>
public static class HudLayoutApplier
{
    public static readonly Vector2 ReferenceResolution = new Vector2(1681f, 936f);

    [MenuItem("Tools/HUD/Apply Layout/1. Minimap")]
    static void ApplyMinimap()
    {
        Canvas canvas = FindHudCanvas();
        if (canvas == null) return;
        SetupScaler(canvas);

        Transform panel = canvas.transform.Find("MinimapPanel");
        if (panel == null) { Debug.LogError("[HudLayout] MinimapPanel 없음"); return; }

        // 레퍼런스: 지름 188px 원, 중심 (116, 122) — 좌상단 기준
        var panelRt = (RectTransform)panel;
        Undo.RecordObject(panelRt, "HUD Minimap");
        SetRect(panelRt, new Vector2(0f, 1f), new Vector2(116f, -122f), new Vector2(188f, 188f));

        // 지도 아래 어두운 바탕 (지도 가장자리 안티앨리어싱 틈 메움)
        var innerBg = ImageAt(panel, "InnerBG", HudStyle.Circle, new Color32(28, 28, 30, 255), 174f);

        // 원형 마스크 + 지도 (링 두께 9~10px 안쪽)
        var maskImg = ImageAt(panel, "MapMask", HudStyle.Circle, Color.white, 170f);
        var mask = maskImg.GetComponent<Mask>();
        if (mask != null) { Undo.RecordObject(mask, "HUD Minimap"); mask.showMaskGraphic = false; }
        var raw = panel.Find("MapMask/MinimapRawImage") as RectTransform;
        if (raw != null) { Undo.RecordObject(raw, "HUD Minimap"); SetRect(raw, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 170f)); }

        // 금속 링 (지도 가장자리를 덮도록 지도보다 위에)
        var ring = ImageAt(panel, "OuterRing", HudStyle.MetalRing, Color.white, 188f);

        // 플레이어 마커: 흰색 내비 화살표 (회전은 MinimapController가 담당)
        var marker = ImageAt(panel, "PlayerMarker", HudStyle.NavArrow, Color.white, 0f);
        SetRect(marker.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 24f));

        // 동/서/남 글자는 레퍼런스에 없음 → 끔 (삭제하지 않음)
        foreach (string dirName in new[] { "Compass_S", "Compass_E", "Compass_W" })
        {
            var t = panel.Find(dirName);
            if (t != null) { Undo.RecordObject(t.gameObject, "HUD Minimap"); t.gameObject.SetActive(false); }
        }

        // N 배지: 링 아래쪽에 걸친 지름 30px 어두운 원 (중심이 지도 중심보다 89px 아래)
        var badge = ImageAt(panel, "NorthBadge", HudStyle.Circle, new Color32(88, 86, 84, 255), 30f, create: true);
        SetRect(badge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1f, -89f), new Vector2(30f, 30f));
        ImageAt(badge.transform, "Fill", HudStyle.Circle, new Color32(37, 37, 40, 255), 27f, create: true);

        var n = panel.Find("Compass_N") ?? badge.transform.Find("Compass_N");
        if (n != null)
        {
            Undo.SetTransformParent(n, badge.transform, "HUD Minimap");
            var nRt = (RectTransform)n;
            SetRect(nRt, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 30f));
            var tmp = n.GetComponent<TextMeshProUGUI>();
            Undo.RecordObject(tmp, "HUD Minimap");
            tmp.text = "N";
            tmp.font = HudStyle.Bold;
            tmp.fontSize = 15f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = false;
            tmp.raycastTarget = false;
        }

        // 그리는 순서: 바탕 → 지도 → 링 → 화살표 → N 배지
        innerBg.transform.SetSiblingIndex(0);
        maskImg.transform.SetSiblingIndex(1);
        ring.transform.SetSiblingIndex(2);
        marker.transform.SetSiblingIndex(3);
        badge.transform.SetAsLastSibling();

        Save("1. Minimap");
    }

    [MenuItem("Tools/HUD/Apply Layout/2. Top Center (Day + Time)")]
    static void ApplyTopCenter()
    {
        Canvas canvas = FindHudCanvas();
        if (canvas == null) return;
        SetupScaler(canvas);

        // 레퍼런스: 블록 중심 x=815.5 (화면 중앙 840.5보다 25px 왼쪽), 제목 줄 y=32
        Transform t = canvas.transform.Find("TopCenterHUD");
        if (t == null)
        {
            var go = new GameObject("TopCenterHUD", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "HUD TopCenter");
            go.transform.SetParent(canvas.transform, false);
            go.layer = canvas.gameObject.layer;
            t = go.transform;
        }
        var rt = (RectTransform)t;
        Undo.RecordObject(rt, "HUD TopCenter");
        SetRect(rt, new Vector2(0.5f, 1f), new Vector2(-25f, -32f), Vector2.zero);
        if (t.GetComponent<SurvivalDayHUD>() == null) Undo.AddComponent<SurvivalDayHUD>(t.gameObject);

        // 기존 사이버펑크 타이머 패널은 끔 (코드/컴포넌트는 유지)
        var oldTimer = canvas.GetComponent<SurvivalTimerUI>();
        if (oldTimer != null) { Undo.RecordObject(oldTimer, "HUD TopCenter"); oldTimer.enabled = false; }

        Save("2. Top Center");
    }

    [MenuItem("Tools/HUD/Apply Layout/3. Status Panel (Bottom Left)")]
    static void ApplyStatusPanel()
    {
        Canvas canvas = FindHudCanvas();
        if (canvas == null) return;
        SetupScaler(canvas);

        // 레퍼런스: 패널 x 27~458, y 661~889 → 중심 (242.5, 775), 화면 아래에서 161
        var rt = GetOrCreateChild(canvas.transform, "StatusPanelHUD");
        Undo.RecordObject(rt, "HUD Status");
        SetRect(rt, new Vector2(0f, 0f), new Vector2(242.5f, 936f - 775f), new Vector2(431f, 228f));
        if (rt.GetComponent<StatusPanelHUD>() == null) Undo.AddComponent<StatusPanelHUD>(rt.gameObject);

        // 기존 상태 패널(팀원 PlayerHealthUI/PlayerStaminaUI 포함)은 끔 (삭제하지 않음)
        var old = canvas.transform.Find("StatusPanel");
        if (old != null) { Undo.RecordObject(old.gameObject, "HUD Status"); old.gameObject.SetActive(false); }

        Save("3. Status Panel");
    }

    const string HotbarSlotPrefabPath = "Assets/Prefab/UI/HUD_HotbarSlot.prefab";
    const string HotbarThemePath = "Assets/Prefab/UI/HUD_UITheme.asset";

    [MenuItem("Tools/HUD/Apply Layout/4. Hotbar")]
    static void ApplyHotbar()
    {
        Canvas canvas = FindHudCanvas();
        if (canvas == null) return;
        SetupScaler(canvas);

        var hotbar = Object.FindFirstObjectByType<WeaponHotbarUI>(FindObjectsInactive.Include);
        if (hotbar == null) { Debug.LogError("[HudLayout] WeaponHotbarUI 없음"); return; }

        // 팀원 코드(WeaponHotbarUI/SlotView)는 그대로 두고, 레퍼런스 스타일의
        // 새 슬롯 프리팹 + 새 테마 에셋을 만들어 씬에서 연결만 바꿈
        UITheme theme = BuildHotbarTheme();
        SlotView slotPrefab = BuildHotbarSlotPrefab();

        var so = new SerializedObject(hotbar);
        so.FindProperty("slotPrefab").objectReferenceValue = slotPrefab;
        so.FindProperty("theme").objectReferenceValue = theme;
        so.ApplyModifiedProperties();

        // 레퍼런스: 슬롯 90x90, 간격 97(틈 7), 묶음 중심 x=829(화면 중앙 -11.5), 세로 중심 y=845.5(아래에서 90.5)
        var rt = (RectTransform)hotbar.transform;
        Undo.RecordObject(rt, "HUD Hotbar");
        SetRect(rt, new Vector2(0.5f, 0f), new Vector2(-11.5f, 936f - 845.5f), new Vector2(5 * 90f + 4 * 7f, 90f));

        var container = rt.Find("SlotContainer") as RectTransform;
        if (container != null)
        {
            Undo.RecordObject(container, "HUD Hotbar");
            SetRect(container, new Vector2(0.5f, 0.5f), Vector2.zero, container.sizeDelta);
            var hlg = container.GetComponent<HorizontalLayoutGroup>();
            Undo.RecordObject(hlg, "HUD Hotbar");
            hlg.spacing = 7f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
        }

        Save("4. Hotbar");
    }

    static UITheme BuildHotbarTheme()
    {
        var theme = AssetDatabase.LoadAssetAtPath<UITheme>(HotbarThemePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, HotbarThemePath);
        }
        // 레퍼런스 핫바: 슬롯 채움 #292A2F, 활성 테두리 #F3D255, 번호는 전부 흰색
        Color slot = new Color32(41, 42, 47, 235);
        theme.slotEmpty = theme.slotFilled = slot;
        theme.consumableBg = theme.weaponBg = theme.materialBg = theme.defaultItemBg = slot;
        theme.slotActive = new Color32(0xF3, 0xD2, 0x55, 0xFF);
        theme.textPrimary = Color.white;
        theme.textMuted = new Color(1f, 1f, 1f, 0.92f);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // SlotView 프리팹 구조(팀원 SlotView 주석 참고)를 그대로 따르되 모양만 레퍼런스대로
    static SlotView BuildHotbarSlotPrefab()
    {
        var root = new GameObject("HUD_HotbarSlot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ((RectTransform)root.transform).sizeDelta = new Vector2(90f, 90f);
        var bg = root.GetComponent<Image>();
        bg.sprite = HudStyle.RoundedRect; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 2f;
        var view = root.AddComponent<SlotView>();

        var icon = HudStyle.NewImage(root.transform, "Icon", null, Color.white, Vector2.zero, Vector2.zero);
        Stretch(icon.rectTransform, 10f);
        icon.preserveAspect = true;

        var nameLabel = HudStyle.NewText(root.transform, "NameLabel", "", HudStyle.Regular, 12f, Color.white,
            TMPro.TextAlignmentOptions.Bottom, new Vector2(0f, -30f), new Vector2(86f, 20f));
        var countLabel = HudStyle.NewText(root.transform, "CountLabel", "", HudStyle.SemiBold, 14f, Color.white,
            TMPro.TextAlignmentOptions.BottomRight, new Vector2(18f, -30f), new Vector2(46f, 20f));

        var frame = HudStyle.NewImage(root.transform, "Frame", HudStyle.RoundedFrame, new Color32(106, 104, 104, 255), Vector2.zero, Vector2.zero);
        frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 2f; Stretch(frame.rectTransform, 0f);
        var active = HudStyle.NewImage(root.transform, "ActiveBar", HudStyle.RoundedFrameThick, Color.white, Vector2.zero, Vector2.zero);
        active.type = Image.Type.Sliced; active.pixelsPerUnitMultiplier = 2f; Stretch(active.rectTransform, -0.5f);

        // 번호: 레퍼런스에서 슬롯 왼쪽 위 모서리 기준 (16, 18) 위치
        var number = HudStyle.NewText(root.transform, "NumberLabel", "1", HudStyle.Regular, 18f, Color.white,
            TMPro.TextAlignmentOptions.Center, Vector2.zero, new Vector2(24f, 24f));
        number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(0f, 1f);
        number.rectTransform.anchoredPosition = new Vector2(16f, -18f);

        var so = new SerializedObject(view);
        so.FindProperty("background").objectReferenceValue = bg;
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("outline").objectReferenceValue = null;
        so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("countLabel").objectReferenceValue = countLabel;
        so.FindProperty("numberLabel").objectReferenceValue = number;
        so.FindProperty("activeBar").objectReferenceValue = active;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, HotbarSlotPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<SlotView>();
    }

    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    static RectTransform GetOrCreateChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "HUD create");
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            t = go.transform;
        }
        return (RectTransform)t;
    }

    // ── 공용 ──

    static Canvas FindHudCanvas()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.name == "Canvas") return c;
        Debug.LogError("[HudLayout] HUD Canvas('Canvas')를 찾을 수 없음");
        return null;
    }

    // 레퍼런스 해상도 기준으로 스케일 → 이미지에서 잰 px를 그대로 사용 가능, 해상도가 달라도 비율 유지
    static void SetupScaler(Canvas canvas)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        Undo.RecordObject(scaler, "HUD Canvas Scaler");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // parent 밑 name의 Image를 찾아(또는 create면 만들어) 스프라이트/색/크기(가운데 정렬 정사각형)를 맞춤
    static Image ImageAt(Transform parent, string name, Sprite sprite, Color color, float size, bool create = false)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            if (!create) { Debug.LogError($"[HudLayout] {parent.name}/{name} 없음"); return null; }
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "HUD create");
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            t = go.transform;
        }
        var img = t.GetComponent<Image>();
        Undo.RecordObject(img, "HUD Image");
        Undo.RecordObject(t, "HUD Image");
        img.sprite = sprite;
        img.type = Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        if (size > 0f) SetRect((RectTransform)t, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        return img;
    }

    static void Save(string section)
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[HudLayout] ✅ {section} 적용 완료");
    }
}

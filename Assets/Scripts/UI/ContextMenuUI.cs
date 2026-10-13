using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 슬롯 우클릭 시 뜨는 컨텍스트 메뉴 팝업.
/// Show()로 열고, 버튼 클릭 시 콜백 실행 후 자동으로 닫힌다.
///
/// 프리팹 구조 (panel 필드에 연결):
///   ContextMenuPanel (RectTransform, Image=배경, Outline, this가 아니라 자식임)
///     └─ Buttons (VerticalLayoutGroup)   ← buttonContainer 필드에 연결
///
/// buttonPrefab에는 ContextMenuButtonView 컴포넌트가 붙어 있어야 한다.
/// </summary>
public class ContextMenuUI : MonoBehaviour
{
    public static ContextMenuUI Instance { get; private set; }

    [Header("Prefab 참조")]
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private ContextMenuButtonView buttonPrefab;
    [SerializeField] private UITheme theme;

    private readonly List<ContextMenuButtonView> spawnedButtons = new();

    void Awake()
    {
        Instance = this;
        Hide();
    }

    /// <summary>
    /// 아이템 슬롯과 액션 목록을 받아 팝업을 띄운다.
    /// actions: ItemActionProvider.GetActions()의 결과
    /// onAction: 선택된 ActionType을 호출자에게 돌려줌
    /// </summary>
    public void Show(Vector2 screenPos, List<ItemAction> actions, Action<ActionType> onAction)
        => Show(screenPos, actions, onAction, null);

    /// <summary>anchorSlot을 주면 그 슬롯 오른쪽 옆에 띄움 (아이템을 가리지 않게)</summary>
    public void Show(Vector2 screenPos, List<ItemAction> actions, Action<ActionType> onAction, RectTransform anchorSlot)
    {
        ClearButtons();
        SetupLayout();

        foreach (var action in actions)
        {
            var captured = action; // 클로저 캡처용
            var btn = Instantiate(buttonPrefab, buttonContainer);
            btn.Setup(captured.label, () =>
            {
                onAction?.Invoke(captured.type);
                Hide();
            }, theme);
            StyleButton(btn);
            spawnedButtons.Add(btn);
        }

        panel.gameObject.SetActive(true);

        // 패널 크기: 가장 긴 글자에 맞춘 폭 × (버튼 높이 × 개수)
        float maxText = 0f;
        foreach (var b in spawnedButtons)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) maxText = Mathf.Max(maxText, t.GetPreferredValues(t.text).x);
        }
        float width = Mathf.Max(MinWidth, maxText + LabelPadX * 2f + PanelPad * 2f);
        float height = actions.Count * ButtonH + Mathf.Max(0, actions.Count - 1) * ButtonGap + PanelPad * 2f;
        panel.sizeDelta = new Vector2(width, height);

        // 위치 (화면 픽셀). 캔버스 스케일을 곱해야 실제 픽셀 크기
        float scale = panel.lossyScale.x;
        float wPx = width * scale, hPx = height * scale;
        float x, y;
        if (anchorSlot != null)
        {
            var corners = new Vector3[4];
            anchorSlot.GetWorldCorners(corners);            // 0=좌하, 1=좌상, 2=우상, 3=우하
            x = corners[2].x + 6f * scale;                  // 슬롯 오른쪽 옆
            y = corners[2].y;
            if (x + wPx > Screen.width) x = corners[1].x - 6f * scale - wPx; // 화면 넘으면 슬롯 왼쪽
        }
        else
        {
            x = screenPos.x + 14f * scale;                  // 커서 오른쪽 아래로 살짝 띄움
            y = screenPos.y - 4f * scale;
            if (x + wPx > Screen.width) x = screenPos.x - 14f * scale - wPx;
        }
        if (y - hPx < 0) y = hPx;
        if (y > Screen.height) y = Screen.height;

        panel.position = new Vector2(x, y);
    }

    // ── 스타일 (한글 폰트 Pretendard, 한 줄에 하나씩 세로 배치) ──
    const float ButtonH = 34f, ButtonGap = 2f, PanelPad = 6f, LabelPadX = 14f, MinWidth = 120f;
    bool layoutReady;

    void SetupLayout()
    {
        if (layoutReady) return;
        layoutReady = true;
        var vlg = buttonContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            // 예전엔 폭/높이를 레이아웃이 안 잡아서 버튼이 60px로 좁아지고 글자가 여러 줄로 겹쳤음
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.spacing = ButtonGap; vlg.padding = new RectOffset(0, 0, 0, 0);
        }
        var cRect = (RectTransform)buttonContainer;
        cRect.anchorMin = Vector2.zero; cRect.anchorMax = Vector2.one;
        cRect.offsetMin = new Vector2(PanelPad, PanelPad); cRect.offsetMax = new Vector2(-PanelPad, -PanelPad);
        var bg = panel.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = new Color(0.07f, 0.07f, 0.08f, 0.96f);
            if (HudStyle.RoundedRect != null) { bg.sprite = HudStyle.RoundedRect; bg.type = Image.Type.Sliced; }
        }
    }

    static void StyleButton(ContextMenuButtonView btn)
    {
        var le = btn.GetComponent<LayoutElement>();
        if (le == null) le = btn.gameObject.AddComponent<LayoutElement>(); // (Unity 가짜 null 때문에 ?? 안 씀)
        le.preferredHeight = ButtonH; le.minHeight = ButtonH;
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label == null) return;
        if (HudStyle.SemiBold != null) label.font = HudStyle.SemiBold;
        label.fontSize = 15f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        var r = label.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(LabelPadX, 0f); r.offsetMax = new Vector2(-LabelPadX, 0f);
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void ClearButtons()
    {
        foreach (var btn in spawnedButtons)
        {
            if (btn == null) continue;
            // Destroy는 프레임 끝에 실행되므로, 먼저 떼어내 레이아웃/클릭에서 즉시 빠지게 한다.
            btn.transform.SetParent(null, false);
            Destroy(btn.gameObject);
        }
        spawnedButtons.Clear();
    }

    void Update()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;

        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        // 좌클릭 또는 우클릭 시 패널 밖이면 닫기
        if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
        {
            if (!EventSystem.current.IsPointerOverGameObject())
                Hide();
        }
    }
}
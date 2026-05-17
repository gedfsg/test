using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 인벤토리 슬롯 우클릭 시 뜨는 컨텍스트 메뉴 팝업.
/// Show()로 열고, 버튼 클릭 시 콜백 실행 후 자동으로 닫힌다.
/// </summary>
public class ContextMenuUI : MonoBehaviour
{
    public static ContextMenuUI Instance { get; private set; }

    private GameObject panel;
    private Transform buttonContainer;

    // 버튼 스타일
    static readonly Color BtnNormal = new Color(0.14f, 0.18f, 0.23f, 1f);
    static readonly Color BtnHover = new Color(0.20f, 0.45f, 0.65f, 1f);
    static readonly Color BtnText = new Color(0.90f, 0.92f, 0.95f, 1f);

    void Awake()
    {
        Instance = this;
        BuildPanel();
        Hide();
    }

    // ── 외부 진입점 ───────────────────────────────

    /// <summary>
    /// 아이템 슬롯과 액션 목록을 받아 팝업을 띄운다.
    /// actions: ItemActionProvider.GetActions()의 결과
    /// onAction: 선택된 ActionType을 호출자에게 돌려줌
    /// </summary>
    public void Show(Vector2 screenPos, List<ItemAction> actions, Action<ActionType> onAction)
    {
        // 기존 버튼 전부 제거
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        foreach (var action in actions)
        {
            var captured = action; // 클로저 캡처용
            CreateButton(captured.label, () =>
            {
                onAction?.Invoke(captured.type);
                Hide();
            });
        }

        // 위치 설정 (화면 밖으로 나가지 않게 보정)
        RectTransform rt = panel.GetComponent<RectTransform>();
        panel.SetActive(true);

        // 버튼 수만큼 패널 높이 조정
        float btnH = 32f;
        float padding = 8f;
        rt.sizeDelta = new Vector2(120f, actions.Count * btnH + padding * 2f);

        // 화면 경계 보정
        float x = screenPos.x;
        float y = screenPos.y;
        if (x + rt.sizeDelta.x > Screen.width) x -= rt.sizeDelta.x;
        if (y - rt.sizeDelta.y < 0) y += rt.sizeDelta.y;

        rt.position = new Vector2(x, y);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    // ── UI 생성 ───────────────────────────────────

    private void BuildPanel()
    {
        // 전용 Canvas
        GameObject canvasObj = new GameObject("ContextMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // 모든 UI 위에
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        // 패널
        panel = new GameObject("ContextMenuPanel");
        panel.transform.SetParent(canvasObj.transform, false);

        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.pivot = new Vector2(0f, 1f); // 좌상단 기준

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.10f, 0.14f, 0.97f);

        // 테두리
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.50f, 0.75f, 0.8f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // 버튼 컨테이너
        GameObject container = new GameObject("Buttons");
        container.transform.SetParent(panel.transform, false);

        RectTransform crt = container.AddComponent<RectTransform>();
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(8f, 4f);
        crt.offsetMax = new Vector2(-8f, -4f);

        VerticalLayoutGroup vlg = container.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        buttonContainer = container.transform;
    }

    private void CreateButton(string label, Action onClick)
    {
        GameObject btnObj = new GameObject(label);
        btnObj.transform.SetParent(buttonContainer, false);

        // 높이 고정
        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.preferredHeight = 32f;
        le.flexibleWidth = 1f;

        Image bg = btnObj.AddComponent<Image>();
        bg.color = BtnNormal;

        // 호버 효과
        var trigger = btnObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerEnter,
            _ => bg.color = BtnHover);
        AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerExit,
            _ => bg.color = BtnNormal);
        AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerClick,
            _ => onClick?.Invoke());

        // 텍스트
        GameObject txtObj = new GameObject("Label");
        txtObj.transform.SetParent(btnObj.transform, false);

        RectTransform trt = txtObj.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 13f;
        tmp.color = BtnText;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    private void AddTrigger(UnityEngine.EventSystems.EventTrigger trigger,
        UnityEngine.EventSystems.EventTriggerType type, Action<UnityEngine.EventSystems.BaseEventData> cb)
    {
        var entry = new UnityEngine.EventSystems.EventTrigger.Entry();
        entry.eventID = type;
        entry.callback.AddListener(e => cb(e));
        trigger.triggers.Add(entry);
    }

    void Update()
    {
        if (panel == null || !panel.activeSelf) return;

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
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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
    {
        ClearButtons();

        foreach (var action in actions)
        {
            var captured = action; // 클로저 캡처용
            var btn = Instantiate(buttonPrefab, buttonContainer);
            btn.Setup(captured.label, () =>
            {
                onAction?.Invoke(captured.type);
                Hide();
            }, theme);
            spawnedButtons.Add(btn);
        }

        panel.gameObject.SetActive(true);

        // 버튼 수만큼 패널 높이 조정 (LayoutElement로 버튼 높이가 고정되어 있다는 전제)
        float btnH = 32f;
        float padding = 8f;
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, actions.Count * btnH + padding * 2f);

        // 화면 경계 보정
        float x = screenPos.x;
        float y = screenPos.y;
        if (x + panel.sizeDelta.x > Screen.width) x -= panel.sizeDelta.x;
        if (y - panel.sizeDelta.y < 0) y += panel.sizeDelta.y;

        panel.position = new Vector2(x, y);
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
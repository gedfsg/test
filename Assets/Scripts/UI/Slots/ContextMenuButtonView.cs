using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 컨텍스트 메뉴에 표시되는 버튼 하나. 프리팹에 붙여서 ContextMenuUI가 Instantiate한다.
/// 프리팹 구조: ButtonRoot (Image = background, this script) → Label (TextMeshProUGUI)
/// </summary>
public class ContextMenuButtonView : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI label;

    private UITheme theme;
    private Action onClick;

    public void Setup(string text, Action onClickCallback, UITheme uiTheme)
    {
        theme = uiTheme;
        onClick = onClickCallback;

        if (label != null) label.text = text;
        if (background != null) background.color = theme.buttonNormal;
        if (label != null) label.color = theme.buttonText;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (background != null) background.color = theme.buttonHover;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (background != null) background.color = theme.buttonNormal;
    }

    public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 조작 방법 패널 (ControlsPanel.prefab). 내용은 고정 그림(키보드·마우스)이라 열고 닫기만 함.
/// 메인 메뉴와 일시정지 메뉴가 같은 프리팹을 씀. 닫히면 onClosed로 원래 메뉴에 알림
/// </summary>
public class ControlsPanel : MonoBehaviour
{
    public Button backButton;

    /// <summary>뒤로/ESC로 닫혔을 때 (여는 쪽이 원래 메뉴를 다시 보여줌)</summary>
    public System.Action onClosed;

    public bool IsOpen => gameObject.activeSelf;

    void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(Close);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        UIAudio.Play(UIAudio.Sound.PanelOpen);
    }

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        UIAudio.Play(UIAudio.Sound.PanelClose);
        onClosed?.Invoke();
    }
}

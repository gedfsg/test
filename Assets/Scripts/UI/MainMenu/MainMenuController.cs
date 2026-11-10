using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 메인 메뉴 (MainMenu.unity, Build 0번).
///  게임 시작 → main 씬 / 조작 방법·설정 → 같은 씬의 ControlsPanel·SettingsPanel 프리팹 열기 / 게임 종료
///  ESC: 열린 패널 닫기
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("타이틀·버튼 묶음 (패널이 열리면 숨김)")]
    public GameObject menuRoot;
    public Button startButton;
    public Button controlsButton;
    public Button settingsButton;
    public Button quitButton;
    public ControlsPanel controlsPanel;
    public SettingsPanel settingsPanel;

    void Awake()
    {
        Time.timeScale = 1f;
        if (startButton != null) startButton.onClick.AddListener(MenuUI.StartGame);
        if (controlsButton != null) controlsButton.onClick.AddListener(OpenControls);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (quitButton != null) quitButton.onClick.AddListener(MenuUI.Quit);
        if (controlsPanel != null) { controlsPanel.onClosed = ShowMenu; controlsPanel.gameObject.SetActive(false); }
        if (settingsPanel != null) { settingsPanel.onClosed = ShowMenu; settingsPanel.gameObject.SetActive(false); }
    }

    void Start()
    {
        // 게임에서 돌아왔을 때 조준선 모드로 숨겨진 커서 복구
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (settingsPanel != null && settingsPanel.IsOpen) settingsPanel.Close();
        else if (controlsPanel != null && controlsPanel.IsOpen) controlsPanel.Close();
    }

    void OpenControls()
    {
        if (menuRoot != null) menuRoot.SetActive(false);
        controlsPanel.Open();
    }

    void OpenSettings()
    {
        if (menuRoot != null) menuRoot.SetActive(false);
        settingsPanel.Open();
    }

    void ShowMenu()
    {
        if (menuRoot != null) menuRoot.SetActive(true);
    }
}

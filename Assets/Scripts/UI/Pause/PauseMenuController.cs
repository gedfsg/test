using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 일시정지 메뉴 (PauseMenu.prefab). 게임 씬이 뜰 때 MenuUI가 전용 캔버스에 꺼내 둠 (내용은 꺼진 상태).
///  ESC: 열기/닫기 (설정·조작 방법이 열려 있으면 그것부터 닫고 일시정지 패널로)
///  열면 Time.timeScale = 0 + 게임 조작(사격·이동·인벤토리·카메라) 막음 + 뒤 화면 흐리게
///  계속하기 / 설정 / 조작 방법 / 다시 시작 / 메인 메뉴로 / 게임 종료
/// 설정·조작 방법은 메인 메뉴와 같은 프리팹(프리팹 안에 중첩)
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Tooltip("배경(흐린 화면 + 어둡게) - 열려 있는 동안 계속 보임")]
    public GameObject backdrop;
    public RawImage blurImage;
    [Tooltip("가운데 일시정지 패널")]
    public GameObject panel;
    public Button resumeButton, settingsButton, controlsButton, restartButton, mainMenuButton, quitButton;
    public SettingsPanel settingsPanel;
    public ControlsPanel controlsPanel;

    public bool IsOpen { get; private set; }
    float prevTimeScale = 1f;

    void Awake()
    {
        Localization.Register(gameObject); // 메뉴 글자 언어 (설정 → 언어)
        if (resumeButton != null) resumeButton.onClick.AddListener(Close);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (controlsButton != null) controlsButton.onClick.AddListener(OpenControls);
        if (restartButton != null) restartButton.onClick.AddListener(MenuUI.RestartGame);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(MenuUI.LoadMainMenu);
        if (quitButton != null) quitButton.onClick.AddListener(MenuUI.Quit);
        if (settingsPanel != null) settingsPanel.onClosed = ShowPanel;
        if (controlsPanel != null) controlsPanel.onClosed = ShowPanel;
        HideAll();
    }

    void HideAll()
    {
        if (backdrop != null) backdrop.SetActive(false);
        if (panel != null) panel.SetActive(false);
        if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        if (controlsPanel != null) controlsPanel.gameObject.SetActive(false);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (MenuUI.DeathShown) return; // 사망 후엔 생존 종료 화면만
        if (MenuUI.CutsceneActive) return; // 탈출 컷신 중엔 일시정지 안 됨

        if (!IsOpen) Open();
        else if (settingsPanel != null && settingsPanel.IsOpen) settingsPanel.Close();
        else if (controlsPanel != null && controlsPanel.IsOpen) controlsPanel.Close();
        else Close();
    }

    public void Open()
    {
        if (IsOpen || MenuUI.DeathShown) return;
        IsOpen = true;
        MenuUI.PauseOpen = true;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        MenuUI.RefreshGameplayInput(); // DeathShown·PauseOpen을 먼저 켠 뒤 → 조작 막힘
        UIAudio.Play(UIAudio.Sound.PauseOn);
        StartCoroutine(OpenAfterCapture());
    }

    // 메뉴가 찍히지 않게: 이번 프레임 화면을 먼저 찍고 나서 메뉴를 띄움
    System.Collections.IEnumerator OpenAfterCapture()
    {
        yield return MenuUI.CaptureBlur(blurImage);
        if (!IsOpen) yield break;
        if (backdrop != null) backdrop.SetActive(true);
        ShowPanel();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        MenuUI.PauseOpen = false;
        HideAll();
        UIAudio.Play(UIAudio.Sound.PauseOff);
        Time.timeScale = prevTimeScale;
        MenuUI.RefreshGameplayInput(); // 인벤토리가 열려 있으면 계속 막힘
    }

    void ShowPanel()
    {
        if (!IsOpen) return;
        if (panel != null) panel.SetActive(true); // UIButton이 다시 켜질 때 '계속하기'가 선택 모양으로 돌아감
    }

    void OpenSettings()
    {
        if (panel != null) panel.SetActive(false);
        settingsPanel.Open();
    }

    void OpenControls()
    {
        if (panel != null) panel.SetActive(false);
        controlsPanel.Open();
    }

    void OnDestroy()
    {
        // 열린 채로 씬이 바뀌면(다시 시작/메인 메뉴) MenuUI가 timeScale·플래그를 정리함
        if (IsOpen) MenuUI.PauseOpen = false;
    }
}

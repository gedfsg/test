using UnityEngine;

/// <summary>
/// 메뉴 UI 프리팹 목록 (Assets/Resources/UI/MenuUIPrefabs.asset). 프리팹 자체는 Assets/Prefabs/UI에 있고,
/// 게임 씬은 이 목록으로 일시정지·생존 종료를 꺼내 씀 (MenuUI). Tools → UI → Build Menu UI가 만들고 채움
/// </summary>
[CreateAssetMenu(menuName = "UI/Menu UI Prefabs")]
public class MenuUIPrefabs : ScriptableObject
{
    public GameObject settingsPanel;
    public GameObject controlsPanel;
    public GameObject pauseMenu;
    public GameObject deathScreen;

    static MenuUIPrefabs cached;
    public static MenuUIPrefabs Load() => cached != null ? cached : cached = Resources.Load<MenuUIPrefabs>("UI/MenuUIPrefabs");
}

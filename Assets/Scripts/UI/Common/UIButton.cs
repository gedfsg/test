using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 메뉴 버튼 공용 모양/호버 (메인 메뉴·일시정지·생존 종료·설정 하단·뒤로).
///  - group이 있으면: 같은 부모 안 같은 group 버튼 중 하나만 "선택"(주황) 모양. 마우스를 올리면 선택이 그 버튼으로 옮겨감
///    (레퍼런스의 주황 강조 버튼). 메뉴를 다시 열면 defaultSelected 버튼으로 돌아감
///  - group이 없으면: 평소 모양 고정 + 호버 시 테두리만 주황으로 밝아짐
/// 모양은 bg / frame / label / icon / arrow 색만 바꿈 (Button의 자체 색 전환은 끔)
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("구성 요소")]
    public Image bg;
    public Image frame;
    public TextMeshProUGUI label;
    public Image icon;
    [Tooltip("선택됐을 때만 보이는 → 화살표 (없어도 됨)")]
    public Image arrow;

    [Header("소리 (UIAudio)")]
    public UIAudio.Sound clickSound = UIAudio.Sound.Click;

    [Header("그룹 (비우면 호버만)")]
    public string group = "";
    public bool defaultSelected;

    [Header("평소")]
    public Color normalBg = new Color32(0x14, 0x16, 0x1B, 0xE0);
    public Color normalFrame = new Color32(0x5A, 0x5D, 0x66, 0xFF);
    public Color normalText = Color.white;
    public Color normalIcon = new Color32(0xC9, 0xCD, 0xD4, 0xFF);

    [Header("선택 (주황)")]
    public Sprite selectedBgSprite; // 주황 그라데이션 (없으면 bg 스프라이트 그대로 + 색)
    public Color selectedBg = new Color32(0xFF, 0xA5, 0x00, 0xFF);
    public Color selectedFrame = new Color32(0xFF, 0xB8, 0x4D, 0xFF);
    public Color selectedText = new Color32(0x1A, 0x14, 0x0C, 0xFF);
    public Color selectedIcon = new Color32(0x1A, 0x14, 0x0C, 0xFF);

    [Header("호버 (그룹 없을 때)")]
    public Color hoverFrame = new Color32(0xFF, 0xA5, 0x00, 0xFF);

    Sprite normalBgSprite;
    bool selected, hovered, inited;

    public bool Selected => selected;

    void Awake()
    {
        Init();
        var b = GetComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => UIAudio.Play(clickSound));
    }

    void Init()
    {
        if (inited) return;
        inited = true;
        if (bg != null) normalBgSprite = bg.sprite;
    }

    void OnEnable()
    {
        Init();
        hovered = false;
        if (!string.IsNullOrEmpty(group)) { if (defaultSelected) Select(); else SetSelected(false); }
        else Refresh();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        hovered = true;
        UIAudio.Play(UIAudio.Sound.Hover);
        if (!string.IsNullOrEmpty(group)) Select();
        else Refresh();
    }

    public void OnPointerExit(PointerEventData e)
    {
        hovered = false;
        Refresh();
    }

    /// <summary>이 버튼을 선택 모양으로, 같은 그룹 형제들은 평소 모양으로</summary>
    public void Select()
    {
        if (transform.parent != null)
            foreach (var other in transform.parent.GetComponentsInChildren<UIButton>(true))
                if (other != this && other.group == group) other.SetSelected(false);
        SetSelected(true);
    }

    void SetSelected(bool on)
    {
        selected = on;
        Refresh();
    }

    void Refresh()
    {
        Init();
        bool sel = selected && !string.IsNullOrEmpty(group);
        if (bg != null)
        {
            bg.sprite = sel && selectedBgSprite != null ? selectedBgSprite : normalBgSprite;
            bg.color = sel ? (selectedBgSprite != null ? Color.white : selectedBg) : normalBg;
        }
        if (frame != null)
        {
            frame.color = sel ? selectedFrame : hovered ? hoverFrame : normalFrame;
            frame.enabled = !sel || selectedFrame.a > 0f;
        }
        if (label != null) label.color = sel ? selectedText : normalText;
        if (icon != null) icon.color = sel ? selectedIcon : normalIcon;
        if (arrow != null) { arrow.enabled = sel; arrow.color = selectedText; }
    }
}

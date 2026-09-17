using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 슬롯과 무기 핫바 슬롯이 공유하는 단일 슬롯 UI 컴포넌트.
/// 프리팹에 이 컴포넌트를 붙이고 아래 필드들을 Inspector에서 연결한 뒤,
/// InventoryUI / WeaponHotbarUI가 Instantiate해서 SetItem()/SetEmpty()로 갱신한다.
///
/// 필요 프리팹 구조 예시:
///   SlotRoot (Image = background, this script, Outline)
///     ├─ Icon        (Image)
///     ├─ NameLabel    (TextMeshProUGUI)   - 아이콘 없을 때 이름 표시
///     ├─ CountLabel   (TextMeshProUGUI)   - 스택 개수 (x3 등)
///     └─ NumberLabel  (TextMeshProUGUI)   - 핫바 전용, 1~5 표시. 인벤토리는 비워둬도 됨
/// </summary>
/// 


[RequireComponent(typeof(RectTransform))]
public class SlotView : MonoBehaviour, IPointerClickHandler
{
    [Header("필수 참조")]
    [SerializeField] private Image background;
    [SerializeField] private Image icon;

    [Header("선택 참조")]
    [SerializeField] private Outline outline;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI countLabel;
    [SerializeField] private TextMeshProUGUI numberLabel;

    [Tooltip("핫바에서 선택된 슬롯일 때만 켜지는 하단 강조 바. 인벤토리 슬롯은 비워두면 무시된다.")]
    [SerializeField] private Image activeBar;

    public event Action<PointerEventData> LeftClicked;
    public event Action<PointerEventData> RightClicked;

    /// <summary>이 슬롯이 현재 표시 중인 아이템 (없으면 null). 클릭 콜백에서 참조용.</summary>
    public ItemData CurrentItem { get; private set; }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[SlotView] 순번={transform.GetSiblingIndex()} / 마우스={eventData.position} / 슬롯중심={((RectTransform)transform).position}");
        if (eventData.button == PointerEventData.InputButton.Right)
            RightClicked?.Invoke(eventData);
        else if (eventData.button == PointerEventData.InputButton.Left)
            LeftClicked?.Invoke(eventData);
    }

    /// <summary>빈 슬롯 상태로 표시한다. isActive가 true면 (핫바처럼) 하단 강조 바를 켠다.</summary>
    public void SetEmpty(UITheme theme, bool isActive = false)
    {
        CurrentItem = null;

        if (background != null) background.color = theme.slotEmpty;
        SetActiveBar(isActive, theme);
        if (outline != null) outline.effectColor = theme.slotOutlineDefault;
        if (icon != null) { icon.enabled = false; icon.sprite = null; }
        if (nameLabel != null) nameLabel.text = "";
        if (countLabel != null) countLabel.text = "";
    }

    /// <summary>
    /// 아이템 정보로 슬롯을 채운다.
    /// </summary>
    /// <param name="item">표시할 아이템</param>
    /// <param name="amount">스택 개수 (1이면 표시 안 함)</param>
    /// <param name="theme">색상 팔레트</param>
    /// <param name="isActive">핫바에서 현재 선택된 슬롯인지 여부 (인벤토리는 항상 false)</param>
    public void SetItem(ItemData item, int amount, UITheme theme, bool isActive = false)
    {
        CurrentItem = item;
        var (bg, outlineColor) = theme.GetItemTypeColors(item.itemType);

        if (background != null) background.color = bg;
        SetActiveBar(isActive, theme);
        if (outline != null) outline.effectColor = outlineColor;

        bool hasIcon = item.icon != null;
        if (icon != null)
        {
            icon.sprite = hasIcon ? item.icon : null;
            icon.color = Color.white;
            icon.enabled = hasIcon;
        }

        if (nameLabel != null)
            nameLabel.text = hasIcon ? "" : ShortenName(item.itemName);

        if (countLabel != null)
            countLabel.text = amount > 1 ? $"x{amount}" : "";
    }

    /// <summary>핫바 슬롯 번호(1~5) 표시. 인벤토리 슬롯은 numberLabel을 비워두면 자동 무시된다.</summary>
    public void SetNumber(int number, bool isActive, UITheme theme)
    {
        if (numberLabel == null) return;
        numberLabel.text = number.ToString();
        numberLabel.color = isActive ? theme.textPrimary : theme.textMuted;
    }

    /// <summary>하단 강조 바를 켜고 끈다. activeBar가 연결되지 않은 슬롯(인벤토리)은 무시된다.</summary>
    private void SetActiveBar(bool isActive, UITheme theme)
    {
        if (activeBar == null) return;
        activeBar.color = theme.slotActive;
        activeBar.gameObject.SetActive(isActive);
    }

    private static string ShortenName(string name) => name.Length > 8 ? name[..8] : name;
}